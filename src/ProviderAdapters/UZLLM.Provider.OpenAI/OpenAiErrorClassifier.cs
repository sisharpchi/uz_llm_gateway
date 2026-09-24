using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.OpenAI;

internal static class OpenAiErrorClassifier
{
    private static readonly HashSet<string> QuotaCodes =
    [
        "credit_balance_exhausted", "organization_spend_limit_exceeded",
        "project_spend_limit_exceeded", "organization_usage_limit_exceeded",
        "insufficient_quota"
    ];

    public static ProviderError FromHttp(int status, string? body, string? requestId)
    {
        var code = ReadCode(body);
        ProviderErrorCategory category;
        ProviderExecutionCertainty certainty = ProviderExecutionCertainty.RejectedBeforeExecution;
        bool retryable = false;
        bool fallback = false;
        if (status is 401 or 403) category = ProviderErrorCategory.Authentication;
        else if (status == 429 && code is not null && QuotaCodes.Contains(code))
            category = ProviderErrorCategory.QuotaExhausted;
        else if (status == 429)
        {
            category = ProviderErrorCategory.RateLimited;
            retryable = fallback = true;
        }
        else if (status == 400 && code == "context_length_exceeded")
            category = ProviderErrorCategory.ContextExceeded;
        else if (status == 400 && code is "content_policy_violation" or "content_filter")
            category = ProviderErrorCategory.ContentRejected;
        else if (status is >= 400 and < 500 && status != 408)
            category = ProviderErrorCategory.InvalidRequest;
        else if (status == 503 && code == "server_is_overloaded")
        {
            category = ProviderErrorCategory.Capacity;
            certainty = ProviderExecutionCertainty.Unknown;
        }
        else
        {
            category = status is 408 or 504 ? ProviderErrorCategory.Timeout
                : status >= 500 ? ProviderErrorCategory.Upstream5xx : ProviderErrorCategory.Unknown;
            certainty = ProviderExecutionCertainty.Unknown;
        }
        return new ProviderError(category, certainty, retryable, fallback, status,
            requestId, SafeMessage(category));
    }

    public static ProviderError UnknownStream(string? requestId) => new(
        ProviderErrorCategory.Unknown, ProviderExecutionCertainty.Unknown,
        false, false, null, requestId, SafeMessage(ProviderErrorCategory.Unknown));

    public static ProviderError TransportTimeout() => new(
        ProviderErrorCategory.Timeout, ProviderExecutionCertainty.Unknown,
        false, false, null, null, SafeMessage(ProviderErrorCategory.Timeout));

    public static ProviderError TransportFailure() => new(
        ProviderErrorCategory.Unknown, ProviderExecutionCertainty.Unknown,
        false, false, null, null, "Provider transport failed; execution outcome is unknown.");

    private static string? ReadCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error)
                ? OpenAiWireMapper.GetOptionalString(error, "code") : null;
        }
        catch (JsonException) { return null; }
    }

    private static string SafeMessage(ProviderErrorCategory category) => category switch
    {
        ProviderErrorCategory.Authentication => "Provider credential was rejected.",
        ProviderErrorCategory.RateLimited => "Provider rate limit was reached.",
        ProviderErrorCategory.QuotaExhausted => "Provider quota or credit is exhausted.",
        ProviderErrorCategory.InvalidRequest => "Provider rejected the request.",
        ProviderErrorCategory.ContextExceeded => "Provider context limit was exceeded.",
        ProviderErrorCategory.ContentRejected => "Provider rejected the content.",
        ProviderErrorCategory.Capacity => "Provider capacity is unavailable.",
        ProviderErrorCategory.Timeout => "Provider request timed out; execution outcome is unknown.",
        _ => "Provider failed; execution outcome is unknown."
    };
}
