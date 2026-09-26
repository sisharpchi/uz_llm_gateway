using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.DeepSeek;

internal static class DeepSeekErrorClassifier
{
    public static ProviderError FromHttp(int status, string? requestId)
    {
        var category = status switch
        {
            400 or 422 => ProviderErrorCategory.InvalidRequest,
            401 or 403 => ProviderErrorCategory.Authentication,
            402 => ProviderErrorCategory.QuotaExhausted,
            429 => ProviderErrorCategory.RateLimited,
            503 => ProviderErrorCategory.Capacity,
            408 or 504 => ProviderErrorCategory.Timeout,
            >= 500 => ProviderErrorCategory.Upstream5xx,
            _ => ProviderErrorCategory.Unknown
        };
        var rejected = status is 400 or 401 or 402 or 403 or 422 or 429;
        return new ProviderError(category, rejected
                ? ProviderExecutionCertainty.RejectedBeforeExecution
                : ProviderExecutionCertainty.Unknown,
            status == 429, status == 429, status, requestId,
            "DeepSeek provider rejected the request.");
    }

    public static ProviderError Unknown(string? requestId) => new(ProviderErrorCategory.Unknown,
        ProviderExecutionCertainty.Unknown, false, false, null, requestId,
        "DeepSeek provider outcome is unknown.");

    public static ProviderError Timeout(string? requestId) => new(ProviderErrorCategory.Timeout,
        ProviderExecutionCertainty.Unknown, false, false, null, requestId,
        "DeepSeek provider timed out; outcome is unknown.");
}
