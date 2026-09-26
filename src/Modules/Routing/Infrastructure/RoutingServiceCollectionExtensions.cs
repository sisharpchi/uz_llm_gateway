using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Domain;

namespace UZLLM.Modules.Routing.Infrastructure;

public static class RoutingServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmRouting(this IServiceCollection services,
        IConfiguration configuration)
    {
        var defaults = ProviderPerformanceOptions.Default;
        var settings = configuration.GetSection("RoutingPerformance");
        var options = new ProviderPerformanceOptions(
            TimeSpan.FromSeconds(settings.GetValue("WindowSeconds", (int)defaults.Window.TotalSeconds)),
            settings.GetValue("MinimumSamples", defaults.MinimumSamples),
            settings.GetValue("MaximumSamples", defaults.MaximumSamples),
            settings.GetValue("Hysteresis", defaults.Hysteresis),
            new PerformanceRouteWeights(
                settings.GetValue("PriceWeight", defaults.Weights.Price),
                settings.GetValue("ErrorWeight", defaults.Weights.Error),
                settings.GetValue("LatencyWeight", defaults.Weights.Latency),
                settings.GetValue("ThroughputWeight", defaults.Weights.Throughput)));
        options.Validate();
        services.AddSingleton(ProviderHealthOptions.Default);
        services.AddSingleton<IProviderHealthService, RedisProviderHealthService>();
        services.AddSingleton(options);
        services.AddSingleton<IProviderPerformanceService, RedisProviderPerformanceService>();
        return services;
    }
}
