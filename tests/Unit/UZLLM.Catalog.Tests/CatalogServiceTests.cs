using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Catalog.Tests;

public sealed class CatalogServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddModelAsync_normalizes_metadata_and_rejects_invalid_limits_or_capabilities()
    {
        var service = new CatalogService(new InMemoryCatalogStore(), new FixedCatalogTimeProvider(Start));

        var model = await service.AddModelAsync(" GPT-5.1 ", " GPT 5.1 ", 128_000, 16_000, [CatalogCapability.Tools, CatalogCapability.Text, CatalogCapability.Tools]);

        Assert.Equal("gpt-5.1", model.CanonicalCode);
        Assert.Equal("GPT 5.1", model.DisplayName);
        Assert.Equal(new[] { CatalogCapability.Text, CatalogCapability.Tools }, model.Capabilities);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AddModelAsync("bad", "Bad", 100, 101, [CatalogCapability.Text]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AddModelAsync("bad2", "Bad", 100, 10, [(CatalogCapability)999]));
    }

    [Fact]
    public async Task AddPriceAsync_selects_contiguous_history_at_exact_effective_boundaries()
    {
        var store = new InMemoryCatalogStore();
        var service = new CatalogService(store, new FixedCatalogTimeProvider(Start));
        var mappingId = Guid.CreateVersion7();
        var next = Start.AddDays(1);

        var oldPrice = await service.AddPriceAsync(mappingId, Start, next, 1_000, 2_000, 500, "{} ");
        var currentPrice = await service.AddPriceAsync(mappingId, next, null, 1_500, 2_500, null, null);

        Assert.Equal(oldPrice.Id, (await service.FindEffectivePriceAsync(mappingId, next.AddTicks(-1)))?.Id);
        Assert.Equal(currentPrice.Id, (await service.FindEffectivePriceAsync(mappingId, next))?.Id);
        Assert.Equal("{}", oldPrice.ExtraPricingJson.Trim());
    }

    [Fact]
    public async Task AddPriceAsync_rejects_overlapping_or_invalid_versions_without_replacing_history()
    {
        var store = new InMemoryCatalogStore();
        var service = new CatalogService(store, new FixedCatalogTimeProvider(Start));
        var mappingId = Guid.CreateVersion7();
        var existing = await service.AddPriceAsync(mappingId, Start, Start.AddDays(2), 1, 2, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPriceAsync(mappingId, Start.AddDays(1), null, 3, 4, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddPriceAsync(mappingId, Start, Start, 3, 4, null, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AddPriceAsync(mappingId, Start.AddDays(2), null, -1, 4, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddPriceAsync(mappingId, Start.AddDays(2), null, 3, 4, null, "[]"));
        Assert.Equal(existing.Id, (await service.FindEffectivePriceAsync(mappingId, Start))?.Id);
    }

    [Fact]
    public async Task SetProviderModelStatusAsync_delegates_mapping_state_transition()
    {
        var store = new InMemoryCatalogStore();
        var service = new CatalogService(store, new FixedCatalogTimeProvider(Start));
        var mapping = await service.AddProviderModelAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "gpt-5.1", null, null);

        Assert.True(await service.SetProviderModelStatusAsync(mapping.Id, CatalogStatus.Disabled));
        Assert.False(await service.SetProviderModelStatusAsync(Guid.CreateVersion7(), CatalogStatus.Disabled));
        Assert.Equal(CatalogStatus.Disabled, store.ProviderModels.Single().Status);
    }
}

internal sealed class InMemoryCatalogStore : ICatalogStore
{
    public List<CatalogProvider> Providers { get; } = [];
    public List<CanonicalModel> Models { get; } = [];
    public List<ProviderModel> ProviderModels { get; } = [];
    public List<ModelPrice> Prices { get; } = [];

    public Task<bool> TryAddProviderAsync(CatalogProvider provider, CancellationToken cancellationToken = default)
    {
        if (Providers.Any(existing => existing.Code == provider.Code)) return Task.FromResult(false);
        Providers.Add(provider);
        return Task.FromResult(true);
    }

    public Task<bool> TryAddModelAsync(CanonicalModel model, CancellationToken cancellationToken = default)
    {
        if (Models.Any(existing => existing.CanonicalCode == model.CanonicalCode)) return Task.FromResult(false);
        Models.Add(model);
        return Task.FromResult(true);
    }

    public Task<bool> TryAddProviderModelAsync(ProviderModel providerModel, CancellationToken cancellationToken = default)
    {
        if (ProviderModels.Any(existing => existing.ProviderId == providerModel.ProviderId && existing.ModelId == providerModel.ModelId)) return Task.FromResult(false);
        ProviderModels.Add(providerModel);
        return Task.FromResult(true);
    }

    public Task<bool> TryAddPriceAsync(ModelPrice price, CancellationToken cancellationToken = default)
    {
        var overlaps = Prices.Any(existing => existing.ProviderModelId == price.ProviderModelId
            && existing.EffectiveFrom < (price.EffectiveTo ?? DateTimeOffset.MaxValue)
            && price.EffectiveFrom < (existing.EffectiveTo ?? DateTimeOffset.MaxValue));
        if (overlaps) return Task.FromResult(false);
        Prices.Add(price);
        return Task.FromResult(true);
    }

    public Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.FromResult(Prices.SingleOrDefault(price => price.ProviderModelId == providerModelId && price.EffectiveFrom <= at && (price.EffectiveTo == null || price.EffectiveTo > at)));

    public Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogModelSummary>>([]);

    public Task<bool> TrySetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult(SetStatus(Providers, providerId, status, provider => provider.Id, (provider, nextStatus) => provider with { Status = nextStatus }, Providers));

    public Task<bool> TrySetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult(SetStatus(Models, modelId, status, model => model.Id, (model, nextStatus) => model with { Status = nextStatus }, Models));

    public Task<bool> TrySetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult(SetStatus(ProviderModels, providerModelId, status, mapping => mapping.Id, (mapping, nextStatus) => mapping with { Status = nextStatus }, ProviderModels));

    private static bool SetStatus<T>(List<T> values, Guid id, CatalogStatus status, Func<T, Guid> getId, Func<T, CatalogStatus, T> withStatus, List<T> destination)
    {
        var index = values.FindIndex(value => getId(value) == id);
        if (index < 0) return false;
        destination[index] = withStatus(values[index], status);
        return true;
    }
}

internal sealed class FixedCatalogTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
