namespace UZLLM.Persistence;

public sealed class UsageRequestEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid CanonicalModelId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ExecutionState { get; set; } = null!;
    public string DeliveryState { get; set; } = null!;
    public string FinancialState { get; set; } = null!;
    public bool IsStream { get; set; }
    public string Operation { get; set; } = null!;
    public string? RouteStrategy { get; set; }
    public string? TraceId { get; set; }
    public int? HttpStatus { get; set; }
    public OrganizationEntity Organization { get; set; } = null!;
    public ProjectEntity Project { get; set; } = null!;
    public GatewayApiKeyEntity ApiKey { get; set; } = null!;
    public CatalogModelEntity CanonicalModel { get; set; } = null!;
    public ICollection<UsageAttemptEntity> Attempts { get; set; } = [];
}

public sealed class UsageIdempotencyClaimEntity
{
    public Guid OrganizationId { get; set; }
    public Guid ApiKeyId { get; set; }
    public string Operation { get; set; } = null!;
    public byte[] KeyHash { get; set; } = null!;
    public byte[] PayloadHash { get; set; } = null!;
    public Guid RequestId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public UsageRequestEntity Request { get; set; } = null!;
}

public sealed class UsageAttemptEntity
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int Number { get; set; }
    public Guid ProviderModelId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ExecutionState { get; set; } = null!;
    public string? ProviderRequestId { get; set; }
    public string? ErrorCategory { get; set; }
    public UsageRequestEntity Request { get; set; } = null!;
    public CatalogProviderModelEntity ProviderModel { get; set; } = null!;
}

public sealed class UsageEvidenceEntity
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid ProviderModelId { get; set; }
    public string State { get; set; } = null!;
    public string Source { get; set; } = null!;
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public Guid? PriceVersionId { get; set; }
    public string? ProviderRequestId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? ReconcileAfter { get; set; }
    public UsageRequestEntity Request { get; set; } = null!;
    public UsageAttemptEntity Attempt { get; set; } = null!;
    public CatalogModelPriceEntity? PriceVersion { get; set; }
}
