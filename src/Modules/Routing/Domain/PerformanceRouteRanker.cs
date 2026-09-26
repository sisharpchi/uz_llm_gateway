using UZLLM.Modules.Routing.Contracts;

namespace UZLLM.Modules.Routing.Domain;

public sealed record PerformanceRouteOption(int Index, decimal EstimatedCustomerMicroUsd,
    ProviderPerformanceWindow? Performance);

public sealed record PerformanceRouteWeights(decimal Price, decimal Error,
    decimal Latency, decimal Throughput)
{
    public static PerformanceRouteWeights Default { get; } = new(0.25m, 0.20m, 0.35m, 0.20m);

    public void Validate()
    {
        if (Price < 0 || Error < 0 || Latency < 0 || Throughput < 0
            || Price + Error + Latency + Throughput != 1m)
            throw new ArgumentException("Routing weights must be nonnegative and total one.");
    }
}

public static class PerformanceRouteRanker
{
    public static IReadOnlyList<int> Rank(string strategy,
        IReadOnlyList<PerformanceRouteOption> options, DateTimeOffset now,
        TimeSpan window, int minimumSamples, decimal hysteresis,
        PerformanceRouteWeights weights)
    {
        if (strategy is not ("latency" or "throughput" or "auto"))
            throw new ArgumentException("Unsupported performance routing strategy.", nameof(strategy));
        if (window <= TimeSpan.Zero || minimumSamples < 1 || hysteresis is < 0 or > 1)
            throw new ArgumentException("Invalid performance routing policy.");
        weights.Validate();
        if (options.Count == 0) return [];
        if (options.Any(option => option.Performance is not { } sample
            || sample.SuccessfulSamples < minimumSamples
            || sample.LatestAt < now - window || sample.LatestAt > now.AddSeconds(10)
            || sample.MedianTimeToFirstTokenMs <= 0
            || sample.MedianOutputTokensPerSecond <= 0
            || sample.ErrorRate is < 0 or > 1))
            return options.Select(option => option.Index).ToArray();

        var maxPrice = Math.Max(1m, options.Max(option => option.EstimatedCustomerMicroUsd));
        var maxLatency = options.Max(option => option.Performance!.MedianTimeToFirstTokenMs);
        var minThroughput = options.Min(option => option.Performance!.MedianOutputTokensPerSecond);
        var scored = options.Select(option =>
        {
            var sample = option.Performance!;
            var score = strategy switch
            {
                "latency" => sample.MedianTimeToFirstTokenMs,
                "throughput" => minThroughput / sample.MedianOutputTokensPerSecond,
                _ => weights.Price * option.EstimatedCustomerMicroUsd / maxPrice
                    + weights.Error * sample.ErrorRate
                    + weights.Latency * sample.MedianTimeToFirstTokenMs / maxLatency
                    + weights.Throughput * minThroughput / sample.MedianOutputTokensPerSecond
            };
            return (option.Index, Score: score);
        }).OrderBy(value => value.Score).ThenBy(value => value.Index).ToArray();

        // Keep deterministic primary if the observed gain is smaller than the configured margin.
        var primary = scored.Single(value => value.Index == options[0].Index);
        if (primary.Score <= scored[0].Score * (1m + hysteresis))
            return [primary.Index, .. scored.Where(value => value.Index != primary.Index)
                .Select(value => value.Index)];
        return scored.Select(value => value.Index).ToArray();
    }
}
