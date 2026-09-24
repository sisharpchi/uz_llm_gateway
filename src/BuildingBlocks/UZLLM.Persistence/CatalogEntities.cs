namespace UZLLM.Persistence;

public sealed class CatalogProviderEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<CatalogProviderModelEntity> ProviderModels { get; set; } = [];
}

public sealed class CatalogModelEntity
{
    public Guid Id { get; set; }
    public string CanonicalCode { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int ContextLength { get; set; }
    public int MaxOutputTokens { get; set; }
    public string CapabilitiesJson { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<CatalogProviderModelEntity> ProviderModels { get; set; } = [];
}

public sealed class CatalogProviderModelEntity
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public Guid ModelId { get; set; }
    public string UpstreamModelCode { get; set; } = null!;
    public string? EndpointReference { get; set; }
    public string Status { get; set; } = null!;
    public string CapabilityOverridesJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public CatalogProviderEntity Provider { get; set; } = null!;
    public CatalogModelEntity Model { get; set; } = null!;
    public ICollection<CatalogModelPriceEntity> Prices { get; set; } = [];
}

public sealed class CatalogModelPriceEntity
{
    public Guid Id { get; set; }
    public Guid ProviderModelId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public long InputPriceMicroUsdPerMillion { get; set; }
    public long OutputPriceMicroUsdPerMillion { get; set; }
    public long? CachedInputPriceMicroUsdPerMillion { get; set; }
    public string ExtraPricingJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public CatalogProviderModelEntity ProviderModel { get; set; } = null!;
}
