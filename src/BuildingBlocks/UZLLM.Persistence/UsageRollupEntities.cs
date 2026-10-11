namespace UZLLM.Persistence;

/// <summary>Transactional invalidation, coalesced per logical request, never a money authority.</summary>
public sealed class UsageRollupQueueEntity
{
    public Guid RequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset Hour { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
}

public sealed class UsageRollupBucketEntity
{
    public Guid OrganizationId { get; set; }
    public DateTimeOffset Hour { get; set; }
    public DateTimeOffset DataAsOf { get; set; }
}

/// <summary>Rebuildable UTC-hour projection. Empty ProviderId means an unrouted request.</summary>
public sealed class UsageHourlyRollupEntity
{
    public Guid OrganizationId { get; set; }
    public DateTimeOffset Hour { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid ModelId { get; set; }
    public Guid ProviderId { get; set; }
    public string ExecutionState { get; set; } = null!;
    public bool IsStream { get; set; }
    public long RequestCount { get; set; }
    public long CompletedCount { get; set; }
    public long ErrorCount { get; set; }
    public long PendingCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ChargedMicroUsd { get; set; }
    public long RefundedMicroUsd { get; set; }
}
