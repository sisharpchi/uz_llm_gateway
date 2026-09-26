namespace UZLLM.Modules.Routing.Domain;

/// <summary>Ranks already eligible, snapshot-priced candidates without mutating the input.</summary>
public sealed record PriceRouteOption(int Index, decimal EstimatedCustomerMicroUsd,
    string ProviderCode, Guid ProviderModelId);

public static class PriceRouteRanker
{
    public static IReadOnlyList<int> Rank(IReadOnlyList<PriceRouteOption> options) => options
        .OrderBy(option => option.EstimatedCustomerMicroUsd)
        .ThenBy(option => option.ProviderCode == "openai" ? 0 : 1)
        .ThenBy(option => option.ProviderCode, StringComparer.Ordinal)
        .ThenBy(option => option.ProviderModelId)
        .Select(option => option.Index)
        .ToArray();
}
