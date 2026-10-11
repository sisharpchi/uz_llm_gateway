using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Worker;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class UsageReadIntegrationTests
{
    [Fact]
    public async Task Rollup_replay_and_competing_workers_do_not_double_count()
    {
        var seed = await ResetAndSeedAsync(aggregate: false);
        await using var provider = fixture.CreateServiceProvider();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var db = first.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(4, await db.Set<UsageRollupQueueEntity>().CountAsync());
        var before = await Read(first.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(0, before.RequestCount);
        Assert.Equal(DateTimeOffset.UnixEpoch, before.DataAsOf);
        Assert.Equal(2, before.Aggregation!.PendingBucketCount);
        Assert.NotNull(before.Aggregation.OldestPendingAt);

        await Task.WhenAll(new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync(),
            new UsageRollupCycle(new PostgreSqlUsageRollupStore(second.ServiceProvider
                .GetRequiredService<FoundationDbContext>())).RunAsync());
        Assert.Empty(await db.Set<UsageRollupQueueEntity>().ToListAsync());
        Assert.Equal(4, await db.Set<UsageHourlyRollupEntity>().SumAsync(value => value.RequestCount));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usage.request SET execution_state = execution_state WHERE id = {FirstRequestId}");
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        Assert.Equal(0, (await new PostgreSqlUsageRollupStore(db).RebuildBatchAsync(100)).RequestsExamined);
        var after = await Read(first.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(3, after.RequestCount);
        Assert.Equal(120, after.InputTokens);
        Assert.Equal("3500", after.ChargedMicroUsd);
        Assert.Equal(0, after.Aggregation!.PendingBucketCount);
        Assert.Null(after.Aggregation.OldestPendingAt);
        Assert.True(after.DataAsOf > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Late_evidence_settlement_and_refund_rebuild_original_hour()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var prior = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        var attempt = await db.Set<UsageAttemptEntity>().AsNoTracking().SingleAsync(value => value.RequestId == UnknownRequestId);
        var price = await db.Set<CatalogModelPriceEntity>().AsNoTracking().SingleAsync(value => value.ProviderModelId == attempt.ProviderModelId);
        var fee = await db.Set<BillingFeePolicyVersionEntity>().Select(value => value.Id).SingleAsync();
        db.Set<UsageEvidenceEntity>().Add(Evidence(UnknownRequestId, attempt.Id, attempt.ProviderModelId, price.Id, 50, 15));
        AddSettlement(db, UnknownRequestId, seed.OrganizationId, seed.ProjectId, seed.ApiKeyId, fee, 400, 800);
        var original = await db.Set<BillingSettlementEntity>().AsNoTracking().SingleAsync(value => value.RequestId == FirstRequestId);
        db.Set<BillingSettlementRefundEntity>().Add(new BillingSettlementRefundEntity
        {
            Id = Guid.CreateVersion7(), SettlementId = original.Id, OrganizationId = seed.OrganizationId,
            ActorAccountId = seed.OwnerAccountId, RefundKey = "rollup-late-refund", AmountMicroUsd = 1000,
            Reason = "Late financial correction fixture", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await db.Set<UsageRequestEntity>().Where(value => value.Id == UnknownRequestId)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.FinancialState, "Settled")
                .SetProperty(value => value.ExecutionState, "Succeeded").SetProperty(value => value.HttpStatus, 200));
        var pending = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(prior.DataAsOf, pending.DataAsOf);
        Assert.Equal(120, pending.InputTokens);
        Assert.Equal("0", pending.RefundedMicroUsd);
        Assert.Equal(2, pending.Aggregation!.PendingBucketCount);
        var live = await Read(scope.ServiceProvider).GetDetailAsync(seed.OrganizationId, UnknownRequestId);
        Assert.Equal(50, live!.Request.InputTokens);
        Assert.Equal("800", live.ChargedMicroUsd);

        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        var corrected = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(3, corrected.RequestCount);
        Assert.Equal(170, corrected.InputTokens);
        Assert.Equal(40, corrected.OutputTokens);
        Assert.Equal(1, corrected.ErrorCount);
        Assert.Equal(0, corrected.PendingCount);
        Assert.Equal("4300", corrected.ChargedMicroUsd);
        Assert.Equal("1000", corrected.RefundedMicroUsd);
        Assert.Equal("3300", corrected.NetChargedMicroUsd);
        var days = await Read(scope.ServiceProvider).GetTimeSeriesAsync(seed.OrganizationId, Query());
        Assert.Equal("1000", days.Items[0].RefundedMicroUsd);
        Assert.Equal("800", days.Items[1].ChargedMicroUsd);
        Assert.Equal(3, await db.Set<UsageHourlyRollupEntity>().Where(value => value.OrganizationId == seed.OrganizationId)
            .SumAsync(value => value.RequestCount));
        Assert.DoesNotContain(await db.Set<UsageRollupBucketEntity>().ToListAsync(), value => value.Hour >= AsOf);
    }

    [Fact]
    public async Task Rollups_preserve_filters_utc_edges_and_tenant_isolation()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);
        var exact = await read.GetSummaryAsync(seed.OrganizationId, Query() with
        {
            From = Start.ToOffset(TimeSpan.FromHours(5)), To = Start.AddHours(1).ToOffset(TimeSpan.FromHours(5)),
            ProjectId = seed.ProjectId, ApiKeyId = seed.ApiKeyId, ModelId = seed.MainModelId,
            Status = "succeeded", IsStream = true
        });
        Assert.Equal(1, exact.RequestCount);
        Assert.Equal("3000", exact.ChargedMicroUsd);
        var edge = await read.GetSummaryAsync(seed.OrganizationId, Query() with
            { From = Start.AddMicroseconds(1), To = Start.AddDays(1).AddMicroseconds(1) });
        Assert.Equal(1, edge.RequestCount);
        Assert.Equal(1, edge.PendingCount);
        Assert.Equal("0", edge.ChargedMicroUsd);
        Assert.Equal(0, (await read.GetSummaryAsync(seed.OrganizationId,
            Query() with { From = Start.AddHours(-1), To = Start })).RequestCount);
        Assert.Equal(2, (await read.GetSummaryAsync(seed.OrganizationId,
            Query() with { From = Start, To = Start.AddMicroseconds(1) })).RequestCount);
        Assert.Equal(1, (await read.GetSummaryAsync(seed.OrganizationId, Query() with
            { ProviderId = seed.AnthropicProviderId })).RequestCount);
        Assert.Equal(0, (await read.GetSummaryAsync(seed.OrganizationId, Query() with
            { ProjectId = seed.ForeignProjectId })).RequestCount);
        Assert.Equal("999000", (await read.GetSummaryAsync(seed.ForeignOrganizationId, Query())).ChargedMicroUsd);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.Set<UsageRequestEntity>().Where(value => value.Id == FirstRequestId)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.ExecutionState, "Failed"));
        var staleState = await read.GetSummaryAsync(seed.OrganizationId, Query() with { Status = "Succeeded" });
        Assert.Equal(1, staleState.RequestCount);
        Assert.Equal(1, staleState.Aggregation!.PendingBucketCount); // Change OUT of this filter is still disclosed.
        var otherProject = await read.GetSummaryAsync(seed.OrganizationId, Query() with { ProjectId = seed.ForeignProjectId });
        Assert.Equal(0, otherProject.Aggregation!.PendingBucketCount);
        var request = await read.GetSummaryAsync(seed.OrganizationId, Query() with { RequestId = FirstRequestId });
        Assert.Equal(1, request.ErrorCount);
        Assert.Equal("AuthoritativeRequest", request.Aggregation!.Source);
        Assert.Equal(0, request.Aggregation.PendingBucketCount);
        var forbidden = await read.GetSummaryAsync(seed.ForeignOrganizationId, Query() with { RequestId = FirstRequestId });
        Assert.Equal(0, forbidden.RequestCount);
        Assert.Equal(0, forbidden.Aggregation!.PendingBucketCount);
    }

    [Fact]
    public async Task Failed_rebuild_rolls_back_projection_and_preserves_queue()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usage.request SET execution_state = 'Failed' WHERE id = {FirstRequestId}");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION usage.test_rebuild_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected rebuild failure'; END $$;
            CREATE TRIGGER test_rebuild_failure BEFORE INSERT ON usage.hourly_rollup
            FOR EACH ROW EXECUTE FUNCTION usage.test_rebuild_failure();
            """);
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => new PostgreSqlUsageRollupStore(db).RebuildBatchAsync(100));
            var after = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
            Assert.Equal(before.ErrorCount, after.ErrorCount);
            Assert.Equal(before.DataAsOf, after.DataAsOf);
            Assert.Equal(before.ChargedMicroUsd, after.ChargedMicroUsd);
            Assert.Single(await db.Set<UsageRollupQueueEntity>().ToListAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_rebuild_failure ON usage.hourly_rollup; DROP FUNCTION usage.test_rebuild_failure();");
        }
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        Assert.Equal(3, (await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query())).ErrorCount);
        Assert.Empty(await db.Set<UsageRollupQueueEntity>().ToListAsync());
    }

    [Fact]
    public async Task Rolled_back_source_change_never_invalidates_or_changes_rollup()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usage.request SET execution_state = 'Failed' WHERE id = {FirstRequestId}");
            Assert.Single(await db.Set<UsageRollupQueueEntity>().ToListAsync());
            await transaction.RollbackAsync();
        }
        Assert.Empty(await db.Set<UsageRollupQueueEntity>().ToListAsync());
        Assert.Equal(0, (await new PostgreSqlUsageRollupStore(db).RebuildBatchAsync(100)).RequestsExamined);
        Assert.Equal(2, (await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query())).ErrorCount);
    }

    [Fact]
    public async Task Source_commit_during_publication_remains_queued_for_next_worker_cycle()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usage.request SET execution_state = execution_state WHERE id = {FirstRequestId}");
        var gate = new PublicationGate();
        await using var workerDb = new FoundationDbContext(new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(fixture.RuntimeConnectionString).AddInterceptors(gate).Options);
        var publication = new PostgreSqlUsageRollupStore(workerDb).RebuildBatchAsync(100);
        await gate.Published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var writer = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await writer.OpenAsync();
        await using var command = writer.CreateCommand();
        command.CommandText = "UPDATE usage.request SET execution_state = 'Failed' WHERE id = @request";
        command.Parameters.AddWithValue("request", FirstRequestId);
        var mutation = command.ExecuteNonQueryAsync();
        try
        {
            // The new financial/source commit must NOT wait for an hourly scan.
            // Its queue row version changes, so publication cannot acknowledge it.
            Assert.Equal(1, await mutation.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(publication.IsCompleted);
        }
        finally { gate.Release.TrySetResult(); }
        await publication;
        Assert.Single(await db.Set<UsageRollupQueueEntity>().ToListAsync());
        Assert.Equal(2, (await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query())).ErrorCount);
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        Assert.Equal(3, (await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query())).ErrorCount);
        Assert.Empty(await db.Set<UsageRollupQueueEntity>().ToListAsync());
    }

    [Fact]
    public async Task Migration_backfills_existing_requests_and_runtime_can_aggregate()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync("20260930193925_AddApiKeyFingerprintKeyVersion");
        await migrator.MigrateAsync();
        var options = new DbContextOptionsBuilder<FoundationDbContext>().UseNpgsql(fixture.RuntimeConnectionString).Options;
        await using var runtime = new FoundationDbContext(options);
        Assert.Equal(4, await runtime.Set<UsageRollupQueueEntity>().CountAsync());
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(runtime)).RunAsync();
        Assert.Equal("3500", (await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query())).ChargedMicroUsd);
        Assert.Empty(await runtime.Set<UsageRollupQueueEntity>().ToListAsync());
        Assert.False(runtime.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Late_attempt_moves_provider_dimension_without_adding_a_logical_request()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var mapping = await db.Set<CatalogProviderModelEntity>().Where(value => value.ProviderId == seed.AnthropicProviderId)
            .Select(value => value.Id).SingleAsync();
        db.Set<UsageAttemptEntity>().Add(Attempt(Guid.CreateVersion7(), UnknownRequestId, 2, mapping, "OutcomeUnknown"));
        await db.SaveChangesAsync();
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        var groups = (await Read(scope.ServiceProvider).GetBreakdownAsync(seed.OrganizationId, Query(),
            UsageBreakdownDimension.Provider)).Items;
        Assert.Equal(2, Assert.Single(groups, value => value.Name == "Anthropic").RequestCount);
        Assert.Equal(1, Assert.Single(groups, value => value.Name == "OpenAI").RequestCount);
        var total = await Read(scope.ServiceProvider).GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(3, total.RequestCount);
        Assert.Equal(120, total.InputTokens);
        Assert.Equal("3500", total.ChargedMicroUsd);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1000, 4)]
    public async Task Rollup_accepts_batch_boundaries_and_leaves_unclaimed_requests(int batchSize, int examined)
    {
        await ResetAndSeedAsync(aggregate: false);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var batch = await new PostgreSqlUsageRollupStore(db).RebuildBatchAsync(batchSize);
        Assert.Equal(examined, batch.RequestsExamined);
        Assert.Equal(4 - examined, await db.Set<UsageRollupQueueEntity>().CountAsync());
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        Assert.Equal(4, await db.Set<UsageHourlyRollupEntity>().SumAsync(value => value.RequestCount));
        Assert.Empty(await db.Set<UsageRollupQueueEntity>().ToListAsync());
    }

    [Fact]
    public async Task Unrouted_unknown_and_empty_tenants_do_not_invent_tokens_or_charges()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.Set<UsageRequestEntity>().Add(NewRequest(Guid.CreateVersion7(), seed.OrganizationId, seed.ProjectId,
            seed.ApiKeyId, seed.MainModelId, Start.AddMinutes(30), Start.AddMinutes(30),
            "RejectedBeforeExecution", "NotStarted", "Released", 400, false, null));
        await db.SaveChangesAsync();
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        var unrouted = Assert.Single((await Read(scope.ServiceProvider).GetBreakdownAsync(seed.OrganizationId,
            Query(), UsageBreakdownDimension.Provider)).Items, value => value.Id == "unrouted");
        Assert.Equal(1, unrouted.RequestCount);
        Assert.Equal(1, unrouted.ErrorCount);
        Assert.Equal(0, unrouted.InputTokens);
        Assert.Equal("0", unrouted.ChargedMicroUsd);
        var empty = await Read(scope.ServiceProvider).GetSummaryAsync(Guid.NewGuid(), Query());
        Assert.Equal(0, empty.RequestCount);
        Assert.Equal("0", empty.NetChargedMicroUsd);
        Assert.Equal(0m, empty.ErrorRatePercent);
        Assert.Empty(empty.TopModels);
        Assert.Equal(0, empty.Aggregation!.PendingBucketCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Rollup_invalid_batch_is_rejected_before_database_access(int size)
    {
        await using var db = new FoundationDbContext(new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql("Host=not-used;Database=not-used").Options);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new PostgreSqlUsageRollupStore(db).RebuildBatchAsync(size));
    }

    private sealed class PublicationGate : DbCommandInterceptor
    {
        public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO usage.hourly_rollup", StringComparison.Ordinal))
            {
                Published.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
