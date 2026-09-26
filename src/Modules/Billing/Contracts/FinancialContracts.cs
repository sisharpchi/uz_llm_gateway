using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Modules.Billing.Contracts;

public enum AdmissionStatus
{
    Reserved, Duplicate, PayloadConflict, InvalidScope, InsufficientWallet,
    ProjectBudgetExceeded, ApiKeyBudgetExceeded, SpendingHeld, InvalidFeePolicy
    , ByokSpendExceeded, ByokUnavailable
}

public enum FinalizationStatus { Settled, Released, AlreadyFinalized, PendingEvidence, NotDue, NotFound }

public sealed record ManagedAdmissionInput(
    PrepareUsageRequest Request, UsdMicroAmount MaximumCharge,
    Guid FeePolicyVersionId, DateTimeOffset ExpiresAt);

public sealed record ByokAdmissionInput(
    PrepareUsageRequest Request, UsdMicroAmount MaximumWalletCharge,
    Guid FeePolicyVersionId, Guid ByokFeePolicyVersionId, Guid CredentialId,
    UsdMicroAmount MaximumExternalSpend, bool AllowManagedFallback,
    DateTimeOffset ExpiresAt);

public sealed record Reservation(
    Guid Id, Guid RequestId, Guid OrganizationId, Guid ProjectId, Guid ApiKeyId,
    Guid FeePolicyVersionId, UsdMicroAmount Amount, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string Status, Guid? ByokCredentialId = null,
    Guid? ByokFeePolicyVersionId = null, UsdMicroAmount MaximumExternalSpend = default,
    bool AllowManagedFallback = false);

public sealed record AdmissionResult(AdmissionStatus Status, Guid? RequestId, Reservation? Reservation);

public sealed record Settlement(
    Guid Id, Guid ReservationId, Guid RequestId, UsdMicroAmount ProviderCost,
    UsdMicroAmount UncappedCustomerCharge, UsdMicroAmount Charged,
    UsdMicroAmount UncollectedCharge, UsdMicroAmount PlatformExposure,
    bool UnresolvedUsage, string Outcome, DateTimeOffset CreatedAt,
    UsdMicroAmount ExternalProviderSpend = default);

public sealed record FinalizationResult(FinalizationStatus Status, Settlement? Settlement);

public sealed record BudgetPolicy(
    Guid Id, Guid OrganizationId, Guid ProjectId, Guid? ApiKeyId,
    BudgetPeriod Period, BudgetWindow Window, UsdMicroAmount Limit,
    UsdMicroAmount Captured, UsdMicroAmount Reserved);

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

public sealed record FinancialAttempt(Guid Id, ExecutionState Execution, bool HasEvidence,
    Guid? CredentialId = null, string? CredentialType = null,
    bool CredentialProviderMatches = true);

public sealed record FinalizationContext(
    Reservation Reservation, FeePolicyVersion FeePolicy, Settlement? ExistingSettlement,
    IReadOnlyList<FinancialAttempt> Attempts,
    IReadOnlyList<UsageEvidence> Evidence,
    IReadOnlyList<PricedUsageEvidence> PricedEvidence,
    FeePolicyVersion? ByokFeePolicy = null);

public sealed record ChargeBreakdown(
    UsdMicroAmount ProviderCost, UsdMicroAmount UncappedCustomerCharge,
    UsdMicroAmount Charged, UsdMicroAmount UncollectedCharge,
    UsdMicroAmount PlatformExposure, UsdMicroAmount ExternalProviderSpend = default);

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
    Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        BudgetPeriod period, UsdMicroAmount limit, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetPolicy>> ListBudgetsAsync(Guid organizationId, Guid projectId,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<FinancialWalletState?> FindWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RecoverAvailableDebtAsync(Guid organizationId, Guid referenceId,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> TryRecordLateExposureAsync(Guid settlementId, Guid evidenceId,
        UsdMicroAmount providerCost, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<bool> TryRecordLateExternalSpendAsync(Guid settlementId, Guid evidenceId,
        Guid credentialId, UsdMicroAmount providerCost, DateTimeOffset now,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public interface IFinancialService
{
    Task<AdmissionResult> ReserveAsync(ManagedAdmissionInput input, CancellationToken cancellationToken = default);
    Task<AdmissionResult> ReserveByokAsync(ByokAdmissionInput input,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("BYOK admission is not available in this implementation.");
    Task<FinalizationResult> FinalizeAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<FinalizationResult> ReleaseUndispatchedAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<FinalizationResult> ReconcileAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        UsdMicroAmount limit, CancellationToken cancellationToken = default);
    Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        BudgetPeriod period, UsdMicroAmount limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetPolicy>> ListBudgetsAsync(Guid organizationId, Guid projectId,
        CancellationToken cancellationToken = default);
    Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, CancellationToken cancellationToken = default);
    Task<FinancialWalletState?> GetWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<bool> RecordLateExposureAsync(Guid evidenceId, CancellationToken cancellationToken = default);
}
