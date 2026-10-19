using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    [Fact]
    public async Task Publication_is_atomic_replayable_tenant_scoped_and_revisions_preserve_old_bytes()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 10, Start.AddMonths(-1), "promotion");
        await StatementCreditAsync(services, seed.OrganizationId, 5, Start, "promotion");
        await StatementCreditAsync(services, seed.OrganizationId, 7, Start.AddMonths(1), "promotion");
        var financial = await PublicationMoneyAsync(db);
        var command = await PublicationCommandAsync(db, seed.OrganizationId);
        var store = Publication(services);
        var pending = await store.RequestAsync(command);
        Assert.Equal("Pending", pending.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => store.BuildAsync(pending.Id, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => store.BuildAsync(pending.Id, 6));
        Assert.Equal(pending.Id, (await store.RequestAsync(command with { Reason = "  Monthly statement  " })).Id);
        await Assert.ThrowsAsync<StatementCommandConflictException>(() => store.RequestAsync(command with { Period = new(2026, 10) }));
        await Assert.ThrowsAsync<StatementCommandConflictException>(() => store.RequestAsync(command with { Reason = "Different valid reason" }));
        Assert.Null(await store.FindOperationAsync(Guid.NewGuid(), pending.Id));
        Assert.Single(await db.Set<BillingStatementReceiptEntity>().ToListAsync());
        var jobs = services.GetRequiredService<ILeasedJobStore>();
        var job = Assert.Single(await jobs.ClaimAvailableAsync("statement-test", 10, TimeSpan.FromMinutes(1)));
        Assert.Equal(PostgreSqlStatementPublicationStore.JobType, job.JobType);
        Assert.Equal(5, job.MaxAttempts);
        var handler = new StatementBuildJobHandler(store);
        await handler.HandleAsync(job, CancellationToken.None);
        // Simulates response loss: job has not been acknowledged before another delivery.
        await handler.HandleAsync(job, CancellationToken.None);
        Assert.True(await jobs.MarkCompletedAsync(job.Id, "statement-test", job.AttemptCount));
        var published = (await store.FindOperationAsync(seed.OrganizationId, pending.Id))!;
        Assert.Equal("Published", published.Status);
        var original = (await store.FindPublishedAsync(seed.OrganizationId, published.VersionId!.Value))!;
        Assert.Null(await store.FindPublishedAsync(Guid.NewGuid(), original.Id));
        Assert.Equal(StatementSnapshotCodec.Hash(original.JsonArtifact), original.JsonSha256);
        Assert.Equal(StatementSnapshotCodec.Hash(original.CsvArtifact), original.CsvSha256);
        var snapshot = StatementSnapshotCodec.Deserialize(original.SnapshotJson);
        Assert.Equal(original.SourceDigest, StatementReconciler.Reconcile(snapshot).SourceDigest);
        var sources = await db.Set<BillingStatementSourceEntity>().Where(value => value.VersionId == original.Id).ToListAsync();
        Assert.Equal(StatementArtifactBuilder.Sources(snapshot).Count, sources.Count);
        Assert.All(sources, source => Assert.Equal(StatementSnapshotCodec.Hash(source.PayloadJson), source.PayloadSha256));
        using (var json = JsonDocument.Parse(original.JsonArtifact))
        {
            var totals = json.RootElement.GetProperty("Totals");
            Assert.Equal("10", totals.GetProperty("OpeningMicroUsd").GetString());
            Assert.Equal("5", totals.GetProperty("MovementMicroUsd").GetString());
            Assert.Equal("15", totals.GetProperty("ClosingMicroUsd").GetString());
            Assert.Equal("7", totals.GetProperty("PostPeriodMovementMicroUsd").GetString());
            Assert.Equal("22", json.RootElement.GetProperty("Balance").GetProperty("PostedMicroUsd").GetString());
        }
        Assert.Single(await db.Set<BillingStatementLineEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingStatementVersionEntity>().ToListAsync());
        Assert.Equal(1, await CountStatementEventsAsync(db, "statement.published"));
        Assert.Equal(1, await CountStatementOutboxAsync(db));
        Assert.Equal(financial, await PublicationMoneyAsync(db));
        await AssertStatementHistoryProtectedAsync(db, original.Id, pending.Id);

        await StatementCreditAsync(services, seed.OrganizationId, 3, Start.AddDays(1), "promotion"); // explicitly backdated correction
        var revisionCommand = command with { IdempotencyKey = Guid.NewGuid(), ExpectedGeneration = 1,
            PredecessorId = original.Id, Reason = "Include late committed posting" };
        await Assert.ThrowsAsync<StatementCommandConflictException>(() => store.RequestAsync(command with { IdempotencyKey = Guid.NewGuid() }));
        var revisionOp = await store.RequestAsync(revisionCommand);
        await store.BuildAsync(revisionOp.Id, 1);
        var revised = (await store.FindPublishedAsync(seed.OrganizationId,
            (await store.FindOperationAsync(seed.OrganizationId, revisionOp.Id))!.VersionId!.Value))!;
        Assert.Equal(2, revised.Revision); Assert.Equal(original.Id, revised.PredecessorId);
        using (var json = JsonDocument.Parse(revised.JsonArtifact))
        {
            Assert.Equal("3", json.RootElement.GetProperty("Delta").GetProperty("ClosingMicroUsd").GetString());
            Assert.Equal("18", json.RootElement.GetProperty("Totals").GetProperty("ClosingMicroUsd").GetString());
        }
        var reread = (await store.FindPublishedAsync(seed.OrganizationId, original.Id))!;
        Assert.Equal(original.JsonArtifact, reread.JsonArtifact); Assert.Equal(original.CsvArtifact, reread.CsvArtifact);
        Assert.Equal(original.SourceDigest, reread.SourceDigest);
        Assert.Equal(pending.Id, (await store.RequestAsync(command)).Id); // receipt wins even after head advanced
        Assert.Equal(1, await CountStatementEventsAsync(db, "statement.revised"));
        Assert.Equal(2, await CountStatementOutboxAsync(db));
        Assert.Equal(2, await db.Set<BillingStatementVersionEntity>().CountAsync());
        var head = await db.Set<BillingStatementHeadEntity>().AsNoTracking().SingleAsync();
        Assert.Equal(2, head.Generation); Assert.Equal(revised.Id, head.LatestVersionId);
        await Assert.ThrowsAsync<PostgresException>(() => services.GetRequiredService<IDatabaseMigrator>()
            .MigrateAsync("20261017080259_AddWalletCreditAdjustments"));
        Assert.Equal(2, await db.Set<BillingStatementVersionEntity>().CountAsync());
    }

    [Fact]
    public async Task Publication_parallel_request_and_worker_delivery_choose_one_winner()
    {
        await ResetAsync(); var seed = await SeedAsync();
        StatementBuildCommand command;
        await using (var provider = fixture.CreateServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            await StatementCreditAsync(scope.ServiceProvider, seed.OrganizationId, 10, Start, "promotion");
            command = await PublicationCommandAsync(scope.ServiceProvider.GetRequiredService<FoundationDbContext>(), seed.OrganizationId);
        }
        async Task<StatementBuildOperation> RequestAsync(StatementBuildCommand input)
        {
            await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
            return await Publication(scope.ServiceProvider).RequestAsync(input);
        }
        var requests = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RequestAsync(command)));
        var operation = requests[0]; Assert.All(requests, value => Assert.Equal(operation.Id, value.Id));
        async Task BuildAsync(Guid id)
        {
            await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
            await Publication(scope.ServiceProvider).BuildAsync(id, 1);
        }
        await Task.WhenAll(BuildAsync(operation.Id), BuildAsync(operation.Id));
        await using var verification = fixture.CreateServiceProvider(); await using var verify = verification.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.Set<BillingStatementReceiptEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingStatementOperationEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingStatementVersionEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingStatementAttemptEntity>().ToListAsync());
        Assert.Equal(1, await CountStatementEventsAsync(db, "statement.requested"));
        Assert.Equal(1, await CountStatementEventsAsync(db, "statement.published"));
        Assert.Equal(1, await CountStatementOutboxAsync(db));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM ops.job WHERE job_type = 'billing.statement.build'").SingleAsync());
        var predecessor = await db.Set<BillingStatementVersionEntity>().AsNoTracking().SingleAsync();
        var revisedCommand = command with { IdempotencyKey = Guid.NewGuid(), ExpectedGeneration = 1,
            PredecessorId = predecessor.Id, Reason = "Parallel explicit revision" };
        var revisions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RequestAsync(revisedCommand)));
        Assert.All(revisions, value => Assert.Equal(revisions[0].Id, value.Id));
        await Task.WhenAll(BuildAsync(revisions[0].Id), BuildAsync(revisions[0].Id));
        Assert.Equal(2, await db.Set<BillingStatementVersionEntity>().CountAsync());
        Assert.Equal(2, await db.Set<BillingStatementReceiptEntity>().CountAsync());
        Assert.Equal(1, await CountStatementEventsAsync(db, "statement.revised"));
        Assert.Equal(2, await CountStatementOutboxAsync(db));
        var latest = await db.Set<BillingStatementVersionEntity>().AsNoTracking().SingleAsync(value => value.Revision == 2);
        async Task<StatementBuildOperation?> CompeteAsync()
        {
            try { return await RequestAsync(revisedCommand with { IdempotencyKey = Guid.NewGuid(), ExpectedGeneration = 2, PredecessorId = latest.Id }); }
            catch (StatementCommandConflictException) { return null; }
        }
        var competitors = await Task.WhenAll(CompeteAsync(), CompeteAsync());
        Assert.Single(competitors, value => value is not null); Assert.Single(competitors, value => value is null);
    }

    [Theory]
    [InlineData("Audit", "Dependency")]
    [InlineData("Outbox", "Dependency")]
    [InlineData("Database", "Dependency")]
    [InlineData("Digest", "SourceDigest")]
    [InlineData("Membership", "SourceDigest")]
    [InlineData("Manifest", "Dependency")]
    [InlineData("Sources", "Bounds")]
    [InlineData("Bytes", "Bounds")]
    [InlineData("Cancellation", "Canceled")]
    [InlineData("Deadline", "Timeout")]
    public async Task Publication_failure_rolls_back_every_publish_row_and_retry_keeps_operation(string fault, string expected)
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 10, Start, "promotion");
        var command = await PublicationCommandAsync(db, seed.OrganizationId);
        var operation = await Publication(services).RequestAsync(command);
        var financial = await PublicationMoneyAsync(db);
        var beforeAudit = await CountStatementEventsAsync(db, "statement.requested");
        using var cancellation = new CancellationTokenSource();
        var reader = new PublicationFaultReader(StatementReader(db), fault, cancellation);
        var limits = fault switch { "Sources" => new(2), "Bytes" => new StatementBuildLimits(MaximumBytes: 1),
            "Deadline" => new StatementBuildLimits(DeadlineMilliseconds: 20), _ => new StatementBuildLimits() };
        var realAudit = new AuditTrail(new PostgreSqlAuditEventStore(db), TimeProvider.System);
        var store = Publication(services, limits, new PublicationFaultBuilder(db, fault), reader,
            fault == "Audit" ? new PublicationFailingAudit(realAudit, "statement.published") : realAudit,
            fault == "Outbox" ? new PublicationFailingOutbox(services.GetRequiredService<IOutboxStore>()) : null);
        await Assert.ThrowsAnyAsync<Exception>(() => store.BuildAsync(operation.Id, 1, cancellation.Token));
        var failed = (await Publication(services).FindOperationAsync(seed.OrganizationId, operation.Id))!;
        Assert.Equal("Failed", failed.Status); Assert.Equal(expected, failed.FailureKind); Assert.Null(failed.VersionId);
        Assert.Empty(await db.Set<BillingStatementVersionEntity>().ToListAsync());
        Assert.Empty(await db.Set<BillingStatementSourceEntity>().ToListAsync());
        Assert.Empty(await db.Set<BillingStatementLineEntity>().ToListAsync());
        Assert.Equal(0, await CountStatementEventsAsync(db, "statement.published"));
        Assert.Equal(0, await CountStatementOutboxAsync(db));
        Assert.Equal(beforeAudit, await CountStatementEventsAsync(db, "statement.requested"));
        Assert.Equal(financial, await PublicationMoneyAsync(db));
        var attempt = await db.Set<BillingStatementAttemptEntity>().AsNoTracking().SingleAsync();
        Assert.Equal("Failed", attempt.Outcome); Assert.Equal(expected, attempt.FailureKind);
        Assert.Equal(operation.Id, (await Publication(services).RequestAsync(command)).Id);
        await Publication(services).BuildAsync(operation.Id, 2);
        Assert.Equal("Published", (await Publication(services).FindOperationAsync(seed.OrganizationId, operation.Id))!.Status);
        Assert.Equal(2, await db.Set<BillingStatementAttemptEntity>().CountAsync());
        Assert.Single(await db.Set<BillingStatementVersionEntity>().ToListAsync());
        Assert.Equal(financial, await PublicationMoneyAsync(db));
    }

    [Fact]
    public async Task Publication_request_audit_failure_leaves_no_receipt_head_or_job()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        var command = await PublicationCommandAsync(db, seed.OrganizationId);
        var realAudit = new AuditTrail(new PostgreSqlAuditEventStore(db), TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(() => Publication(services, audit: new PublicationFailingAudit(realAudit, "statement.requested")).RequestAsync(command));
        Assert.Empty(await db.Set<BillingStatementHeadEntity>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<BillingStatementOperationEntity>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<BillingStatementReceiptEntity>().ToListAsync());
        Assert.Empty(await services.GetRequiredService<ILeasedJobStore>().ClaimAvailableAsync("no-phantom-job", 10, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Publication_repeatable_read_excludes_commit_after_membership_and_does_not_lock_wallet()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 10, Start, "promotion");
        var operation = await Publication(services).RequestAsync(await PublicationCommandAsync(db, seed.OrganizationId));
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new PublicationBarrierReader(StatementReader(db), acquired, release);
        var building = Publication(services, reader: reader).BuildAsync(operation.Id, 1);
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var writer = fixture.CreateServiceProvider(); await using var writeScope = writer.CreateAsyncScope();
            // Must commit while publication holds its own report-head lock and RR snapshot.
            await StatementCreditAsync(writeScope.ServiceProvider, seed.OrganizationId, 7, Start, "promotion").WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(); }
        await building;
        var version = await db.Set<BillingStatementVersionEntity>().AsNoTracking().SingleAsync();
        var snapshot = StatementSnapshotCodec.Deserialize(version.SnapshotJson);
        Assert.Single(snapshot.Ledger); Assert.Equal(10, snapshot.Balance.PostedMicroUsd);
        Assert.Equal(10, StatementReconciler.Reconcile(snapshot).ClosingMicroUsd);
        Assert.Equal(17, await db.Set<BillingWalletEntity>().Select(value => value.PostedBalanceMicroUsd).SingleAsync());
        Assert.Equal(3, version.SourceCount);
    }

    [Fact]
    public async Task Publication_quarantined_dump_restore_preserves_bytes_receipt_and_sequence_without_dispatch()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 10, Start, "promotion");
        var command = await PublicationCommandAsync(db, seed.OrganizationId); var store = Publication(services);
        var operation = await store.RequestAsync(command); await store.BuildAsync(operation.Id, 1);
        var original = (await store.FindPublishedAsync(seed.OrganizationId, (await store.FindOperationAsync(seed.OrganizationId, operation.Id))!.VersionId!.Value))!;
        await StatementCreditAsync(services, seed.OrganizationId, 3, Start, "promotion");
        var revision = await store.RequestAsync(command with { IdempotencyKey = Guid.NewGuid(), ExpectedGeneration = 1,
            PredecessorId = original.Id, Reason = "Restore explicit revision" });
        await store.BuildAsync(revision.Id, 1);
        var pendingCommand = command with { IdempotencyKey = Guid.NewGuid(), Period = new(2026, 10) };
        var pending = await store.RequestAsync(pendingCommand);
        var connection = await fixture.RestoreStatementQuarantineAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connection }).Build();
        await using var restoredProvider = new ServiceCollection().AddUzllmPersistence(config).BuildServiceProvider(validateScopes: true);
        await using var restoredScope = restoredProvider.CreateAsyncScope();
        var restored = Publication(restoredScope.ServiceProvider);
        var copy = (await restored.FindPublishedAsync(seed.OrganizationId, original.Id))!;
        Assert.Equal(original.JsonArtifact, copy.JsonArtifact); Assert.Equal(original.CsvArtifact, copy.CsvArtifact);
        Assert.Equal(original.JsonSha256, copy.JsonSha256); Assert.Equal(original.CsvSha256, copy.CsvSha256);
        Assert.Equal(copy.SourceDigest, StatementReconciler.Reconcile(StatementSnapshotCodec.Deserialize(copy.SnapshotJson)).SourceDigest);
        Assert.Equal(operation.Id, (await restored.RequestAsync(command)).Id);
        Assert.Equal(pending.Id, (await restored.RequestAsync(pendingCommand)).Id);
        Assert.Equal("Pending", (await restored.FindOperationAsync(seed.OrganizationId, pending.Id))!.Status);
        await restored.BuildAsync(operation.Id, 2); // already committed; cannot create another version
        var restoredDb = restoredScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(2, (await restoredDb.Set<BillingStatementHeadEntity>().AsNoTracking().SingleAsync(value => value.PeriodStart == command.Period.From)).Generation);
        var versions = await restoredDb.Set<BillingStatementVersionEntity>().AsNoTracking().OrderBy(value => value.Revision).ToListAsync();
        Assert.Equal(2, versions.Count); Assert.Equal(original.Id, versions[1].PredecessorId);
        Assert.All(versions, version => Assert.Equal(version.SourceDigest,
            StatementReconciler.Reconcile(StatementSnapshotCodec.Deserialize(version.SnapshotJson)).SourceDigest));
        foreach (var version in versions)
        {
            var expected = StatementArtifactBuilder.Sources(StatementSnapshotCodec.Deserialize(version.SnapshotJson));
            var saved = await restoredDb.Set<BillingStatementSourceEntity>().AsNoTracking().Where(value => value.VersionId == version.Id)
                .Select(value => new StatementSourceData(value.Kind, value.SourceId, value.PayloadJson, value.PayloadSha256)).ToListAsync();
            Assert.Equal(expected.ToArray(), saved.OrderBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());
        }
        Assert.Equal(2, await CountStatementOutboxAsync(restoredDb));
        Assert.Equal(0, await restoredDb.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM ops.consumer_inbox").SingleAsync());
        await AssertStatementHistoryProtectedAsync(restoredDb, original.Id, operation.Id);
    }

    [Fact]
    public async Task Publication_committed_outbox_consumer_replay_acknowledges_once()
    {
        await ResetAsync(); var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider; var db = services.GetRequiredService<FoundationDbContext>();
        var op = await Publication(services).RequestAsync(await PublicationCommandAsync(db, seed.OrganizationId));
        await Publication(services).BuildAsync(op.Id, 1);
        var message = Assert.Single(await services.GetRequiredService<IOutboxStore>().ClaimAvailableAsync("statement-outbox-test", 10, TimeSpan.FromMinutes(1)));
        var handler = new StatementPublishedEventHandler(services.GetRequiredService<ITransactionCoordinator>(), services.GetRequiredService<IConsumerInboxStore>(),
            NullLogger<StatementPublishedEventHandler>.Instance);
        await handler.HandleAsync(message, CancellationToken.None); await handler.HandleAsync(message, CancellationToken.None);
        Assert.True(await services.GetRequiredService<IConsumerInboxStore>().HasProcessedAsync("statement-publications", message.Id));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM ops.consumer_inbox WHERE consumer = 'statement-publications'").SingleAsync());
    }

    private static PostgreSqlStatementPublicationStore Publication(IServiceProvider services, StatementBuildLimits? limits = null,
        IStatementArtifactBuilder? builder = null, IStatementSourceReader? reader = null, IAuditTrail? audit = null, IOutboxStore? outbox = null)
    {
        var db = services.GetRequiredService<FoundationDbContext>();
        return new(db, reader ?? StatementReader(db), builder ?? new StatementArtifactBuilder(),
            audit ?? new AuditTrail(new PostgreSqlAuditEventStore(db), TimeProvider.System), outbox ?? services.GetRequiredService<IOutboxStore>(),
            services.GetRequiredService<ILeasedJobStore>(), TimeProvider.System, limits ?? new());
    }

    private static async Task<StatementBuildCommand> PublicationCommandAsync(FoundationDbContext db, Guid org) =>
        new(org, await db.Set<IdentityAccountEntity>().Select(value => value.Id).SingleAsync(), new(2026, 9), Guid.NewGuid(), 0, null, "Monthly statement");

    private static Task<int> CountStatementEventsAsync(FoundationDbContext db, string action) =>
        db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit.audit_event WHERE action = {action}").SingleAsync();
    private static Task<int> CountStatementOutboxAsync(FoundationDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM ops.outbox WHERE event_type = 'billing.statement.published'").SingleAsync();

    private static Task<string> PublicationMoneyAsync(FoundationDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT md5(coalesce(string_agg(source,'|' ORDER BY source),'')) AS "Value" FROM (
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
        ) sources
        """).SingleAsync();

    private static async Task AssertStatementHistoryProtectedAsync(FoundationDbContext db, Guid version, Guid operation)
    {
        foreach (var table in new[] { "statement_version", "statement_source", "statement_line", "statement_receipt", "statement_attempt" })
        {
            // UPDATE and DELETE are rejected even if values would not change.
            // Identifiers come only from this fixed in-source allowlist, never a caller.
            var column = table is "statement_source" or "statement_line" ? "version_id" : "id";
            var updateSql = $"UPDATE billing.{table} SET {column} = {column}";
            var deleteSql = $"DELETE FROM billing.{table}";
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(updateSql))).SqlState);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(deleteSql))).SqlState);
        }
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.statement_operation SET reason = 'Rewrite immutable reason' WHERE id = {operation}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.statement_head SET organization_id = {Guid.NewGuid()} WHERE id = (SELECT head_id FROM billing.statement_version WHERE id = {version})"));
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing.statement_operation (id, organization_id, head_id, expected_generation,
                requested_by_account_id, reason, status, created_at)
            SELECT {Guid.NewGuid()}, {Guid.NewGuid()}, head_id, 0, requested_by_account_id,
                'Wrong tenant operation', 'Pending', created_at FROM billing.statement_operation WHERE id = {operation}
            """))).SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing.statement_source (version_id, organization_id, kind, source_id, payload_json, payload_sha256)
            SELECT id, organization_id, 'extra', {Guid.NewGuid()}, {"{}"}, repeat('a',64) FROM billing.statement_version WHERE id = {version}
            """));
    }

    private sealed class PublicationFailingAudit(IAuditTrail inner, string action) : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            input.Action == action ? throw new IOException("Synthetic statement audit outage") : inner.RecordAsync(input, cancellationToken);
    }

    private sealed class PublicationFaultReader(IStatementSourceReader inner, string fault, CancellationTokenSource caller) : IStatementSourceReader
    {
        public async Task<StatementSnapshot?> ReadAsync(Guid org, StatementPeriod period, CancellationToken token = default)
        {
            if (fault == "Cancellation") caller.Cancel();
            if (fault == "Deadline") await Task.Delay(Timeout.Infinite, token);
            return await inner.ReadAsync(org, period, token);
        }
    }

    private sealed class PublicationBarrierReader(IStatementSourceReader inner, TaskCompletionSource acquired, TaskCompletionSource release) : IStatementSourceReader
    {
        public async Task<StatementSnapshot?> ReadAsync(Guid org, StatementPeriod period, CancellationToken token = default)
        {
            var snapshot = await inner.ReadAsync(org, period, token);
            acquired.TrySetResult(); await release.Task.WaitAsync(token); return snapshot;
        }
    }

    private sealed class PublicationFaultBuilder(FoundationDbContext db, string fault) : IStatementArtifactBuilder
    {
        public StatementArtifact Build(StatementSnapshot snapshot, Guid version, long revision, Guid? predecessor, StatementSnapshot? previous, StatementBuildLimits limits)
        {
            if (fault == "Database") db.Database.ExecuteSqlRaw("SELECT 1 / 0");
            var artifact = new StatementArtifactBuilder().Build(snapshot, version, revision, predecessor, previous, limits);
            return fault switch
            {
                "Digest" => artifact with { Reconciliation = artifact.Reconciliation with { SourceDigest = new('0', 64) } },
                "Membership" => artifact with { Sources = artifact.Sources.Skip(1).ToArray() },
                "Manifest" => artifact with { TotalBytes = artifact.TotalBytes + 1 },
                _ => artifact
            };
        }
    }

    private sealed class PublicationFailingOutbox(IOutboxStore inner) : IOutboxStore
    {
        public Task<Guid> EnqueueAsync(string eventType, string payload, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic statement outbox outage");
        public Task<IReadOnlyList<OutboxMessage>> ClaimAvailableAsync(string workerId, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => inner.ClaimAvailableAsync(workerId, maxCount, leaseDuration, cancellationToken);
        public Task<bool> RenewLeaseAsync(Guid eventId, string workerId, int attemptCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => inner.RenewLeaseAsync(eventId, workerId, attemptCount, leaseDuration, cancellationToken);
        public Task<bool> MarkProcessedAsync(Guid eventId, string workerId, int attemptCount, CancellationToken cancellationToken = default) => inner.MarkProcessedAsync(eventId, workerId, attemptCount, cancellationToken);
        public Task<bool> MarkFailedAsync(Guid eventId, string workerId, int attemptCount, TimeSpan retryDelay, string failureKind, CancellationToken cancellationToken = default) => inner.MarkFailedAsync(eventId, workerId, attemptCount, retryDelay, failureKind, cancellationToken);
    }
}
