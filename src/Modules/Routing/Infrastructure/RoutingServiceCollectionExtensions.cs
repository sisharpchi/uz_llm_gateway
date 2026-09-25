using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Routing.Contracts;

namespace UZLLM.Modules.Routing.Infrastructure;

public static class RoutingServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmRouting(this IServiceCollection services)
    {
        services.AddSingleton(ProviderHealthOptions.Default);
        services.AddSingleton<IProviderHealthService, RedisProviderHealthService>();
        return services;
    }
}
