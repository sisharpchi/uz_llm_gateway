namespace UZLLM.Modules.Payments.Contracts;

/// <summary>Tenant-scoped payment state. A local callback is not independent merchant evidence.</summary>
public sealed record CustomerTopUp(Guid Id, Guid OrganizationId, string Provider, Guid QuoteId,
    string Status, string AmountTiyin, string FeeTiyin, string CreditMicroUsd,
    Guid FxSnapshotId, string UzsTiyinPerUsd, string? ExternalTransactionId,
    DateTimeOffset CreatedAt, DateTimeOffset? BoundAt, DateTimeOffset? PaidAt,
    DateTimeOffset? CanceledAt,
    bool HasCredit, bool HasReversal, bool HasOpenReconciliationCase);

/// <summary>A wallet-credit refund of an inference charge, never a cash payout.</summary>
public sealed record CustomerSettlementRefund(Guid Id, Guid SettlementId, Guid OrganizationId,
    string AmountMicroUsd, DateTimeOffset CreatedAt);

public interface ICustomerBillingReadStore
{
    Task<IReadOnlyList<CustomerTopUp>> ListTopUpsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerSettlementRefund>> ListRefundsAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
