using System.Globalization;
using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Management.Api.Catalog;

/// <summary>Published customer catalog at one effective instant; it is not a live health or quota check.</summary>
public sealed record CustomerCatalogResponse(DateTimeOffset DataAsOf, Guid FeePolicyVersionId,
    int MarkupBasisPoints, string FixedFeeMicroUsdPerRequest,
    IReadOnlyList<CustomerCatalogModelResponse> Models);

/// <summary>Canonical chat model and its published managed provider mappings.</summary>
public sealed record CustomerCatalogModelResponse(string Id, string DisplayName,
    int ContextLength, int MaxOutputTokens, IReadOnlyList<string> Capabilities,
    string Status, IReadOnlyList<CustomerCatalogMappingResponse> Providers);

/// <summary>Effective customer rates in USD micro-units per million tokens; a fixed request fee is separate.</summary>
public sealed record CustomerCatalogMappingResponse(Guid MappingId, string Provider, string ProviderName,
    IReadOnlyList<string> Capabilities, Guid PriceVersionId, DateTimeOffset PriceEffectiveFrom,
    DateTimeOffset? PriceEffectiveTo, string InputPriceMicroUsdPerMillion,
    string OutputPriceMicroUsdPerMillion, string? CachedInputPriceMicroUsdPerMillion);

public interface ICustomerCatalogService
{
    Task<CustomerCatalogResponse?> ListAsync(Guid accountId, Guid organizationId,
        Guid projectId, CancellationToken cancellationToken);
}

public sealed class CustomerCatalogService(IProjectService projects, IOrganizationService organizations,
    ICatalogService catalog,
    IPricingHistoryService pricing, TimeProvider clock) : ICustomerCatalogService
{
    public async Task<CustomerCatalogResponse?> ListAsync(Guid accountId, Guid organizationId,
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(accountId, organizationId, projectId, cancellationToken);
        if (project is not { Status: ProjectStatus.Active }) return null;
        if (!(await organizations.ListForAccountAsync(accountId, cancellationToken))
            .Any(value => value.Id == organizationId && value.Status == OrganizationStatus.Active)) return null;

        var at = clock.GetUtcNow();
        var fee = (await pricing.ListFeePolicyVersionsAsync("default", cancellationToken))
            .SingleOrDefault(value => value.EffectiveFrom <= at
                && (value.EffectiveTo is null || value.EffectiveTo > at));
        if (fee is null) throw new InvalidOperationException("Managed pricing is unavailable.");

        var factor = (10_000m + fee.MarkupBasisPoints) / 10_000m;
        var models = (await catalog.ListActiveModelsAsync(at, cancellationToken))
            .Select(model => new CustomerCatalogModelResponse(model.Model.CanonicalCode,
                model.Model.DisplayName, model.Model.ContextLength, model.Model.MaxOutputTokens,
                model.Model.Capabilities.Select(value => value.ToString()).ToArray(),
                "Published", model.ProviderMappings
                    .Where(mapping => IsSupportedChatPrice(model.Model, mapping))
                    .Select(mapping => new CustomerCatalogMappingResponse(mapping.Mapping.Id, mapping.Provider.Code,
                        mapping.Provider.Name,
                        model.Model.Capabilities.Concat(mapping.Mapping.CapabilityOverrides)
                            .Distinct().Order().Select(value => value.ToString()).ToArray(),
                        mapping.Price.Id, mapping.Price.EffectiveFrom, mapping.Price.EffectiveTo,
                        Rate(mapping.Price.InputPriceMicroUsdPerMillion, factor),
                        Rate(mapping.Price.OutputPriceMicroUsdPerMillion, factor),
                        mapping.Price.CachedInputPriceMicroUsdPerMillion is { } cached
                            ? Rate(cached, factor) : null))
                    .ToArray()))
            .Where(model => model.Providers.Count != 0)
            .ToArray();
        return new CustomerCatalogResponse(at, fee.Id, fee.MarkupBasisPoints,
            fee.FixedFee.Value.ToString(CultureInfo.InvariantCulture), models);
    }

    private static bool IsSupportedChatPrice(CanonicalModel model, CatalogProviderModelSummary mapping)
    {
        if (mapping.Provider.Code is not ("openai" or "anthropic" or "google" or "deepseek")
            || !model.Capabilities.Concat(mapping.Mapping.CapabilityOverrides)
                .Contains(CatalogCapability.Text)
            || mapping.Provider.Code == "deepseek"
                && mapping.Price.CachedInputPriceMicroUsdPerMillion is null)
            return false;
        using var extras = JsonDocument.Parse(mapping.Price.ExtraPricingJson);
        return extras.RootElement.ValueKind == JsonValueKind.Object
            && !extras.RootElement.EnumerateObject().Any();
    }

    private static string Rate(long providerMicroUsdPerMillion, decimal factor) =>
        (providerMicroUsdPerMillion * factor).ToString("0.################", CultureInfo.InvariantCulture);
}
