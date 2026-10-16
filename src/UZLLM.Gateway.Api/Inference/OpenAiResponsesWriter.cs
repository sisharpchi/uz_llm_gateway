using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

/// <summary>Writes the non-streaming Responses text/refusal subset without storing conversational state.</summary>
public sealed class OpenAiResponsesWriter(HttpContext context, ParsedChatRequest request) : ICompletionWriter
{
    public bool Started => context.Response.HasStarted;

    public static void ValidateCompletion(ProviderCompletion completion)
    {
        if (completion.Message.Role != "assistant" || completion.Message.ToolCalls is { Count: > 0 }
            || completion.Message.Content.Any(part => part.Kind != ProviderContentKind.Text)
            || completion.FinishReason is not ("stop" or "length" or "content_filter"))
            throw new ProviderExecutionException(new(ProviderErrorCategory.Unknown,
                ProviderExecutionCertainty.Unknown, false, false, null, completion.ProviderRequestId,
                "Provider returned an unsupported response shape."));
    }

    public async Task WriteCompletionAsync(ProviderCompletion completion, string model, Guid requestId,
        CancellationToken cancellationToken)
    {
        ValidateCompletion(completion);
        var incomplete = completion.FinishReason is "length" or "content_filter";
        var content = new List<object>();
        if (completion.Message.Content.Count > 0)
            content.Add(new { type = "output_text", text = string.Concat(completion.Message.Content.Select(part => part.Value)),
                annotations = Array.Empty<object>(), logprobs = Array.Empty<object>() });
        if (completion.Refusal is not null) content.Add(new { type = "refusal", refusal = completion.Refusal });
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            id = "resp_" + requestId.ToString("N"), @object = "response",
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            status = incomplete ? "incomplete" : "completed", error = (object?)null,
            incomplete_details = incomplete ? new { reason = completion.FinishReason == "length" ? "max_output_tokens" : "content_filter" } : null,
            model,
            output = new[] { new { id = "msg_" + requestId.ToString("N"), type = "message", role = "assistant",
                status = incomplete ? "incomplete" : "completed", content } },
            instructions = request.Instructions, max_output_tokens = request.ProviderRequest.MaxOutputTokens,
            store = false, previous_response_id = (string?)null, parallel_tool_calls = false,
            reasoning = new { effort = (string?)null, summary = (string?)null },
            temperature = request.ProviderRequest.Temperature, top_p = request.ProviderRequest.TopP,
            text = new { format = new { type = "text" } }, tool_choice = "none", tools = Array.Empty<object>(),
            truncation = "disabled", metadata = new Dictionary<string, string>(),
            usage = completion.Usage is not { } usage ? null : new
            {
                input_tokens = usage.InputTokens, output_tokens = usage.OutputTokens,
                total_tokens = checked((long)usage.InputTokens + usage.OutputTokens),
                input_tokens_details = new { cached_tokens = usage.CachedInputTokens },
                output_tokens_details = new { reasoning_tokens = usage.ReasoningTokens ?? 0 }
            }
        }, cancellationToken);
    }

    public Task WriteStreamEventAsync(ProviderStreamEvent item, string model, Guid requestId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Responses streaming is not implemented.");
    public Task FinishStreamAsync(CancellationToken cancellationToken) => throw new NotSupportedException("Responses streaming is not implemented.");
    public Task WriteStreamErrorAsync(string message, string code, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Responses streaming is not implemented.");
}
