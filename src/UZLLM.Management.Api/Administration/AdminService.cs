using System.Text.Json;
using System.Globalization;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Administration;

public sealed class AdminService(
    IAdminReadStore reads, ICatalogService catalog, IPlatformCredentialService credentials,
    IPricingHistoryService pricing,
    ISettlementRefundStore refunds,
    IPlatformControlStore controls, IAuditTrail audit, ITransactionCoordinator transactions,
    TimeProvider clock) : IAdminService
{
    public Task<IReadOnlyList<AdminAccountResponse>> SearchAccountsAsync(string query, CancellationToken ct) =>
        reads.SearchAccountsAsync(ValidateSearch(query), ct);
    public Task<IReadOnlyList<AdminOrganizationResponse>> SearchOrganizationsAsync(string query, CancellationToken ct) =>
        reads.SearchOrganizationsAsync(ValidateSearch(query), ct);
    public Task<IReadOnlyList<AdminProviderResponse>> ListProvidersAsync(CancellationToken ct) => reads.ListProvidersAsync(ct);
    public Task<IReadOnlyList<AdminPriceResponse>> ListPricesAsync(Guid mappingId, CancellationToken ct) =>
        reads.ListPricesAsync(RequiredId(mappingId), ct);
    public Task<IReadOnlyList<AdminLedgerEntryResponse>> ListLedgerAsync(Guid organizationId, int limit, CancellationToken ct) =>
        reads.ListLedgerAsync(RequiredId(organizationId), ValidateLimit(limit), ct);
    public Task<IReadOnlyList<AdminPaymentResponse>> ListPaymentsAsync(Guid? organizationId, int limit, CancellationToken ct) =>
        reads.ListPaymentsAsync(organizationId is { } id ? RequiredId(id) : null, ValidateLimit(limit), ct);
    public Task<IReadOnlyList<AdminAuditResponse>> ListAuditAsync(int limit, CancellationToken ct) =>
        reads.ListAuditAsync(ValidateLimit(limit), ct);
    public async Task<IReadOnlyList<AdminPlatformControlResponse>> ListControlsAsync(CancellationToken ct) =>
        (await controls.ListAsync(ct)).Select(value => new AdminPlatformControlResponse(
            value.Feature.ToString(), value.Enabled, value.UpdatedAt)).ToArray();
    public Task<AdminFinancialRiskResponse> GetFinancialRiskAsync(int limit, CancellationToken ct) =>
        reads.GetFinancialRiskAsync(clock.GetUtcNow(), ValidateLimit(limit), ct);

    public async Task<IReadOnlyList<AdminFeePolicyResponse>> ListFeePoliciesAsync(string policyCode, CancellationToken ct) =>
        (await pricing.ListFeePolicyVersionsAsync(policyCode, ct)).Select(value =>
            new AdminFeePolicyResponse(value.Id, value.PolicyCode, value.MarkupBasisPoints,
                value.FixedFee.Value.ToString(CultureInfo.InvariantCulture), value.EffectiveFrom,
                value.EffectiveTo, value.CreatedAt)).ToArray();

    public async Task<IReadOnlyList<AdminFxRateResponse>> ListFxRatesAsync(int limit, CancellationToken ct) =>
        (await pricing.ListFxRateSnapshotsAsync(ValidateLimit(limit), ct)).Select(value =>
            new AdminFxRateResponse(value.Id, value.Source,
                value.UzsTiyinPerUsd.ToString("G29", CultureInfo.InvariantCulture), value.ObservedAt)).ToArray();

    public async Task<IReadOnlyList<AdminSettlementRefundResponse>> ListSettlementRefundsAsync(
        Guid? organizationId, int limit, CancellationToken ct) =>
        (await refunds.ListAsync(organizationId is { } id ? RequiredId(id) : null,
            ValidateLimit(limit), ct)).Select(ToRefundResponse).ToArray();

    public async Task<AdminSettlementRefundResponse?> FindSettlementRefundAsync(Guid refundId,
        CancellationToken ct) =>
        await refunds.FindAsync(RequiredId(refundId), ct) is { } refund
            ? ToRefundResponse(refund) : null;

    public async Task<AdminSettlementRefundResponse> RefundSettlementAsync(Guid actorId,
        CreateSettlementRefundRequest request, CancellationToken ct)
    {
        RequiredId(actorId);
        RequiredId(request.SettlementId);
        var reason = ValidateReason(request.Reason);
        if (!long.TryParse(request.AmountMicroUsd, NumberStyles.None,
            CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new ArgumentException("Refund amount must be a positive USD micro-unit integer.");
        await using var transaction = await transactions.BeginAsync(ct);
        var refund = await refunds.ApplyAsync(actorId, request.SettlementId, request.RefundKey,
            new UsdMicroAmount(amount), reason, clock.GetUtcNow(), ct);
        if (!refund.Duplicate)
            await audit.RecordAsync(new AuditEventInput(refund.OrganizationId, actorId,
                "settlement.refunded", "settlement_refund", refund.Id, null,
                JsonSerializer.Serialize(new { settlementId = refund.SettlementId,
                    amountMicroUsd = refund.Amount.Value, reason })), ct);
        await transaction.CommitAsync(ct);
        return ToRefundResponse(refund);
    }

    public Task<Guid> CreateProviderAsync(Guid actorId, CreateAdminProviderRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "provider.created", "provider", async () =>
            (await catalog.AddProviderAsync(request.Code, request.Name, ct)).Id, ct);

    public async Task<Guid> CreateModelAsync(Guid actorId, CreateAdminModelRequest request, CancellationToken ct)
    {
        var capabilities = request.Capabilities.Select(value => Enum.TryParse<CatalogCapability>(value, true, out var parsed)
            && Enum.IsDefined(parsed) ? parsed : throw new ArgumentException("Unknown model capability.")).ToArray();
        return await CreateAsync(actorId, request.Reason, "model.created", "model", async () =>
            (await catalog.AddModelAsync(request.Code, request.Name, request.ContextLength,
                request.MaxOutputTokens, capabilities, ct)).Id, ct);
    }

    public Task<Guid> CreateMappingAsync(Guid actorId, CreateAdminMappingRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "mapping.created", "provider_model", async () =>
            (await catalog.AddProviderModelAsync(request.ProviderId, request.ModelId,
                request.UpstreamModelCode, request.EndpointReference, null, ct)).Id, ct);

    public Task<Guid> CreateCredentialAsync(Guid actorId, CreateAdminCredentialRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "credential.created", "provider_credential", async () =>
            (await credentials.CreateAsync(request.ProviderId, request.Secret, ct)).Id, ct);

    public Task<Guid> SchedulePriceAsync(Guid actorId, CreateAdminPriceRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "price.scheduled", "model_price", async () =>
        {
            if (request.EffectiveFrom <= clock.GetUtcNow())
                throw new ArgumentException("Price must take effect in the future.");
            await reads.CloseCurrentPriceAsync(request.ProviderModelId, request.EffectiveFrom, ct);
            return (await catalog.AddPriceAsync(request.ProviderModelId, request.EffectiveFrom, null,
                request.InputPriceMicroUsdPerMillion, request.OutputPriceMicroUsdPerMillion,
                request.CachedInputPriceMicroUsdPerMillion, null, ct)).Id;
        }, ct);

    public Task<Guid> PublishFeePolicyAsync(Guid actorId, PublishAdminFeePolicyRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "fee-policy.published", "fee_policy_version", async () =>
        {
            if (!long.TryParse(request.FixedFeeMicroUsd, NumberStyles.None,
                CultureInfo.InvariantCulture, out var fixedFee))
                throw new ArgumentException("Fixed fee must be a nonnegative USD micro-unit integer.");
            return (await pricing.ScheduleFeePolicyVersionAsync(request.PolicyCode,
                request.MarkupBasisPoints, new UsdMicroAmount(fixedFee), request.EffectiveFrom,
                request.EffectiveTo, ct)).Id;
        }, ct);

    public Task<Guid> PublishFxRateAsync(Guid actorId, PublishAdminFxRateRequest request, CancellationToken ct) =>
        CreateAsync(actorId, request.Reason, "fx-rate.published", "fx_rate_snapshot", async () =>
        {
            if (!decimal.TryParse(request.UzsTiyinPerUsd, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var rate))
                throw new ArgumentException("FX rate must be a positive fixed-precision decimal.");
            return (await pricing.PublishFxRateSnapshotAsync(request.Source, rate,
                request.ObservedAt, ct)).Id;
        }, ct);

    public Task<bool> SetProviderEnabledAsync(Guid actorId, Guid id, SetAdminStatusRequest request, CancellationToken ct) =>
        SetAsync(actorId, id, request, "provider.status", "provider",
            () => catalog.SetProviderStatusAsync(id, Status(request.Enabled), ct), ct);
    public Task<bool> SetModelEnabledAsync(Guid actorId, Guid id, SetAdminStatusRequest request, CancellationToken ct) =>
        SetAsync(actorId, id, request, "model.status", "model",
            () => catalog.SetModelStatusAsync(id, Status(request.Enabled), ct), ct);
    public Task<bool> SetMappingEnabledAsync(Guid actorId, Guid id, SetAdminStatusRequest request, CancellationToken ct) =>
        SetAsync(actorId, id, request, "mapping.status", "provider_model",
            () => catalog.SetProviderModelStatusAsync(id, Status(request.Enabled), ct), ct);
    public Task<bool> SetCredentialEnabledAsync(Guid actorId, Guid id, SetAdminStatusRequest request, CancellationToken ct) =>
        SetAsync(actorId, id, request, "credential.status", "provider_credential",
            () => credentials.SetStatusAsync(id, request.Enabled ? ProviderCredentialStatus.Active : ProviderCredentialStatus.Disabled, ct), ct);
    public Task<bool> SetPlatformEnabledAsync(Guid actorId, string feature, SetAdminStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformFeature>(feature, true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ArgumentException("Unknown platform feature.");
        return SetAsync(actorId, null, request, "platform.status", parsed.ToString(),
            () => controls.SetEnabledAsync(parsed, request.Enabled, clock.GetUtcNow(), ct), ct);
    }

    private async Task<Guid> CreateAsync(Guid actor, string reason, string action, string resource,
        Func<Task<Guid>> operation, CancellationToken ct)
    {
        RequiredId(actor);
        reason = ValidateReason(reason);
        await using var tx = await transactions.BeginAsync(ct);
        var id = await operation();
        await RecordAsync(actor, action, resource, id, reason, null, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    private async Task<bool> SetAsync(Guid actor, Guid? id, SetAdminStatusRequest request,
        string action, string resource, Func<Task<bool>> operation, CancellationToken ct)
    {
        RequiredId(actor);
        if (id is { } value) RequiredId(value);
        var reason = ValidateReason(request.Reason);
        await using var tx = await transactions.BeginAsync(ct);
        if (!await operation()) return false;
        await RecordAsync(actor, action, resource, id, reason, request.Enabled, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private Task RecordAsync(Guid actor, string action, string resource, Guid? id,
        string reason, bool? enabled, CancellationToken ct) => audit.RecordAsync(new AuditEventInput(
            null, actor, action, resource, id, null,
            JsonSerializer.Serialize(new { reason, enabled })), ct);

    private static AdminSettlementRefundResponse ToRefundResponse(SettlementRefund value) => new(
        value.Id, value.SettlementId, value.OrganizationId, value.ActorAccountId,
        value.RefundKey, value.Amount.Value.ToString(CultureInfo.InvariantCulture),
        value.Reason, value.CreatedAt, value.Duplicate);

    private static CatalogStatus Status(bool enabled) => enabled ? CatalogStatus.Active : CatalogStatus.Disabled;
    private static Guid RequiredId(Guid value) => value != Guid.Empty ? value : throw new ArgumentException("ID is required.");
    private static int ValidateLimit(int value) => value is >= 1 and <= 100 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    private static string ValidateSearch(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 2 and <= 100
        ? value.Trim() : throw new ArgumentException("Search must contain 2–100 characters.");
    private static string ValidateReason(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 8 and <= 500
        ? value.Trim() : throw new ArgumentException("An 8–500 character reason is required.");
}
