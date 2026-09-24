namespace UZLLM.Persistence;

internal sealed class OutboxMessageEntity
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }

    public string? LastError { get; set; }
}

internal sealed class ConsumerInboxEntryEntity
{
    public Guid Id { get; set; }

    public string Consumer { get; set; } = null!;

    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

internal sealed class LeasedJobEntity
{
    public Guid Id { get; set; }

    public string JobType { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public string DeduplicationKey { get; set; } = null!;

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }

    public string? LastError { get; set; }
}

internal sealed class OperationalAlertEntity
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string DeduplicationKey { get; set; } = null!;

    public string Details { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
