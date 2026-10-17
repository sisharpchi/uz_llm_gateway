namespace UZLLM.Modules.Billing.Contracts;

/// <summary>Immutable USD-credit request and its optional single checker approval.</summary>
public sealed record WalletAdjustment(Guid Id, Guid OrganizationId, Guid RequestedByAccountId,
    string AdjustmentKey, UsdMicroAmount Amount, string Reason, DateTimeOffset CreatedAt,
    WalletAdjustmentApproval? Approval, bool Duplicate = false);

public sealed record WalletAdjustmentApproval(Guid Id, Guid ApprovedByAccountId,
    string Reason, DateTimeOffset CreatedAt);

public interface IWalletAdjustmentStore
{
    // Mutations require the caller's shared PostgreSQL transaction; no provider I/O.
    Task<WalletAdjustment> RequestAsync(Guid actorId, Guid organizationId, string adjustmentKey,
        UsdMicroAmount amount, string reason, DateTimeOffset now, CancellationToken cancellationToken);
    Task<WalletAdjustment> ApproveAsync(Guid actorId, Guid requestId, string reason,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<WalletAdjustment?> FindAsync(Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WalletAdjustment>> ListAsync(Guid? organizationId, int limit, CancellationToken cancellationToken);
}
