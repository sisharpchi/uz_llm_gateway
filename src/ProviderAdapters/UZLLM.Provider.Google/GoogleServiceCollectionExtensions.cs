using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Google;

public static class GoogleServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmGoogleAdapter(this IServiceCollection services,
        IConfiguration configuration)
    {
        var raw = configuration["Providers:Google:BaseUrl"];
        var endpoint = raw is null ? GoogleAdapterOptions.Production.BaseAddress
            : new Uri(raw, UriKind.Absolute);
        if (endpoint.Scheme != Uri.UriSchemeHttps
            || !endpoint.AbsolutePath.EndsWith("/v1beta/", StringComparison.Ordinal)
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("Google BaseUrl must be an HTTPS /v1beta/ endpoint.");
        services.AddSingleton(new GoogleAdapterOptions(endpoint));
        services.AddHttpClient<GoogleContentAdapter>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<GoogleAdapterOptions>().BaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddScoped<ILlmProviderAdapter>(provider => provider.GetRequiredService<GoogleContentAdapter>());
        return services;
    }
}
