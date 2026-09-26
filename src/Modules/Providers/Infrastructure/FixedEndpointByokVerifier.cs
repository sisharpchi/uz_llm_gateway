using System.Net;
using System.Net.Http.Headers;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

/// <summary>Checks credentials only against provider-owned, fixed model-list endpoints.</summary>
public sealed class FixedEndpointByokVerifier(HttpClient client) : IByokCredentialVerifier
{
    private static readonly Uri OpenAiModels = new("https://api.openai.com/v1/models");
    private static readonly Uri AnthropicModels = new("https://api.anthropic.com/v1/models");

    public async Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, providerCode switch
        {
            "openai" => OpenAiModels,
            "anthropic" => AnthropicModels,
            _ => throw new ArgumentException("Unsupported provider for BYOK verification.")
        });
        if (providerCode == "openai")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        else
        {
            request.Headers.Add("x-api-key", secret);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        try
        {
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ByokTestStatus.Invalid,
                _ when response.IsSuccessStatusCode => ByokTestStatus.Valid,
                _ => ByokTestStatus.Unavailable
            };
        }
        catch (HttpRequestException) { return ByokTestStatus.Unavailable; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ByokTestStatus.Unavailable; }
    }
}
