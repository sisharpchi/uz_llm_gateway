namespace UZLLM.Modules.Catalog.Contracts;

public enum CatalogStatus
{
    Active,
    Disabled
}

public enum CatalogCapability
{
    Text,
    Vision,
    Tools,
    StructuredOutput,
    Reasoning,
    Embeddings,
    ImageGeneration,
    Audio
}

public sealed record CatalogProvider(
    Guid Id,
    string Code,
    string Name,
    CatalogStatus Status,
    DateTimeOffset CreatedAt);

public sealed record CanonicalModel(
    Guid Id,
    string CanonicalCode,
    string DisplayName,
    int ContextLength,
    int MaxOutputTokens,
    IReadOnlyList<CatalogCapability> Capabilities,
    CatalogStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ProviderModel(
    Guid Id,
    Guid ProviderId,
    Guid ModelId,
    string UpstreamModelCode,
    string? EndpointReference,
    CatalogStatus Status,
    IReadOnlyList<CatalogCapability> CapabilityOverrides,
    DateTimeOffset CreatedAt);

public sealed record ModelPrice(
    Guid Id,
    Guid ProviderModelId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long InputPriceMicroUsdPerMillion,
    long OutputPriceMicroUsdPerMillion,
    long? CachedInputPriceMicroUsdPerMillion,
    string ExtraPricingJson,
    DateTimeOffset CreatedAt);

public sealed record CatalogProviderModelSummary(
    ProviderModel Mapping,
    CatalogProvider Provider,
    ModelPrice Price);

public sealed record CatalogModelSummary(
    CanonicalModel Model,
    IReadOnlyList<CatalogProviderModelSummary> ProviderMappings);

public interface ICatalogStore
{
    Task<bool> TryAddProviderAsync(CatalogProvider provider, CancellationToken cancellationToken = default);

    Task<bool> TryAddModelAsync(CanonicalModel model, CancellationToken cancellationToken = default);

    Task<bool> TryAddProviderModelAsync(ProviderModel providerModel, CancellationToken cancellationToken = default);

    Task<bool> TryAddPriceAsync(ModelPrice price, CancellationToken cancellationToken = default);

    Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<bool> TrySetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default);

    Task<bool> TrySetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default);

    Task<bool> TrySetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default);
}

public interface ICatalogService
{
    Task<CatalogProvider> AddProviderAsync(string code, string name, CancellationToken cancellationToken = default);

    Task<CanonicalModel> AddModelAsync(
        string canonicalCode,
        string displayName,
        int contextLength,
        int maxOutputTokens,
        IEnumerable<CatalogCapability> capabilities,
        CancellationToken cancellationToken = default);

    Task<ProviderModel> AddProviderModelAsync(
        Guid providerId,
        Guid modelId,
        string upstreamModelCode,
        string? endpointReference,
        IEnumerable<CatalogCapability>? capabilityOverrides,
        CancellationToken cancellationToken = default);

    Task<ModelPrice> AddPriceAsync(
        Guid providerModelId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        long inputPriceMicroUsdPerMillion,
        long outputPriceMicroUsdPerMillion,
        long? cachedInputPriceMicroUsdPerMillion,
        string? extraPricingJson,
        CancellationToken cancellationToken = default);

    Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<bool> SetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default);

    Task<bool> SetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default);

    Task<bool> SetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default);
}
