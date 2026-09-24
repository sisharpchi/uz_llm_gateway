using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class CatalogPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Price_history_selects_the_effective_version_without_overwriting_prior_rows()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var service = new CatalogService(new PostgreSqlCatalogStore(dbContext), new FixedCatalogTimeProvider(Start));
        var mapping = await CreateActiveMappingAsync(service);
        var next = Start.AddDays(1);

        var prior = await service.AddPriceAsync(mapping.Id, Start, next, 1_000, 2_000, 500, null);
        var current = await service.AddPriceAsync(mapping.Id, next, null, 1_500, 2_500, null, null);

        Assert.Equal(prior.Id, (await service.FindEffectivePriceAsync(mapping.Id, next.AddTicks(-1)))?.Id);
        Assert.Equal(current.Id, (await service.FindEffectivePriceAsync(mapping.Id, next))?.Id);
        Assert.Equal(2, await dbContext.Set<CatalogModelPriceEntity>().CountAsync());
    }

    [Fact]
    public async Task Database_rejects_overlapping_price_versions_for_one_provider_mapping()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var service = new CatalogService(new PostgreSqlCatalogStore(dbContext), new FixedCatalogTimeProvider(Start));
        var mapping = await CreateActiveMappingAsync(service);
        await service.AddPriceAsync(mapping.Id, Start, Start.AddDays(2), 1, 2, null, null);

        dbContext.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
        {
            Id = Guid.CreateVersion7(),
            ProviderModelId = mapping.Id,
            EffectiveFrom = Start.AddDays(1),
            EffectiveTo = null,
            InputPriceMicroUsdPerMillion = 3,
            OutputPriceMicroUsdPerMillion = 4,
            ExtraPricingJson = "{}",
            CreatedAt = Start
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Disabled_provider_mapping_is_excluded_from_effective_catalog_results()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var service = new CatalogService(new PostgreSqlCatalogStore(dbContext), new FixedCatalogTimeProvider(Start));
        var mapping = await CreateActiveMappingAsync(service);
        await service.AddPriceAsync(mapping.Id, Start, null, 1, 2, null, null);

        Assert.Single(await service.ListActiveModelsAsync(Start));
        Assert.True(await service.SetProviderModelStatusAsync(mapping.Id, CatalogStatus.Disabled));
        Assert.Empty(await service.ListActiveModelsAsync(Start));
        Assert.Null(await service.FindEffectivePriceAsync(mapping.Id, Start));
    }

    private static async Task<ProviderModel> CreateActiveMappingAsync(ICatalogService service)
    {
        var provider = await service.AddProviderAsync("openai", "OpenAI");
        var model = await service.AddModelAsync("gpt-5.1", "GPT 5.1", 128_000, 16_000, [CatalogCapability.Text, CatalogCapability.Tools]);
        return await service.AddProviderModelAsync(provider.Id, model.Id, "gpt-5.1", null, null);
    }
}

internal sealed class FixedCatalogTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
