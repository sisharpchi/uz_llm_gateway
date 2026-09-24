using System.Text.Json;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Modules.Catalog.Application;

public sealed class CatalogService(ICatalogStore store, TimeProvider timeProvider) : ICatalogService
{
    public async Task<CatalogProvider> AddProviderAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var provider = new CatalogProvider(Guid.CreateVersion7(), NormalizeCode(code, nameof(code)), NormalizeText(name, 120, nameof(name)), CatalogStatus.Active, timeProvider.GetUtcNow());
        if (!await store.TryAddProviderAsync(provider, cancellationToken))
        {
            throw new InvalidOperationException("A provider with this code already exists.");
        }

        return provider;
    }

    public async Task<CanonicalModel> AddModelAsync(string canonicalCode, string displayName, int contextLength, int maxOutputTokens, IEnumerable<CatalogCapability> capabilities, CancellationToken cancellationToken = default)
    {
        if (contextLength <= 0 || maxOutputTokens <= 0 || maxOutputTokens > contextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens), "Model context and output limits must be positive, and output cannot exceed context.");
        }

        var model = new CanonicalModel(
            Guid.CreateVersion7(),
            NormalizeCode(canonicalCode, nameof(canonicalCode)),
            NormalizeText(displayName, 200, nameof(displayName)),
            contextLength,
            maxOutputTokens,
            NormalizeCapabilities(capabilities, nameof(capabilities)),
            CatalogStatus.Active,
            timeProvider.GetUtcNow());
        if (!await store.TryAddModelAsync(model, cancellationToken))
        {
            throw new InvalidOperationException("A model with this canonical code already exists.");
        }

        return model;
    }

    public async Task<ProviderModel> AddProviderModelAsync(Guid providerId, Guid modelId, string upstreamModelCode, string? endpointReference, IEnumerable<CatalogCapability>? capabilityOverrides, CancellationToken cancellationToken = default)
    {
        ValidateId(providerId, nameof(providerId));
        ValidateId(modelId, nameof(modelId));
        var mapping = new ProviderModel(
            Guid.CreateVersion7(),
            providerId,
            modelId,
            NormalizeText(upstreamModelCode, 300, nameof(upstreamModelCode)),
            NormalizeOptionalText(endpointReference, 500, nameof(endpointReference)),
            CatalogStatus.Active,
            NormalizeCapabilities(capabilityOverrides ?? [], nameof(capabilityOverrides), allowEmpty: true),
            timeProvider.GetUtcNow());
        if (!await store.TryAddProviderModelAsync(mapping, cancellationToken))
        {
            throw new InvalidOperationException("This provider mapping already exists or references an unknown provider/model.");
        }

        return mapping;
    }

    public async Task<ModelPrice> AddPriceAsync(Guid providerModelId, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, long inputPriceMicroUsdPerMillion, long outputPriceMicroUsdPerMillion, long? cachedInputPriceMicroUsdPerMillion, string? extraPricingJson, CancellationToken cancellationToken = default)
    {
        ValidateId(providerModelId, nameof(providerModelId));
        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new ArgumentException("The price effective end must be after its start.", nameof(effectiveTo));
        }

        if (inputPriceMicroUsdPerMillion < 0 || outputPriceMicroUsdPerMillion < 0 || cachedInputPriceMicroUsdPerMillion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputPriceMicroUsdPerMillion), "Provider prices cannot be negative.");
        }

        var price = new ModelPrice(
            Guid.CreateVersion7(),
            providerModelId,
            effectiveFrom,
            effectiveTo,
            inputPriceMicroUsdPerMillion,
            outputPriceMicroUsdPerMillion,
            cachedInputPriceMicroUsdPerMillion,
            NormalizeJsonObject(extraPricingJson, nameof(extraPricingJson)),
            timeProvider.GetUtcNow());
        if (!await store.TryAddPriceAsync(price, cancellationToken))
        {
            throw new InvalidOperationException("The price overlaps an existing version or references an unknown provider mapping.");
        }

        return price;
    }

    public Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ValidateId(providerModelId, nameof(providerModelId));
        return store.FindEffectivePriceAsync(providerModelId, at, cancellationToken);
    }

    public Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at, CancellationToken cancellationToken = default) =>
        store.ListActiveModelsAsync(at, cancellationToken);

    public Task<bool> SetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default)
    {
        ValidateId(providerId, nameof(providerId));
        return store.TrySetProviderStatusAsync(providerId, status, cancellationToken);
    }

    public Task<bool> SetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default)
    {
        ValidateId(modelId, nameof(modelId));
        return store.TrySetModelStatusAsync(modelId, status, cancellationToken);
    }

    public Task<bool> SetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default)
    {
        ValidateId(providerModelId, nameof(providerModelId));
        return store.TrySetProviderModelStatusAsync(providerModelId, status, cancellationToken);
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }

    private static string NormalizeCode(string value, string parameterName)
    {
        var normalized = NormalizeText(value, 200, parameterName).ToLowerInvariant();
        if (!char.IsAsciiLetterOrDigit(normalized[0]) || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new ArgumentException("Catalog codes must use lowercase letters, digits, hyphens, underscores, periods, or colons.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeText(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < 1 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"A value up to {maximumLength} characters is required.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeText(value, maximumLength, parameterName);

    private static IReadOnlyList<CatalogCapability> NormalizeCapabilities(IEnumerable<CatalogCapability> values, string parameterName, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        var normalized = values.Distinct().Order().ToArray();
        if (normalized.Any(capability => !Enum.IsDefined(capability)))
        {
            throw new ArgumentOutOfRangeException(parameterName, "An unsupported catalog capability was supplied.");
        }

        if (!allowEmpty && normalized.Length == 0)
        {
            throw new ArgumentException("At least one capability is required.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeJsonObject(string? value, string parameterName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Pricing metadata must be a JSON object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Pricing metadata must be valid JSON.", parameterName, exception);
        }

        return normalized;
    }
}
