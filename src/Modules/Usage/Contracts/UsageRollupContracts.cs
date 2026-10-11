namespace UZLLM.Modules.Usage.Contracts;

public sealed record UsageRollupBatchResult(int RequestsExamined, int BucketsRebuilt, bool Busy = false);

public interface IUsageRollupStore
{
    Task<UsageRollupBatchResult> RebuildBatchAsync(int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Backlog is scoped to this tenant and time window, not a financial balance.</summary>
public sealed record UsageAggregationInfo(long PendingBucketCount, DateTimeOffset? OldestPendingAt,
    string Source = "HourlyRollupsWithLiveEdges");
