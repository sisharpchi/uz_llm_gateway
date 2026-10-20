using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Observability;
using UZLLM.Worker;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class SliObservationIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static SliCollectorTrust Trust(string producer, ObservationSource source = ObservationSource.Gateway) =>
        new(new Dictionary<string, ObservationProducerTrust> { [producer] = new(Key, source, "test") });

    [Fact]
    public async Task Receipt_audit_failure_and_cancellation_leave_no_partially_accepted_source()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "audit-" + Guid.NewGuid().ToString("N");
        var bytes = ObservationProtocol.Sign(producer, Batch(), Key);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION ops.sli_test_receipt_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic receipt write failure'; END $$;
            CREATE TRIGGER sli_test_receipt_failure BEFORE INSERT ON ops.sli_import_receipt
              FOR EACH ROW WHEN (NEW.producer LIKE 'audit-%') EXECUTE FUNCTION ops.sli_test_receipt_failure();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => ImportAsync(producer, bytes));
            Assert.False(await db.Set<SliManifestEntity>().AnyAsync(value => value.Producer == producer));
            Assert.False(await db.Set<SliObservationEntity>().AnyAsync(value => value.Producer == producer));
            Assert.False(await db.Set<SliImportReceiptEntity>().AnyAsync(value => value.Producer == producer));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER sli_test_receipt_failure ON ops.sli_import_receipt; DROP FUNCTION ops.sli_test_receipt_failure();");
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Importer(scope.ServiceProvider, producer).ImportAsync(bytes, canceled.Token));
        Assert.False(await db.Set<SliManifestEntity>().AnyAsync(value => value.Producer == producer));
        Assert.Equal(SliImportStatus.Accepted, (await ImportAsync(producer, bytes)).Status);
    }

    [Fact]
    public async Task Parallel_import_replay_conflict_overlap_and_restart_preserve_one_explicit_source_set()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "pg-" + Guid.NewGuid().ToString("N");
        var batch = Batch();
        var bytes = ObservationProtocol.Sign(producer, batch, Key);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ImportAsync(producer, bytes)));
        Assert.Single(results, value => value.Status == SliImportStatus.Accepted);
        Assert.Equal(7, results.Count(value => value.Status == SliImportStatus.Duplicate));
        Assert.Single(results.Select(value => value.ManifestId).Distinct());
        var changed = batch with { Observations = [batch.Observations[0] with { GatewayFault = true }] };
        Assert.Equal(SliImportStatus.Conflict, (await ImportAsync(producer, ObservationProtocol.Sign(producer, changed, Key))).Status);
        var overlap = changed with { BatchNumber = 2 };
        Assert.Equal(SliImportStatus.Conflict, (await ImportAsync(producer, ObservationProtocol.Sign(producer, overlap, Key))).Status);
        var duplicateEvent = batch with { BatchNumber = 3, FirstSequence = 2, LastSequence = 2,
            Observations = [batch.Observations[0] with { Sequence = 2 }] };
        Assert.Equal(SliImportStatus.Conflict, (await ImportAsync(producer, ObservationProtocol.Sign(producer, duplicateEvent, Key))).Status);
        var restarted = batch with { Epoch = Guid.NewGuid() };
        var restart = await ImportAsync(producer, ObservationProtocol.Sign(producer, restarted, Key));
        Assert.Equal(SliImportStatus.Accepted, restart.Status);
        Assert.True(restart.Warnings.HasFlag(SliCoverageWarning.EpochBoundary));
        await using var provider = new ServiceCollection().AddUzllmPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build())
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(2, await db.Set<SliManifestEntity>().CountAsync(value => value.Producer == producer));
        Assert.Equal(2, await db.Set<SliObservationEntity>().CountAsync(value => value.Producer == producer));
        Assert.Equal(12, await db.Set<SliImportReceiptEntity>().CountAsync(value => value.Producer == producer));
        var original = await db.Set<SliObservationEntity>().SingleAsync(value => value.Producer == producer && value.Epoch == batch.Epoch);
        Assert.False(JsonSerializer.Deserialize<ExecutionObservation>(original.PayloadJson, ObservationProtocol.Json)!.GatewayFault);
    }

    [Fact]
    public async Task Missing_out_of_order_dropped_clock_zero_traffic_and_probe_evidence_remains_explicit()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "coverage-" + Guid.NewGuid().ToString("N");
        var batch = Batch() with { BatchNumber = 3, FirstSequence = 7, LastSequence = 9, Dropped = 2,
            Observations = [Item() with { Sequence = 9, StartedAt = new(2026, 9, 30, 23, 59, 59, TimeSpan.Zero),
                Mode = ObservationMode.Sse, Phase = ObservationPhase.Terminal, TerminalSuccess = false,
                ClientDisconnected = true, GatewayFault = true, ProviderFault = ObservationProviderFault.Timeout }] };
        var result = await ImportAsync(producer, ObservationProtocol.Sign(producer, batch, Key));
        Assert.Equal(SliImportStatus.Accepted, result.Status);
        foreach (var flag in new[] { SliCoverageWarning.MissingPredecessor, SliCoverageWarning.Dropped,
            SliCoverageWarning.ClockUnverified, SliCoverageWarning.TerminalPartial, SliCoverageWarning.CollectorDiscontinuous })
            Assert.True(result.Warnings.HasFlag(flag), flag.ToString());
        var earlier = batch with { BatchNumber = 2, FirstSequence = 6, LastSequence = 6, Dropped = 0,
            Observations = [Item() with { Sequence = 6 }] };
        Assert.Equal(SliImportStatus.Accepted, (await ImportAsync(producer, ObservationProtocol.Sign(producer, earlier, Key))).Status);
        var heartbeat = batch with { BatchNumber = 4, FirstSequence = 10, LastSequence = 9,
            Dropped = 0, Observations = [] };
        Assert.True((await ImportAsync(producer, ObservationProtocol.Sign(producer, heartbeat, Key))).Warnings.HasFlag(SliCoverageWarning.ZeroTraffic));
        var skipped = heartbeat with { BatchNumber = 5, FirstSequence = 12, LastSequence = 11 };
        Assert.True((await ImportAsync(producer, ObservationProtocol.Sign(producer, skipped, Key))).Warnings.HasFlag(SliCoverageWarning.SequenceGap));
        var future = Batch() with { Epoch = batch.Epoch, BatchNumber = 6, FirstSequence = 12, LastSequence = 12,
            IntervalEnd = Now.AddMinutes(2), Observations = [Item() with { Sequence = 12 }] };
        Assert.True((await ImportAsync(producer, ObservationProtocol.Sign(producer, future, Key))).Warnings.HasFlag(SliCoverageWarning.ClockSkew));
        var probeProducer = "probe-" + Guid.NewGuid().ToString("N");
        var probe = Batch() with { Source = ObservationSource.Probe, TerminalCoverage = "NotApplicable",
            Observations = [Item() with { Phase = ObservationPhase.Probe, Operation = ObservationOperation.Readiness, HttpStatus = 0 }] };
        Assert.True((await ImportAsync(probeProducer, ObservationProtocol.Sign(probeProducer, probe, Key), ObservationSource.Probe))
            .Warnings.HasFlag(SliCoverageWarning.ProbeUnreachable));
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var stored = await db.Set<SliObservationEntity>().SingleAsync(value => value.ManifestId == result.ManifestId);
        Assert.Equal(9, stored.StartedAt.Month);
        Assert.Equal(10, stored.At.Month);
        Assert.Contains("\"terminalSuccess\": false", stored.PayloadJson);
        Assert.Equal((int)result.Warnings, (await db.Set<SliManifestEntity>().SingleAsync(value => value.Id == result.ManifestId)).CoverageWarnings);
    }

    [Fact]
    public async Task Import_commit_failure_rolls_back_rows_receipt_and_all_financial_side_effects()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "rollback-" + Guid.NewGuid().ToString("N");
        var bytes = ObservationProtocol.Sign(producer, Batch(), Key);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = await CountsAsync(db);
        var failing = new PostgreSqlSliManifestImporter(db,
            new FailCommit(scope.ServiceProvider.GetRequiredService<ITransactionCoordinator>()), Trust(producer), new FixedClock());
        await Assert.ThrowsAsync<IOException>(() => failing.ImportAsync(bytes));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Set<SliManifestEntity>().CountAsync(value => value.Producer == producer));
        Assert.Equal(0, await db.Set<SliObservationEntity>().CountAsync(value => value.Producer == producer));
        Assert.Equal(0, await db.Set<SliImportReceiptEntity>().CountAsync(value => value.Producer == producer));
        Assert.Equal(before, await CountsAsync(db));
        Assert.Equal(SliImportStatus.Accepted, (await ImportAsync(producer, bytes)).Status);
        Assert.Equal(SliImportStatus.Duplicate, (await ImportAsync(producer, bytes)).Status);
    }

    [Fact]
    public async Task Published_source_membership_is_sealed_and_retention_keeps_manifest_and_expiry_receipt()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "retention-" + Guid.NewGuid().ToString("N");
        var batch = Batch() with { LastSequence = 2, Dropped = 1 };
        var accepted = await ImportAsync(producer, ObservationProtocol.Sign(producer, batch, Key));
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var sql in new[]
        {
            "UPDATE ops.sli_manifest SET dropped = dropped WHERE id = {0}",
            "DELETE FROM ops.sli_manifest WHERE id = {0}",
            "DELETE FROM ops.sli_import_receipt WHERE manifest_id = {0}",
            "UPDATE ops.sli_observation SET operation = operation WHERE manifest_id = {0}",
            "INSERT INTO ops.sli_observation (manifest_id,producer,epoch,sequence,execution_id,event_key,operation,started_at,at,payload_json) SELECT manifest_id, producer, epoch, 2, execution_id, event_key || 'late', operation, started_at, at, payload_json FROM ops.sli_observation WHERE manifest_id = {0}"
        })
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, accepted.ManifestId!));
        var original = await db.Set<SliManifestEntity>().AsNoTracking().SingleAsync(value => value.Id == accepted.ManifestId);
        var sweep = new PostgreSqlRetentionSweep(db, new RetentionSchedule("synthetic-approved-fixture", 1, 90), new FixedClock(Now.AddDays(3)));
        Assert.True((await sweep.SweepAsync(1000)).SliObservations > 0);
        Assert.False(await db.Set<SliObservationEntity>().AnyAsync(value => value.ManifestId == accepted.ManifestId));
        var after = await db.Set<SliManifestEntity>().AsNoTracking().SingleAsync(value => value.Id == accepted.ManifestId);
        Assert.Equal(original.Sha256, after.Sha256);
        Assert.Equal(original.ObservationCount, after.ObservationCount);
        Assert.Equal(1, (await db.Set<SliImportReceiptEntity>().SingleAsync(value => value.ManifestId == accepted.ManifestId && value.Status == "Expired")).ObservationCount);
        Assert.Equal(SliImportStatus.Duplicate, (await ImportAsync(producer, ObservationProtocol.Sign(producer, batch, Key))).Status);
        // Replay must never reconstruct expired raw observations or erase loss disclosure.
        Assert.False(await db.Set<SliObservationEntity>().AnyAsync(value => value.ManifestId == accepted.ManifestId));
    }

    [Fact]
    public async Task Private_spool_recovers_after_sink_failure_and_committed_response_loss_without_reexecuting()
    {
        await fixture.ApplyMigrationsAsync();
        var producer = "spool-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "uzllm-sli-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(directory, "owned blocking file");
        var clock = new FixedClock();
        var buffer = new ObservationBuffer(clock);
        buffer.TryRecord(Item());
        var options = new ObservationCaptureOptions(true, producer, "test", directory, Key, false, new([]));
        using var exporter = new ObservationSpoolExporter(buffer, options, NullLogger<ObservationSpoolExporter>.Instance);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => exporter.ExportOnceAsync(CancellationToken.None));
            for (var i = 0; i < 1025; i++) buffer.TryRecord(Item()); // Sink failure never waits here.
            File.Delete(directory);
            Directory.CreateDirectory(directory);
            await exporter.ExportOnceAsync(CancellationToken.None); // Original pending batch, unchanged.
            await using var provider = fixture.CreateServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var importer = Importer(scope.ServiceProvider, producer);
            var importOptions = new SliImportOptions(true, directory, Trust(producer));
            var lostReply = new SliImportCycle(importOptions, new LoseFirstReply(importer));
            await Assert.ThrowsAsync<IOException>(() => lostReply.RunAsync(CancellationToken.None));
            Assert.Single(Directory.GetFiles(directory, "*.manifest"));
            var cycle = new SliImportCycle(importOptions, importer);
            Assert.Equal(1, await cycle.RunAsync(CancellationToken.None));
            Assert.Empty(Directory.GetFiles(directory, "*.manifest"));
            await exporter.ExportOnceAsync(CancellationToken.None);
            Assert.Equal(1, await cycle.RunAsync(CancellationToken.None));
            var manifests = await db.Set<SliManifestEntity>().Where(value => value.Producer == producer).OrderBy(value => value.BatchNumber).ToListAsync();
            Assert.Equal(2, manifests.Count);
            Assert.Equal(1, manifests[0].ObservationCount);
            Assert.Equal(1024, manifests[1].ObservationCount);
            Assert.Equal(1, manifests[1].Dropped);
            Assert.Equal(1025, await db.Set<SliObservationEntity>().CountAsync(value => value.Producer == producer));
            Assert.Equal(new[] { "Accepted", "Accepted", "Duplicate" },
                await db.Set<SliImportReceiptEntity>().Where(value => value.Producer == producer).OrderBy(value => value.Status).Select(value => value.Status).ToArrayAsync());
            await File.WriteAllTextAsync(Path.Combine(directory, "bad.manifest"), "{\"prompt\":\"never stored\"}");
            await File.WriteAllTextAsync(Path.Combine(directory, "partial.manifest.tmp"), "not a delivery");
            Assert.Equal(1, await cycle.RunAsync(CancellationToken.None));
            Assert.Single(Directory.GetFiles(directory, "*.rejected"));
            Assert.Equal(2, await db.Set<SliManifestEntity>().CountAsync(value => value.Producer == producer));
            for (var i = 0; i < 17; i++) await File.WriteAllTextAsync(Path.Combine(directory, $"bounded-{i}.manifest"), "{}");
            Assert.Equal(16, await cycle.RunAsync(CancellationToken.None));
            Assert.Single(Directory.GetFiles(directory, "*.manifest"));
            Assert.Equal(1, await cycle.RunAsync(CancellationToken.None));
        }
        finally
        {
            if (File.Exists(directory)) File.Delete(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<SliImportResult> ImportAsync(string producer, byte[] bytes, ObservationSource source = ObservationSource.Gateway)
    {
        await using var provider = new ServiceCollection().AddUzllmPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build())
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        return await Importer(scope.ServiceProvider, producer, source).ImportAsync(bytes);
    }
    private static PostgreSqlSliManifestImporter Importer(IServiceProvider services, string producer, ObservationSource source = ObservationSource.Gateway) =>
        new(services.GetRequiredService<FoundationDbContext>(), services.GetRequiredService<ITransactionCoordinator>(), Trust(producer, source), new FixedClock());
    private static async Task<string> CountsAsync(FoundationDbContext db) =>
        string.Join(":", await db.Set<BillingWalletEntity>().CountAsync(), await db.Set<BillingLedgerEntryEntity>().CountAsync(),
            await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM ops.outbox").SingleAsync());
    private static ObservationManifest Batch() => new(1, Guid.NewGuid(), 1, ObservationSource.Gateway,
        "test", 1, 1, Now, Now, 0, false, false, "Partial", [Item()]);
    private static ExecutionObservation Item() => new(1, Guid.NewGuid(), ObservationPhase.Started, ObservationOperation.Chat,
        ObservationMode.Unknown, Now, Now, true, 200, false, false, false, false, ObservationProviderFault.None, ObservationCredential.Unknown);
    private sealed class FixedClock(DateTimeOffset? now = null) : TimeProvider { public override DateTimeOffset GetUtcNow() => now ?? Now; }
    private sealed class LoseFirstReply(ISliManifestImporter inner) : ISliManifestImporter
    {
        public async Task<SliImportResult> ImportAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        { await inner.ImportAsync(bytes, cancellationToken); throw new IOException("lost committed response"); }
    }
    private sealed class FailCommit(ITransactionCoordinator inner) : ITransactionCoordinator
    {
        public async Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default) => new Scope(await inner.BeginAsync(cancellationToken));
        private sealed class Scope(ITransactionScope inner) : ITransactionScope
        {
            public Task CommitAsync(CancellationToken cancellationToken = default) => throw new IOException("simulated commit failure");
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
