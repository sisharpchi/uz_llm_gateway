using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Infrastructure;

public sealed class PostgreSqlPaymentReconciliationStore(FoundationDbContext db) : IPaymentReconciliationStore
{
    public async Task<ProviderObservation?> FindObservationAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.Set<PaymentProviderObservationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return entity is null ? null : ToContract(entity);
    }

    public async Task LockExternalTransactionAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default)
    {
        // Distinct report rows for the same merchant transaction must be evaluated serially.
        var key = $"payment-reconcile:{provider}:{merchantScope}:{externalTransactionId}";
        _ = await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_advisory_xact_lock(hashtextextended({key}, 0))")
            .SingleAsync(cancellationToken);
    }

    public async Task LockCaseExternalTransactionAsync(Guid caseId,
        CancellationToken cancellationToken = default)
    {
        var value = await db.Set<PaymentReconciliationCaseEntity>().AsNoTracking()
            .Where(item => item.Id == caseId)
            .Select(item => new { item.Provider, item.MerchantScope, item.ExternalTransactionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (value is { Provider: not null, MerchantScope: not null, ExternalTransactionId: not null })
            await LockExternalTransactionAsync(Enum.Parse<PaymentProvider>(value.Provider),
                value.MerchantScope, value.ExternalTransactionId, cancellationToken);
    }

    public async Task<ProviderObservation?> FindBySourceAsync(PaymentProvider provider, string merchantScope,
        string sourceReference, string rowReference, CancellationToken cancellationToken = default)
    {
        var providerName = provider.ToString();
        var entity = await db.Set<PaymentProviderObservationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Provider == providerName && value.MerchantScope == merchantScope
                && value.SourceReference == sourceReference && value.RowReference == rowReference,
                cancellationToken);
        return entity is null ? null : ToContract(entity);
    }

    public Task<bool> TryInsertObservationAsync(ProviderObservation observation,
        CancellationToken cancellationToken = default) => InsertAsync(observation, cancellationToken);

    private async Task<bool> InsertAsync(ProviderObservation value, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payment.provider_observation
                (id, payment_intent_id, provider, merchant_scope, source_reference, source_sha256,
                 row_reference, external_transaction_id, status, amount_tiyin, provider_observed_at,
                 recorded_at, recorded_by_account_id)
            VALUES ({value.Id}, {value.IntentId}, {value.Provider.ToString()}, {value.MerchantScope},
                {value.SourceReference}, {value.SourceSha256}, {value.RowReference},
                {value.ExternalTransactionId}, {value.Status.ToString()}, {value.Amount.Value},
                {value.ProviderObservedAt}, {value.RecordedAt}, {value.RecordedByAccountId})
            ON CONFLICT (provider, merchant_scope, source_reference, row_reference) DO NOTHING;
            """, ct) == 1;

    public Task<int> CountByExternalAsync(PaymentProvider provider, string merchantScope,
        string externalTransactionId, CancellationToken cancellationToken = default) =>
        db.Set<PaymentProviderObservationEntity>().AsNoTracking().CountAsync(value =>
            value.Provider == provider.ToString() && value.MerchantScope == merchantScope
                && value.ExternalTransactionId == externalTransactionId, cancellationToken);

    public async Task<Guid?> EnsureCaseAsync(Guid? intentId, Guid? observationId,
        PaymentProvider? provider, string? merchantScope, string? externalTransactionId,
        string reason, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var id = Guid.CreateVersion7();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payment.reconciliation_case
                (id, payment_intent_id, provider_observation_id, provider, merchant_scope,
                 external_transaction_id, reason, status, created_at)
            VALUES ({id}, {intentId}, {observationId}, {provider?.ToString()},
                {merchantScope}, {externalTransactionId}, {reason}, 'Open', {now})
            ON CONFLICT DO NOTHING;
            """, cancellationToken);
        return inserted == 1 ? id : null;
    }

    public async Task<IReadOnlyList<PaymentReconciliationCase>> ListCasesAsync(int limit,
        CancellationToken cancellationToken = default) =>
        await db.Set<PaymentReconciliationCaseEntity>().AsNoTracking()
            .OrderBy(value => value.Status == "Open" ? 0 : 1)
            .ThenByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id)
            .Take(limit)
            .Select(value => new PaymentReconciliationCase(value.Id, value.IntentId,
                value.Provider, value.ExternalTransactionId, value.Reason, value.Status,
                value.CreatedAt, value.ResolvedAt, value.ResolutionReference))
            .ToListAsync(cancellationToken);

    public async Task<bool> ResolveCaseAsync(Guid caseId, Guid actorId, string resolutionReference,
        DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await db.Set<PaymentReconciliationCaseEntity>()
            .Where(value => value.Id == caseId && value.Status == "Open")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, "Resolved")
                .SetProperty(value => value.ResolvedAt, (DateTimeOffset?)now)
                .SetProperty(value => value.ResolvedByAccountId, (Guid?)actorId)
                .SetProperty(value => value.ResolutionReference, resolutionReference),
                cancellationToken) == 1;

    private static ProviderObservation ToContract(PaymentProviderObservationEntity value) => new(
        value.Id, value.IntentId, Enum.Parse<PaymentProvider>(value.Provider), value.MerchantScope,
        value.SourceReference, value.SourceSha256, value.RowReference, value.ExternalTransactionId,
        Enum.Parse<ProviderObservationStatus>(value.Status), new UzsTiyinAmount(value.AmountTiyin),
        value.ProviderObservedAt, value.RecordedAt, value.RecordedByAccountId);
}
