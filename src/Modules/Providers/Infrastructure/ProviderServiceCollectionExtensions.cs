using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Providers.Application;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmProviders(this IServiceCollection services)
    {
        services.AddScoped<IProviderCredentialStore, PostgreSqlProviderCredentialStore>();
        services.AddScoped<IPlatformCredentialService, PlatformCredentialService>();
        services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();
        services.AddSingleton<IProviderSecretProtector, ProviderEnvelopeSecretProtector>();
        return services;
    }
}
