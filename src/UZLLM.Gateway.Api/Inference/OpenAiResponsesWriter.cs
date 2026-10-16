using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

/// <summary>Writes the stateless Responses text/refusal subset, including incremental SSE.</summary>
public sealed class OpenAiResponsesWriter(HttpContext context, ParsedChatRequest request) : ICompletionWriter
{
    public const int MaximumStreamCharacters = 1_048_576;
    private readonly List<StreamPart> parts = [];
    private readonly long createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private int sequence, characters;
    private bool started, terminal;
    private string? finishReason, streamModel;
    private Guid streamRequestId;
    private ProviderUsage? streamUsage;
    public bool HasInvalidUsageEvidence { get; private set; }
    public bool Started => started || context.Response.HasStarted;

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
        await context.Response.WriteAsJsonAsync(Response(model, requestId,
            incomplete ? "incomplete" : "completed", [new { id = "msg_" + requestId.ToString("N"),
                type = "message", role = "assistant", status = incomplete ? "incomplete" : "completed", content }],
            completion.Usage, completion.FinishReason), cancellationToken);
    }

    private object Response(string model, Guid requestId, string status, object[] output,
        ProviderUsage? providerUsage, string? reason = null, object? error = null) => new
        {
            id = "resp_" + requestId.ToString("N"), @object = "response",
            created_at = createdAt, status, error,
            incomplete_details = status == "incomplete" ? new { reason = reason == "length" ? "max_output_tokens" : "content_filter" } : null,
            model, output,
            instructions = request.Instructions, max_output_tokens = request.ProviderRequest.MaxOutputTokens,
            store = false, previous_response_id = (string?)null, parallel_tool_calls = false,
            reasoning = new { effort = (string?)null, summary = (string?)null },
            temperature = request.ProviderRequest.Temperature, top_p = request.ProviderRequest.TopP,
            text = new { format = new { type = "text" } }, tool_choice = "none", tools = Array.Empty<object>(),
            truncation = "disabled", metadata = new Dictionary<string, string>(),
            usage = providerUsage is not { } usage ? null : new
            {
                input_tokens = usage.InputTokens, output_tokens = usage.OutputTokens,
                total_tokens = checked((long)usage.InputTokens + usage.OutputTokens),
                input_tokens_details = new { cached_tokens = usage.CachedInputTokens },
                output_tokens_details = new { reasoning_tokens = usage.ReasoningTokens ?? 0 }
            }
        };

    public async Task WriteStreamEventAsync(ProviderStreamEvent item, string model, Guid requestId,
        CancellationToken cancellationToken)
    {
        // Validate before writing headers. Unknown/unsupported output is never silently coerced to text.
        if (terminal || item.Kind is not (ProviderStreamKind.TextDelta or ProviderStreamKind.Refusal
            or ProviderStreamKind.Finish or ProviderStreamKind.Usage)) throw InvalidStream();
        if (item.Kind == ProviderStreamKind.Finish)
        {
            if (finishReason is not null || item.FinishReason is not ("stop" or "length" or "content_filter"))
                throw InvalidStream();
            finishReason = item.FinishReason;
        }
        else if (item.Kind == ProviderStreamKind.Usage)
        {
            if (streamUsage is not null || item.Usage is not { } usage || usage.InputTokens < 0 || usage.OutputTokens < 0
                || usage.CachedInputTokens < 0 || usage.CachedInputTokens > usage.InputTokens
                || usage.ReasoningTokens < 0 || usage.ReasoningTokens > usage.OutputTokens)
            {
                // Contradictory counters are not authoritative evidence; neither snapshot may settle the hold.
                streamUsage = null; HasInvalidUsageEvidence = true;
                throw InvalidStream();
            }
            streamUsage = item.Usage;
        }
        else if (finishReason is not null || item.Text is null
            || item.Text.Length > MaximumStreamCharacters - characters) throw InvalidStream();

        if (!started)
        {
            streamModel = model; streamRequestId = requestId;
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            started = true; // Also guards fallback in transports whose HasStarted is deferred.
            await EmitAsync("response.created", new { response = Snapshot("in_progress", false) }, cancellationToken);
            await EmitAsync("response.in_progress", new { response = Snapshot("in_progress", false) }, cancellationToken);
            await EmitAsync("response.output_item.added", new { output_index = 0, item = Message("in_progress") }, cancellationToken);
        }
        if (item.Kind is not (ProviderStreamKind.TextDelta or ProviderStreamKind.Refusal)) return;
        var kind = item.Kind == ProviderStreamKind.TextDelta ? "output_text" : "refusal";
        var part = parts.Find(value => value.Kind == kind);
        if (part is null)
        {
            part = new StreamPart(kind, parts.Count); parts.Add(part);
            await EmitAsync("response.content_part.added", new { item_id = MessageId, output_index = 0,
                content_index = part.Index, part = Part(part) }, cancellationToken);
        }
        part.Text.Append(item.Text); characters += item.Text!.Length;
        await EmitAsync("response." + kind + ".delta", new { item_id = MessageId, output_index = 0,
            content_index = part.Index, delta = item.Text, logprobs = Array.Empty<object>() }, cancellationToken);
    }

    // Called before recording success/settling, not after writing a fabricated terminal event.
    public void ValidateStreamEnd()
    {
        if (finishReason is null || !started || terminal) throw InvalidStream();
    }

    public async Task FinishStreamAsync(CancellationToken cancellationToken)
    {
        ValidateStreamEnd();
        var status = finishReason == "stop" ? "completed" : "incomplete";
        foreach (var part in parts)
        {
            var value = new Dictionary<string, object?> { ["item_id"] = MessageId, ["output_index"] = 0,
                ["content_index"] = part.Index, [part.Kind == "output_text" ? "text" : "refusal"] = part.Text.ToString() };
            if (part.Kind == "output_text") value["logprobs"] = Array.Empty<object>();
            await EmitAsync("response." + part.Kind + ".done", value, cancellationToken);
            await EmitAsync("response.content_part.done", new { item_id = MessageId, output_index = 0,
                content_index = part.Index, part = Part(part) }, cancellationToken);
        }
        await EmitAsync("response.output_item.done", new { output_index = 0, item = Message(status) }, cancellationToken);
        await EmitAsync("response." + status, new { response = Snapshot(status) }, cancellationToken);
        terminal = true;
    }

    public async Task WriteStreamErrorAsync(string message, string code, CancellationToken cancellationToken)
    {
        if (!started || terminal) return;
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        delivery.CancelAfter(TimeSpan.FromSeconds(10));
        await EmitAsync("response.failed", new { response = Snapshot("failed", error: new { code, message }) }, delivery.Token);
        terminal = true;
    }

    private string MessageId => "msg_" + streamRequestId.ToString("N");
    private object Message(string status) => new { id = MessageId, type = "message", role = "assistant",
        status, content = parts.Select(Part).ToArray() };
    private static object Part(StreamPart part) => part.Kind == "output_text"
        ? new { type = "output_text", text = part.Text.ToString(), annotations = Array.Empty<object>(), logprobs = Array.Empty<object>() }
        : (object)new { type = "refusal", refusal = part.Text.ToString() };
    private object Snapshot(string status, bool includeOutput = true, object? error = null) =>
        Response(streamModel!, streamRequestId, status, includeOutput ? [Message(status == "failed" ? "incomplete" : status)] : [],
            status == "in_progress" ? null : streamUsage, finishReason, error);
    private async Task EmitAsync(string type, object payload, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToNode(payload)!.AsObject();
        body["type"] = type; body["sequence_number"] = sequence++;
        await context.Response.WriteAsync("event: " + type + "\ndata: " + body.ToJsonString() + "\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
    private static ProviderExecutionException InvalidStream() => new(new(ProviderErrorCategory.Unknown,
        ProviderExecutionCertainty.Unknown, false, false, null, null, "Provider returned an unsupported or incomplete stream."));
    private sealed class StreamPart(string kind, int index)
    {
        public string Kind { get; } = kind;
        public int Index { get; } = index;
        public StringBuilder Text { get; } = new();
    }
}
