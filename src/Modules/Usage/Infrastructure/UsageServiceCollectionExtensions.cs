using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Modules.Usage.Infrastructure;

public static class UsageServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmUsage(this IServiceCollection services)
    {
        services.AddScoped<IUsageStore, PostgreSqlUsageStore>();
        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IUsageReadStore, PostgreSqlUsageReadStore>();
        services.AddScoped<IUsageReadService, UsageReadService>();
        return services;
    }
}
