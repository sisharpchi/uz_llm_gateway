using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public static class GatewayCostEstimator
{
    public static UsdMicroAmount MaximumEmbeddingProviderCost(CanonicalModel model, ModelPrice price, int inputCount)
    {
        ValidatePriceDimensions(price);
        if (model.MaxOutputTokens != 0 || inputCount is < 1 or > 32
            || price.OutputPriceMicroUsdPerMillion != 0 || price.CachedInputPriceMicroUsdPerMillion is not null)
            throw new InvalidOperationException("Embeddings requires input-only model pricing.");
        return new UsdMicroAmount(Math.Max(1, checked((long)decimal.Ceiling(
            (decimal)Math.Min(model.ContextLength, 8192) * inputCount * price.InputPriceMicroUsdPerMillion / 1_000_000m))));
    }

    public static UsdMicroAmount MaximumEmbeddingCharge(CanonicalModel model, ModelPrice price,
        FeePolicyVersion fee, int inputCount)
    {
        var external = MaximumEmbeddingProviderCost(model, price, inputCount);
        return new UsdMicroAmount(Math.Max(1, checked((long)decimal.Ceiling(
            (decimal)external.Value * (10_000m + fee.MarkupBasisPoints) / 10_000m + fee.FixedFee.Value))));
    }

    // Rank against the catalog price snapshot, never against live provider quotes.
    // Reservation still uses MaximumCharge across every selected attempt.
    public static decimal EstimatedCustomerCost(CanonicalModel model, ModelPrice price,
        FeePolicyVersion fee, int estimatedInputTokens, int outputLimit)
    {
        ValidatePriceDimensions(price);
        if (estimatedInputTokens < 0 || (model.MaxOutputTokens == 0 ? outputLimit != 0 : outputLimit <= 0) || outputLimit > model.MaxOutputTokens)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));
        if (model.MaxOutputTokens == 0) MaximumEmbeddingProviderCost(model, price, 1);
        var input = model.MaxOutputTokens == 0 ? estimatedInputTokens : Math.Min(estimatedInputTokens, model.ContextLength);
        var inputRate = Math.Max(price.InputPriceMicroUsdPerMillion,
            price.CachedInputPriceMicroUsdPerMillion ?? 0);
        var providerCost = ((decimal)input * inputRate
            + (decimal)outputLimit * price.OutputPriceMicroUsdPerMillion) / 1_000_000m;
        return providerCost * (10_000m + fee.MarkupBasisPoints) / 10_000m
            + fee.FixedFee.Value;
    }

    public static UsdMicroAmount MaximumCharge(CanonicalModel model, ModelPrice price,
        FeePolicyVersion fee, int outputLimit)
    {
        var maximumProviderCost = MaximumProviderCost(model, price, outputLimit);
        var maximumCustomerCost = checked((long)decimal.Ceiling(
            (decimal)maximumProviderCost.Value * (10_000 + fee.MarkupBasisPoints) / 10_000m
            + fee.FixedFee.Value));
        return new UsdMicroAmount(Math.Max(1, maximumCustomerCost));
    }

    public static UsdMicroAmount MaximumByokFee(UsdMicroAmount maximumProviderCost,
        FeePolicyVersion fee) => new(checked((long)decimal.Ceiling(
            (decimal)maximumProviderCost.Value * fee.MarkupBasisPoints / 10_000m
            + fee.FixedFee.Value)));

    public static UsdMicroAmount MaximumProviderCost(CanonicalModel model, ModelPrice price,
        int outputLimit)
    {
        ValidatePriceDimensions(price);
        if (outputLimit <= 0 || outputLimit > model.MaxOutputTokens)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));
        var maximumInputRate = Math.Max(price.InputPriceMicroUsdPerMillion,
            price.CachedInputPriceMicroUsdPerMillion ?? 0);
        var maximumProviderCost = checked((long)decimal.Ceiling(
            ((decimal)model.ContextLength * maximumInputRate
                + (decimal)outputLimit * price.OutputPriceMicroUsdPerMillion) / 1_000_000m));
        return new UsdMicroAmount(Math.Max(1, maximumProviderCost));
    }

    private static void ValidatePriceDimensions(ModelPrice price)
    {
        using var extras = JsonDocument.Parse(price.ExtraPricingJson);
        if (extras.RootElement.ValueKind != JsonValueKind.Object
            || extras.RootElement.EnumerateObject().Any())
            throw new InvalidOperationException("Provider pricing has unsupported extra dimensions.");
    }
}
