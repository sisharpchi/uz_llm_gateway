namespace UZLLM.Persistence;

public sealed class SliReportHeadEntity
{
    public Guid Id { get; set; }
    public string ScopeId { get; set; } = "";
    public DateTimeOffset PeriodStart { get; set; }
    public long Generation { get; set; }
    public Guid? LatestVersionId { get; set; }
    public Guid? ActiveOperationId { get; set; }
}
public sealed class SliReportOperationEntity
{
    public Guid Id { get; set; }
    public Guid HeadId { get; set; }
    public long ExpectedGeneration { get; set; }
    public Guid? PredecessorId { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
    public string ScopeJson { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public Guid? VersionId { get; set; }
    public string? FailureKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class SliReportReceiptEntity
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = "";
    public Guid IdempotencyKey { get; set; }
    public string CommandSha256 { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class SliReportVersionEntity
{
    public Guid Id { get; set; }
    public Guid HeadId { get; set; }
    public Guid OperationId { get; set; }
    public long Revision { get; set; }
    public Guid? PredecessorId { get; set; }
    public string ScopeJson { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string MetadataJson { get; set; } = "";
    public string SourceDigest { get; set; } = "";
    public int SourceCount { get; set; }
    public byte[] Artifact { get; set; } = [];
    public string ArtifactSha256 { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
}
public sealed class SliReportSourceEntity
{
    public Guid VersionId { get; set; }
    public Guid ManifestId { get; set; }
    public string SourceJson { get; set; } = "";
}
public sealed class SliReportAttemptEntity
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public int AttemptNumber { get; set; }
    public string Outcome { get; set; } = "";
    public string? FailureKind { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
