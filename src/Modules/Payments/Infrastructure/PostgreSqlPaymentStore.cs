using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Infrastructure;

public sealed class PostgreSqlPaymentStore(FoundationDbContext db) : IPaymentStore
{
    public async Task<FxRateSnapshot?> FindLatestFxAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<BillingFxRateSnapshotEntity>().AsNoTracking()
            .Where(item => item.ObservedAt <= now)
            .OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return value is null ? null : new FxRateSnapshot(value.Id, value.Source,
            value.UzsTiyinPerUsd, value.ObservedAt);
    }

    public async Task InsertQuoteAsync(PaymentQuote quote, CancellationToken cancellationToken = default)
    {
        db.Set<PaymentQuoteEntity>().Add(new PaymentQuoteEntity
        {
            Id = quote.Id, OrganizationId = quote.OrganizationId, Provider = quote.Provider.ToString(),
            AmountTiyin = quote.Amount.Value, FeeTiyin = quote.Fee.Value,
            FxSnapshotId = quote.FxSnapshotId, UzsTiyinPerUsd = quote.UzsTiyinPerUsd,
            CreditMicroUsd = quote.Credit.Value, CreatedAt = quote.CreatedAt, ExpiresAt = quote.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PaymentQuote?> FindQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<PaymentQuoteEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == quoteId, cancellationToken);
        return value is null ? null : ToQuote(value);
    }

    public async Task<bool> TryInsertIntentAsync(PaymentIntent intent, CancellationToken cancellationToken = default)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payment.payment_intent
                (id, organization_id, quote_id, provider, status, amount_tiyin, fee_tiyin,
                 fx_snapshot_id, uzs_tiyin_per_usd, credit_micro_usd, idempotency_key,
                 merchant_scope, created_at)
            VALUES ({intent.Id}, {intent.OrganizationId}, {intent.QuoteId}, {intent.Provider.ToString()},
                {intent.Status.ToString()}, {intent.Amount.Value}, {intent.Fee.Value},
                {intent.FxSnapshotId}, {intent.UzsTiyinPerUsd}, {intent.Credit.Value},
                {intent.IdempotencyKey}, {intent.MerchantScope}, {intent.CreatedAt})
            ON CONFLICT DO NOTHING;
            """, cancellationToken);
        return inserted == 1;
    }

    public async Task<PaymentIntent?> FindIntentAsync(Guid intentId, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == intentId, cancellationToken);
        return value is null ? null : ToIntent(value);
    }

    public async Task<PaymentIntent?> FindByQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.QuoteId == quoteId, cancellationToken);
        return value is null ? null : ToIntent(value);
    }

    public async Task<PaymentIntent?> FindByIdempotencyKeyAsync(Guid organizationId, string key,
        CancellationToken cancellationToken = default)
    {
        var value = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.IdempotencyKey == key,
                cancellationToken);
        return value is null ? null : ToIntent(value);
    }

    public async Task<PaymentIntent?> FindByExternalAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default)
    {
        var providerName = provider.ToString();
        var value = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Provider == providerName
                && item.MerchantScope == merchantScope
                && item.ExternalTransactionId == externalTransactionId, cancellationToken);
        return value is null ? null : ToIntent(value);
    }

    public async Task<PaymentIntent?> LockIntentAsync(Guid intentId, CancellationToken cancellationToken = default)
    {
        var organizationId = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .Where(item => item.Id == intentId).Select(item => (Guid?)item.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (organizationId is null) return null;
        // Financial lock order: wallet first, then payment intent. A no-op UPDATE
        // takes a row lock without mutating the wallet version.
        if (await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE billing.wallet SET version = version WHERE organization_id = {organizationId.Value};",
                cancellationToken) != 1)
            throw new InvalidOperationException("Payment organization wallet does not exist.");
        var value = await db.Set<PaymentIntentEntity>().FromSqlInterpolated(
                $"SELECT * FROM payment.payment_intent WHERE id = {intentId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);
        return ToIntent(value);
    }

    public async Task<int> NextClickPrepareIdAsync(CancellationToken cancellationToken = default)
    {
        var value = await db.Database.SqlQueryRaw<long>(
            "SELECT nextval('payment.click_prepare_id_seq') AS \"Value\"").SingleAsync(cancellationToken);
        return checked((int)value);
    }

    public async Task BindAsync(Guid intentId, string externalTransactionId, int? clickPrepareId,
        long? providerCreatedTimeUnixMs, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE payment.payment_intent SET status = 'Created',
                external_transaction_id = {externalTransactionId}, click_prepare_id = {clickPrepareId},
                provider_created_time_unix_ms = {providerCreatedTimeUnixMs}, bound_at = {now}
            WHERE id = {intentId} AND status = 'Pending' AND external_transaction_id IS NULL;
            """, cancellationToken);
        if (count != 1) throw new InvalidOperationException("Payment intent binding raced or violated its state.");
    }

    public async Task SetStatusAsync(Guid intentId, PaymentStatus status, DateTimeOffset now,
        int? cancelReason, CancellationToken cancellationToken = default)
    {
        var count = status switch
        {
            PaymentStatus.Paid => await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE payment.payment_intent SET status = 'Paid', paid_at = {now}
                WHERE id = {intentId} AND status = 'Created';
                """, cancellationToken),
            PaymentStatus.Canceled => await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE payment.payment_intent SET status = 'Canceled', canceled_at = {now},
                    cancel_reason = {cancelReason}
                WHERE id = {intentId} AND status IN ('Created', 'Paid');
                """, cancellationToken),
            PaymentStatus.Expired => await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE payment.payment_intent SET status = 'Expired'
                WHERE id = {intentId} AND status = 'Pending';
                """, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        if (count != 1) throw new InvalidOperationException("Payment state transition raced or is invalid.");
    }

    public Task<bool> HasTopUpCreditAsync(Guid intentId, CancellationToken cancellationToken = default) =>
        db.Set<BillingLedgerEntryEntity>().AsNoTracking().AnyAsync(item => item.ReferenceType == "payment_intent"
            && item.ReferenceId == intentId && item.Type == LedgerEntryType.TopUp.ToString(), cancellationToken);

    public Task<bool> HasReversalAsync(Guid intentId, CancellationToken cancellationToken = default) =>
        db.Set<BillingReversalEntity>().AsNoTracking()
            .AnyAsync(item => item.ExternalReferenceId == intentId, cancellationToken);

    public async Task<IReadOnlyList<PaymentIntent>> ListForOrganizationAsync(Guid organizationId,
        int limit, CancellationToken cancellationToken = default) =>
        (await db.Set<PaymentIntentEntity>().AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Take(limit).ToListAsync(cancellationToken)).Select(ToIntent).ToArray();

    public async Task<IReadOnlyList<PaymentStatementEntry>> FindPaymeStatementAsync(string merchantScope,
        long fromUnixMs, long toUnixMs, CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<PaymentIntentEntity>().AsNoTracking()
            .Where(item => item.Provider == "Payme" && item.MerchantScope == merchantScope
                && item.ProviderCreatedTimeUnixMs >= fromUnixMs
                && item.ProviderCreatedTimeUnixMs <= toUnixMs)
            .OrderBy(item => item.ProviderCreatedTimeUnixMs).ThenBy(item => item.Id)
            .Take(10_001).ToListAsync(cancellationToken);
        if (rows.Count > 10_000)
            throw new InvalidOperationException("Payme statement exceeds the safe response limit; narrow the range.");
        return rows.Select(item => new PaymentStatementEntry(ToIntent(item),
            item.ProviderCreatedTimeUnixMs!.Value)).ToArray();
    }

    public async Task AppendCallbackReceiptAsync(PaymentProvider provider, Guid? intentId,
        string externalRequestId, byte[] requestHash, string responseCode, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        db.Set<PaymentCallbackReceiptEntity>().Add(new PaymentCallbackReceiptEntity
        {
            Id = Guid.CreateVersion7(), IntentId = intentId, Provider = provider.ToString(),
            ExternalRequestId = externalRequestId, RequestHash = requestHash,
            ResponseCode = responseCode, ReceivedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> EnsureReconciliationCaseAsync(Guid intentId, string reason, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payment.reconciliation_case (id, payment_intent_id, reason, status, created_at)
            VALUES ({Guid.CreateVersion7()}, {intentId}, {reason}, 'Open', {now})
            ON CONFLICT (payment_intent_id, reason) DO NOTHING;
            """, cancellationToken) == 1;

    private static PaymentQuote ToQuote(PaymentQuoteEntity value) => new(value.Id, value.OrganizationId,
        Enum.Parse<PaymentProvider>(value.Provider), new UzsTiyinAmount(value.AmountTiyin),
        new UzsTiyinAmount(value.FeeTiyin), value.FxSnapshotId, value.UzsTiyinPerUsd,
        new UsdMicroAmount(value.CreditMicroUsd), value.CreatedAt, value.ExpiresAt);

    private static PaymentIntent ToIntent(PaymentIntentEntity value) => new(value.Id, value.OrganizationId,
        Enum.Parse<PaymentProvider>(value.Provider), value.QuoteId, Enum.Parse<PaymentStatus>(value.Status),
        new UzsTiyinAmount(value.AmountTiyin), new UzsTiyinAmount(value.FeeTiyin),
        value.FxSnapshotId, value.UzsTiyinPerUsd, new UsdMicroAmount(value.CreditMicroUsd),
        value.IdempotencyKey, value.MerchantScope, value.ExternalTransactionId,
        value.ClickPrepareId, value.ProviderCreatedTimeUnixMs, value.CreatedAt,
        value.BoundAt, value.PaidAt, value.CanceledAt, value.CancelReason);
}
