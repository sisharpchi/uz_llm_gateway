using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.Statements;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class StatementApiTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Month = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Statement_facade_rechecks_session_ReadBilling_and_recent_operator_Mfa_before_any_store_access()
    {
        var identity = new Identity(); var auth = new Authorization(); var read = new Reads(); var publication = new Publication();
        var service = Service(identity, auth, read, publication);
        await Assert.ThrowsAsync<StatementSessionRequiredException>(() => service.ListAsync("missing", Org, null, null, null, null, default));
        auth.Denied = true;
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => service.FindAsync("session", Org, Org, default));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => service.LinesAsync("session", Org, Org, null, null, default));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => service.ExportAsync("session", Org, Org, "csv", default));
        Assert.Equal(OrganizationPermission.ReadBilling, auth.LastPermission); Assert.Equal(Org, auth.LastOrg);
        var command = new CreateStatementRequest(Month, Org, 0, "Monthly statement");
        identity.Operator = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync("session", Org, command, default));
        identity.Operator = true; identity.Recent = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviseAsync("session", Org, Org, new(Org, 1, "Explicit revision"), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.JobAsync("session", Org, Org, default));
        Assert.Equal(0, read.Calls); Assert.Equal(0, publication.Calls);
    }

    [Fact]
    public async Task Statement_cursors_bind_tenant_filters_page_size_and_version_without_exposing_internal_sources()
    {
        var read = new Reads { Rows = [Metadata(Org, 2), Metadata(Guid.NewGuid(), 1)] };
        var service = Service(read: read);
        var page = await service.ListAsync("session", Org, null, null, 1, null, default);
        Assert.Equal(2, read.Take); Assert.Equal(2, Assert.Single(page.Items).Revision); Assert.NotNull(page.NextCursor);
        read.Rows = [Metadata(Guid.NewGuid(), 1)];
        var second = await service.ListAsync("session", Org, null, null, 1, page.NextCursor, default);
        Assert.Equal(Month, read.BeforePeriod); Assert.Equal(2, read.BeforeRevision); Assert.Null(second.NextCursor);
        foreach (var (org, from, limit, cursor) in new[] {
            (Guid.NewGuid(), (DateTimeOffset?)null, 1, page.NextCursor), (Org, (DateTimeOffset?)Month, 1, page.NextCursor),
            (Org, (DateTimeOffset?)null, 2, page.NextCursor), (Org, (DateTimeOffset?)null, 1, "broken") })
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("session", org, from, null, limit, cursor, default));
        read.LineRows = [new(1, Org, "UsageCharge", Month, "-5", "reservation", Org), new(2, Guid.NewGuid(), "Refund", Month.AddDays(1), "2", "refund", Org)];
        var lines = (await service.LinesAsync("session", Org, Org, 1, null, default))!;
        Assert.Equal(1, Assert.Single(lines.Items).Ordinal); Assert.NotNull(lines.NextCursor);
        read.LineRows = [read.LineRows[1]];
        var last = (await service.LinesAsync("session", Org, Org, 1, lines.NextCursor, default))!;
        Assert.Equal(1, read.AfterOrdinal); Assert.Null(last.NextCursor); Assert.Equal("2", Assert.Single(last.Items).AmountMicroUsd);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinesAsync("session", Org, Guid.NewGuid(), 1, lines.NextCursor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinesAsync("session", Guid.NewGuid(), Org, 1, lines.NextCursor, default));
        read.Found = false; Assert.Null(await service.LinesAsync("session", Org, Org, 1, null, default));
    }

    [Theory]
    [InlineData(null, 21, 51)]
    [InlineData(1, 2, 2)]
    [InlineData(100, 101, 101)]
    public async Task Statement_page_limits_preserve_defaults_and_inclusive_bounds(int? limit, int listTake, int lineTake)
    {
        var read = new Reads(); var service = Service(read: read);
        Assert.Empty((await service.ListAsync("session", Org, null, null, limit, null, default)).Items); Assert.Equal(listTake, read.Take);
        Assert.Empty((await service.LinesAsync("session", Org, Org, limit, null, default))!.Items); Assert.Equal(lineTake, read.Take);
    }

    [Theory]
    [InlineData(0)] [InlineData(101)] [InlineData(-1)]
    public async Task Statement_invalid_page_limits_are_rejected_before_queries(int limit)
    {
        var read = new Reads(); var service = Service(read: read);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("session", Org, null, null, limit, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinesAsync("session", Org, Org, limit, null, default)); Assert.Equal(0, read.Calls);
    }

    [Fact]
    public async Task Statement_month_ranges_and_initial_revision_generation_are_not_silently_coerced()
    {
        var publication = new Publication(); var read = new Reads(); var service = Service(read: read, publication: publication);
        foreach (var from in new[] { Month.AddDays(1), Month.ToOffset(TimeSpan.FromHours(1)) })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("session", Org, from, null, null, null, default));
            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("session", Org, new(from, Org, 0, "Monthly statement"), default));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("session", Org, Month, Month, null, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("session", Org, new(Month, Org, 1, "Monthly statement"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReviseAsync("session", Org, Org, new(Org, 0, "Explicit revision"), default));
        Assert.Equal(0, publication.Calls);
        read.Found = false;
        Assert.Null(await service.ReviseAsync("session", Org, Org, new(Org, 1, "Explicit revision"), default));
        read.Exists = false;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync("session", Org, new(Month, Org, 0, "Monthly statement"), default));
    }

    [Fact]
    public async Task Statement_export_validates_format_digest_and_audit_before_returning_version_bytes()
    {
        var read = new Reads(); var audit = new Audit(); var service = Service(read: read, audit: audit);
        var value = await service.ExportAsync("session", Org, Org, null, default);
        Assert.Equal(read.Csv.Content, value!.Content); Assert.Equal(1, audit.Calls);
        Assert.Equal("statement.export.requested", audit.Last!.Action); Assert.Equal(Org, audit.Last.ResourceId);
        Assert.DoesNotContain("Content", audit.Last.MetadataJson); Assert.DoesNotContain("session", audit.Last.MetadataJson);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync("session", Org, Org, "pdf", default));
        read.Csv = read.Csv with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<StatementArtifactIntegrityException>(() => service.ExportAsync("session", Org, Org, "csv", default));
        Assert.Equal(1, audit.Calls);
        read.Csv = new(new byte[32 * 1024 * 1024 + 1], new string('a', 64));
        var oversized = await Assert.ThrowsAsync<StatementBuildRejectedException>(() => service.ExportAsync("session", Org, Org, "csv", default));
        Assert.Equal("Bounds", oversized.Code); Assert.Equal(1, audit.Calls);
        read.Csv = new([], Convert.ToHexStringLower(SHA256.HashData([]))); audit.Fail = true;
        await Assert.ThrowsAsync<StatementReadDependencyException>(() => service.ExportAsync("session", Org, Org, "csv", default));
        audit.Fail = false; audit.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync("session", Org, Org, "csv", default));
        read.Found = false; Assert.Null(await service.ExportAsync("session", Org, Org, "csv", default));
    }

    [Theory]
    [InlineData("dependency", 503)] [InlineData("audit", 503)] [InlineData("integrity", 503)]
    [InlineData("deadline", 504)] [InlineData("conflict", 409)] [InlineData("bounds", 413)]
    [InlineData("mutation-audit", 503)]
    public async Task Statement_http_failures_return_safe_no_artifact_errors(string failure, int status)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(new StatementApiLimits(failure == "deadline" ? 20 : 30_000)).BuildServiceProvider();
        var builder = new ApplicationBuilder(services); builder.UseUzllmStatementApiBounds();
        builder.Run(async http =>
        {
            if (failure == "deadline") await Task.Delay(Timeout.Infinite, http.RequestAborted);
            throw failure switch {
                "audit" => new StatementReadDependencyException(new Exception("SECRET_PROVIDER_DATA")),
                "mutation-audit" => new InvalidOperationException("SECRET_PROVIDER_DATA"),
                "integrity" => new StatementArtifactIntegrityException(), "conflict" => new StatementCommandConflictException(),
                "bounds" => new StatementBuildRejectedException("Bounds"), _ => new TimeoutException("SECRET_PROVIDER_DATA") };
        });
        var context = new DefaultHttpContext { RequestServices = services }; context.Request.Method = "GET";
        context.Request.Path = $"/management/v1/organizations/{Org}/statements/{Org}/export"; context.Response.Body = new MemoryStream();
        await builder.Build()(context); Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0; var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("SECRET_PROVIDER_DATA", body); Assert.DoesNotContain("ordinal,ledger_id", body);
        Assert.Contains(status.ToString(), body); Assert.False(context.Response.Headers.ContainsKey("X-UZLLM-Report-Sha256"));
    }

    [Theory]
    [InlineData("Missing")] [InlineData("Queued")] [InlineData("Leased")] [InlineData("AwaitingReclaim")]
    [InlineData("Completed")] [InlineData("DeadLettered")]
    public async Task Statement_job_transport_states_do_not_override_operation_state_or_requeue(string state)
    {
        var publication = new Publication { Operation = new(Org, Org, Org, "Pending", 0, null, null) };
        var now = DateTimeOffset.UtcNow;
        var work = new Work { Progress = state == "Missing" ? null : new(Org, 5, 5, now,
            state == "Leased" ? now.AddHours(1) : state == "AwaitingReclaim" ? now.AddHours(-1) : null,
            state == "Completed" ? now : null, state == "DeadLettered" ? now : null) };
        var service = Service(publication: publication, work: work);
        var value = (await service.JobAsync("session", Org, Org, default))!;
        Assert.Equal(state, value.JobState); Assert.Equal("Pending", value.OperationStatus); Assert.Null(value.VersionId);
        Assert.Equal("billing.statement.build", work.Type); Assert.Equal(Org.ToString("N"), work.Key); Assert.Equal(0, publication.Calls);
        publication.Operation = null; work.Type = null;
        Assert.Null(await service.JobAsync("session", Org, Org, default)); Assert.Null(work.Type);
    }

    [Fact]
    public async Task Statement_client_disconnect_propagates_cancellation_without_artifact_or_success()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var builder = new ApplicationBuilder(services); builder.UseUzllmStatementApiBounds();
        builder.Run(http => { http.RequestAborted.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = canceled.Token };
        context.Request.Method = "GET"; context.Request.Path = $"/management/v1/organizations/{Org}/statements/{Org}/export";
        context.Response.Body = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.Build()(context));
        Assert.Equal(0, context.Response.Body.Length); Assert.False(context.Response.HasStarted);
        Assert.Equal(canceled.Token, context.RequestAborted);
    }

    private static StatementApiService Service(Identity? identity = null, Authorization? auth = null, Reads? read = null,
        Publication? publication = null, Audit? audit = null, Work? work = null) => new(identity ?? new(), auth ?? new(), read ?? new(),
            publication ?? new(), work ?? new(), audit ?? new(), new EphemeralDataProtectionProvider(), TimeProvider.System);
    private static StatementMetadataResponse Metadata(Guid id, long revision) => new(1, "account-statement-v1", id,
        "AccountStatement", "Not a tax invoice / No payment due from this statement", Org, new(Month, Month.AddMonths(1)), revision,
        revision == 1 ? null : Org, Month, new string('a', 64), "ReconciledSnapshot", new(0, 0, 0),
        new("0", "0", "0", "0", "0", "0", "0"), new("0", "0", "0", "0"), new("0", 0, 0, 0), 0, Month);

    private sealed class Reads : IStatementReadStore
    {
        public int Calls; public int Take; public long BeforeRevision; public DateTimeOffset BeforePeriod; public int AfterOrdinal;
        public bool Found = true; public bool Exists = true;
        public IReadOnlyList<StatementMetadataResponse> Rows = [];
        public IReadOnlyList<StatementLineResponse> LineRows = [];
        public StatementCsv Csv = new(Encoding.UTF8.GetBytes("safe persisted bytes"), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("safe persisted bytes"))));
        public Task<bool> OrganizationExistsAsync(Guid org, CancellationToken ct) { Calls++; return Task.FromResult(Exists); }
        public Task<IReadOnlyList<StatementMetadataResponse>> ListAsync(Guid org, DateTimeOffset from, DateTimeOffset to,
            DateTimeOffset before, long revision, int take, CancellationToken ct)
        { Calls++; Take = take; BeforePeriod = before; BeforeRevision = revision; return Task.FromResult(Rows); }
        public Task<StatementMetadataResponse?> FindAsync(Guid org, Guid id, CancellationToken ct)
        { Calls++; return Task.FromResult(Found ? Metadata(id, 1) : null); }
        public Task<IReadOnlyList<StatementLineResponse>> LinesAsync(Guid org, Guid id, int after, int take, CancellationToken ct)
        { Calls++; Take = take; AfterOrdinal = after; return Task.FromResult(LineRows); }
        public Task<StatementCsv?> CsvAsync(Guid org, Guid id, CancellationToken ct) { Calls++; return Task.FromResult(Found ? Csv : null); }
    }
    private sealed class Publication : IStatementPublicationStore
    {
        public int Calls;
        public StatementBuildOperation? Operation;
        public Task<StatementBuildOperation> RequestAsync(StatementBuildCommand command, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new StatementBuildOperation(Org, command.OrganizationId, Org, "Pending", command.ExpectedGeneration, null, null)); }
        public Task<StatementBuildOperation?> FindOperationAsync(Guid org, Guid id, CancellationToken ct = default) => Task.FromResult(Operation);
        public Task<PublishedStatement?> FindPublishedAsync(Guid org, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task BuildAsync(Guid id, int attempt, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Work : IOperationalWorkMonitor
    {
        public JobProgress? Progress; public string? Type; public string? Key;
        public Task<JobProgress?> FindJobAsync(string type, string key, CancellationToken ct = default)
        { Type = type; Key = key; return Task.FromResult(Progress); }
        public Task<IReadOnlyList<DeadLetteredWork>> ListDeadLettersAsync(int limit = 50, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Authorization : IOrganizationAuthorizationService
    {
        public bool Denied; public OrganizationPermission LastPermission; public Guid LastOrg;
        public Task EnsurePermissionAsync(Guid actor, Guid org, OrganizationPermission permission, Guid? project = null, CancellationToken cancellationToken = default)
        { LastPermission = permission; LastOrg = org; if (Denied) throw new TenantAccessDeniedException(); return Task.CompletedTask; }
        public Task EnsureOwnerAsync(Guid actor, Guid org, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>?> GetReadableProjectIdsAsync(Guid actor, Guid org, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Audit : IAuditTrail
    {
        public bool Fail; public bool Cancel; public int Calls; public AuditEventInput? Last;
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default)
        {
            Calls++; Last = input; if (Cancel) throw new OperationCanceledException(); if (Fail) throw new InvalidOperationException("audit unavailable");
            return Task.FromResult(new AuditEvent(Org, input.OrganizationId, input.ActorAccountId, input.Action,
                input.ResourceType, input.ResourceId, input.IpAddress, input.MetadataJson!, Month));
        }
    }
    private sealed class Identity : IIdentityService
    {
        public bool Operator = true; public bool Recent = true;
        public Task<AuthenticatedIdentity?> AuthenticateSessionAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(token == "session" ? new AuthenticatedIdentity(Org, "example@example.test", true, Operator) : null);
        public Task<bool> HasRecentOperatorReauthenticationAsync(string token, CancellationToken ct = default) => Task.FromResult(Recent);
        public Task<IdentityRegistration> RegisterAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> VerifyEmailAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BrowserSessionTokens?> AuthenticateAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeSessionAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> BeginPasswordRecoveryAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ResetPasswordAsync(string token, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task GrantOperatorAccessAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperatorMfaEnrollment> EnrollOperatorMfaAsync(Guid id, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ConfirmOperatorMfaAsync(string token, string code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> VerifyOperatorMfaAsync(string token, string code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ResetOperatorMfaAsync(string actor, Guid target, string reason, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
