using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public interface ICompletionWriter
{
    bool Started { get; }
    Task WriteCompletionAsync(ProviderCompletion completion, string model, Guid requestId,
        CancellationToken cancellationToken);
    Task WriteStreamEventAsync(ProviderStreamEvent item, string model, Guid requestId,
        CancellationToken cancellationToken);
    Task FinishStreamAsync(CancellationToken cancellationToken);
    Task WriteStreamErrorAsync(string message, string code, CancellationToken cancellationToken);
}

public interface ICompletionWriterFactory
{
    ICompletionWriter Create(HttpContext context);
}

public sealed class OpenAiCompletionWriterFactory : ICompletionWriterFactory
{
    public ICompletionWriter Create(HttpContext context) => new OpenAiCompletionWriter(context);
}

public sealed class OpenAiCompletionWriter(HttpContext context) : ICompletionWriter
{
    public bool Started => context.Response.HasStarted;

    public async Task WriteCompletionAsync(ProviderCompletion completion, string model,
        Guid requestId, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            id = CompletionId(requestId),
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model,
            choices = new[] { new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = completion.Message.Content.Count == 0 ? null
                        : string.Concat(completion.Message.Content.Select(part => part.Value)),
                    refusal = completion.Refusal,
                    tool_calls = completion.Message.ToolCalls?.Select(call => new
                    {
                        id = call.Id, type = "function",
                        function = new { name = call.Name, arguments = call.ArgumentsJson }
                    }).ToArray()
                },
                finish_reason = completion.FinishReason
            } },
            usage = Usage(completion.Usage)
        }, cancellationToken);
    }

    public async Task WriteStreamEventAsync(ProviderStreamEvent item, string model,
        Guid requestId, CancellationToken cancellationToken)
    {
        if (!Started)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await WriteDataAsync(new
            {
                id = CompletionId(requestId), @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
                choices = new[] { new { index = 0, delta = new { role = "assistant" }, finish_reason = (string?)null } }
            }, cancellationToken);
        }
        if (item.Kind == ProviderStreamKind.Error)
        {
            await WriteStreamErrorAsync(item.Error?.SafeMessage ?? "Provider stream failed.",
                "provider_error", cancellationToken);
            return;
        }
        object payload = item.Kind switch
        {
            ProviderStreamKind.Usage => new
            {
                id = CompletionId(requestId), @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
                choices = Array.Empty<object>(), usage = Usage(item.Usage)
            },
            _ => new
            {
                id = CompletionId(requestId), @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model,
                choices = new[] { new
                {
                    index = 0,
                    delta = (object)(item.Kind switch
                    {
                        ProviderStreamKind.TextDelta => new { content = item.Text } as object,
                        ProviderStreamKind.Refusal => new { refusal = item.Text },
                        ProviderStreamKind.ToolCallDelta => new { tool_calls = new[] { new
                        {
                            index = item.ToolIndex, id = item.ToolCallId, type = "function",
                            function = new { name = item.ToolName, arguments = item.ArgumentsDelta }
                        } } },
                        _ => new { }
                    }),
                    finish_reason = item.Kind == ProviderStreamKind.Finish ? item.FinishReason : null
                } }
            }
        };
        await WriteDataAsync(payload, cancellationToken);
    }

    public async Task FinishStreamAsync(CancellationToken cancellationToken)
    {
        if (!Started)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
        }
        await context.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    public async Task WriteStreamErrorAsync(string message, string code, CancellationToken cancellationToken)
    {
        if (!Started) return;
        var envelope = JsonSerializer.Serialize(new { error = new
        {
            message, type = "upstream_error", code, param = (string?)null
        } });
        await context.Response.WriteAsync($"event: error\ndata: {envelope}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteDataAsync(object payload, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync("data: " + JsonSerializer.Serialize(payload) + "\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static string CompletionId(Guid requestId) => "chatcmpl-" + requestId.ToString("N");

    private static object? Usage(ProviderUsage? usage) => usage is null ? null : new
    {
        prompt_tokens = usage.InputTokens,
        completion_tokens = usage.OutputTokens,
        total_tokens = checked(usage.InputTokens + usage.OutputTokens),
        prompt_tokens_details = new { cached_tokens = usage.CachedInputTokens },
        completion_tokens_details = new { reasoning_tokens = usage.ReasoningTokens ?? 0 }
    };
}
