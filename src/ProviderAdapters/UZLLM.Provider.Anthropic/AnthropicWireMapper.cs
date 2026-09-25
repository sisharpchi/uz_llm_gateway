using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Anthropic;

internal static class AnthropicWireMapper
{
    public static byte[] BuildRequest(ProviderChatRequest request, string model, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(model) || request.Messages is not { Count: > 0 }
            || request.MaxOutputTokens is not > 0 || request.Temperature is < 0 or > 1
            || request.TopP is <= 0 or > 1)
            throw new ArgumentException("Anthropic requires a model, messages, and supported output settings.");

        var system = new List<object>();
        var messages = new List<object>();
        var dialogueStarted = false;
        foreach (var message in request.Messages)
        {
            if (message.Role is "system" or "developer")
            {
                if (dialogueStarted || message.ToolCalls is { Count: > 0 }
                    || message.Content.Any(part => part.Kind != ProviderContentKind.Text))
                    throw new ArgumentException("Anthropic system instructions must precede dialogue and contain text.");
                foreach (var part in message.Content) system.Add(new { type = "text", text = part.Value });
                continue;
            }
            dialogueStarted = true;
            messages.Add(MapMessage(message));
        }
        if (messages.Count == 0) throw new ArgumentException("Anthropic requires a dialogue message.");

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["messages"] = messages,
            ["stream"] = stream
        };
        if (system.Count > 0) body["system"] = system;
        if (request.Temperature is not null) body["temperature"] = request.Temperature;
        if (request.TopP is not null) body["top_p"] = request.TopP;
        if (request.Stop is { Count: > 0 }) body["stop_sequences"] = request.Stop;
        if (request.Tools is { Count: > 0 }) body["tools"] = request.Tools.Select(MapTool).ToArray();
        if (request.ToolChoice is not null) body["tool_choice"] = MapToolChoice(request.ToolChoice, request.Tools);
        if (request.ResponseFormat is { Kind: ProviderResponseFormatKind.JsonObject })
            body["output_config"] = new { format = new { type = "json_schema", schema = new { type = "object" } } };
        else if (request.ResponseFormat is { Kind: ProviderResponseFormatKind.JsonSchema } format)
        {
            if (format.Schema is not { ValueKind: JsonValueKind.Object })
                throw new ArgumentException("JSON schema output requires an object schema.");
            body["output_config"] = new { format = new { type = "json_schema", schema = format.Schema } };
        }
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static ProviderCompletion ParseCompletion(JsonElement root, string? requestId)
    {
        var content = new List<ProviderContentPart>();
        var tools = new List<ProviderToolCall>();
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    content.Add(new ProviderContentPart(ProviderContentKind.Text,
                        block.GetProperty("text").GetString()!));
                    break;
                case "tool_use":
                    tools.Add(new ProviderToolCall(block.GetProperty("id").GetString()!,
                        block.GetProperty("name").GetString()!, block.GetProperty("input").GetRawText()));
                    break;
                default:
                    throw new JsonException("Anthropic returned an unsupported content block.");
            }
        }
        var stopReason = GetOptionalString(root, "stop_reason");
        return new ProviderCompletion(root.GetProperty("id").GetString()!,
            root.GetProperty("model").GetString()!, new ProviderMessage("assistant", content, tools),
            MapStopReason(stopReason), ParseUsage(root.GetProperty("usage")),
            requestId, stopReason == "refusal" ? "Provider refused the request." : null);
    }

    public static ProviderUsage ParseUsage(JsonElement usage)
    {
        var uncached = usage.GetProperty("input_tokens").GetInt32();
        var output = usage.GetProperty("output_tokens").GetInt32();
        var read = GetOptionalInt(usage, "cache_read_input_tokens") ?? 0;
        var created = GetOptionalInt(usage, "cache_creation_input_tokens") ?? 0;
        // Cache creation has a different upstream price. Until Catalog can price
        // that dimension, refuse to claim verified billable usage for it.
        if (uncached < 0 || output < 0 || read < 0 || created != 0)
            throw new JsonException("Anthropic usage has unsupported token dimensions.");
        return new ProviderUsage(checked(uncached + read), output, read, null);
    }

    public static string? MapStopReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => "stop",
        "max_tokens" => "length",
        "model_context_window_exceeded" => "length",
        "tool_use" => "tool_calls",
        "refusal" => "content_filter",
        null => null,
        _ => throw new JsonException("Anthropic stop reason is unsupported.")
    };

    public static string? GetOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    public static int? GetOptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number)
            throw new JsonException("Anthropic usage count must be numeric.");
        return value.GetInt32();
    }

    private static object MapMessage(ProviderMessage message)
    {
        var blocks = new List<object>();
        if (message.Role == "tool")
        {
            if (string.IsNullOrWhiteSpace(message.ToolCallId) || message.Content.Count != 1
                || message.Content[0].Kind != ProviderContentKind.Text)
                throw new ArgumentException("Anthropic tool results require a call ID and text.");
            blocks.Add(new { type = "tool_result", tool_use_id = message.ToolCallId,
                content = message.Content[0].Value });
            return new { role = "user", content = blocks };
        }
        if (message.Role is not ("user" or "assistant"))
            throw new ArgumentException("Anthropic supports user, assistant, and tool dialogue roles.");
        foreach (var part in message.Content)
            blocks.Add(part.Kind switch
            {
                ProviderContentKind.Text => new { type = "text", text = part.Value } as object,
                ProviderContentKind.ImageUrl when message.Role == "user"
                    && Uri.TryCreate(part.Value, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps =>
                    new { type = "image", source = new { type = "url", url = part.Value } },
                _ => throw new ArgumentException("Anthropic content part is unsupported.")
            });
        if (message.ToolCalls is { Count: > 0 })
        {
            if (message.Role != "assistant") throw new ArgumentException("Only assistant messages may call tools.");
            foreach (var call in message.ToolCalls)
            {
                using var args = JsonDocument.Parse(call.ArgumentsJson);
                if (args.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Tool call arguments must be a JSON object.");
                blocks.Add(new { type = "tool_use", id = call.Id, name = call.Name,
                    input = args.RootElement.Clone() });
            }
        }
        if (blocks.Count == 0) throw new ArgumentException("Anthropic dialogue messages require content.");
        return new { role = message.Role, content = blocks };
    }

    private static object MapTool(ProviderToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name) || tool.Parameters.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Anthropic tools require a name and input schema.");
        return new { name = tool.Name, description = tool.Description, input_schema = tool.Parameters };
    }

    private static object MapToolChoice(ProviderToolChoice choice,
        IReadOnlyList<ProviderToolDefinition>? tools) => choice.Mode switch
    {
        ProviderToolChoiceMode.Auto => new { type = "auto" } as object,
        ProviderToolChoiceMode.None => new { type = "none" },
        ProviderToolChoiceMode.Required when tools is { Count: > 0 } => new { type = "any" },
        ProviderToolChoiceMode.Named when tools?.Any(tool => tool.Name == choice.Name) == true =>
            new { type = "tool", name = choice.Name },
        _ => throw new ArgumentException("Anthropic tool choice is unsupported.")
    };
}
