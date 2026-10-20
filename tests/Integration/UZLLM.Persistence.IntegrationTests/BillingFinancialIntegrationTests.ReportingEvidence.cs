using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Management.Api;
using UZLLM.Management.Api.ReportingEvidence;
using UZLLM.Management.Api.Statements;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    private static readonly DateTimeOffset EvidenceFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ReportingEvidenceProfile EvidenceProfile() => new("synthetic-evidence", 1, true, "synthetic-issuer", "test-jurisdiction", "test-series",
        "TEST", 3, "Invoice", "synthetic-issuer-snapshot", "synthetic-tax-policy", "synthetic-correction-policy", "synthetic-archive-policy",
        EvidenceFrom, EvidenceFrom.AddYears(2),
        [.. new[] { "Finance", "Legal", "Security", "Operations", "Commercial" }.Select(role => new ReportingApproval(role, "synthetic-reviewer", "synthetic-approval", EvidenceFrom.AddDays(-1)))],
        [new("evidence-chat", "test", "Chat", "Sse")]);
    private static RegisterFiscalEvidenceRequest EvidenceFiscal() => new("synthetic-evidence", 1, Guid.NewGuid(), "external-operation-test", "Registered",
        "external-process-test", "Synthetic document observation", new("TEST-001", EvidenceFrom.AddDays(1), "Invoice", "synthetic-customer-snapshot",
            new("9007199254740993", null, null), new('a', 64), "synthetic-controlled-archive", "synthetic-issuer-verification"));
    private static RegisterContractReferenceRequest EvidenceContract() => new("synthetic-evidence", 1, Guid.NewGuid(), "contract-test", "policy-v1",
        "evidence-chat", "synthetic-customer-snapshot", EvidenceFrom, EvidenceFrom.AddYears(1), "EvidenceOnly", "Synthetic contract provenance");
    private static string EvidencePath(Guid org) => $"/management/v1/admin/organizations/{org}/reporting-evidence";

    private async Task<WebApplication> EvidenceHostAsync(Action<IServiceCollection>? configure = null, ReportingEvidenceRegistry? registry = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration); builder.Services.AddUzllmIdentity(); builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmBilling(); builder.Services.AddUzllmOrganizations(); builder.Services.AddUzllmPayments(builder.Configuration);
        builder.Services.AddUzllmStatementPublication(); builder.Services.AddUzllmStatementApi(); builder.Services.AddUzllmSliReports(builder.Configuration);
        builder.Services.AddSingleton(new SliReportProfiles(new Dictionary<string, SliReportScope> { ["evidence-chat"] = new("evidence-chat", "test", "Chat", "Sse", [new("edge-test", "Edge"), new("gateway-test", "Gateway"), new("probe-test", "Probe")]) }));
        builder.Services.AddUzllmReportingEvidence(builder.Configuration, builder.Environment); builder.Services.AddUzllmReportingEvidenceApi();
        builder.Services.AddSingleton(registry ?? new(false, [EvidenceProfile()])); configure?.Invoke(builder.Services);
        var app = builder.Build(); app.UseUzllmManagementNoStore(); app.UseUzllmReportingEvidenceApiBounds(); app.UseUzllmManagementSession();
        app.MapUzllmReportingEvidenceEndpoints(); await app.StartAsync(); return app;
    }

    [Fact]
    public async Task Fiscal_evidence_reconciles_unknown_once_and_preserves_correction_cancellation_and_financial_history()
    {
        await ResetAsync(); var seed = await SeedAsync(); await CreditAsync(seed.OrganizationId, 100_000);
        await using (var financialProvider = fixture.CreateServiceProvider())
        await using (var financialScope = financialProvider.CreateAsyncScope())
        {
            var held = await Financial(financialScope.ServiceProvider, new MutableFinancialClock(Start)).ReserveAsync(Admission(seed, 1_000));
            Assert.NotNull(held.RequestId);
        }
        await using var app = await EvidenceHostAsync(); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        var (op, actor) = await StatementSessionAsync(services, null, null, true); var baseline = await StatementMoneyRowsAsync(db);
        var path = EvidencePath(seed.OrganizationId); var original = EvidenceFiscal();
        var unknown = original with { Outcome = "Unknown", Document = null };
        var pending = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, unknown, HttpStatusCode.Created);
        Assert.Equal("Unknown", pending.State); Assert.Null(pending.Fiscal!.Document); Assert.Equal("EvidenceOnly", pending.Eligibility);
        using (var duplicateOperation = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/fiscal", op, original with { IdempotencyKey = Guid.NewGuid() })))
            Assert.Equal(HttpStatusCode.Conflict, duplicateOperation.StatusCode);
        var reconcile = original with { IdempotencyKey = Guid.NewGuid(), PredecessorId = pending.Id };
        var issued = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, reconcile, HttpStatusCode.Created);
        Assert.Equal("Registered", issued.State); Assert.Equal("9007199254740993", issued.Fiscal!.Document!.Amounts.TotalUnits);
        Assert.Null(issued.Fiscal.Document.Amounts.TaxUnits); Assert.Equal(actor, issued.ActorId); Assert.True(issued.Profile.Synthetic);
        Assert.Equal(issued.Id, (await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, reconcile, HttpStatusCode.Created)).Id);
        foreach (var command in new[] { reconcile with { Reason = "Changed replay terms" }, reconcile with { IdempotencyKey = Guid.NewGuid() },
            original with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "other-operation" } })
        { using var conflict = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/fiscal", op, command)); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); }
        var correction = original with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "correction-operation", PredecessorId = issued.Id,
            Document = original.Document! with { Number = "TEST-002", Amounts = new("1001", "1000", "1") } };
        var uncertain = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op,
            correction with { Outcome = "Unknown", Document = null }, HttpStatusCode.Created);
        var review = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op,
            correction with { IdempotencyKey = Guid.NewGuid(), PredecessorId = uncertain.Id, Outcome = "ReviewRequired", Document = null }, HttpStatusCode.Created);
        Assert.Equal("ReviewRequired", review.State); Assert.Null(review.Fiscal!.StatementVersionId);
        correction = correction with { IdempotencyKey = Guid.NewGuid(), PredecessorId = review.Id };
        var corrected = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, correction, HttpStatusCode.Created);
        var cancellation = correction with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "cancellation-operation", PredecessorId = corrected.Id,
            Outcome = "Cancelled", Document = null };
        var cancelled = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, cancellation, HttpStatusCode.Created);
        Assert.Equal(corrected.Id, cancelled.PredecessorId); Assert.Null(cancelled.Fiscal!.Document);
        Assert.Equal("9007199254740993", (await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Get, path + "/fiscal/" + issued.Id, op)).Fiscal!.Document!.Amounts.TotalUnits);
        Assert.Equal(baseline, await StatementMoneyRowsAsync(db));
        Assert.Equal(6, await db.Set<BillingReportingEvidenceEntity>().CountAsync());
        var audits = await db.Set<AuditEventEntity>().Where(a => a.Action == "reporting.evidence.recorded").ToListAsync(); Assert.Equal(6, audits.Count);
        Assert.All(audits, a => { Assert.Equal(actor, a.ActorAccountId); Assert.DoesNotContain("TEST-001", a.MetadataJson); Assert.DoesNotContain("synthetic-customer", a.MetadataJson); });
    }

    [Fact]
    public async Task Fiscal_parallel_replay_and_issuer_identity_fences_include_other_tenants()
    {
        await ResetAsync(); var seed = await SeedAsync(); await using var app = await EvidenceHostAsync(); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider; var (op, _) = await StatementSessionAsync(services, null, null, true);
        var r = EvidenceFiscal(); var path = EvidencePath(seed.OrganizationId);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, r, HttpStatusCode.Created)));
        Assert.Single(results.Select(v => v.Id).Distinct()); var db = services.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingReportingEvidenceEntity>().CountAsync()); Assert.Equal(1, await db.Set<AuditEventEntity>().CountAsync(a => a.Action == "reporting.evidence.recorded"));
        var other = Guid.NewGuid(); db.Add(new OrganizationEntity { Id = other, Name = "Other tenant", Status = "Active", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        using var conflict = await client.SendAsync(StatementRequest(HttpMethod.Post, EvidencePath(other) + "/fiscal", op,
            r with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "other-tenant-operation" })); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var missing = await client.SendAsync(StatementRequest(HttpMethod.Get, EvidencePath(other) + "/fiscal/" + results[0].Id, op)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Contract_links_pin_same_tenant_month_and_scope_without_qualifying_signed_or_unknown_SLI()
    {
        await ResetAsync(); var seed = await SeedAsync(); var wrongProfile = EvidenceProfile() with { Id = "wrong-scope-profile",
            Scopes = [new("wrong-scope", "other-region", "Chat", "Sse")] };
        await using var app = await EvidenceHostAsync(registry: new(false, [EvidenceProfile(), wrongProfile])); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var s = scope.ServiceProvider; var (op, actor) = await StatementSessionAsync(s, null, null, true);
        var month = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero); var path = EvidencePath(seed.OrganizationId);
        var stmt = await s.GetRequiredService<IStatementPublicationStore>().RequestAsync(new(seed.OrganizationId, actor, new(2026, 9), Guid.NewGuid(), 0, null, "Synthetic statement snapshot"));
        await s.GetRequiredService<IStatementPublicationStore>().BuildAsync(stmt.Id, 1);
        var statementId = (await s.GetRequiredService<IStatementPublicationStore>().FindOperationAsync(seed.OrganizationId, stmt.Id, default))!.VersionId!.Value;
        var slis = s.GetRequiredService<ISliReportStore>(); var job = await slis.RequestAsync(new(actor, "evidence-chat", month, Guid.NewGuid(), 0, null, "Synthetic SLI snapshot"));
        await slis.BuildAsync(job.Id, 1); var sliId = (await slis.OperationAsync(job.Id))!.VersionId!.Value;
        var terms = EvidenceContract() with { SignedContractReference = "synthetic-signed-agreement", ContentSha256 = new('b', 64) };
        var contract = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/contracts", op, terms, HttpStatusCode.Created);
        using (var overlap = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/contracts", op,
            terms with { IdempotencyKey = Guid.NewGuid(), ContractId = "overlapping-other-contract" }))) Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        var command = new LinkContractReportRequest(Guid.NewGuid(), contract.Id, month, "Synthetic evidence-only link", statementId, sliId);
        var link = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/links", op, command, HttpStatusCode.Created);
        Assert.Equal("EvidenceOnly", link.State); Assert.Equal("EvidenceOnly", link.Eligibility); Assert.Equal("NoData", link.SliCoverage);
        Assert.NotNull(link.StatementSourceDigest); Assert.NotNull(link.SliSourceDigest);
        var otherOrg = Guid.NewGuid(); var db = s.GetRequiredService<FoundationDbContext>();
        db.Add(new OrganizationEntity { Id = otherOrg, Name = "Other reporting tenant", Status = "Active", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        using (var crossTenant = await client.SendAsync(StatementRequest(HttpMethod.Post, EvidencePath(otherOrg) + "/links", op, command with { IdempotencyKey = Guid.NewGuid() })))
            Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        var otherContract = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, EvidencePath(otherOrg) + "/contracts", op, terms with { IdempotencyKey = Guid.NewGuid() }, HttpStatusCode.Created);
        using (var crossStatement = await client.SendAsync(StatementRequest(HttpMethod.Post, EvidencePath(otherOrg) + "/links", op,
            command with { IdempotencyKey = Guid.NewGuid(), ContractReferenceId = otherContract.Id }))) Assert.Equal(HttpStatusCode.NotFound, crossStatement.StatusCode);
        var wrongContract = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/contracts", op,
            terms with { IdempotencyKey = Guid.NewGuid(), ProfileId = wrongProfile.Id, ServiceScopeId = "wrong-scope", ContractId = "other-service-contract" }, HttpStatusCode.Created);
        using (var wrongScope = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/links", op,
            command with { IdempotencyKey = Guid.NewGuid(), ContractReferenceId = wrongContract.Id, StatementVersionId = null }))) Assert.Equal(HttpStatusCode.Conflict, wrongScope.StatusCode);
        foreach (var invalid in new[] { command with { IdempotencyKey = Guid.NewGuid() }, command with { IdempotencyKey = Guid.NewGuid(), PeriodStart = month.AddMonths(-1) },
            command with { IdempotencyKey = Guid.NewGuid(), PeriodStart = EvidenceFrom.AddMonths(-1) } })
        { using var result = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/links", op, invalid)); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); }
        var amendment = terms with { IdempotencyKey = Guid.NewGuid(), PolicyVersion = "policy-v2", PredecessorId = contract.Id, Reason = "Synthetic signed contract amendment" };
        var amended = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/contracts", op, amendment, HttpStatusCode.Created);
        var revoke = amendment with { IdempotencyKey = Guid.NewGuid(), State = "Revoked", PredecessorId = amended.Id, Reason = "Synthetic contract revocation" };
        var revoked = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/contracts", op, revoke, HttpStatusCode.Created);
        Assert.Equal(amended.Id, revoked.PredecessorId);
        using var denied = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/links", op, command with { IdempotencyKey = Guid.NewGuid(), SliReportVersionId = null }));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal(link.Id, (await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Get, path + "/links/" + link.Id, op)).Id);
        Assert.Empty(await s.GetRequiredService<FoundationDbContext>().Set<BillingWalletAdjustmentEntity>().ToListAsync());
    }

    [Fact]
    public async Task Evidence_profile_withdrawal_preserves_replay_but_blocks_new_registration_and_same_version_redefinition()
    {
        await ResetAsync(); var seed = await SeedAsync(); var r = EvidenceFiscal(); Guid actor; Guid id;
        await using (var first = await EvidenceHostAsync())
        await using (var scope = first.Services.CreateAsyncScope())
        {
            (_, actor) = await StatementSessionAsync(scope.ServiceProvider, null, null, true);
            id = (await scope.ServiceProvider.GetRequiredService<IReportingEvidenceStore>().RegisterFiscalAsync(seed.OrganizationId, actor, r, default)).Id;
        }
        foreach (var registry in new[] { new ReportingEvidenceRegistry(false, []), new(false, [EvidenceProfile() with { RevokedAt = EvidenceFrom.AddDays(2) }]),
            new(false, [EvidenceProfile() with { Synthetic = false }]) })
        {
            await using var app = await EvidenceHostAsync(registry: registry); await using var scope = app.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IReportingEvidenceStore>();
            Assert.Equal(id, (await store.RegisterFiscalAsync(seed.OrganizationId, actor, r, default)).Id);
            await Assert.ThrowsAsync<ReportingProfileUnavailableException>(() => store.RegisterFiscalAsync(seed.OrganizationId, actor,
                r with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "new-operation", Document = r.Document! with { Number = "TEST-002" } }, default));
        }
        await using var changed = await EvidenceHostAsync(registry: new(false, [EvidenceProfile() with { Currency = "OTHER" }]));
        await using var changedScope = changed.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<ReportingEvidenceConflictException>(() => changedScope.ServiceProvider.GetRequiredService<IReportingEvidenceStore>()
            .RegisterFiscalAsync(seed.OrganizationId, actor, r with { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "changed-profile-op",
                Document = r.Document! with { Number = "TEST-003" } }, default));
        Assert.Equal(1, await changedScope.ServiceProvider.GetRequiredService<FoundationDbContext>().Set<BillingReportingEvidenceEntity>().CountAsync());
    }

    [Fact]
    public async Task Evidence_HTTP_rechecks_operator_MFA_CSRF_no_store_bounds_and_tenant_kind_cursor()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var app = await EvidenceHostAsync(services => services.AddSingleton<TimeProvider>(new EvidencePreciseClock())); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var s = scope.ServiceProvider;
        var (op, _) = await StatementSessionAsync(s, null, null, true); var (stale, _) = await StatementSessionAsync(s, null, null, true, false);
        var (owner, _) = await StatementSessionAsync(s, seed.OrganizationId, "Owner"); var path = EvidencePath(seed.OrganizationId); var command = EvidenceFiscal();
        foreach (var (session, status) in new[] { (owner, HttpStatusCode.Forbidden), (stale, HttpStatusCode.Forbidden) })
            foreach (var suffix in new[] { "/fiscal", "/fiscal/" + Guid.NewGuid(), "/contracts", "/links" })
            { using var denied = await client.SendAsync(StatementRequest(HttpMethod.Get, path + suffix, session)); Assert.Equal(status, denied.StatusCode); AssertNoStore(denied); }
        using (var anonymous = await client.GetAsync(path + "/fiscal")) { Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); AssertNoStore(anonymous); }
        foreach (var (suffix, body) in new (string, object)[] { ("/fiscal", command), ("/contracts", EvidenceContract()),
            ("/links", new LinkContractReportRequest(Guid.NewGuid(), Guid.NewGuid(), EvidenceFrom, "Synthetic CSRF denial", Guid.NewGuid())) })
        { using var denied = await client.SendAsync(StatementRequest(HttpMethod.Post, path + suffix, op, body, false)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, command, HttpStatusCode.Created);
        await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, command with
        { IdempotencyKey = Guid.NewGuid(), ExternalOperationReference = "second-op", Document = command.Document! with { Number = "TEST-002" } }, HttpStatusCode.Created);
        var first = await StatementBody<ReportingEvidencePage>(client, HttpMethod.Get, path + "/fiscal?limit=1", op); Assert.Single(first.Items); Assert.NotNull(first.NextCursor);
        Assert.Equal(0, first.Items[0].RecordedAt.Ticks % 10);
        var next = await StatementBody<ReportingEvidencePage>(client, HttpMethod.Get, path + "/fiscal?limit=1&cursor=" + Uri.EscapeDataString(first.NextCursor!), op);
        Assert.Single(next.Items); Assert.Null(next.NextCursor); Assert.NotEqual(first.Items[0].Id, next.Items[0].Id);
        foreach (var target in new[] { path + "/contracts?limit=1", path + "/fiscal?limit=2", EvidencePath(Guid.NewGuid()) + "/fiscal?limit=1" })
        { using var invalid = await client.SendAsync(StatementRequest(HttpMethod.Get, target + "&cursor=" + Uri.EscapeDataString(first.NextCursor!), op)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var largeRequest = StatementRequest(HttpMethod.Post, path.ToUpperInvariant() + "/FISCAL", op);
        largeRequest.Content = new StringContent(new string('x', 8193)); using var large = await client.SendAsync(largeRequest); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode); AssertNoStore(large);
    }

    [Fact]
    public async Task Evidence_audit_failure_rolls_back_profile_receipt_and_record_then_retries_without_financial_effect()
    {
        await ResetAsync(); var seed = await SeedAsync(); var armed = false;
        await using var app = await EvidenceHostAsync(services => services.AddScoped<IAuditTrail>(sp => new EvidenceAuditFault(
            new UZLLM.Modules.Audit.Application.AuditTrail(sp.GetRequiredService<IAuditEventStore>(), TimeProvider.System), () => armed)));
        using var client = StatementClient(app); await using var scope = app.Services.CreateAsyncScope(); var s = scope.ServiceProvider;
        var (op, _) = await StatementSessionAsync(s, null, null, true); var command = EvidenceFiscal(); var path = EvidencePath(seed.OrganizationId);
        var db = s.GetRequiredService<FoundationDbContext>(); var money = await StatementMoneyRowsAsync(db); armed = true;
        using (var failed = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/fiscal", op, command))) Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Empty(await db.Set<BillingReportingProfileEntity>().ToListAsync()); Assert.Empty(await db.Set<BillingReportingEvidenceEntity>().ToListAsync());
        armed = false; await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, command, HttpStatusCode.Created);
        Assert.Equal(money, await StatementMoneyRowsAsync(db)); Assert.Equal(1, await db.Set<BillingReportingEvidenceEntity>().CountAsync());
    }

    [Fact]
    public async Task Evidence_unknown_commit_reply_recovers_same_receipt_and_preserves_quarantined_restore_history()
    {
        await ResetAsync(); var seed = await SeedAsync(); var fault = new EvidenceCommitReplyLoss();
        await using var app = await EvidenceHostAsync(services => services.AddDbContext<FoundationDbContext>(options => options.AddInterceptors(fault)));
        using var client = StatementClient(app); await using var scope = app.Services.CreateAsyncScope(); var s = scope.ServiceProvider;
        var (op, _) = await StatementSessionAsync(s, null, null, true); var command = EvidenceFiscal(); var path = EvidencePath(seed.OrganizationId);
        fault.Armed = true; using (var lost = await client.SendAsync(StatementRequest(HttpMethod.Post, path + "/fiscal", op, command))) Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var recovered = await StatementBody<ReportingEvidenceResponse>(client, HttpMethod.Post, path + "/fiscal", op, command, HttpStatusCode.Created);
        Assert.Equal(1, fault.Failures); var db = s.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.Set<BillingReportingEvidenceEntity>().CountAsync());
        foreach (var sql in new[] { "UPDATE billing.reporting_evidence SET state='Unknown'", "DELETE FROM billing.reporting_evidence", "UPDATE billing.reporting_profile SET sha256='changed'", "DELETE FROM billing.reporting_profile" })
            Assert.Equal(PostgresErrorCodes.RaiseException, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        var original = await db.Set<BillingReportingEvidenceEntity>().AsNoTracking().SingleAsync();
        await using (var runtime = new NpgsqlConnection(fixture.RuntimeConnectionString))
        {
            await runtime.OpenAsync(); await using var truncate = runtime.CreateCommand(); truncate.CommandText = "TRUNCATE billing.reporting_evidence, billing.reporting_profile";
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync())).SqlState);
        }
        var restored = await fixture.RestoreStatementQuarantineAsync(); await using var connection = new NpgsqlConnection(restored); await connection.OpenAsync();
        await using var read = connection.CreateCommand(); read.CommandText = "SELECT payload_json, payload_sha256 FROM billing.reporting_evidence";
        await using var rows = await read.ExecuteReaderAsync(); Assert.True(await rows.ReadAsync()); var bytes = rows.GetString(0);
        Assert.Equal(original.PayloadJson, bytes); Assert.Equal(rows.GetString(1), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bytes))));
        Assert.Equal(recovered.Id, JsonSerializer.Deserialize<ReportingEvidenceResponse>(bytes, ReportingEvidencePolicy.Json)!.Id); Assert.False(await rows.ReadAsync());
        await rows.CloseAsync(); await using var profileRead = connection.CreateCommand(); profileRead.CommandText = "SELECT snapshot_json, sha256 FROM billing.reporting_profile";
        await using var profileRows = await profileRead.ExecuteReaderAsync(); Assert.True(await profileRows.ReadAsync());
        Assert.Equal(profileRows.GetString(1), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileRows.GetString(0)))));
        Assert.Equal(ReportingEvidencePolicy.Digest(recovered.Profile), profileRows.GetString(1)); await profileRows.CloseAsync();
        // Simulate a corrupt imported/restored artifact without weakening the append-only guards.
        var badId = Guid.NewGuid(); var badKey = Guid.NewGuid();
        var badResponse = recovered with { Id = badId, State = "Unknown", Fiscal = command with
            { IdempotencyKey = badKey, Outcome = "Unknown", Document = null, ExternalOperationReference = "corrupt-observation" } };
        db.Add(new BillingReportingEvidenceEntity { Id = badId, OrganizationId = seed.OrganizationId, Kind = "fiscal", State = "Unknown",
            IdempotencyKey = badKey, CommandSha256 = new('a', 64), ProfileId = recovered.Profile.Id, ProfileVersion = recovered.Profile.Version,
            ActorId = recovered.ActorId, IssuerKey = new('c', 64), ExternalOperationReference = "corrupt-observation", IsOperationRoot = true,
            RecordedAt = recovered.RecordedAt, PayloadJson = JsonSerializer.Serialize(badResponse, ReportingEvidencePolicy.Json), PayloadSha256 = new('0', 64) });
        await db.SaveChangesAsync();
        using (var corrupt = await client.SendAsync(StatementRequest(HttpMethod.Get, path + "/fiscal/" + badId, op)))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, corrupt.StatusCode); Assert.DoesNotContain("synthetic-customer", await corrupt.Content.ReadAsStringAsync()); }
        await using var migratorProvider = fixture.CreateServiceProvider(); await using var migratorScope = migratorProvider.CreateAsyncScope();
        Assert.Contains("history must be preserved", (await Assert.ThrowsAsync<PostgresException>(() => migratorScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
            .MigrateAsync("20261020054553_AddSliMonthlyReports"))).MessageText);
    }

    private sealed class EvidenceAuditFault(IAuditTrail inner, Func<bool> armed) : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput value, CancellationToken cancellationToken = default) =>
            value.Action == "reporting.evidence.recorded" && armed() ? throw new InvalidOperationException("Synthetic audit fault") : inner.RecordAsync(value, cancellationToken);
    }
    private sealed class EvidenceCommitReplyLoss : DbTransactionInterceptor
    {
        public bool Armed; public int Failures;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (Armed) { Armed = false; Failures++; throw new InvalidOperationException("Synthetic committed reply loss"); } return Task.CompletedTask; }
    }
    private sealed class EvidencePreciseClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero).AddTicks(1);
    }
}
