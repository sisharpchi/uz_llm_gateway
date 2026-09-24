using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public static class GatewayCostEstimator
{
    public static UsdMicroAmount MaximumCharge(CanonicalModel model, ModelPrice price,
        FeePolicyVersion fee, int outputLimit)
    {
        using var extras = JsonDocument.Parse(price.ExtraPricingJson);
        if (extras.RootElement.ValueKind != JsonValueKind.Object
            || extras.RootElement.EnumerateObject().Any())
            throw new InvalidOperationException("Provider pricing has unsupported extra dimensions.");
        if (outputLimit <= 0 || outputLimit > model.MaxOutputTokens)
            throw new ArgumentOutOfRangeException(nameof(outputLimit));
        var maximumInputRate = Math.Max(price.InputPriceMicroUsdPerMillion,
            price.CachedInputPriceMicroUsdPerMillion ?? 0);
        var maximumProviderCost = checked((long)decimal.Ceiling(
            ((decimal)model.ContextLength * maximumInputRate
                + (decimal)outputLimit * price.OutputPriceMicroUsdPerMillion) / 1_000_000m));
        var maximumCustomerCost = checked((long)decimal.Ceiling(
            (decimal)maximumProviderCost * (10_000 + fee.MarkupBasisPoints) / 10_000m
            + fee.FixedFee.Value));
        return new UsdMicroAmount(Math.Max(1, maximumCustomerCost));
    }
}
