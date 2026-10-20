using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using UZLLM.Management.Api;
using UZLLM.Management.Api.SliReports;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Observability;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class SliReportIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Month = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static SliReportScope Profile()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new("sli-" + suffix, "test", "Chat", "All", [new("edge-" + suffix, "Edge"), new("gateway-" + suffix, "Gateway"), new("probe-" + suffix, "Probe")]);
    }
    private ServiceProvider Provider(SliReportScope profile, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
        var services = new ServiceCollection().AddLogging().AddUzllmPersistence(configuration).AddUzllmIdentity()
            .AddUzllmAudit().AddUzllmUsage().AddUzllmSliReports(configuration);
        services.AddSingleton(new SliReportProfiles(new Dictionary<string, SliReportScope> { [profile.Id] = profile }));
        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }
    private static async Task<Guid> ActorAsync(IServiceProvider services) => (await services.GetRequiredService<IIdentityService>()
        .RegisterAsync($"sli-{Guid.NewGuid():N}@example.test", "correct horse battery staple")).AccountId;
    private static SliReportCommand Command(Guid actor, SliReportScope profile) => new(actor, profile.Id, Month, Guid.NewGuid(), 0, null, "Monthly operational evidence");

    [Fact]
    public async Task Parallel_commands_and_worker_replay_publish_one_immutable_version_receipt_audit_and_outbox()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile); await using var scope = provider.CreateAsyncScope();
        var actor = await ActorAsync(scope.ServiceProvider); var command = Command(actor, profile);
        var money = await MoneyAsync(scope.ServiceProvider.GetRequiredService<FoundationDbContext>());
        var operations = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => {
            await using var worker = provider.CreateAsyncScope(); return await worker.ServiceProvider.GetRequiredService<ISliReportStore>().RequestAsync(command); }));
        Assert.Single(operations.Select(value => value.Id).Distinct()); var id = operations[0].Id;
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            await using var worker = provider.CreateAsyncScope(); await worker.ServiceProvider.GetRequiredService<ISliReportStore>().BuildAsync(id, 1); }));
        var store = scope.ServiceProvider.GetRequiredService<ISliReportStore>(); var op = (await store.OperationAsync(id))!;
        Assert.Equal("Published", op.Status); Assert.NotNull(op.VersionId);
        Assert.Equal("NoData", (await store.FindAsync(op.VersionId.Value))!.Metadata.TargetStatus);
        Assert.Equal(id, (await store.RequestAsync(command)).Id);
        await Assert.ThrowsAsync<SliReportConflictException>(() => store.RequestAsync(command with { Reason = "Changed immutable terms" }));
        await Assert.ThrowsAsync<SliReportConflictException>(() => store.RequestAsync(command with { IdempotencyKey = Guid.NewGuid() }));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<SliReportVersionEntity>().CountAsync(value => value.OperationId == id));
        Assert.Equal(1, await db.Set<SliReportReceiptEntity>().CountAsync(value => value.OperationId == id));
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit.audit_event WHERE resource_id={id} OR resource_id={op.VersionId}").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ops.outbox WHERE event_type='usage.sli.published' AND payload::jsonb->>'operationId'={id.ToString()}").SingleAsync());
        var eventId = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM ops.outbox WHERE event_type='usage.sli.published' AND payload::jsonb->>'operationId'={id.ToString()}").SingleAsync();
        var lifecycle = servicesHandler(scope.ServiceProvider);
        var message = new OutboxMessage(eventId, lifecycle.EventType, "{}", DateTimeOffset.UtcNow, 1, 10);
        await lifecycle.HandleAsync(message, default); await lifecycle.HandleAsync(message, default);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ops.consumer_inbox WHERE consumer='sli-publications' AND event_id={eventId}").SingleAsync());
        Assert.Equal(money, await MoneyAsync(db));
        static IOutboxHandler servicesHandler(IServiceProvider services) => services.GetServices<IOutboxHandler>().Single(value => value.EventType == "usage.sli.published");
    }

    [Fact]
    public async Task Late_terminal_revision_uses_start_month_keeps_original_bytes_and_survives_raw_expiry()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile); await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var actor = await ActorAsync(services); var store = services.GetRequiredService<ISliReportStore>(); var db = services.GetRequiredService<FoundationDbContext>();
        var execution = Guid.NewGuid(); var started = Month.AddMonths(1).AddSeconds(-2); var epoch = Guid.NewGuid();
        var edge = Observation(execution, ObservationPhase.Terminal, started, started.AddSeconds(3));
        await ImportAsync(services, profile.Producers[0], Guid.NewGuid(), 1, 1, edge.At, [edge]);
        await ImportAsync(services, profile.Producers[1], epoch, 1, 1, started, [Observation(execution, ObservationPhase.Started, started, started)]);
        var first = await store.RequestAsync(Command(actor, profile)); await store.BuildAsync(first.Id, 1);
        var oldId = (await store.OperationAsync(first.Id))!.VersionId!.Value; var old = (await store.FindAsync(oldId))!;
        Assert.Equal("U", Assert.Single(old.Executions).Class);
        var oldBytes = (await db.Set<SliReportVersionEntity>().AsNoTracking().SingleAsync(value => value.Id == oldId)).Artifact;
        await ImportAsync(services, profile.Producers[1], epoch, 2, 2, edge.At,
            [Observation(execution, ObservationPhase.Terminal, started, edge.At) with { TerminalSuccess = true }]);
        var revisedCommand = Command(actor, profile) with { ExpectedGeneration = 1, PredecessorId = oldId };
        var revised = await store.RequestAsync(revisedCommand); await store.BuildAsync(revised.Id, 1);
        var current = (await store.FindAsync((await store.OperationAsync(revised.Id))!.VersionId!.Value))!;
        Assert.Equal(2, current.Metadata.Revision); Assert.Equal(oldId, current.Metadata.PredecessorId);
        Assert.Equal("H", Assert.Single(current.Executions).Class); Assert.Equal(started, current.Executions[0].StartedAt);
        Assert.NotEqual(old.Metadata.SourceDigest, current.Metadata.SourceDigest);
        Assert.Equal(oldBytes, (await db.Set<SliReportVersionEntity>().AsNoTracking().SingleAsync(value => value.Id == oldId)).Artifact);
        await new PostgreSqlRetentionSweep(db, new RetentionSchedule("synthetic-approved-sli", 1, 90), TimeProvider.System).SweepAsync(1000);
        Assert.NotEqual(0, await db.Set<SliImportReceiptEntity>().CountAsync(value => value.Status == "Expired"));
        Assert.Equal("U", (await store.FindAsync(oldId))!.Executions[0].Class);
        Assert.Equal("H", (await store.FindAsync(current.Metadata.Id))!.Executions[0].Class);
        foreach (var sql in new[] { "UPDATE ops.sli_report_version SET policy_version=policy_version WHERE id={0}",
            "DELETE FROM ops.sli_report_version WHERE id={0}", "DELETE FROM ops.sli_report_source WHERE version_id={0}",
            "UPDATE ops.sli_report_operation SET reason=reason WHERE version_id={0}" })
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, oldId));
        var missing = await store.RequestAsync(Command(actor, profile) with { ExpectedGeneration = 2, PredecessorId = current.Metadata.Id });
        await store.BuildAsync(missing.Id, 1);
        var expired = (await store.FindAsync((await store.OperationAsync(missing.Id))!.VersionId!.Value))!;
        Assert.Equal("NoData", expired.Metadata.Coverage); Assert.Contains("RawEvidenceMissing", expired.Metadata.Warnings);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    [InlineData("bytes")]
    [InlineData("facts")]
    public async Task Publication_failure_leaves_no_partial_report_or_financial_effect_and_retries_same_operation(string failure)
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile, services => {
            if (failure == "audit") services.AddScoped<IAuditTrail>(sp => new PublicationAuditFault(new UZLLM.Modules.Audit.Application.AuditTrail(sp.GetRequiredService<IAuditEventStore>(), sp.GetRequiredService<TimeProvider>())));
            if (failure == "bytes") services.AddSingleton(new SliReportLimits(MaximumBytes: 1));
            if (failure == "facts") services.AddSingleton(new SliReportLimits(MaximumFacts: 1));
        });
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var actor = await ActorAsync(services); var store = services.GetRequiredService<ISliReportStore>();
        var operation = await store.RequestAsync(Command(actor, profile));
        if (failure == "facts") await ImportAsync(services, profile.Producers[0], Guid.NewGuid(), 1, 1, Month.AddMinutes(1),
            [Observation(Guid.NewGuid(), ObservationPhase.Terminal, Month, Month.AddMinutes(1))]);
        await using var owner = fixture.CreateServiceProvider(); await using var ownerScope = owner.CreateAsyncScope();
        var ddl = ownerScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var db = services.GetRequiredService<FoundationDbContext>(); var money = await MoneyAsync(db);
        if (failure == "outbox") await ddl.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION ops.sli_report_outbox_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.event_type='usage.sli.published' THEN RAISE EXCEPTION 'synthetic outbox fault'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER sli_report_outbox_fault BEFORE INSERT ON ops.outbox FOR EACH ROW EXECUTE FUNCTION ops.sli_report_outbox_fault();
            """);
        try
        {
            var failureResult = await Record.ExceptionAsync(() => store.BuildAsync(operation.Id, 1));
            if (failure == "audit") Assert.IsType<IOException>(failureResult);
            else if (failure == "outbox") Assert.IsType<DbUpdateException>(failureResult);
            else Assert.Equal("Bounds", Assert.IsType<SliReportRejectedException>(failureResult).Code);
            db.ChangeTracker.Clear();
            Assert.False(await db.Set<SliReportVersionEntity>().AnyAsync(value => value.OperationId == operation.Id));
            Assert.Equal("Failed", (await store.OperationAsync(operation.Id))!.Status);
            Assert.Equal("Failed", (await db.Set<SliReportAttemptEntity>().SingleAsync(value => value.OperationId == operation.Id)).Outcome);
            Assert.Equal(money, await MoneyAsync(db));
        }
        finally { if (failure == "outbox") await ddl.Database.ExecuteSqlRawAsync("DROP TRIGGER sli_report_outbox_fault ON ops.outbox; DROP FUNCTION ops.sli_report_outbox_fault();"); }
        await using var recovery = Provider(profile); await using var retry = recovery.CreateAsyncScope(); var recovered = retry.ServiceProvider.GetRequiredService<ISliReportStore>();
        await recovered.BuildAsync(operation.Id, 2); Assert.Equal("Published", (await recovered.OperationAsync(operation.Id))!.Status);
    }

    [Fact]
    public async Task Real_HTTP_operator_MFA_CSRF_scope_and_immutable_reads_preserve_no_store_and_job_identity()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration).AddUzllmIdentity().AddUzllmAudit().AddUzllmUsage()
            .AddUzllmSliReports(builder.Configuration).AddUzllmSliReportApi();
        builder.Services.AddSingleton(new SliReportProfiles(new Dictionary<string, SliReportScope> { [profile.Id] = profile }));
        await using var app = builder.Build(); app.UseUzllmManagementNoStore(); app.UseUzllmSliReportApiBounds(); app.UseUzllmManagementSession();
        app.MapUzllmSliReportEndpoints(); await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider; var identity = services.GetRequiredService<IIdentityService>();
        var email = $"sli-http-{Guid.NewGuid():N}@example.test"; var registration = await identity.RegisterAsync(email, "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var session = (await identity.AuthenticateAsync(email, "correct horse battery staple"))!;
        const string path = "/management/v1/admin/sli-reports";
        using (var anonymous = await client.GetAsync(path + "?scopeId=" + profile.Id)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var ordinary = await client.SendAsync(Request(HttpMethod.Get, path + "?scopeId=" + profile.Id, session))) Assert.Equal(HttpStatusCode.Forbidden, ordinary.StatusCode);
        await identity.GrantOperatorAccessAsync(registration.AccountId);
        using (var noMfa = await client.SendAsync(Request(HttpMethod.Get, path + "?scopeId=" + profile.Id, session))) Assert.Equal(HttpStatusCode.Forbidden, noMfa.StatusCode);
        var enrollment = await identity.EnrollOperatorMfaAsync(registration.AccountId, "correct horse battery staple");
        Assert.True(await identity.ConfirmOperatorMfaAsync(session.SessionToken, new TotpAuthenticator().CreateCode(enrollment.SharedSecret, TimeProvider.System.GetUtcNow())));
        var command = new CreateSliReportRequest(profile.Id, Month, Guid.NewGuid(), 0, "Monthly operational evidence");
        using (var csrf = await client.SendAsync(Request(HttpMethod.Post, path, session, command, csrf: false))) Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        using var accepted = await client.SendAsync(Request(HttpMethod.Post, path, session, command));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); Assert.Equal("no-store", accepted.Headers.CacheControl!.ToString());
        var operation = (await accepted.Content.ReadFromJsonAsync<SliReportJobResponse>())!;
        Assert.Equal(path + "/jobs/" + operation.Operation.Id, accepted.Headers.Location!.ToString());
        await services.GetRequiredService<ISliReportStore>().BuildAsync(operation.Operation.Id, 1);
        using var jobResponse = await client.SendAsync(Request(HttpMethod.Get, accepted.Headers.Location.ToString(), session));
        var job = (await jobResponse.Content.ReadFromJsonAsync<SliReportJobResponse>())!; Assert.Equal("Published", job.Operation.Status);
        using var read = await client.SendAsync(Request(HttpMethod.Get, path + "/" + job.Operation.VersionId, session));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.Equal("no-store", read.Headers.CacheControl!.ToString());
        Assert.Equal("NoData", (await read.Content.ReadFromJsonAsync<SliReportResponse>())!.Metadata.TargetStatus);
        using var replay = await client.SendAsync(Request(HttpMethod.Post, path, session, command));
        Assert.Equal(operation.Operation.Id, (await replay.Content.ReadFromJsonAsync<SliReportJobResponse>())!.Operation.Id);
        using var invalid = await client.SendAsync(Request(HttpMethod.Get, path + "?scopeId=unregistered", session)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missing = await client.SendAsync(Request(HttpMethod.Get, path + "/" + Guid.NewGuid(), session)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var tooLarge = Request(HttpMethod.Post, path, session); tooLarge.Content = new StringContent(new string('x', 8193));
        using var bounded = await client.SendAsync(tooLarge); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bounded.StatusCode);
    }

    private static ExecutionObservation Observation(Guid id, ObservationPhase phase, DateTimeOffset start, DateTimeOffset at) =>
        new(1, id, phase, ObservationOperation.Chat, ObservationMode.Sse, at, start, true, 200, false, false, false, false, ObservationProviderFault.None, ObservationCredential.Managed);

    [Fact]
    public async Task Repeatable_read_freezes_membership_while_a_late_manifest_commits_and_revision_discovers_it()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = Provider(profile, services => services.AddScoped<ISliReportSourceReader>(sp => new SnapshotBarrier(
            new PostgreSqlSliReportSourceReader(sp.GetRequiredService<FoundationDbContext>(), TimeProvider.System, new()), entered, release)));
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider; var actor = await ActorAsync(services);
        var store = services.GetRequiredService<ISliReportStore>(); var operation = await store.RequestAsync(Command(actor, profile));
        var build = store.BuildAsync(operation.Id, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var other = provider.CreateAsyncScope();
            await ImportAsync(other.ServiceProvider, profile.Producers[0], Guid.NewGuid(), 1, 1, Month.AddMinutes(1),
                [Observation(Guid.NewGuid(), ObservationPhase.Terminal, Month, Month.AddMinutes(1))]);
        }
        finally { release.TrySetResult(); }
        await build;
        var prior = (await store.FindAsync((await store.OperationAsync(operation.Id))!.VersionId!.Value))!;
        Assert.Empty(prior.Sources); Assert.Equal("NoData", prior.Metadata.Coverage);
        var revision = await store.RequestAsync(Command(actor, profile) with { ExpectedGeneration = 1, PredecessorId = prior.Metadata.Id });
        await store.BuildAsync(revision.Id, 1);
        Assert.Single((await store.FindAsync((await store.OperationAsync(revision.Id))!.VersionId!.Value))!.Sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_deadline_and_client_cancellation_preserve_failed_attempt_and_no_published_version(bool clientCancel)
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = Provider(profile, services => {
            services.AddSingleton(new SliReportLimits(DeadlineMilliseconds: clientCancel ? 30000 : 500));
            services.AddScoped<ISliReportSourceReader>(_ => new WaitingReader(entered)); });
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ISliReportStore>(); var actor = await ActorAsync(services); var operation = await store.RequestAsync(Command(actor, profile));
        using var canceled = new CancellationTokenSource(); var build = store.BuildAsync(operation.Id, 1, canceled.Token);
        if (clientCancel) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); canceled.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
        Assert.Equal(clientCancel ? "Canceled" : "Timeout", (await store.OperationAsync(operation.Id))!.FailureKind);
        Assert.False(await services.GetRequiredService<FoundationDbContext>().Set<SliReportVersionEntity>().AnyAsync(value => value.OperationId == operation.Id));
    }

    [Fact]
    public async Task Request_audit_failure_rolls_back_command_head_receipt_and_job()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile, services => services.AddScoped<IAuditTrail>(sp => new RequestAuditFault(
            new UZLLM.Modules.Audit.Application.AuditTrail(sp.GetRequiredService<IAuditEventStore>(), TimeProvider.System))));
        await using var scope = provider.CreateAsyncScope(); var actor = await ActorAsync(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<ISliReportStore>();
        await Assert.ThrowsAsync<IOException>(() => store.RequestAsync(Command(actor, profile)));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); db.ChangeTracker.Clear();
        Assert.False(await db.Set<SliReportHeadEntity>().AnyAsync(value => value.ScopeId == profile.Id));
    }

    private sealed class SnapshotBarrier(ISliReportSourceReader inner, TaskCompletionSource entered, TaskCompletionSource release) : ISliReportSourceReader
    {
        public async Task<SliReportSnapshot> ReadAsync(SliReportScope scope, DateTimeOffset month, CancellationToken ct)
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); return await inner.ReadAsync(scope, month, ct); }
    }

    [Fact]
    public async Task Committed_reply_loss_recovers_published_operation_without_second_version_or_side_effect()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile(); var fault = new CommitReplyLoss();
        await using var provider = Provider(profile, services => services.AddDbContext<FoundationDbContext>(options => options.AddInterceptors(fault)));
        await using var scope = provider.CreateAsyncScope(); var actor = await ActorAsync(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<ISliReportStore>(); var command = Command(actor, profile);
        var operation = await store.RequestAsync(command); fault.Armed = true;
        await store.BuildAsync(operation.Id, 1);
        Assert.Equal(1, fault.Failures); Assert.Equal("Published", (await store.OperationAsync(operation.Id))!.Status);
        await store.BuildAsync(operation.Id, 1); Assert.Equal(operation.Id, (await store.RequestAsync(command)).Id);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Set<SliReportVersionEntity>().CountAsync(value => value.OperationId == operation.Id));
    }

    [Fact]
    public async Task Published_history_and_membership_cannot_be_mutated_or_downgraded_away()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile); await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var actor = await ActorAsync(services); var store = services.GetRequiredService<ISliReportStore>();
        var operation = await store.RequestAsync(Command(actor, profile)); await store.BuildAsync(operation.Id, 1);
        var version = (await store.OperationAsync(operation.Id))!.VersionId!.Value;
        var db = services.GetRequiredService<FoundationDbContext>();
        var sealedSource = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ops.sli_report_source(version_id,manifest_id,source_json) VALUES ({version},{Guid.Empty},'{{}}'::jsonb)"));
        Assert.Equal("P0001", sealedSource.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops.sli_report_head SET scope_id='rewritten' WHERE scope_id={profile.Id}"));
        await using var owner = fixture.CreateServiceProvider(); await using var ownerScope = owner.CreateAsyncScope();
        var migration = ownerScope.ServiceProvider.GetRequiredService<FoundationDbContext>().GetService<IMigrator>();
        var refused = await Assert.ThrowsAsync<PostgresException>(() => migration.MigrateAsync("20261019155630_AddSliObservationEvidence"));
        Assert.Equal("P0001", refused.SqlState); Assert.NotNull(await store.FindAsync(version));
    }

    private sealed class CommitReplyLoss : DbTransactionInterceptor
    {
        public bool Armed; public int Failures;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; Failures++; throw new IOException("synthetic committed response loss"); }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Conflicting_authenticated_source_epoch_is_Unknown_in_published_revision_not_known_success()
    {
        await fixture.ApplyMigrationsAsync(); var profile = Profile();
        await using var provider = Provider(profile); await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var actor = await ActorAsync(services); var store = services.GetRequiredService<ISliReportStore>();
        var execution = Guid.NewGuid(); var at = Month.AddMinutes(1); var epoch = Guid.NewGuid();
        var start = Observation(execution, ObservationPhase.Started, Month, at);
        var terminal = Observation(execution, ObservationPhase.Terminal, Month, at) with { TerminalSuccess = true };
        await ImportAsync(services, profile.Producers[0], Guid.NewGuid(), 1, 1, at, [terminal with { TerminalSuccess = false }]);
        await ImportAsync(services, profile.Producers[1], epoch, 1, 1, at, [start, terminal]);
        var first = await store.RequestAsync(Command(actor, profile)); await store.BuildAsync(first.Id, 1);
        var original = (await store.FindAsync((await store.OperationAsync(first.Id))!.VersionId!.Value))!;
        Assert.Equal(1, original.Metadata.Counts.H);
        var conflicting = new ObservationManifest(1, epoch, 1, ObservationSource.Gateway, "test", 1, 2, at, at, 0, false, true, "Partial",
            [start, terminal with { Sequence = 2, TerminalSuccess = false, GatewayFault = true }]);
        var importer = new PostgreSqlSliManifestImporter(services.GetRequiredService<FoundationDbContext>(), services.GetRequiredService<ITransactionCoordinator>(),
            new(new Dictionary<string, ObservationProducerTrust> { [profile.Producers[1].Producer] = new(Key, ObservationSource.Gateway, "test") }), TimeProvider.System);
        Assert.Equal(SliImportStatus.Conflict, (await importer.ImportAsync(ObservationProtocol.Sign(profile.Producers[1].Producer, conflicting, Key))).Status);
        var revision = await store.RequestAsync(Command(actor, profile) with { ExpectedGeneration = 1, PredecessorId = original.Metadata.Id }); await store.BuildAsync(revision.Id, 1);
        var disputed = (await store.FindAsync((await store.OperationAsync(revision.Id))!.VersionId!.Value))!;
        Assert.Equal(new SliCounts(0, 0, 0, 0, 1), disputed.Metadata.Counts);
        Assert.Equal("SourceConflict", Assert.Single(disputed.Executions).Reason);
        Assert.Null(disputed.Metadata.GatewayKnown.Value); Assert.Equal("Incomplete", disputed.Metadata.TargetStatus);
        Assert.Contains(disputed.Sources, value => value.Conflict && value.ConflictReceipts!.Length > 0);
        Assert.Equal(1, (await store.FindAsync(original.Metadata.Id))!.Metadata.Counts.H);
    }
    private sealed class WaitingReader(TaskCompletionSource entered) : ISliReportSourceReader
    {
        public async Task<SliReportSnapshot> ReadAsync(SliReportScope scope, DateTimeOffset month, CancellationToken ct)
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException("unreachable"); }
    }
    private sealed class RequestAuditFault(IAuditTrail inner) : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput value, CancellationToken cancellationToken = default) =>
            value.Action == "sli.report.requested" ? throw new IOException("synthetic request audit fault") : inner.RecordAsync(value, cancellationToken);
    }
    private static async Task ImportAsync(IServiceProvider services, SliRequiredProducer producer, Guid epoch, long batch, long first, DateTimeOffset at, ExecutionObservation[] rows)
    {
        var manifest = new ObservationManifest(1, epoch, batch, Enum.Parse<ObservationSource>(producer.Source), "test", first, first + rows.Length - 1,
            at, at, 0, false, true, "Partial", rows.Select((value, index) => value with { Sequence = first + index }).ToArray());
        var importer = new PostgreSqlSliManifestImporter(services.GetRequiredService<FoundationDbContext>(), services.GetRequiredService<ITransactionCoordinator>(),
            new(new Dictionary<string, ObservationProducerTrust> { [producer.Producer] = new(Key, manifest.Source, "test") }), TimeProvider.System);
        Assert.Equal(SliImportStatus.Accepted, (await importer.ImportAsync(ObservationProtocol.Sign(producer.Producer, manifest, Key))).Status);
    }
    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session, object? body = null, bool csrf = true)
    {
        var result = new HttpRequestMessage(method, path); result.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) result.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) result.Content = JsonContent.Create(body); return result;
    }
    private static Task<List<string>> MoneyAsync(FoundationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT value AS "Value" FROM (
          SELECT 'wallet:' || r::text AS value FROM billing.wallet r
          UNION ALL SELECT 'ledger:' || r::text FROM billing.ledger_entry r
          UNION ALL SELECT 'settlement:' || r::text FROM billing.settlement r
          UNION ALL SELECT 'hold:' || r::text FROM billing.reservation r
          UNION ALL SELECT 'debt:' || r::text FROM billing.recovery_debt r
        ) money ORDER BY value
        """).ToListAsync();
    private sealed class PublicationAuditFault(IAuditTrail inner) : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput value, CancellationToken cancellationToken = default) =>
            value.Action is "sli.report.published" or "sli.report.revised" ? throw new IOException("synthetic audit fault") : inner.RecordAsync(value, cancellationToken);
    }
}
