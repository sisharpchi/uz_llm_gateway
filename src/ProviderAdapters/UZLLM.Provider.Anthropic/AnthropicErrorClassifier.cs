using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Anthropic;

internal static class AnthropicErrorClassifier
{
    public static ProviderError FromHttp(int status, string? body, string? requestId)
    {
        var type = ReadType(body);
        var category = status switch
        {
            401 or 403 => ProviderErrorCategory.Authentication,
            402 => ProviderErrorCategory.QuotaExhausted,
            429 => ProviderErrorCategory.RateLimited,
            529 => ProviderErrorCategory.Capacity,
            408 or 504 => ProviderErrorCategory.Timeout,
            >= 500 => ProviderErrorCategory.Upstream5xx,
            _ when type is "permission_error" => ProviderErrorCategory.Authentication,
            _ when type is "billing_error" => ProviderErrorCategory.QuotaExhausted,
            _ => ProviderErrorCategory.InvalidRequest
        };
        // Explicit 4xx rejections (except timeout) and overload are pre-execution.
        // A provider 5xx/timeout may have generated and billed tokens.
        var preExecution = status is >= 400 and < 500 && status != 408 || status == 529;
        var retryable = status is 429 or 529;
        return new ProviderError(category,
            preExecution ? ProviderExecutionCertainty.RejectedBeforeExecution
                : ProviderExecutionCertainty.Unknown,
            retryable, retryable, status, requestId, SafeMessage(category));
    }

    public static ProviderError Unknown(string? requestId) => new(
        ProviderErrorCategory.Unknown, ProviderExecutionCertainty.Unknown, false, false,
        null, requestId, "Provider outcome is unknown.");

    public static ProviderError TransportTimeout() => new(
        ProviderErrorCategory.Timeout, ProviderExecutionCertainty.Unknown, false, false,
        null, null, "Provider request timed out; execution outcome is unknown.");

    private static string? ReadType(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string SafeMessage(ProviderErrorCategory category) => category switch
    {
        ProviderErrorCategory.Authentication => "Provider credential was rejected.",
        ProviderErrorCategory.QuotaExhausted => "Provider credit is unavailable.",
        ProviderErrorCategory.RateLimited => "Provider rate limit was reached.",
        ProviderErrorCategory.Capacity => "Provider capacity is unavailable.",
        ProviderErrorCategory.Timeout => "Provider request timed out; execution outcome is unknown.",
        ProviderErrorCategory.InvalidRequest => "Provider rejected the request.",
        _ => "Provider failed; execution outcome is unknown."
    };
}
