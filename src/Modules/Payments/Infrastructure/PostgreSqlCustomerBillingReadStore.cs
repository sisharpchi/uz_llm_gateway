using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Infrastructure;

public sealed class PostgreSqlCustomerBillingReadStore(FoundationDbContext db) : ICustomerBillingReadStore
{
    public async Task<IReadOnlyList<CustomerTopUp>> ListTopUpsAsync(Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .Where(intent => intent.OrganizationId == organizationId)
            .OrderByDescending(intent => intent.CreatedAt).ThenByDescending(intent => intent.Id)
            .Take(50).Select(intent => new
            {
                intent.Id, intent.OrganizationId, intent.Provider, intent.QuoteId, intent.Status,
                intent.AmountTiyin, intent.FeeTiyin, intent.CreditMicroUsd, intent.FxSnapshotId,
                intent.UzsTiyinPerUsd, intent.ExternalTransactionId, intent.CreatedAt,
                intent.BoundAt, intent.PaidAt, intent.CanceledAt,
                HasCredit = db.Set<BillingLedgerEntryEntity>().Any(entry =>
                    entry.OrganizationId == organizationId && entry.Type == "TopUp"
                    && entry.ReferenceType == "payment_intent" && entry.ReferenceId == intent.Id),
                HasReversal = db.Set<BillingReversalEntity>().Any(reversal =>
                    reversal.OrganizationId == organizationId && reversal.ExternalReferenceId == intent.Id),
                HasOpenCase = db.Set<PaymentReconciliationCaseEntity>().Any(item =>
                    item.IntentId == intent.Id && item.Status == "Open")
            }).ToListAsync(cancellationToken);
        return rows.Select(row => new CustomerTopUp(row.Id, row.OrganizationId, row.Provider,
            row.QuoteId, row.Status, row.AmountTiyin.ToString(CultureInfo.InvariantCulture),
            row.FeeTiyin.ToString(CultureInfo.InvariantCulture),
            row.CreditMicroUsd.ToString(CultureInfo.InvariantCulture), row.FxSnapshotId,
            row.UzsTiyinPerUsd.ToString(CultureInfo.InvariantCulture), row.ExternalTransactionId,
            row.CreatedAt, row.BoundAt, row.PaidAt, row.CanceledAt,
            row.HasCredit, row.HasReversal, row.HasOpenCase)).ToArray();
    }

    public async Task<IReadOnlyList<CustomerSettlementRefund>> ListRefundsAsync(Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<BillingSettlementRefundEntity>().AsNoTracking()
            .Where(refund => refund.OrganizationId == organizationId)
            .OrderByDescending(refund => refund.CreatedAt).ThenByDescending(refund => refund.Id)
            .Take(50).Select(refund => new { refund.Id, refund.SettlementId,
                refund.OrganizationId, refund.AmountMicroUsd, refund.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new CustomerSettlementRefund(row.Id, row.SettlementId,
            row.OrganizationId, row.AmountMicroUsd.ToString(CultureInfo.InvariantCulture),
            row.CreatedAt)).ToArray();
    }
}
