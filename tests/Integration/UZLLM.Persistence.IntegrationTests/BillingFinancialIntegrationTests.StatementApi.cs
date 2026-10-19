using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.Statements;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    [Fact]
    public async Task Statement_api_build_revision_export_and_paging_preserve_exact_posting_month_and_old_bytes()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var app = await StatementHostAsync();
        using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var (owner, ownerId) = await StatementSessionAsync(services, seed.OrganizationId, "Owner");
        var (op, _) = await StatementSessionAsync(services, null, null, true);
        // Tenant creation intentionally initializes its empty wallet; do all setup before the read-only baseline.
        var otherOrg = Guid.NewGuid();
        db.Set<OrganizationEntity>().Add(new() { Id = otherOrg, Name = "Other authorized tenant", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
        db.Set<OrganizationMemberEntity>().Add(new() { OrganizationId = otherOrg, AccountId = ownerId, Role = "Owner", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await StatementCreditAsync(services, seed.OrganizationId, 10, Start.AddMonths(-1), "promotion");
        await StatementCreditAsync(services, seed.OrganizationId, 5, Start, "promotion");
        await StatementCreditAsync(services, seed.OrganizationId, 2, Start.AddDays(1), "promotion");
        await StatementCreditAsync(services, seed.OrganizationId, 7, Start.AddMonths(1), "promotion");
        var money = await StatementMoneyRowsAsync(db);
        var root = $"/management/v1/organizations/{seed.OrganizationId}/statements";
        var admin = $"/management/v1/admin/organizations/{seed.OrganizationId}/reports";
        Assert.Empty((await StatementBody<StatementPage>(client, HttpMethod.Get, root, owner)).Items);
        var command = new CreateStatementRequest(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid(), 0, "Monthly account statement");
        using var accepted = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + "/statements", op, command));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); AssertNoStore(accepted);
        var operation = (await accepted.Content.ReadFromJsonAsync<StatementJobResponse>())!;
        Assert.Equal(admin + "/jobs/" + operation.Id, accepted.Headers.Location!.OriginalString);
        Assert.Equal("Pending", operation.OperationStatus); Assert.Equal("Queued", operation.JobState);
        Assert.Equal(operation.Id, (await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + "/statements", op, command, HttpStatusCode.Accepted)).Id);
        using (var conflict = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + "/statements", op, command with { Reason = "Changed command terms" })))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await StatementBuildAndAckAsync(services, operation.Id);
        var published = await StatementBody<StatementJobResponse>(client, HttpMethod.Get, admin + "/jobs/" + operation.Id, op);
        Assert.Equal("Published", published.OperationStatus); Assert.Equal("Completed", published.JobState);
        var versionId = published.VersionId!.Value;
        var detail = await StatementBody<StatementMetadataResponse>(client, HttpMethod.Get, root + "/" + versionId, owner);
        Assert.Equal(1, detail.Generation); Assert.Equal(2, detail.LineCount);
        Assert.Equal("AccountStatement", detail.Kind); Assert.Equal("Not a tax invoice / No payment due from this statement", detail.Label);
        Assert.Equal("10", detail.Totals.OpeningMicroUsd); Assert.Equal("7", detail.Totals.MovementMicroUsd);
        Assert.Equal("17", detail.Totals.ClosingMicroUsd); Assert.Equal("7", detail.Totals.PostPeriodMovementMicroUsd);
        Assert.Equal("24", detail.Balance.PostedMicroUsd); Assert.Equal(command.PeriodStart, detail.Period.From);
        var monthFilter = "from=2026-09-01T00:00:00Z&to=2026-10-01T00:00:00Z";
        Assert.Equal(versionId, Assert.Single((await StatementBody<StatementPage>(client, HttpMethod.Get, root + "?" + monthFilter, owner)).Items).Id);
        Assert.Empty((await StatementBody<StatementPage>(client, HttpMethod.Get,
            root + "?from=2026-08-01T00:00:00Z&to=2026-09-01T00:00:00Z", owner)).Items);
        Assert.Empty((await StatementBody<StatementPage>(client, HttpMethod.Get,
            root + "?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z", owner)).Items);
        var augustCommand = command with { PeriodStart = command.PeriodStart.AddMonths(-1), IdempotencyKey = Guid.NewGuid() };
        var augustJob = await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + "/statements", op, augustCommand, HttpStatusCode.Accepted);
        await StatementBuildAndAckAsync(services, augustJob.Id);
        var augustVersion = (await StatementBody<StatementJobResponse>(client, HttpMethod.Get, admin + "/jobs/" + augustJob.Id, op)).VersionId!.Value;
        var first = await StatementBody<StatementLinePage>(client, HttpMethod.Get, root + $"/{versionId}/lines?limit=1", owner);
        Assert.Equal(1, Assert.Single(first.Items).Ordinal); Assert.NotNull(first.NextCursor);
        var last = await StatementBody<StatementLinePage>(client, HttpMethod.Get,
            root + $"/{versionId}/lines?limit=1&cursor=" + Uri.EscapeDataString(first.NextCursor!), owner);
        Assert.Equal(2, Assert.Single(last.Items).Ordinal); Assert.Null(last.NextCursor);
        byte[] original;
        using (var csv = await client.SendAsync(StatementRequest(HttpMethod.Get, root + $"/{versionId}/export?format=csv", owner)))
        {
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode); AssertNoStore(csv);
            original = await csv.Content.ReadAsByteArrayAsync();
            Assert.Equal(original.Length, csv.Content.Headers.ContentLength);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), csv.Headers.GetValues("X-UZLLM-Report-Sha256").Single());
            Assert.Equal("nosniff", csv.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Contains(versionId.ToString("N"), csv.Content.Headers.ContentDisposition!.FileName);
            Assert.StartsWith("\"Not a tax invoice / No payment due from this statement\"\r\nordinal,ledger_id,category,posted_at,amount_micro_usd\r\n", Encoding.UTF8.GetString(original));
            Assert.Equal(4, Encoding.UTF8.GetString(original).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        }
        var revision = new ReviseStatementRequest(Guid.NewGuid(), 1, "Include explicit late posting");
        Assert.Equal(money, await StatementMoneyRowsAsync(db));
        await StatementCreditAsync(services, seed.OrganizationId, 3, Start.AddDays(2), "promotion");
        money = await StatementMoneyRowsAsync(db);
        var revise = await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + $"/statements/{versionId}/revisions", op, revision, HttpStatusCode.Accepted);
        await StatementBuildAndAckAsync(services, revise.Id);
        var nextVersion = (await StatementBody<StatementJobResponse>(client, HttpMethod.Get, admin + "/jobs/" + revise.Id, op)).VersionId!.Value;
        var page = await StatementBody<StatementPage>(client, HttpMethod.Get, root + "?limit=1", owner);
        Assert.Equal(nextVersion, Assert.Single(page.Items).Id); Assert.NotNull(page.NextCursor);
        var page2 = await StatementBody<StatementPage>(client, HttpMethod.Get, root + "?limit=1&cursor=" + Uri.EscapeDataString(page.NextCursor!), owner);
        Assert.Equal(versionId, Assert.Single(page2.Items).Id); Assert.NotNull(page2.NextCursor);
        var page3 = await StatementBody<StatementPage>(client, HttpMethod.Get, root + "?limit=1&cursor=" + Uri.EscapeDataString(page2.NextCursor!), owner);
        Assert.Equal(augustVersion, Assert.Single(page3.Items).Id); Assert.Null(page3.NextCursor);
        Assert.Equal(2, (await StatementBody<StatementPage>(client, HttpMethod.Get, root + "?" + monthFilter, owner)).Items.Count);
        using (var old = await client.SendAsync(StatementRequest(HttpMethod.Get, root + $"/{versionId}/export", owner)))
            Assert.Equal(original, await old.Content.ReadAsByteArrayAsync());
        Assert.Equal(revise.Id, (await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + $"/statements/{versionId}/revisions", op, revision, HttpStatusCode.Accepted)).Id);
        using (var stale = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + $"/statements/{versionId}/revisions", op, revision with { IdempotencyKey = Guid.NewGuid() })))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var wrongVersion = await client.SendAsync(StatementRequest(HttpMethod.Get,
            root + $"/{nextVersion}/lines?limit=1&cursor=" + Uri.EscapeDataString(first.NextCursor!), owner)))
            Assert.Equal(HttpStatusCode.BadRequest, wrongVersion.StatusCode);
        using (var wrongFilter = await client.SendAsync(StatementRequest(HttpMethod.Get,
            root + "?limit=1&from=2026-09-01T00:00:00Z&cursor=" + Uri.EscapeDataString(page.NextCursor!), owner)))
            Assert.Equal(HttpStatusCode.BadRequest, wrongFilter.StatusCode);
        var otherRoot = $"/management/v1/organizations/{otherOrg}/statements";
        Assert.Empty((await StatementBody<StatementPage>(client, HttpMethod.Get, otherRoot, owner)).Items);
        foreach (var suffix in new[] { $"/{versionId}", $"/{versionId}/lines", $"/{versionId}/export" })
        {
            using var alien = await client.SendAsync(StatementRequest(HttpMethod.Get, otherRoot + suffix, owner));
            Assert.Equal(HttpStatusCode.NotFound, alien.StatusCode); AssertNoStore(alien);
        }
        using (var alienCursor = await client.SendAsync(StatementRequest(HttpMethod.Get,
            otherRoot + "?limit=1&cursor=" + Uri.EscapeDataString(page.NextCursor!), owner)))
            Assert.Equal(HttpStatusCode.BadRequest, alienCursor.StatusCode);
        using (var alienRevision = await client.SendAsync(StatementRequest(HttpMethod.Post,
            $"/management/v1/admin/organizations/{otherOrg}/reports/statements/{versionId}/revisions", op, revision)))
            Assert.Equal(HttpStatusCode.NotFound, alienRevision.StatusCode);
        var audit = await db.Set<AuditEventEntity>().Where(value => value.Action == "statement.export.requested").ToListAsync();
        Assert.Equal(2, audit.Count); Assert.All(audit, value => Assert.Equal(ownerId, value.ActorAccountId));
        Assert.Equal(money, await StatementMoneyRowsAsync(db));
        var json = JsonSerializer.Serialize(detail);
        foreach (var forbidden in new[] { "SnapshotJson", "PaymentAnnotations", "RequestedByAccountId", "Reason", "ProviderCost", "MetadataJson" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Statement_api_enforces_roles_tenant_ids_operator_Mfa_Csrf_and_body_bounds()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var app = await StatementHostAsync(); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var root = $"/management/v1/organizations/{seed.OrganizationId}/statements";
        var admin = $"/management/v1/admin/organizations/{seed.OrganizationId}/reports";
        var (owner, _) = await StatementSessionAsync(services, seed.OrganizationId, "Owner");
        var (op, opId) = await StatementSessionAsync(services, null, null, true, false);
        var request = new CreateStatementRequest(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid(), 0, "Monthly account statement");
        foreach (var role in new[] { "Owner", "Admin", "BillingViewer", "Developer", "ReadOnly" })
        {
            var (session, accountId) = await StatementSessionAsync(services, seed.OrganizationId, role);
            if (role is "Developer" or "ReadOnly")
            {
                var db = services.GetRequiredService<FoundationDbContext>();
                db.Set<ProjectGrantEntity>().Add(new() { OrganizationId = seed.OrganizationId, AccountId = accountId,
                    ProjectId = seed.ProjectId, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
            }
            using var read = await client.SendAsync(StatementRequest(HttpMethod.Get, root, session)); AssertNoStore(read);
            Assert.Equal(role is "Developer" or "ReadOnly" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, read.StatusCode);
            foreach (var suffix in new[] { "/" + Guid.NewGuid(), $"/{Guid.NewGuid()}/lines", $"/{Guid.NewGuid()}/export" })
            {
                using var resource = await client.SendAsync(StatementRequest(HttpMethod.Get, root + suffix, session)); AssertNoStore(resource);
                Assert.Equal(role is "Developer" or "ReadOnly" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, resource.StatusCode);
            }
        }
        using (var anonymous = await client.GetAsync(root)) { Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); AssertNoStore(anonymous); }
        using (var bearer = new HttpRequestMessage(HttpMethod.Get, root))
        { bearer.Headers.Authorization = new("Bearer", "uzllm_not_a_session"); using var response = await client.SendAsync(bearer); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        foreach (var session in new[] { owner, op })
        {
            using var denied = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + "/statements", session, request));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); AssertNoStore(denied);
        }
        await StatementConfirmMfaAsync(services, opId, op);
        foreach (var (path, body) in new (string, object)[] {
            (admin + "/statements", request),
            (admin + $"/statements/{Guid.NewGuid()}/revisions", new ReviseStatementRequest(Guid.NewGuid(), 1, "Explicit revision request")) })
        {
            using (var noCsrf = await client.SendAsync(StatementRequest(HttpMethod.Post, path, op, body, false)))
            { Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode); AssertNoStore(noCsrf); }
            using var wrongProof = StatementRequest(HttpMethod.Post, path, op, body);
            wrongProof.Headers.Remove(IdentityCookieNames.CsrfHeader);
            wrongProof.Headers.Add(IdentityCookieNames.CsrfHeader, "incorrect-proof");
            using var denied = await client.SendAsync(wrongProof);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); AssertNoStore(denied);
        }
        foreach (var missing in new[] { "periodStart", "idempotencyKey", "expectedGeneration", "reason" })
        {
            var json = JsonSerializer.SerializeToNode(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            Assert.True(json.Remove(missing));
            using var omitted = StatementRequest(HttpMethod.Post, admin + "/statements", op);
            omitted.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(omitted); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); AssertNoStore(response);
        }
        using (var extra = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + "/statements", op,
            new { request.PeriodStart, request.IdempotencyKey, request.ExpectedGeneration, request.Reason, amountMicroUsd = "1" })))
            Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        using (var media = StatementRequest(HttpMethod.Post, admin + "/statements", op))
        {
            media.Content = new StringContent("plain-text", Encoding.UTF8, "text/plain");
            using var response = await client.SendAsync(media); Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode); AssertNoStore(response);
        }
        foreach (var size in new[] { 8192, 8193 })
        {
            using var oversized = StatementRequest(HttpMethod.Post, admin + "/statements", op);
            oversized.Content = new StringContent(new string(' ', size), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(oversized); AssertNoStore(response);
            Assert.Equal(size == 8192 ? HttpStatusCode.BadRequest : HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        using (var chunked = StatementRequest(HttpMethod.Post, admin + "/statements", op))
        {
            chunked.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string(' ', 8193))));
            chunked.Content.Headers.ContentType = new("application/json"); chunked.Headers.TransferEncodingChunked = true;
            using var response = await client.SendAsync(chunked); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        foreach (var suffix in new[] { "?limit=0", "?limit=101", "?cursor=broken", "?from=2026-09-02T00:00:00Z", "?from=2026-09-01T01:00:00%2B01:00" })
        { using var invalid = await client.SendAsync(StatementRequest(HttpMethod.Get, root + suffix, owner)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using (var invalid = await client.SendAsync(StatementRequest(HttpMethod.Post, admin + "/statements", op, request with { SchemaVersion = 2 })))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var missingOrg = await client.SendAsync(StatementRequest(HttpMethod.Post,
            $"/management/v1/admin/organizations/{Guid.NewGuid()}/reports/statements", op, request)))
            Assert.Equal(HttpStatusCode.NotFound, missingOrg.StatusCode);
        using (var alienJob = await client.SendAsync(StatementRequest(HttpMethod.Get, admin + "/jobs/" + Guid.NewGuid(), op)))
            Assert.Equal(HttpStatusCode.NotFound, alienJob.StatusCode);
        var facade = services.GetRequiredService<IStatementApiService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => facade.CreateAsync(owner.SessionToken, seed.OrganizationId, request, default));
        await Assert.ThrowsAsync<StatementSessionRequiredException>(() => facade.ListAsync("", seed.OrganizationId, null, null, null, null, default));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => facade.ListAsync(owner.SessionToken, seed.OrganizationId, null, null, null, null, canceled.Token));
        Assert.Equal(0, await services.GetRequiredService<FoundationDbContext>().Set<BillingStatementReceiptEntity>().CountAsync());
    }

    [Fact]
    public async Task Statement_job_exposes_dead_letter_even_when_crash_left_operation_pending_without_restarting_work()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var app = await StatementHostAsync(); using var client = StatementClient(app);
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var (op, _) = await StatementSessionAsync(services, null, null, true);
        var admin = $"/management/v1/admin/organizations/{seed.OrganizationId}/reports";
        var command = new CreateStatementRequest(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid(), 0, "Monthly account statement");
        var accepted = await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + "/statements", op, command, HttpStatusCode.Accepted);
        var jobs = services.GetRequiredService<ILeasedJobStore>();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var job = Assert.Single(await jobs.ClaimAvailableAsync("crashed-builder", 10, TimeSpan.FromMinutes(1)));
            Assert.Equal(attempt, job.AttemptCount);
            Assert.True(await jobs.MarkFailedAsync(job.Id, "crashed-builder", attempt, TimeSpan.Zero, "Dependency"));
        }
        var terminal = await StatementBody<StatementJobResponse>(client, HttpMethod.Get, admin + "/jobs/" + accepted.Id, op);
        Assert.Equal("Pending", terminal.OperationStatus); Assert.Equal("DeadLettered", terminal.JobState);
        Assert.Equal(5, terminal.Job!.AttemptCount); Assert.NotNull(terminal.Job.DeadLetteredAt);
        var replay = await StatementBody<StatementJobResponse>(client, HttpMethod.Post, admin + "/statements", op, command, HttpStatusCode.Accepted);
        Assert.Equal(accepted.Id, replay.Id); Assert.Equal("DeadLettered", replay.JobState);
        Assert.Empty(await jobs.ClaimAvailableAsync("must-not-restart", 10, TimeSpan.FromMinutes(1)));
        using var crossTenant = await client.SendAsync(StatementRequest(HttpMethod.Get,
            $"/management/v1/admin/organizations/{Guid.NewGuid()}/reports/jobs/{accepted.Id}", op));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    private async Task<WebApplication> StatementHostAsync()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration); builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit(); builder.Services.AddUzllmBilling(); builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmPayments(builder.Configuration); builder.Services.AddUzllmStatementPublication(); builder.Services.AddUzllmStatementApi();
        var app = builder.Build(); app.UseUzllmManagementNoStore(); app.UseUzllmStatementApiBounds(); app.UseUzllmManagementSession();
        app.MapUzllmStatementEndpoints(); await app.StartAsync(); return app;
    }
    private static HttpClient StatementClient(WebApplication app) => new() { BaseAddress = new(app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    private static async Task<(BrowserSessionTokens, Guid)> StatementSessionAsync(IServiceProvider services, Guid? org, string? role,
        bool isOperator = false, bool mfa = true)
    {
        var identity = services.GetRequiredService<IIdentityService>(); var email = $"report-{Guid.NewGuid():N}@example.test";
        var registered = await identity.RegisterAsync(email, "correct horse battery staple"); Assert.True(await identity.VerifyEmailAsync(registered.VerificationToken));
        if (isOperator) await identity.GrantOperatorAccessAsync(registered.AccountId);
        var session = (await identity.AuthenticateAsync(email, "correct horse battery staple"))!;
        if (org is { } orgId)
        {
            var db = services.GetRequiredService<FoundationDbContext>();
            db.Set<OrganizationMemberEntity>().Add(new() { OrganizationId = orgId, AccountId = registered.AccountId, Role = role!, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        if (isOperator && mfa) await StatementConfirmMfaAsync(services, registered.AccountId, session);
        return (session, registered.AccountId);
    }
    private static async Task StatementConfirmMfaAsync(IServiceProvider services, Guid actor, BrowserSessionTokens session)
    {
        var identity = services.GetRequiredService<IIdentityService>();
        var enrollment = await identity.EnrollOperatorMfaAsync(actor, "correct horse battery staple");
        var code = new TotpAuthenticator().CreateCode(enrollment.SharedSecret, services.GetRequiredService<TimeProvider>().GetUtcNow());
        Assert.True(await identity.ConfirmOperatorMfaAsync(session.SessionToken, code));
    }
    private static HttpRequestMessage StatementRequest(HttpMethod method, string path, BrowserSessionTokens session, object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body); return request;
    }
    private static async Task<T> StatementBody<T>(HttpClient client, HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.SendAsync(StatementRequest(method, path, session, body));
        Assert.Equal(expected, response.StatusCode); AssertNoStore(response); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static void AssertNoStore(HttpResponseMessage response) => Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    private static Task<List<string>> StatementMoneyRowsAsync(FoundationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT source AS "Value" FROM (
          SELECT 'wallet:' || r::text AS source FROM billing.wallet r
          UNION ALL SELECT 'ledger:' || r::text FROM billing.ledger_entry r
          UNION ALL SELECT 'reservation:' || r::text FROM billing.reservation r
          UNION ALL SELECT 'settlement:' || r::text FROM billing.settlement r
          UNION ALL SELECT 'refund:' || r::text FROM billing.settlement_refund r
          UNION ALL SELECT 'budget:' || r::text FROM billing.budget_bucket r
          UNION ALL SELECT 'debt:' || r::text FROM billing.debt_entry r
          UNION ALL SELECT 'recovery:' || r::text FROM billing.recovery_debt r
          UNION ALL SELECT 'reversal:' || r::text FROM billing.reversal r
          UNION ALL SELECT 'adjustment:' || r::text FROM billing.wallet_adjustment r
          UNION ALL SELECT 'payment:' || r::text FROM payment.payment_intent r
          UNION ALL SELECT 'usage:' || r::text FROM usage.evidence r
        ) sources ORDER BY source
        """).ToListAsync();
    private static async Task StatementBuildAndAckAsync(IServiceProvider services, Guid operationId)
    {
        var jobs = services.GetRequiredService<ILeasedJobStore>();
        var job = Assert.Single(await jobs.ClaimAvailableAsync("statement-http", 20, TimeSpan.FromMinutes(1)), value => value.JobType == "billing.statement.build");
        await services.GetRequiredService<IStatementPublicationStore>().BuildAsync(operationId, job.AttemptCount);
        Assert.True(await jobs.MarkCompletedAsync(job.Id, "statement-http", job.AttemptCount));
    }
}
