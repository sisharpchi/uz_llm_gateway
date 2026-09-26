using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.DeepSeek;

public static class DeepSeekServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmDeepSeekAdapter(this IServiceCollection services,
        IConfiguration configuration)
    {
        var raw = configuration["Providers:DeepSeek:BaseUrl"];
        var endpoint = raw is null ? DeepSeekAdapterOptions.Production.BaseAddress
            : new Uri(raw, UriKind.Absolute);
        if (endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.AbsolutePath != "/v1/"
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("DeepSeek BaseUrl must be an HTTPS /v1/ endpoint.");
        services.AddSingleton(new DeepSeekAdapterOptions(endpoint));
        services.AddHttpClient<DeepSeekChatAdapter>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<DeepSeekAdapterOptions>().BaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddScoped<ILlmProviderAdapter>(provider => provider.GetRequiredService<DeepSeekChatAdapter>());
        return services;
    }
}
