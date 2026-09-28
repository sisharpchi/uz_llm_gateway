namespace UZLLM.Modules.Billing.Contracts;

/// <summary>A wallet-credit counter-entry against one finalized customer charge, not a cash payout.</summary>
public sealed record SettlementRefund(Guid Id, Guid SettlementId, Guid OrganizationId,
    Guid ActorAccountId, string RefundKey, UsdMicroAmount Amount, string Reason,
    DateTimeOffset CreatedAt, bool Duplicate = false);

public interface ISettlementRefundStore
{
    /// <summary>Requires the caller's PostgreSQL transaction; also posts the ledger and recovers debt.</summary>
    Task<SettlementRefund> ApplyAsync(Guid actorId, Guid settlementId, string refundKey,
        UsdMicroAmount amount, string reason, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<SettlementRefund?> FindAsync(Guid refundId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettlementRefund>> ListAsync(Guid? organizationId, int limit,
        CancellationToken cancellationToken = default);
}
