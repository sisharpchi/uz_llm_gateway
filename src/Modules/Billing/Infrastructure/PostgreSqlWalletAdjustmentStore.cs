using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class PostgreSqlWalletAdjustmentStore(FoundationDbContext db,
    IWalletLedgerStore ledger, IFinancialStore financial) : IWalletAdjustmentStore
{
    public async Task<WalletAdjustment> RequestAsync(Guid actorId, Guid organizationId,
        string adjustmentKey, UsdMicroAmount amount, string reason, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        RequireTransaction();
        Required(actorId); Required(organizationId);
        var key = WalletAdjustmentPolicy.Key(adjustmentKey);
        reason = WalletAdjustmentPolicy.Reason(reason);
        WalletAdjustmentPolicy.EnsureCredit(amount.Value);
        now = WalletAdjustmentPolicy.Utc(now);
        await LockWalletAsync(organizationId, cancellationToken);
        var prior = await db.Set<BillingWalletAdjustmentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId && value.AdjustmentKey == key, cancellationToken);
        if (prior is not null)
        {
            if (prior.RequestedByAccountId != actorId || prior.AmountMicroUsd != amount.Value || prior.Reason != reason)
                throw new InvalidOperationException("Adjustment key was reused with different terms or maker.");
            return (await FindAsync(prior.Id, cancellationToken))! with { Duplicate = true };
        }
        var request = new BillingWalletAdjustmentEntity { Id = Guid.CreateVersion7(), OrganizationId = organizationId,
            RequestedByAccountId = actorId, AdjustmentKey = key, AmountMicroUsd = amount.Value, Reason = reason, CreatedAt = now };
        db.Set<BillingWalletAdjustmentEntity>().Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return Response(request, null);
    }

    public async Task<WalletAdjustment> ApproveAsync(Guid actorId, Guid requestId, string reason,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        RequireTransaction(); Required(actorId); Required(requestId);
        reason = WalletAdjustmentPolicy.Reason(reason);
        now = WalletAdjustmentPolicy.Utc(now);
        var request = await db.Set<BillingWalletAdjustmentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Adjustment request not found.");
        WalletAdjustmentPolicy.EnsureDifferentOperator(request.RequestedByAccountId, actorId);
        await LockWalletAsync(request.OrganizationId, cancellationToken);
        var prior = await db.Set<BillingWalletAdjustmentApprovalEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.RequestId == requestId, cancellationToken);
        if (prior is not null)
        {
            if (prior.ApprovedByAccountId != actorId || prior.Reason != reason)
                throw new InvalidOperationException("Adjustment already approved with different checker or reason.");
            return Response(request, prior) with { Duplicate = true };
        }
        if (now < request.CreatedAt) throw new InvalidOperationException("Approval cannot precede the request.");
        var wallet = await ledger.FindWalletAsync(request.OrganizationId, cancellationToken)
            ?? throw new KeyNotFoundException("Wallet not found.");
        WalletAdjustmentPolicy.EnsureCapacity(wallet.PostedBalance.Value, request.AmountMicroUsd);
        var approval = new BillingWalletAdjustmentApprovalEntity { Id = Guid.CreateVersion7(), RequestId = request.Id,
            OrganizationId = request.OrganizationId, RequestedByAccountId = request.RequestedByAccountId,
            ApprovedByAccountId = actorId, Reason = reason, CreatedAt = now };
        db.Set<BillingWalletAdjustmentApprovalEntity>().Add(approval);
        await db.SaveChangesAsync(cancellationToken);
        var posted = await ledger.TryAppendAsync(new LedgerEntry(Guid.CreateVersion7(), request.OrganizationId,
            LedgerEntryType.AdjustmentCredit, new SignedUsdMicroAmount(request.AmountMicroUsd), "wallet_adjustment", request.Id,
            JsonSerializer.Serialize(new { approvalId = approval.Id }), now), cancellationToken);
        if (posted != LedgerPostingStatus.Posted) throw new InvalidOperationException("Adjustment posting failed.");
        await financial.RecoverAvailableDebtAsync(request.OrganizationId, request.Id, now, cancellationToken);
        return Response(request, approval);
    }

    public async Task<WalletAdjustment?> FindAsync(Guid requestId, CancellationToken cancellationToken)
    {
        Required(requestId);
        var request = await db.Set<BillingWalletAdjustmentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == requestId, cancellationToken);
        if (request is null) return null;
        var approval = await db.Set<BillingWalletAdjustmentApprovalEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.RequestId == requestId, cancellationToken);
        return Response(request, approval);
    }

    public async Task<IReadOnlyList<WalletAdjustment>> ListAsync(Guid? organizationId, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (organizationId is { } id) Required(id);
        var query = db.Set<BillingWalletAdjustmentEntity>().AsNoTracking();
        if (organizationId.HasValue) query = query.Where(value => value.OrganizationId == organizationId.Value);
        var requests = await query.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id)
            .Take(limit).ToListAsync(cancellationToken);
        var ids = requests.Select(value => value.Id).ToArray();
        var approvals = await db.Set<BillingWalletAdjustmentApprovalEntity>().AsNoTracking()
            .Where(value => ids.Contains(value.RequestId)).ToDictionaryAsync(value => value.RequestId, cancellationToken);
        return requests.Select(value => Response(value, approvals.GetValueOrDefault(value.Id))).ToArray();
    }

    private async Task LockWalletAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.wallet SET version = version WHERE organization_id = {organizationId};", cancellationToken) != 1)
            throw new KeyNotFoundException("Organization wallet not found.");
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Adjustment requires a PostgreSQL transaction.");
    }
    private static void Required(Guid id) { if (id == Guid.Empty) throw new ArgumentException("ID is required."); }
    private static WalletAdjustment Response(BillingWalletAdjustmentEntity value, BillingWalletAdjustmentApprovalEntity? approval) =>
        new(value.Id, value.OrganizationId, value.RequestedByAccountId, value.AdjustmentKey,
            new UsdMicroAmount(value.AmountMicroUsd), value.Reason, value.CreatedAt,
            approval is null ? null : new(approval.Id, approval.ApprovedByAccountId, approval.Reason, approval.CreatedAt));
}
