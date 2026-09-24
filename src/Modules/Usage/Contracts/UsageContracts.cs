namespace UZLLM.Modules.Usage.Contracts;

public enum ExecutionState { Prepared, Dispatched, Succeeded, Failed, Canceled, OutcomeUnknown, RejectedBeforeExecution }
public enum DeliveryState { NotStarted, Partial, Completed, ClientDisconnected }
public enum FinancialState { PendingAdmission, Reserved, PendingEvidence, PendingSettlement, Settled, Released }
public enum EvidenceState { Unknown, Verified }
public enum EvidenceSource { Provider, Estimated, Reconciled, Unknown }
public enum ClaimResultKind { Created, Duplicate, PayloadConflict }

public sealed record UsageRequest(
    Guid Id, Guid OrganizationId, Guid ProjectId, Guid ApiKeyId, Guid CanonicalModelId,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, ExecutionState Execution,
    DeliveryState Delivery, FinancialState Financial, bool IsStream, string Operation,
    string? RouteStrategy, string? TraceId, int? HttpStatus);

public sealed record UsageAttempt(
    Guid Id, Guid RequestId, int Number, Guid ProviderModelId, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, ExecutionState Execution, string? ProviderRequestId,
    string? ErrorCategory);

public sealed record UsageEvidence(
    Guid Id, Guid RequestId, Guid AttemptId, Guid ProviderModelId, EvidenceState State, EvidenceSource Source,
    int? InputTokens, int? OutputTokens, int? CachedInputTokens, int? ReasoningTokens,
    Guid? PriceVersionId, string? ProviderRequestId, DateTimeOffset CapturedAt,
    DateTimeOffset? ReconcileAfter);

public sealed record PrepareUsageRequest(
    Guid OrganizationId, Guid ProjectId, Guid ApiKeyId, Guid CanonicalModelId,
    bool IsStream, string Operation, string? IdempotencyKey, byte[] PayloadHash,
    string? TraceId);

public sealed record ClaimResult(ClaimResultKind Kind, Guid RequestId);

public sealed record VerifiedUsageInput(
    Guid RequestId, Guid AttemptId, EvidenceSource Source, int InputTokens,
    int OutputTokens, int CachedInputTokens, int? ReasoningTokens,
    Guid PriceVersionId, string? ProviderRequestId);

public interface IUsageStore
{
    Task InsertRequestAsync(UsageRequest request, CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(Guid organizationId, Guid apiKeyId, string operation, byte[] keyHash,
        byte[] payloadHash, Guid requestId, DateTimeOffset expiresAt, DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<(Guid RequestId, byte[] PayloadHash)?> FindClaimAsync(Guid organizationId, Guid apiKeyId,
        string operation, byte[] keyHash, CancellationToken cancellationToken = default);
    Task<(Guid RequestId, byte[] PayloadHash)?> FindLiveClaimAsync(Guid organizationId, Guid apiKeyId,
        string operation, byte[] keyHash, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<UsageRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<UsageAttempt?> FindAttemptAsync(Guid attemptId, CancellationToken cancellationToken = default);
    Task<UsageAttempt> AddAttemptAsync(UsageAttempt attempt, CancellationToken cancellationToken = default);
    Task<bool> TryTransitionAttemptAsync(Guid attemptId, ExecutionState expected, ExecutionState next,
        DateTimeOffset? completedAt, CancellationToken cancellationToken = default);
    Task<bool> TryFinishAttemptAsync(Guid attemptId, ExecutionState next,
        string? providerRequestId, string? errorCategory, DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
    Task<bool> TryFinishRequestAsync(Guid requestId, ExecutionState execution,
        DeliveryState delivery, int httpStatus, string routeStrategy, DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
    Task<bool> TryAppendEvidenceAsync(UsageEvidence evidence, FinancialState nextFinancialState,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageEvidence>> ListEvidenceAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public interface IUsageService
{
    Task<ClaimResult> PrepareAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default);
    // The caller must own a PostgreSQL transaction and commit only after financial admission succeeds.
    Task<ClaimResult?> TryPrepareInTransactionAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default);
    Task<ClaimResult> ResolveDuplicateAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default);
    Task<ClaimResult?> FindExistingClaimAsync(Guid organizationId, Guid apiKeyId,
        string operation, string idempotencyKey, byte[] payloadHash,
        CancellationToken cancellationToken = default);
    Task<UsageAttempt> StartAttemptAsync(Guid requestId, Guid providerModelId, CancellationToken cancellationToken = default);
    Task<bool> MarkDispatchedAsync(Guid attemptId, CancellationToken cancellationToken = default);
    Task<bool> FinishAttemptAsync(Guid attemptId, ExecutionState next,
        string? providerRequestId, string? errorCategory, CancellationToken cancellationToken = default);
    Task<bool> FinishRequestAsync(Guid requestId, ExecutionState execution,
        DeliveryState delivery, int httpStatus, string routeStrategy, CancellationToken cancellationToken = default);
    Task<UsageEvidence?> RecordUnknownAsync(Guid requestId, Guid attemptId, CancellationToken cancellationToken = default);
    Task<UsageEvidence?> RecordVerifiedAsync(VerifiedUsageInput input, CancellationToken cancellationToken = default);
    Task<UsageRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageEvidence>> ListEvidenceAsync(Guid requestId, CancellationToken cancellationToken = default);
}
