using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class PostgreSqlSettlementRefundStore(FoundationDbContext db,
    IWalletLedgerStore ledger, IFinancialStore financial) : ISettlementRefundStore
{
    public async Task<SettlementRefund> ApplyAsync(Guid actorId, Guid settlementId,
        string refundKey, UsdMicroAmount amount, string reason, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Refund posting requires a PostgreSQL transaction.");
        if (actorId == Guid.Empty || settlementId == Guid.Empty || now.Offset != TimeSpan.Zero)
            throw new ArgumentException("A valid operator, settlement and UTC time are required.");
        refundKey = Normalize(refundKey, 8, 120, nameof(refundKey));
        reason = Normalize(reason, 8, 500, nameof(reason));
        if (amount.Value <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        var settlement = await db.Set<BillingSettlementEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == settlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Settlement not found.");
        var organizationId = settlement.OrganizationId;
        // Wallet-first serialization is shared with settlement and payment reversal.
        if (await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.wallet SET version = version WHERE organization_id = {organizationId};",
            cancellationToken) != 1)
            throw new InvalidOperationException("The settlement organization has no wallet.");

        var prior = await db.Set<BillingSettlementRefundEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.RefundKey == refundKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.SettlementId != settlementId || prior.AmountMicroUsd != amount.Value
                || prior.Reason != reason)
                throw new InvalidOperationException("Refund key was reused with different terms.");
            return ToContract(prior) with { Duplicate = true };
        }

        var refunded = await db.Set<BillingSettlementRefundEntity>().AsNoTracking()
            .Where(value => value.SettlementId == settlementId)
            .SumAsync(value => (long?)value.AmountMicroUsd, cancellationToken) ?? 0;
        SettlementRefundPolicy.EnsureAllowed(settlement.Outcome, settlement.ChargedMicroUsd,
            refunded, amount.Value);
        var refund = new BillingSettlementRefundEntity
        {
            Id = Guid.CreateVersion7(), SettlementId = settlementId, OrganizationId = organizationId,
            ActorAccountId = actorId, RefundKey = refundKey, AmountMicroUsd = amount.Value,
            Reason = reason, CreatedAt = now
        };
        db.Set<BillingSettlementRefundEntity>().Add(refund);
        await db.SaveChangesAsync(cancellationToken);

        var posting = await ledger.TryAppendAsync(new LedgerEntry(Guid.CreateVersion7(), organizationId,
            LedgerEntryType.Refund, new SignedUsdMicroAmount(amount.Value), "settlement_refund",
            refund.Id, JsonSerializer.Serialize(new { settlementId }), now), cancellationToken);
        if (posting != LedgerPostingStatus.Posted)
            throw new InvalidOperationException("The refund ledger counter-entry could not be posted.");
        // A refund must not bypass a payment-reversal spending hold.
        await financial.RecoverAvailableDebtAsync(organizationId, refund.Id, now, cancellationToken);
        return ToContract(refund);
    }

    public async Task<SettlementRefund?> FindAsync(Guid refundId, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<BillingSettlementRefundEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == refundId, cancellationToken);
        return value is null ? null : ToContract(value);
    }

    public async Task<IReadOnlyList<SettlementRefund>> ListAsync(Guid? organizationId, int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = db.Set<BillingSettlementRefundEntity>().AsNoTracking();
        if (organizationId is { } id) query = query.Where(value => value.OrganizationId == id);
        var values = await query.OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id).Take(limit).ToListAsync(cancellationToken);
        return values.Select(ToContract).ToArray();
    }

    private static SettlementRefund ToContract(BillingSettlementRefundEntity value) => new(
        value.Id, value.SettlementId, value.OrganizationId, value.ActorAccountId,
        value.RefundKey, new UsdMicroAmount(value.AmountMicroUsd), value.Reason, value.CreatedAt);

    private static string Normalize(string value, int minimum, int maximum, string name) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= minimum
            && value.Trim().Length <= maximum ? value.Trim()
            : throw new ArgumentException($"{name} must contain {minimum}–{maximum} characters.", name);
}
