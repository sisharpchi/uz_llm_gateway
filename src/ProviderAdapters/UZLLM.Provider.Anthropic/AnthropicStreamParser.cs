using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Anthropic;

internal sealed class AnthropicStreamParser(string? requestId)
{
    private int? inputTokens;
    private int? uncachedInputTokens;
    private int? outputTokens;
    private int cachedRead;
    private readonly HashSet<int> toolIndices = [];
    public bool Completed { get; private set; }

    public IReadOnlyList<ProviderStreamEvent> Parse(AnthropicSseFrame frame)
    {
        using var document = JsonDocument.Parse(frame.Data);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        if (type != frame.EventType)
            throw new JsonException("Anthropic event type mismatch.");
        switch (type)
        {
            case "ping": return [];
            case "error":
                return [new ProviderStreamEvent(ProviderStreamKind.Error,
                    Error: AnthropicErrorClassifier.Unknown(requestId), ProviderRequestId: requestId)];
            case "message_start":
                UpdateUsage(root.GetProperty("message").GetProperty("usage"));
                return [];
            case "content_block_start":
                return StartBlock(root);
            case "content_block_delta":
                return Delta(root);
            case "content_block_stop": return [];
            case "message_delta":
                if (root.TryGetProperty("usage", out var usage)) UpdateUsage(usage);
                var rawReason = AnthropicWireMapper.GetOptionalString(root.GetProperty("delta"), "stop_reason");
                var reason = AnthropicWireMapper.MapStopReason(rawReason);
                if (reason is null) return [];
                return rawReason == "refusal"
                    ? [new ProviderStreamEvent(ProviderStreamKind.Refusal,
                            Text: "Provider refused the request.", ProviderRequestId: requestId),
                        new ProviderStreamEvent(ProviderStreamKind.Finish,
                            FinishReason: reason, ProviderRequestId: requestId)]
                    : [new ProviderStreamEvent(ProviderStreamKind.Finish,
                        FinishReason: reason, ProviderRequestId: requestId)];
            case "message_stop":
                if (inputTokens is null || outputTokens is null)
                    throw new JsonException("Anthropic stream ended without final usage.");
                Completed = true;
                return [new ProviderStreamEvent(ProviderStreamKind.Usage,
                    Usage: new ProviderUsage(inputTokens.Value, outputTokens.Value, cachedRead, null),
                    ProviderRequestId: requestId)];
            default: return []; // Anthropic may add new event types.
        }
    }

    private IReadOnlyList<ProviderStreamEvent> StartBlock(JsonElement root)
    {
        var index = root.GetProperty("index").GetInt32();
        var block = root.GetProperty("content_block");
        return block.GetProperty("type").GetString() switch
        {
            "text" when AnthropicWireMapper.GetOptionalString(block, "text") is { Length: > 0 } text =>
                [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: text,
                    ProviderRequestId: requestId)],
            "tool_use" => StartTool(index, block),
            "text" => [],
            _ => throw new JsonException("Anthropic returned an unsupported content block.")
        };
    }

    private IReadOnlyList<ProviderStreamEvent> StartTool(int index, JsonElement block)
    {
        if (!toolIndices.Add(index)) throw new JsonException("Duplicate Anthropic tool block index.");
        return [new ProviderStreamEvent(ProviderStreamKind.ToolCallDelta,
            ToolIndex: index, ToolCallId: block.GetProperty("id").GetString(),
            ToolName: block.GetProperty("name").GetString(), ArgumentsDelta: "",
            ProviderRequestId: requestId)];
    }

    private IReadOnlyList<ProviderStreamEvent> Delta(JsonElement root)
    {
        var index = root.GetProperty("index").GetInt32();
        var delta = root.GetProperty("delta");
        return delta.GetProperty("type").GetString() switch
        {
            "text_delta" => [new ProviderStreamEvent(ProviderStreamKind.TextDelta,
                Text: delta.GetProperty("text").GetString(), ProviderRequestId: requestId)],
            "input_json_delta" when toolIndices.Contains(index) =>
                [new ProviderStreamEvent(ProviderStreamKind.ToolCallDelta, ToolIndex: index,
                    ArgumentsDelta: delta.GetProperty("partial_json").GetString(),
                    ProviderRequestId: requestId)],
            _ => throw new JsonException("Anthropic returned an unsupported content delta.")
        };
    }

    private void UpdateUsage(JsonElement usage)
    {
        var newInput = AnthropicWireMapper.GetOptionalInt(usage, "input_tokens");
        var newOutput = AnthropicWireMapper.GetOptionalInt(usage, "output_tokens");
        var read = AnthropicWireMapper.GetOptionalInt(usage, "cache_read_input_tokens");
        var created = AnthropicWireMapper.GetOptionalInt(usage, "cache_creation_input_tokens");
        if (created is > 0 || newInput is < 0 || newOutput is < 0 || read is < 0)
            throw new JsonException("Anthropic usage has unsupported token dimensions.");
        if (newInput is not null) uncachedInputTokens = newInput;
        if (read is not null) cachedRead = read.Value;
        if (uncachedInputTokens is not null)
            inputTokens = checked(uncachedInputTokens.Value + cachedRead);
        if (newOutput is not null)
        {
            if (outputTokens is not null && newOutput < outputTokens)
                throw new JsonException("Anthropic cumulative output usage regressed.");
            outputTokens = newOutput;
        }
        if (inputTokens < cachedRead)
            throw new JsonException("Anthropic cache usage exceeded total input.");
    }
}
