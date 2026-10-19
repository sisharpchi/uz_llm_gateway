using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Infrastructure;

public sealed class PostgreSqlStatementPaymentEvidenceReader(FoundationDbContext db) : IStatementPaymentEvidenceReader
{
    public async Task<StatementPaymentSources> ReadAsync(Guid organizationId, IReadOnlyCollection<Guid> intentIds,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || intentIds.Count > 100_000)
            throw new ArgumentException("A tenant and bounded source membership are required.");
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead)
            throw new InvalidOperationException("Payment evidence requires the caller's Repeatable Read snapshot.");
        var ids = intentIds.Distinct().ToArray();
        var items = await (from intent in db.Set<PaymentIntentEntity>().AsNoTracking()
            where intent.OrganizationId == organizationId && ids.Contains(intent.Id)
            join quote in db.Set<PaymentQuoteEntity>().AsNoTracking()
                on new { intent.QuoteId, intent.OrganizationId } equals new { QuoteId = quote.Id, quote.OrganizationId }
            join fx in db.Set<BillingFxRateSnapshotEntity>().AsNoTracking() on intent.FxSnapshotId equals fx.Id
            select new StatementPaymentEvidence(intent.Id, intent.OrganizationId, quote.Id, intent.FxSnapshotId,
                intent.AmountTiyin, intent.FeeTiyin, intent.UzsTiyinPerUsd, intent.CreditMicroUsd,
                intent.AmountTiyin == quote.AmountTiyin && intent.FeeTiyin == quote.FeeTiyin
                && intent.CreditMicroUsd == quote.CreditMicroUsd && intent.FxSnapshotId == quote.FxSnapshotId
                && intent.UzsTiyinPerUsd == quote.UzsTiyinPerUsd && intent.UzsTiyinPerUsd == fx.UzsTiyinPerUsd
                && intent.Provider == quote.Provider)).ToListAsync(cancellationToken);
        var mismatches = await (from item in db.Set<PaymentReconciliationCaseEntity>().AsNoTracking()
            join intent in db.Set<PaymentIntentEntity>().AsNoTracking() on item.IntentId equals intent.Id
            where intent.OrganizationId == organizationId && item.Status == "Open"
            select item.Id).CountAsync(cancellationToken);
        var unmatched = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .CountAsync(intent => intent.OrganizationId == organizationId
                && (intent.Status == "Paid" || intent.PaidAt != null)
                && !db.Set<BillingLedgerEntryEntity>().Any(entry => entry.OrganizationId == organizationId
                    && entry.Type == "TopUp" && entry.ReferenceType == "payment_intent" && entry.ReferenceId == intent.Id
                    && entry.AmountMicroUsd == intent.CreditMicroUsd), cancellationToken);
        return new(items, mismatches, unmatched);
    }
}
