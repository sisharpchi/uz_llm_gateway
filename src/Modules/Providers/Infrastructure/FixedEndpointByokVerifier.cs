using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

/// <summary>Checks credentials only against provider-owned, fixed model-list endpoints.</summary>
public sealed class FixedEndpointByokVerifier(HttpClient client) : IByokCredentialVerifier
{
    private static readonly Uri OpenAiModels = new("https://api.openai.com/v1/models");
    private static readonly Uri AnthropicModels = new("https://api.anthropic.com/v1/models");
    private static readonly Uri GoogleModels = new("https://generativelanguage.googleapis.com/v1beta/models");
    private static readonly Uri DeepSeekModels = new("https://api.deepseek.com/models");

    public async Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, providerCode switch
        {
            "openai" => OpenAiModels,
            "anthropic" => AnthropicModels,
            "google" => GoogleModels,
            "deepseek" => DeepSeekModels,
            _ => throw new ArgumentException("Unsupported provider for BYOK verification.")
        });
        if (providerCode is "openai" or "deepseek")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        else if (providerCode == "anthropic")
        {
            request.Headers.Add("x-api-key", secret);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else request.Headers.Add("x-goog-api-key", secret);
        // ResponseHeadersRead does not extend HttpClient.Timeout to body reads.
        // One bounded deadline covers headers and the Google error-only parser.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (providerCode == "google" && response.StatusCode == HttpStatusCode.BadRequest)
                return await IsInvalidGoogleKeyAsync(response, deadline.Token)
                    ? ByokTestStatus.Invalid : ByokTestStatus.Unavailable;
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ByokTestStatus.Invalid,
                _ when response.IsSuccessStatusCode => ByokTestStatus.Valid,
                _ => ByokTestStatus.Unavailable
            };
        }
        catch (HttpRequestException) { return ByokTestStatus.Unavailable; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ByokTestStatus.Unavailable; }
        catch (JsonException) { return ByokTestStatus.Unavailable; }
    }

    private static async Task<bool> IsInvalidGoogleKeyAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Only structured ErrorInfo proves invalid authentication. Generic 400,
        // billing preconditions or provider message text must not invalidate a key.
        await response.Content.LoadIntoBufferAsync(16 * 1024, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array) return false;
        foreach (var detail in details.EnumerateArray())
            if (detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String
                && type.GetString() == "type.googleapis.com/google.rpc.ErrorInfo"
                && detail.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                && reason.GetString() == "API_KEY_INVALID") return true;
        return false;
    }
}
