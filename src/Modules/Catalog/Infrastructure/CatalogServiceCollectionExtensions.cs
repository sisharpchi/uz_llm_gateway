using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Modules.Catalog.Infrastructure;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmCatalog(this IServiceCollection services)
    {
        services.AddScoped<ICatalogStore, PostgreSqlCatalogStore>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProviderDataPolicyStore, PostgreSqlProviderDataPolicyStore>();
        services.AddScoped<IProviderDataPolicyService, ProviderDataPolicyService>();
        return services;
    }
}
