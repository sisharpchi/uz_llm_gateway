using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Catalog.Infrastructure;

public sealed class PostgreSqlCatalogStore(FoundationDbContext dbContext) : ICatalogStore
{
    public Task<bool> TryAddProviderAsync(CatalogProvider provider, CancellationToken cancellationToken = default) =>
        TrySaveAsync(new CatalogProviderEntity
        {
            Id = provider.Id,
            Code = provider.Code,
            Name = provider.Name,
            Status = provider.Status.ToString(),
            CreatedAt = provider.CreatedAt
        }, cancellationToken);

    public Task<bool> TryAddModelAsync(CanonicalModel model, CancellationToken cancellationToken = default) =>
        TrySaveAsync(new CatalogModelEntity
        {
            Id = model.Id,
            CanonicalCode = model.CanonicalCode,
            DisplayName = model.DisplayName,
            ContextLength = model.ContextLength,
            MaxOutputTokens = model.MaxOutputTokens,
            CapabilitiesJson = SerializeCapabilities(model.Capabilities),
            Status = model.Status.ToString(),
            CreatedAt = model.CreatedAt
        }, cancellationToken);

    public Task<bool> TryAddProviderModelAsync(ProviderModel providerModel, CancellationToken cancellationToken = default) =>
        TrySaveAsync(new CatalogProviderModelEntity
        {
            Id = providerModel.Id,
            ProviderId = providerModel.ProviderId,
            ModelId = providerModel.ModelId,
            UpstreamModelCode = providerModel.UpstreamModelCode,
            EndpointReference = providerModel.EndpointReference,
            Status = providerModel.Status.ToString(),
            CapabilityOverridesJson = SerializeCapabilities(providerModel.CapabilityOverrides),
            CreatedAt = providerModel.CreatedAt
        }, cancellationToken);

    public Task<bool> TryAddPriceAsync(ModelPrice price, CancellationToken cancellationToken = default) =>
        TrySaveAsync(new CatalogModelPriceEntity
        {
            Id = price.Id,
            ProviderModelId = price.ProviderModelId,
            EffectiveFrom = price.EffectiveFrom,
            EffectiveTo = price.EffectiveTo,
            InputPriceMicroUsdPerMillion = price.InputPriceMicroUsdPerMillion,
            OutputPriceMicroUsdPerMillion = price.OutputPriceMicroUsdPerMillion,
            CachedInputPriceMicroUsdPerMillion = price.CachedInputPriceMicroUsdPerMillion,
            ExtraPricingJson = price.ExtraPricingJson,
            CreatedAt = price.CreatedAt
        }, cancellationToken);

    public async Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<CatalogModelPriceEntity>().AsNoTracking()
            .Where(price => price.ProviderModelId == providerModelId
                && price.EffectiveFrom <= at
                && (price.EffectiveTo == null || price.EffectiveTo > at)
                && price.ProviderModel.Status == CatalogStatus.Active.ToString()
                && price.ProviderModel.Provider.Status == CatalogStatus.Active.ToString()
                && price.ProviderModel.Model.Status == CatalogStatus.Active.ToString())
            .SingleOrDefaultAsync(cancellationToken)) is { } price
            ? ToContract(price)
            : null;

    public async Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var mappings = await dbContext.Set<CatalogProviderModelEntity>().AsNoTracking()
            .Include(mapping => mapping.Provider)
            .Include(mapping => mapping.Model)
            .Include(mapping => mapping.Prices.Where(price => price.EffectiveFrom <= at && (price.EffectiveTo == null || price.EffectiveTo > at)))
            .Where(mapping => mapping.Status == CatalogStatus.Active.ToString()
                && mapping.Provider.Status == CatalogStatus.Active.ToString()
                && mapping.Model.Status == CatalogStatus.Active.ToString()
                && mapping.Prices.Any(price => price.EffectiveFrom <= at && (price.EffectiveTo == null || price.EffectiveTo > at)))
            .OrderBy(mapping => mapping.Model.CanonicalCode)
            .ThenBy(mapping => mapping.Provider.Code)
            .ToListAsync(cancellationToken);

        return mappings.GroupBy(mapping => mapping.ModelId)
            .Select(group => new CatalogModelSummary(
                ToContract(group.First().Model),
                group.Select(mapping => new CatalogProviderModelSummary(
                    ToContract(mapping),
                    ToContract(mapping.Provider),
                    ToContract(mapping.Prices.Single())))
                    .ToArray()))
            .ToArray();
    }

    public Task<bool> TrySetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        SetProviderStatusAsync(providerId, status, cancellationToken);

    public Task<bool> TrySetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        SetModelStatusAsync(modelId, status, cancellationToken);

    public Task<bool> TrySetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        SetProviderModelStatusAsync(providerModelId, status, cancellationToken);

    private async Task<bool> TrySaveAsync(object entity, CancellationToken cancellationToken)
    {
        dbContext.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<bool> SetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken) =>
        await dbContext.Set<CatalogProviderEntity>().Where(provider => provider.Id == providerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(provider => provider.Status, status.ToString()), cancellationToken) == 1;

    private async Task<bool> SetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken) =>
        await dbContext.Set<CatalogModelEntity>().Where(model => model.Id == modelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(model => model.Status, status.ToString()), cancellationToken) == 1;

    private async Task<bool> SetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken) =>
        await dbContext.Set<CatalogProviderModelEntity>().Where(mapping => mapping.Id == providerModelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(mapping => mapping.Status, status.ToString()), cancellationToken) == 1;

    private static CatalogProvider ToContract(CatalogProviderEntity provider) =>
        new(provider.Id, provider.Code, provider.Name, ParseStatus(provider.Status), provider.CreatedAt);

    private static CanonicalModel ToContract(CatalogModelEntity model) =>
        new(model.Id, model.CanonicalCode, model.DisplayName, model.ContextLength, model.MaxOutputTokens, DeserializeCapabilities(model.CapabilitiesJson), ParseStatus(model.Status), model.CreatedAt);

    private static ProviderModel ToContract(CatalogProviderModelEntity mapping) =>
        new(mapping.Id, mapping.ProviderId, mapping.ModelId, mapping.UpstreamModelCode, mapping.EndpointReference, ParseStatus(mapping.Status), DeserializeCapabilities(mapping.CapabilityOverridesJson), mapping.CreatedAt);

    private static ModelPrice ToContract(CatalogModelPriceEntity price) =>
        new(price.Id, price.ProviderModelId, price.EffectiveFrom, price.EffectiveTo, price.InputPriceMicroUsdPerMillion, price.OutputPriceMicroUsdPerMillion, price.CachedInputPriceMicroUsdPerMillion, price.ExtraPricingJson, price.CreatedAt);

    private static CatalogStatus ParseStatus(string value) => Enum.Parse<CatalogStatus>(value, ignoreCase: false);

    private static string SerializeCapabilities(IEnumerable<CatalogCapability> capabilities) =>
        JsonSerializer.Serialize(capabilities.Distinct().Order().Select(capability => capability.ToString()));

    private static IReadOnlyList<CatalogCapability> DeserializeCapabilities(string json) =>
        JsonSerializer.Deserialize<string[]>(json)?.Select(value => Enum.Parse<CatalogCapability>(value, ignoreCase: false)).Order().ToArray()
        ?? [];
}
