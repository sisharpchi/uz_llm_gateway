namespace UZLLM.Persistence;

public sealed class BillingStatementHeadEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public string Kind { get; set; } = "AccountStatement";
    public long Generation { get; set; }
    public Guid? LatestVersionId { get; set; }
    public Guid? ActiveOperationId { get; set; }
}

public sealed class BillingStatementOperationEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid HeadId { get; set; }
    public long ExpectedGeneration { get; set; }
    public Guid? PredecessorId { get; set; }
    public Guid RequestedByAccountId { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public string? FailureKind { get; set; }
    public Guid? PublishedVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingStatementReceiptEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OperationId { get; set; }
    public string OperationKind { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public string CommandSha256 { get; set; } = null!;
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingStatementVersionEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid HeadId { get; set; }
    public Guid OperationId { get; set; }
    public long Revision { get; set; }
    public Guid? PredecessorId { get; set; }
    public string CalculationVersion { get; set; } = null!;
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset SnapshotCapturedAt { get; set; }
    public string SourceDigest { get; set; } = null!;
    public string SnapshotJson { get; set; } = null!;
    public byte[] JsonArtifact { get; set; } = [];
    public byte[] CsvArtifact { get; set; } = [];
    public string JsonSha256 { get; set; } = null!;
    public string CsvSha256 { get; set; } = null!;
    public int SourceCount { get; set; }
    public int LineCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class BillingStatementLineEntity
{
    public Guid VersionId { get; set; }
    public Guid OrganizationId { get; set; }
    public int Ordinal { get; set; }
    public Guid LedgerId { get; set; }
    public string PayloadJson { get; set; } = null!;
}

public sealed class BillingStatementSourceEntity
{
    public Guid VersionId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid SourceId { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string PayloadSha256 { get; set; } = null!;
}

public sealed class BillingStatementAttemptEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OperationId { get; set; }
    public int AttemptNumber { get; set; }
    public string Outcome { get; set; } = null!;
    public string? FailureKind { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
