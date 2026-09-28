using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Payments.Contracts;

public enum PaymentProvider { Payme, Click }

public enum PaymentStatus { Pending, Created, Paid, Canceled, Expired }

public enum ProviderObservationStatus { Created, Paid, Canceled, Reversed }

/// <summary>One operator-transcribed row from an independently obtained merchant statement.</summary>
public sealed record ProviderObservationInput(PaymentProvider Provider, string SourceReference,
    string SourceSha256, string RowReference, string ExternalTransactionId,
    ProviderObservationStatus Status, UzsTiyinAmount Amount, DateTimeOffset ProviderObservedAt,
    string Reason);

/// <summary>Immutable statement row and any mismatch cases opened by its ingestion.</summary>
public sealed record ProviderObservationResult(Guid Id, bool Duplicate,
    IReadOnlyList<string> CaseReasons);

public sealed record ProviderObservation(Guid Id, Guid? IntentId, PaymentProvider Provider,
    string MerchantScope, string SourceReference, string SourceSha256, string RowReference,
    string ExternalTransactionId, ProviderObservationStatus Status, UzsTiyinAmount Amount,
    DateTimeOffset ProviderObservedAt, DateTimeOffset RecordedAt, Guid RecordedByAccountId);

/// <summary>Operator-visible payment reconciliation case; closure never posts wallet credit.</summary>
public sealed record PaymentReconciliationCase(Guid Id, Guid? IntentId, string? Provider,
    string? ExternalTransactionId, string Reason, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt, string? ResolutionReference);

public interface IPaymentReconciliationService
{
    Task<ProviderObservationResult> RecordObservationAsync(Guid actorId,
        ProviderObservationInput input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentReconciliationCase>> ListCasesAsync(int limit,
        CancellationToken cancellationToken = default);
    Task<ProviderObservation?> FindObservationAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task<bool> ResolveCaseAsync(Guid actorId, Guid caseId, string reason,
        string resolutionReference, CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationStore
{
    Task LockExternalTransactionAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default);
    Task LockCaseExternalTransactionAsync(Guid caseId,
        CancellationToken cancellationToken = default);
    Task<ProviderObservation?> FindBySourceAsync(PaymentProvider provider, string merchantScope,
        string sourceReference, string rowReference, CancellationToken cancellationToken = default);
    Task<ProviderObservation?> FindObservationAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task<bool> TryInsertObservationAsync(ProviderObservation observation,
        CancellationToken cancellationToken = default);
    Task<int> CountByExternalAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default);
    Task<Guid?> EnsureCaseAsync(Guid? intentId, Guid? observationId, PaymentProvider? provider,
        string? merchantScope, string? externalTransactionId, string reason, DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentReconciliationCase>> ListCasesAsync(int limit,
        CancellationToken cancellationToken = default);
    Task<bool> ResolveCaseAsync(Guid caseId, Guid actorId, string resolutionReference,
        DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed record PaymentQuote(
    Guid Id, Guid OrganizationId, PaymentProvider Provider, UzsTiyinAmount Amount,
    UzsTiyinAmount Fee, Guid FxSnapshotId, decimal UzsTiyinPerUsd,
    UsdMicroAmount Credit, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record PaymentIntent(
    Guid Id, Guid OrganizationId, PaymentProvider Provider, Guid QuoteId,
    PaymentStatus Status, UzsTiyinAmount Amount, UzsTiyinAmount Fee,
    Guid FxSnapshotId, decimal UzsTiyinPerUsd, UsdMicroAmount Credit,
    string IdempotencyKey, string MerchantScope, string? ExternalTransactionId,
    int? ClickPrepareId, long? ProviderCreatedTimeUnixMs,
    DateTimeOffset CreatedAt, DateTimeOffset? BoundAt, DateTimeOffset? PaidAt,
    DateTimeOffset? CanceledAt, int? CancelReason);

public sealed record PaymentStatementEntry(
    PaymentIntent Intent, long ProviderCreatedTimeUnixMs);

public enum PaymentCommandStatus
{
    Accepted, Duplicate, NotFound, WrongAmount, Conflict, Expired,
    WrongProvider, WrongPrepareId, InvalidState
}

public sealed record PaymentCommandResult(PaymentCommandStatus Status, PaymentIntent? Intent);

public sealed record PaymentCreateIntentResult(PaymentIntent Intent, bool Duplicate, string? CheckoutUrl);

public sealed record PaymentReconciliationResult(Guid IntentId, string Outcome, bool CaseCreated);

public interface IPaymentStore
{
    Task<FxRateSnapshot?> FindLatestFxAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task InsertQuoteAsync(PaymentQuote quote, CancellationToken cancellationToken = default);
    Task<PaymentQuote?> FindQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default);
    Task<bool> TryInsertIntentAsync(PaymentIntent intent, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> FindIntentAsync(Guid intentId, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> FindByQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> FindByIdempotencyKeyAsync(Guid organizationId, string key, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> FindByExternalAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> LockIntentAsync(Guid intentId, CancellationToken cancellationToken = default);
    Task<int> NextClickPrepareIdAsync(CancellationToken cancellationToken = default);
    Task BindAsync(Guid intentId, string externalTransactionId, int? clickPrepareId,
        long? providerCreatedTimeUnixMs, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task SetStatusAsync(Guid intentId, PaymentStatus status, DateTimeOffset now,
        int? cancelReason, CancellationToken cancellationToken = default);
    Task<bool> HasTopUpCreditAsync(Guid intentId, CancellationToken cancellationToken = default);
    Task<bool> HasReversalAsync(Guid intentId, CancellationToken cancellationToken = default);
    Task<bool> HasProviderObservationAsync(Guid intentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentIntent>> ListForOrganizationAsync(Guid organizationId,
        int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentStatementEntry>> FindPaymeStatementAsync(string merchantScope,
        long fromUnixMs, long toUnixMs, CancellationToken cancellationToken = default);
    Task AppendCallbackReceiptAsync(PaymentProvider provider, Guid? intentId, string externalRequestId,
        byte[] requestHash, string responseCode, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> EnsureReconciliationCaseAsync(Guid intentId, string reason, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<PaymentQuote> CreateQuoteAsync(Guid accountId, Guid organizationId,
        PaymentProvider provider, UzsTiyinAmount amount, CancellationToken cancellationToken = default);
    Task<PaymentCreateIntentResult> CreateIntentAsync(Guid accountId, Guid organizationId, Guid quoteId,
        string idempotencyKey, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> GetIntentAsync(Guid accountId, Guid organizationId,
        Guid intentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentIntent>> ListIntentsAsync(Guid accountId, Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<PaymentCommandResult> CheckAsync(PaymentProvider provider, Guid intentId,
        UzsTiyinAmount amount, CancellationToken cancellationToken = default);
    Task<PaymentCommandResult> PrepareAsync(PaymentProvider provider, Guid intentId,
        string externalTransactionId, UzsTiyinAmount amount,
        long? providerCreatedTimeUnixMs, string externalRequestId,
        byte[] requestHash, CancellationToken cancellationToken = default);
    Task<PaymentCommandResult> CompleteAsync(PaymentProvider provider, string externalTransactionId,
        int? clickPrepareId, UzsTiyinAmount? amount, string externalRequestId, byte[] requestHash,
        CancellationToken cancellationToken = default);
    Task<PaymentCommandResult> CancelAsync(PaymentProvider provider, string externalTransactionId,
        int? clickPrepareId, UzsTiyinAmount? amount, int? reason, string externalRequestId, byte[] requestHash,
        CancellationToken cancellationToken = default);
    Task<PaymentIntent?> FindByExternalAsync(PaymentProvider provider,
        string externalTransactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentStatementEntry>> GetPaymeStatementAsync(long fromUnixMs,
        long toUnixMs, CancellationToken cancellationToken = default);
    Task<PaymentReconciliationResult> ReconcileAsync(Guid intentId, CancellationToken cancellationToken = default);
}
