using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Administration;

public sealed class PostgreSqlAdminReadStore(FoundationDbContext db) : IAdminReadStore
{
    public async Task<IReadOnlyList<AdminAccountResponse>> SearchAccountsAsync(string query,
        CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(query)}%";
        return await db.Set<IdentityAccountEntity>().AsNoTracking()
            .Where(value => EF.Functions.ILike(value.Email, pattern, "\\"))
            .OrderBy(value => value.Email).Take(50)
            .Select(value => new AdminAccountResponse(value.Id, value.Email, value.Status,
                value.EmailVerifiedAt != null, value.OperatorAccess != null && value.OperatorAccess.IsActive,
                value.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminOrganizationResponse>> SearchOrganizationsAsync(string query,
        CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(query)}%";
        var rows = await db.Set<OrganizationEntity>().AsNoTracking()
            .Where(value => EF.Functions.ILike(value.Name, pattern, "\\"))
            .OrderBy(value => value.Name).Take(50)
            .Select(value => new
            {
                value.Id, value.Name, value.Status, value.CreatedAt,
                Posted = db.Set<BillingWalletEntity>().Where(wallet => wallet.OrganizationId == value.Id)
                    .Select(wallet => (long?)wallet.PostedBalanceMicroUsd).SingleOrDefault(),
                Reserved = db.Set<BillingWalletEntity>().Where(wallet => wallet.OrganizationId == value.Id)
                    .Select(wallet => (long?)wallet.ReservedBalanceMicroUsd).SingleOrDefault()
            }).ToListAsync(cancellationToken);
        return rows.Select(value => new AdminOrganizationResponse(value.Id, value.Name, value.Status,
            value.Posted?.ToString(CultureInfo.InvariantCulture),
            value.Reserved?.ToString(CultureInfo.InvariantCulture), value.CreatedAt)).ToArray();
    }

    public async Task<IReadOnlyList<AdminProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = await db.Set<CatalogProviderEntity>().AsNoTracking()
            .OrderBy(value => value.Code).Take(100).ToListAsync(cancellationToken);
        var mappings = await db.Set<CatalogProviderModelEntity>().AsNoTracking()
            .OrderBy(value => value.ProviderId).ThenBy(value => value.Model.CanonicalCode)
            .Select(value => new AdminMappingResponse(value.Id, value.ProviderId, value.ModelId,
                value.Model.CanonicalCode, value.Model.Status, value.UpstreamModelCode, value.Status))
            .Take(2000).ToListAsync(cancellationToken);
        var credentials = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == null && value.CredentialType == "Platform")
            .OrderBy(value => value.ProviderId).ThenBy(value => value.CreatedAt)
            .Select(value => new AdminCredentialResponse(value.Id, value.ProviderId, value.Status,
                value.KeyVersion, value.CreatedAt))
            .Take(1000).ToListAsync(cancellationToken);
        return providers.Select(value => new AdminProviderResponse(value.Id, value.Code, value.Name,
            value.Status, mappings.Where(mapping => mapping.ProviderId == value.Id).ToArray(),
            credentials.Where(credential => credential.ProviderId == value.Id).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<AdminPriceResponse>> ListPricesAsync(Guid mappingId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<CatalogModelPriceEntity>().AsNoTracking()
            .Where(value => value.ProviderModelId == mappingId)
            .OrderByDescending(value => value.EffectiveFrom).Take(100).ToListAsync(cancellationToken);
        return rows.Select(value => new AdminPriceResponse(value.Id, value.ProviderModelId,
            value.EffectiveFrom, value.EffectiveTo,
            value.InputPriceMicroUsdPerMillion.ToString(CultureInfo.InvariantCulture),
            value.OutputPriceMicroUsdPerMillion.ToString(CultureInfo.InvariantCulture),
            value.CachedInputPriceMicroUsdPerMillion?.ToString(CultureInfo.InvariantCulture))).ToArray();
    }

    public async Task<IReadOnlyList<AdminLedgerEntryResponse>> ListLedgerAsync(Guid organizationId,
        int limit, CancellationToken cancellationToken)
    {
        var rows = await db.Set<BillingLedgerEntryEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId)
            .OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.Id)
            .Take(limit).ToListAsync(cancellationToken);
        return rows.Select(value => new AdminLedgerEntryResponse(value.Id, value.OrganizationId,
            value.Type, value.AmountMicroUsd.ToString(CultureInfo.InvariantCulture),
            value.ReferenceType, value.ReferenceId, value.OccurredAt)).ToArray();
    }

    public async Task<IReadOnlyList<AdminPaymentResponse>> ListPaymentsAsync(Guid? organizationId,
        int limit, CancellationToken cancellationToken)
    {
        var query = db.Set<PaymentIntentEntity>().AsNoTracking();
        if (organizationId is { } id) query = query.Where(value => value.OrganizationId == id);
        var rows = await query.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id)
            .Take(limit).Select(value => new
            {
                value.Id, value.OrganizationId, value.Provider, value.Status,
                value.AmountTiyin, value.CreditMicroUsd, value.ExternalTransactionId, value.CreatedAt,
                HasCredit = db.Set<BillingLedgerEntryEntity>().Any(ledger => ledger.OrganizationId == value.OrganizationId
                    && ledger.Type == "TopUp" && ledger.ReferenceType == "payment_intent" && ledger.ReferenceId == value.Id),
                HasReversal = db.Set<BillingReversalEntity>().Any(reversal => reversal.OrganizationId == value.OrganizationId
                    && reversal.ExternalReferenceId == value.Id),
                CallbackCount = db.Set<PaymentCallbackReceiptEntity>().Count(callback => callback.IntentId == value.Id),
                AcceptedCallback = db.Set<PaymentCallbackReceiptEntity>().Any(callback => callback.IntentId == value.Id
                    && (callback.ResponseCode == "Accepted" || callback.ResponseCode == "Duplicate")),
                Case = db.Set<PaymentReconciliationCaseEntity>().Where(item => item.IntentId == value.Id)
                    .OrderByDescending(item => item.CreatedAt)
                    .Select(item => new { item.Reason, item.Status }).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        return rows.Select(value => new AdminPaymentResponse(value.Id, value.OrganizationId,
            value.Provider, value.Status, value.AcceptedCallback ? "VerifiedCallbackSeen" : "Unverified",
            value.AmountTiyin.ToString(CultureInfo.InvariantCulture),
            value.CreditMicroUsd.ToString(CultureInfo.InvariantCulture), value.ExternalTransactionId,
            value.HasCredit, value.HasReversal, value.Case?.Reason, value.Case?.Status,
            value.CallbackCount, value.CreatedAt)).ToArray();
    }

    public async Task<IReadOnlyList<AdminAuditResponse>> ListAuditAsync(int limit,
        CancellationToken cancellationToken) =>
        await db.Set<AuditEventEntity>().AsNoTracking().OrderByDescending(value => value.OccurredAt)
            .ThenByDescending(value => value.Id).Take(limit)
            .Select(value => new AdminAuditResponse(value.Id, value.OrganizationId,
                value.ActorAccountId, value.Action, value.ResourceType, value.ResourceId,
                value.OccurredAt)).ToListAsync(cancellationToken);

    public async Task<AdminFinancialRiskResponse> GetFinancialRiskAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken)
    {
        var reservations = await db.Set<BillingReservationEntity>().AsNoTracking()
            .Where(value => value.Status == "Reserved" && value.ExpiresAt <= now)
            .OrderBy(value => value.ExpiresAt).ThenBy(value => value.Id).Take(limit)
            .Select(value => new { value.Id, value.RequestId, value.OrganizationId,
                value.AmountMicroUsd, value.ExpiresAt }).ToListAsync(cancellationToken);
        var requestIds = reservations.Select(value => value.RequestId).ToArray();
        var attempts = await db.Set<UsageAttemptEntity>().AsNoTracking()
            .Where(value => requestIds.Contains(value.RequestId))
            .Select(value => new { value.Id, value.RequestId, value.ExecutionState })
            .ToListAsync(cancellationToken);
        var evidence = await db.Set<UsageEvidenceEntity>().AsNoTracking()
            .Where(value => requestIds.Contains(value.RequestId))
            .Select(value => new { value.AttemptId, value.RequestId, value.State,
                value.ReconcileAfter }).ToListAsync(cancellationToken);
        var pending = reservations.Select(reservation =>
        {
            var relevantAttempts = attempts.Where(value => value.RequestId == reservation.RequestId).ToArray();
            var relevantEvidence = evidence.Where(value => value.RequestId == reservation.RequestId).ToArray();
            var verifiedIds = relevantEvidence.Where(value => value.State == "Verified")
                .Select(value => value.AttemptId).ToHashSet();
            var unknown = relevantEvidence.Where(value => value.State == "Unknown"
                && !verifiedIds.Contains(value.AttemptId)).ToArray();
            var missing = relevantAttempts.Any(value => value.ExecutionState is not
                ("Prepared" or "RejectedBeforeExecution")
                && !relevantEvidence.Any(item => item.AttemptId == value.Id));
            var state = missing ? "MissingEvidence" : unknown.Length != 0 ? "UnknownEvidence"
                : verifiedIds.Count != 0 ? "PendingSettlement" : "Undispatched";
            DateTimeOffset? nextReview = unknown.Length == 0 ? null :
                unknown.Select(value => value.ReconcileAfter ?? reservation.ExpiresAt)
                    .Append(reservation.ExpiresAt.AddHours(24)).Max();
            return new AdminFinancialRecoveryResponse(reservation.Id, reservation.RequestId,
                reservation.OrganizationId, reservation.AmountMicroUsd.ToString(CultureInfo.InvariantCulture),
                reservation.ExpiresAt, state, nextReview);
        }).ToArray();

        var settlements = await db.Set<BillingSettlementEntity>().AsNoTracking()
            .Where(value => value.UnresolvedUsage || value.PlatformExposureMicroUsd > 0
                || db.Set<BillingPlatformExposureEntity>().Any(late => late.SettlementId == value.Id))
            .OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id)
            .Take(limit).Select(value => new { value.Id, value.ReservationId, value.OrganizationId,
                value.PlatformExposureMicroUsd, value.UncollectedChargeMicroUsd,
                value.UnresolvedUsage, value.CreatedAt }).ToListAsync(cancellationToken);
        var settlementIds = settlements.Select(value => value.Id).ToArray();
        var lateExposure = await db.Set<BillingPlatformExposureEntity>().AsNoTracking()
            .Where(value => settlementIds.Contains(value.SettlementId))
            .GroupBy(value => value.SettlementId)
            .Select(group => new { SettlementId = group.Key,
                Amount = group.Sum(value => value.ProviderCostMicroUsd) })
            .ToDictionaryAsync(value => value.SettlementId, value => value.Amount, cancellationToken);
        var exposure = settlements.Select(value => new AdminFinancialExposureResponse(value.Id,
            value.ReservationId, value.OrganizationId,
            checked(value.PlatformExposureMicroUsd + lateExposure.GetValueOrDefault(value.Id))
                .ToString(CultureInfo.InvariantCulture),
            value.UncollectedChargeMicroUsd.ToString(CultureInfo.InvariantCulture),
            value.UnresolvedUsage, value.CreatedAt)).ToArray();
        var debts = await db.Set<BillingRecoveryDebtEntity>().AsNoTracking()
            .Where(value => value.OutstandingMicroUsd > 0)
            .OrderByDescending(value => value.OutstandingMicroUsd).ThenBy(value => value.OrganizationId)
            .Take(limit).Select(value => new { value.OrganizationId,
                value.OutstandingMicroUsd, value.SpendingHeld }).ToListAsync(cancellationToken);
        return new AdminFinancialRiskResponse(now, pending, exposure,
            debts.Select(value => new AdminFinancialDebtResponse(value.OrganizationId,
                value.OutstandingMicroUsd.ToString(CultureInfo.InvariantCulture),
                value.SpendingHeld)).ToArray());
    }

    public async Task CloseCurrentPriceAsync(Guid mappingId, DateTimeOffset effectiveFrom,
        CancellationToken cancellationToken)
    {
        // Lock the mapping first so concurrent operator schedules for the same mapping serialize.
        var mapping = await db.Set<CatalogProviderModelEntity>()
            .FromSqlInterpolated($"SELECT * FROM catalog.provider_model WHERE id = {mappingId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (mapping is null) throw new KeyNotFoundException("Provider mapping was not found.");
        var latest = await db.Set<CatalogModelPriceEntity>().AsNoTracking()
            .Where(value => value.ProviderModelId == mappingId)
            .OrderByDescending(value => value.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return;
        if (latest.EffectiveFrom >= effectiveFrom)
            throw new InvalidOperationException("Prices must be scheduled in increasing effective-time order.");
        if (latest.EffectiveTo is { } end)
        {
            if (end != effectiveFrom)
                throw new InvalidOperationException("The latest price interval does not meet the requested start.");
            return;
        }
        await db.Set<CatalogModelPriceEntity>().Where(value => value.Id == latest.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.EffectiveTo,
                (DateTimeOffset?)effectiveFrom), cancellationToken);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\")
        .Replace("%", "\\%").Replace("_", "\\_");
}
