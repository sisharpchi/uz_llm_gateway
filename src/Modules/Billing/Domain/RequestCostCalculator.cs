using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Billing.Domain;

public static class RequestCostCalculator
{
    public static ChargeBreakdown Calculate(
        IReadOnlyList<PricedUsageEvidence> evidence,
        FeePolicyVersion feePolicy,
        UsdMicroAmount reservation)
    {
        decimal exactProviderCost = 0;
        foreach (var item in evidence)
        {
            if (item.InputTokens < 0 || item.OutputTokens < 0 || item.CachedInputTokens < 0
                || item.CachedInputTokens > item.InputTokens || item.ReasoningTokens is < 0
                || item.ReasoningTokens > item.OutputTokens || item.InputRate < 0
                || item.OutputRate < 0 || item.CachedInputRate is < 0
                || item.AttemptStartedAt < item.PriceEffectiveFrom
                || item.PriceEffectiveTo is { } end && item.AttemptStartedAt >= end)
                throw new InvalidOperationException("Usage or its price version is inconsistent.");

            using var extras = JsonDocument.Parse(item.ExtraPricingJson);
            if (extras.RootElement.ValueKind != JsonValueKind.Object
                || extras.RootElement.EnumerateObject().Any())
                throw new NotSupportedException("Provider-specific extra pricing requires an explicit calculator.");

            var regularInput = item.InputTokens - item.CachedInputTokens;
            var cachedRate = item.CachedInputRate ?? item.InputRate;
            exactProviderCost += ((decimal)regularInput * item.InputRate
                + (decimal)item.CachedInputTokens * cachedRate
                + (decimal)item.OutputTokens * item.OutputRate) / 1_000_000m;
        }

        var providerCost = new UsdMicroAmount(checked((long)decimal.Ceiling(exactProviderCost)));
        var exactCustomer = exactProviderCost * (10_000m + feePolicy.MarkupBasisPoints) / 10_000m
            + feePolicy.FixedFee.Value;
        var uncapped = new UsdMicroAmount(checked((long)decimal.Ceiling(exactCustomer)));
        var charged = new UsdMicroAmount(Math.Min(uncapped.Value, reservation.Value));
        return new ChargeBreakdown(providerCost, uncapped, charged,
            new UsdMicroAmount(uncapped.Value - charged.Value),
            new UsdMicroAmount(Math.Max(0, providerCost.Value - charged.Value)));
    }
}
