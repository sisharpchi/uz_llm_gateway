using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public static class OpenAiEmbeddingsWriter
{
    public static Task WriteAsync(HttpContext context, ProviderEmbeddingResult result,
        string model, CancellationToken cancellationToken) => context.Response.WriteAsJsonAsync(new
        {
            @object = "list", model,
            data = result.Data.Select(item => new { @object = "embedding", index = item.Index, embedding = item.Values }),
            usage = result.Usage is { } usage ? new { prompt_tokens = usage.InputTokens, total_tokens = usage.InputTokens } : null
        }, cancellationToken);
}
