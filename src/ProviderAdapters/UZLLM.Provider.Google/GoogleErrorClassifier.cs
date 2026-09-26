using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Google;

internal static class GoogleErrorClassifier
{
    public static ProviderError FromHttp(int statusCode, string? body, string? requestId)
    {
        var providerStatus = ReadStatus(body);
        var invalidKey = body is not null && body.Contains("API_KEY_INVALID", StringComparison.Ordinal);
        var category = statusCode switch
        {
            400 when invalidKey || providerStatus == "UNAUTHENTICATED" => ProviderErrorCategory.Authentication,
            400 => ProviderErrorCategory.InvalidRequest,
            401 or 403 => ProviderErrorCategory.Authentication,
            402 => ProviderErrorCategory.QuotaExhausted,
            404 => ProviderErrorCategory.InvalidRequest,
            429 => ProviderErrorCategory.RateLimited,
            503 => ProviderErrorCategory.Capacity,
            504 => ProviderErrorCategory.Timeout,
            >= 500 => ProviderErrorCategory.Upstream5xx,
            _ => ProviderErrorCategory.Unknown
        };
        // A completed HTTP rejection is safe to classify as pre-execution only
        // for explicit client/quota/rate failures. 5xx may have consumed tokens.
        var preExecution = statusCode is 400 or 401 or 402 or 403 or 404 or 429;
        var fallback = statusCode == 429;
        return new(category, preExecution ? ProviderExecutionCertainty.RejectedBeforeExecution
                : ProviderExecutionCertainty.Unknown,
            fallback, fallback, statusCode, requestId, "Google provider rejected the request.");
    }

    public static ProviderError Unknown(string? requestId) => new(
        ProviderErrorCategory.Unknown, ProviderExecutionCertainty.Unknown,
        false, false, null, requestId, "Google provider outcome is unknown.");

    public static ProviderError Timeout(string? requestId) => new(ProviderErrorCategory.Timeout,
        ProviderExecutionCertainty.Unknown, false, false, null, requestId,
        "Google provider timed out; outcome is unknown.");

    private static string? ReadStatus(string? body)
    {
        if (body is null) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String ? status.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
