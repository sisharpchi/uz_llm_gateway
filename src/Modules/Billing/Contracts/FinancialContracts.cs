using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Modules.Billing.Contracts;

public enum AdmissionStatus
{
    Reserved, Duplicate, PayloadConflict, InvalidScope, InsufficientWallet,
    ProjectBudgetExceeded, ApiKeyBudgetExceeded, SpendingHeld, InvalidFeePolicy
}

public enum FinalizationStatus { Settled, Released, AlreadyFinalized, PendingEvidence, NotDue, NotFound }

public sealed record ManagedAdmissionInput(
    PrepareUsageRequest Request, UsdMicroAmount MaximumCharge,
    Guid FeePolicyVersionId, DateTimeOffset ExpiresAt);

public sealed record Reservation(
    Guid Id, Guid RequestId, Guid OrganizationId, Guid ProjectId, Guid ApiKeyId,
    Guid FeePolicyVersionId, UsdMicroAmount Amount, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string Status);

public sealed record AdmissionResult(AdmissionStatus Status, Guid? RequestId, Reservation? Reservation);

public sealed record Settlement(
    Guid Id, Guid ReservationId, Guid RequestId, UsdMicroAmount ProviderCost,
    UsdMicroAmount UncappedCustomerCharge, UsdMicroAmount Charged,
    UsdMicroAmount UncollectedCharge, UsdMicroAmount PlatformExposure,
    bool UnresolvedUsage, string Outcome, DateTimeOffset CreatedAt);

public sealed record FinalizationResult(FinalizationStatus Status, Settlement? Settlement);

public sealed record BudgetPolicy(
    Guid Id, Guid OrganizationId, Guid ProjectId, Guid? ApiKeyId,
    UsdMicroAmount Limit, UsdMicroAmount Captured, UsdMicroAmount Reserved);

public sealed record ReversalResult(
    Guid Id, Guid OrganizationId, Guid ExternalReferenceId, UsdMicroAmount Amount,
    UsdMicroAmount Recovered, UsdMicroAmount DebtCreated, bool Duplicate);

public sealed record FinancialWalletState(Wallet Wallet, UsdMicroAmount RecoveryDebt, bool SpendingHeld);

public sealed record PricedUsageEvidence(
    Guid EvidenceId, Guid AttemptId, int InputTokens, int OutputTokens,
    int CachedInputTokens, int? ReasoningTokens, long InputRate,
    long OutputRate, long? CachedInputRate, string ExtraPricingJson,
    DateTimeOffset AttemptStartedAt, DateTimeOffset PriceEffectiveFrom,
    DateTimeOffset? PriceEffectiveTo);

public sealed record FinancialAttempt(Guid Id, ExecutionState Execution, bool HasEvidence);

public sealed record FinalizationContext(
    Reservation Reservation, FeePolicyVersion FeePolicy, Settlement? ExistingSettlement,
    IReadOnlyList<FinancialAttempt> Attempts,
    IReadOnlyList<UsageEvidence> Evidence,
    IReadOnlyList<PricedUsageEvidence> PricedEvidence);

public sealed record ChargeBreakdown(
    UsdMicroAmount ProviderCost, UsdMicroAmount UncappedCustomerCharge,
    UsdMicroAmount Charged, UsdMicroAmount UncollectedCharge,
    UsdMicroAmount PlatformExposure);

public interface IFinancialStore
{
    Task<AdmissionStatus> TryReserveAsync(Reservation reservation, CancellationToken cancellationToken = default);
    Task<FinalizationContext?> LockAndLoadAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<Guid?> FindReservationIdByEvidenceAsync(Guid evidenceId, CancellationToken cancellationToken = default);
    Task<Settlement> ApplyFinalizationAsync(FinalizationContext context, ChargeBreakdown charge,
        bool unresolvedUsage, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        UsdMicroAmount limit, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<FinancialWalletState?> FindWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RecoverAvailableDebtAsync(Guid organizationId, Guid referenceId,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> TryRecordLateExposureAsync(Guid settlementId, Guid evidenceId,
        UsdMicroAmount providerCost, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IFinancialService
{
    Task<AdmissionResult> ReserveAsync(ManagedAdmissionInput input, CancellationToken cancellationToken = default);
    Task<FinalizationResult> FinalizeAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<FinalizationResult> ReleaseUndispatchedAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<FinalizationResult> ReconcileAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        UsdMicroAmount limit, CancellationToken cancellationToken = default);
    Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, CancellationToken cancellationToken = default);
    Task<FinancialWalletState?> GetWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<bool> RecordLateExposureAsync(Guid evidenceId, CancellationToken cancellationToken = default);
}
