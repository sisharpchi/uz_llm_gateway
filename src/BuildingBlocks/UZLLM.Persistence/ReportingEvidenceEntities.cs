namespace UZLLM.Persistence;

public sealed class BillingReportingProfileEntity
{
    public string Id { get; set; } = null!;
    public int Version { get; set; }
    public string SnapshotJson { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
}

/// <summary>Append-only fiscal/contract/link journal with explicit relational fences and a frozen receipt.</summary>
public sealed class BillingReportingEvidenceEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Kind { get; set; } = null!;
    public string State { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public string CommandSha256 { get; set; } = null!;
    public string ProfileId { get; set; } = null!;
    public int ProfileVersion { get; set; }
    public Guid ActorId { get; set; }
    public Guid? PredecessorId { get; set; }
    public Guid? ContractReferenceId { get; set; }
    public Guid? StatementVersionId { get; set; }
    public Guid? SliReportVersionId { get; set; }
    public string? IssuerKey { get; set; }
    public string? ExternalOperationReference { get; set; }
    public bool IsOperationRoot { get; set; }
    public string? DocumentNumber { get; set; }
    public string? ContractKey { get; set; }
    public string? PolicyVersion { get; set; }
    public string? ScopeId { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? LinkKey { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string PayloadSha256 { get; set; } = null!;
}
