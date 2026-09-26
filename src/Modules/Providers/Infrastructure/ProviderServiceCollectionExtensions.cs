using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Providers.Application;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmProviders(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IProviderCredentialStore, PostgreSqlProviderCredentialStore>();
        services.AddScoped<IByokCredentialStore, PostgreSqlByokCredentialStore>();
        services.AddScoped<IByokCredentialService, ByokCredentialService>();
        services.AddScoped<IByokCredentialResolver, ByokCredentialResolver>();
        services.AddHttpClient<IByokCredentialVerifier, FixedEndpointByokVerifier>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false, UseCookies = false
            });
        services.AddScoped<IPlatformCredentialService, PlatformCredentialService>();
        services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();
        services.AddSingleton<IProviderSecretProtector>(new ProviderEnvelopeSecretProtector(configuration));
        return services;
    }
}
