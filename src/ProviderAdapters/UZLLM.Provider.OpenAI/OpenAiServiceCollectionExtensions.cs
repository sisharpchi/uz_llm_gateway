using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.OpenAI;

public static class OpenAiServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmOpenAiAdapter(this IServiceCollection services,
        IConfiguration configuration)
    {
        var rawBaseUrl = configuration["Providers:OpenAI:BaseUrl"];
        var endpoint = rawBaseUrl is null ? OpenAiAdapterOptions.Production.BaseAddress
            : new Uri(rawBaseUrl, UriKind.Absolute);
        if (endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.AbsolutePath.EndsWith("/v1/",
                StringComparison.Ordinal) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("OpenAI BaseUrl must be an HTTPS /v1/ endpoint.");

        services.AddSingleton(new OpenAiAdapterOptions(endpoint));
        services.AddHttpClient<OpenAiChatAdapter>((provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<OpenAiAdapterOptions>().BaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddScoped<ILlmProviderAdapter>(provider => provider.GetRequiredService<OpenAiChatAdapter>());
        return services;
    }
}
