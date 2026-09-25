using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Anthropic;

public static class AnthropicServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmAnthropicAdapter(this IServiceCollection services,
        IConfiguration configuration)
    {
        var raw = configuration["Providers:Anthropic:BaseUrl"];
        var endpoint = raw is null ? AnthropicAdapterOptions.Production.BaseAddress
            : new Uri(raw, UriKind.Absolute);
        if (endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.AbsolutePath.EndsWith("/v1/",
                StringComparison.Ordinal) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("Anthropic BaseUrl must be an HTTPS /v1/ endpoint.");

        services.AddSingleton(new AnthropicAdapterOptions(endpoint));
        services.AddHttpClient<AnthropicMessagesAdapter>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<AnthropicAdapterOptions>().BaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddScoped<ILlmProviderAdapter>(provider => provider.GetRequiredService<AnthropicMessagesAdapter>());
        return services;
    }
}
