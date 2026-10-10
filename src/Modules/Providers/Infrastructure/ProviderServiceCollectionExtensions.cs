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
        services.AddUzllmProviderResolution(configuration);
        services.AddScoped<IByokCredentialService, ByokCredentialService>();
        services.AddHttpClient<IByokCredentialVerifier, FixedEndpointByokVerifier>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false, UseCookies = false
            });
        services.AddScoped<IPlatformCredentialService, PlatformCredentialService>();
        services.AddScoped<ProviderCredentialKeyRotation>();
        return services;
    }

    // The inference host only resolves encrypted credentials; management-only BYOK
    // mutation and key rotation require Organization/Audit registrations.
    public static IServiceCollection AddUzllmProviderResolution(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IProviderCredentialStore, PostgreSqlProviderCredentialStore>();
        services.AddScoped<IByokCredentialStore, PostgreSqlByokCredentialStore>();
        services.AddScoped<IByokCredentialResolver, ByokCredentialResolver>();
        services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();
        services.AddSingleton(new ProviderEnvelopeSecretProtector(configuration));
        services.AddSingleton<IProviderSecretProtector>(provider =>
            provider.GetRequiredService<ProviderEnvelopeSecretProtector>());
        return services;
    }
}
