using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Domain;

namespace UZLLM.Gateway.ContractTests;

public sealed class PerformanceRouteRankerTests
{
    [Fact]
    public void Small_latency_advantage_does_not_oscillate_away_from_deterministic_primary()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new[]
        {
            new PerformanceRouteOption(0, 100m, new ProviderPerformanceWindow(20, 20,
                105m, 20m, 0m, now)),
            new PerformanceRouteOption(1, 100m, new ProviderPerformanceWindow(20, 20,
                100m, 20m, 0m, now))
        };

        var rank = PerformanceRouteRanker.Rank("latency", options, now,
            TimeSpan.FromMinutes(5), 10, 0.10m, PerformanceRouteWeights.Default);

        Assert.Equal([0, 1], rank);
    }

    [Fact]
    public void Material_latency_advantage_changes_route_only_with_sufficient_samples()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new[]
        {
            new PerformanceRouteOption(0, 100m, new ProviderPerformanceWindow(20, 20,
                200m, 20m, 0m, now)),
            new PerformanceRouteOption(1, 100m, new ProviderPerformanceWindow(20, 20,
                100m, 20m, 0m, now))
        };
        Assert.Equal([1, 0], PerformanceRouteRanker.Rank("latency", options, now,
            TimeSpan.FromMinutes(5), 10, 0.10m, PerformanceRouteWeights.Default));
        Assert.Equal([0, 1], PerformanceRouteRanker.Rank("latency",
            [options[0], options[1] with { Performance = options[1].Performance! with
                { SuccessfulSamples = 9 } }], now, TimeSpan.FromMinutes(5), 10,
            0.10m, PerformanceRouteWeights.Default));
    }

    [Fact]
    public void Invalid_weights_are_rejected_at_configuration_boundary()
    {
        Assert.Throws<ArgumentException>(() =>
            new PerformanceRouteWeights(0.5m, 0.5m, 0.5m, 0.5m).Validate());
    }

    [Fact]
    public void Auto_strategy_can_prefer_reliable_mapping_over_slightly_faster_unreliable_one()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new[]
        {
            new PerformanceRouteOption(0, 100m, new ProviderPerformanceWindow(20, 20,
                100m, 20m, 0m, now)),
            new PerformanceRouteOption(1, 100m, new ProviderPerformanceWindow(40, 20,
                90m, 20m, 0.5m, now))
        };

        Assert.Equal([0, 1], PerformanceRouteRanker.Rank("auto", options, now,
            TimeSpan.FromMinutes(5), 10, 0.10m, PerformanceRouteWeights.Default));
    }
}
