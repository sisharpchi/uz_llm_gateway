namespace UZLLM.Management.Api.Administration;

/// <summary>Safe operator lookup result; no password or session material.</summary>
public sealed record AdminAccountResponse(Guid Id, string Email, string Status, bool EmailVerified,
    bool IsOperator, DateTimeOffset CreatedAt);

/// <summary>Organization support lookup with authoritative wallet balances.</summary>
public sealed record AdminOrganizationResponse(Guid Id, string Name, string Status,
    string? PostedBalanceMicroUsd, string? ReservedBalanceMicroUsd, DateTimeOffset CreatedAt);

/// <summary>Provider and mapping metadata; encrypted credentials are never returned.</summary>
public sealed record AdminProviderResponse(Guid Id, string Code, string Name, string Status,
    IReadOnlyList<AdminMappingResponse> Mappings, IReadOnlyList<AdminCredentialResponse> Credentials);

/// <summary>Provider-to-canonical-model mapping visible to operators.</summary>
public sealed record AdminMappingResponse(Guid Id, Guid ProviderId, Guid ModelId,
    string ModelCode, string ModelStatus, string UpstreamModelCode, string Status);

/// <summary>Non-secret credential metadata.</summary>
public sealed record AdminCredentialResponse(Guid Id, Guid ProviderId, string Status,
    string KeyVersion, DateTimeOffset CreatedAt);

/// <summary>Historical provider price version.</summary>
public sealed record AdminPriceResponse(Guid Id, Guid ProviderModelId, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, string InputPriceMicroUsdPerMillion,
    string OutputPriceMicroUsdPerMillion, string? CachedInputPriceMicroUsdPerMillion);

/// <summary>Read-only immutable ledger entry.</summary>
public sealed record AdminLedgerEntryResponse(Guid Id, Guid OrganizationId, string Type,
    string AmountMicroUsd, string ReferenceType, Guid ReferenceId, DateTimeOffset OccurredAt);

/// <summary>Payment state and locally verified callback evidence; independent provider state may be unknown.</summary>
public sealed record AdminPaymentResponse(Guid Id, Guid OrganizationId, string Provider,
    string LocalStatus, string ProviderObservation, string AmountTiyin,
    string CreditMicroUsd, string? ExternalTransactionId, bool HasCredit,
    bool HasReversal, string? ReconciliationReason, string? ReconciliationStatus,
    int CallbackCount, DateTimeOffset CreatedAt);

/// <summary>Global admission and checkout incident switches.</summary>
public sealed record AdminPlatformControlResponse(string Feature, bool Enabled, DateTimeOffset UpdatedAt);

/// <summary>Operator-only financial recovery state; amounts are USD micro-units.</summary>
public sealed record AdminFinancialRecoveryResponse(Guid ReservationId, Guid RequestId,
    Guid OrganizationId, string HeldMicroUsd, DateTimeOffset ExpiresAt,
    string State, DateTimeOffset? NextReviewAt);

public sealed record AdminFinancialExposureResponse(Guid SettlementId, Guid ReservationId,
    Guid OrganizationId, string PlatformExposureMicroUsd, string UncollectedChargeMicroUsd,
    bool UnresolvedUsage, DateTimeOffset CreatedAt);

public sealed record AdminFinancialDebtResponse(Guid OrganizationId, string OutstandingMicroUsd,
    bool SpendingHeld);

public sealed record AdminFinancialRiskResponse(DateTimeOffset DataAsOf,
    IReadOnlyList<AdminFinancialRecoveryResponse> Pending,
    IReadOnlyList<AdminFinancialExposureResponse> Exposure,
    IReadOnlyList<AdminFinancialDebtResponse> Debt);

/// <summary>Operator audit record; organization is null for global controls.</summary>
public sealed record AdminAuditResponse(Guid Id, Guid? OrganizationId, Guid ActorAccountId,
    string Action, string ResourceType, Guid? ResourceId, DateTimeOffset OccurredAt);

/// <summary>Create a provider in the canonical catalog.</summary>
public sealed record CreateAdminProviderRequest(string Code, string Name, string Reason);

/// <summary>Create a canonical model.</summary>
public sealed record CreateAdminModelRequest(string Code, string Name, int ContextLength,
    int MaxOutputTokens, string[] Capabilities, string Reason);

/// <summary>Create a provider mapping for a canonical model.</summary>
public sealed record CreateAdminMappingRequest(Guid ProviderId, Guid ModelId,
    string UpstreamModelCode, string? EndpointReference, string Reason);

/// <summary>Provision an encrypted platform credential; the secret is never returned.</summary>
public sealed record CreateAdminCredentialRequest(Guid ProviderId, string Secret, string Reason);

/// <summary>Publish a future-effective price without changing historical rate values.</summary>
public sealed record CreateAdminPriceRequest(Guid ProviderModelId, DateTimeOffset EffectiveFrom,
    long InputPriceMicroUsdPerMillion, long OutputPriceMicroUsdPerMillion,
    long? CachedInputPriceMicroUsdPerMillion, string Reason);

/// <summary>Change an operational status with a mandatory reason.</summary>
public sealed record SetAdminStatusRequest(bool Enabled, string Reason);

public interface IAdminReadStore
{
    Task<IReadOnlyList<AdminAccountResponse>> SearchAccountsAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminOrganizationResponse>> SearchOrganizationsAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPriceResponse>> ListPricesAsync(Guid mappingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminLedgerEntryResponse>> ListLedgerAsync(Guid organizationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPaymentResponse>> ListPaymentsAsync(Guid? organizationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminAuditResponse>> ListAuditAsync(int limit, CancellationToken cancellationToken);
    Task<AdminFinancialRiskResponse> GetFinancialRiskAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken);
    Task CloseCurrentPriceAsync(Guid mappingId, DateTimeOffset effectiveFrom,
        CancellationToken cancellationToken);
}

public interface IAdminService
{
    Task<IReadOnlyList<AdminAccountResponse>> SearchAccountsAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminOrganizationResponse>> SearchOrganizationsAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPriceResponse>> ListPricesAsync(Guid mappingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminLedgerEntryResponse>> ListLedgerAsync(Guid organizationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPaymentResponse>> ListPaymentsAsync(Guid? organizationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminAuditResponse>> ListAuditAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPlatformControlResponse>> ListControlsAsync(CancellationToken cancellationToken);
    Task<AdminFinancialRiskResponse> GetFinancialRiskAsync(int limit, CancellationToken cancellationToken);
    Task<Guid> CreateProviderAsync(Guid actorId, CreateAdminProviderRequest request, CancellationToken cancellationToken);
    Task<Guid> CreateModelAsync(Guid actorId, CreateAdminModelRequest request, CancellationToken cancellationToken);
    Task<Guid> CreateMappingAsync(Guid actorId, CreateAdminMappingRequest request, CancellationToken cancellationToken);
    Task<Guid> CreateCredentialAsync(Guid actorId, CreateAdminCredentialRequest request, CancellationToken cancellationToken);
    Task<Guid> SchedulePriceAsync(Guid actorId, CreateAdminPriceRequest request, CancellationToken cancellationToken);
    Task<bool> SetProviderEnabledAsync(Guid actorId, Guid providerId, SetAdminStatusRequest request, CancellationToken cancellationToken);
    Task<bool> SetModelEnabledAsync(Guid actorId, Guid modelId, SetAdminStatusRequest request, CancellationToken cancellationToken);
    Task<bool> SetMappingEnabledAsync(Guid actorId, Guid mappingId, SetAdminStatusRequest request, CancellationToken cancellationToken);
    Task<bool> SetCredentialEnabledAsync(Guid actorId, Guid credentialId, SetAdminStatusRequest request, CancellationToken cancellationToken);
    Task<bool> SetPlatformEnabledAsync(Guid actorId, string feature, SetAdminStatusRequest request, CancellationToken cancellationToken);
}
