using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.OpenAI;

internal static class OpenAiWireMapper
{
    private static readonly HashSet<string> Roles = ["system", "developer", "user", "assistant", "tool"];

    public static byte[] BuildRequest(ProviderChatRequest request, string model, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(model) || request.Messages is null || request.Messages.Count == 0)
            throw new ArgumentException("An upstream model and messages are required.");
        if (request.Temperature is < 0 or > 2 || request.TopP is <= 0 or > 1
            || request.MaxOutputTokens is <= 0 || request.Stop is { Count: > 4 }
            || request.Stop?.Any(string.IsNullOrEmpty) == true)
            throw new ArgumentException("A sampling or output parameter is unsupported.");

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = request.Messages.Select(MapMessage).ToArray(),
            ["stream"] = stream
        };
        if (stream) body["stream_options"] = new { include_usage = true };
        if (request.Temperature is not null) body["temperature"] = request.Temperature;
        if (request.TopP is not null) body["top_p"] = request.TopP;
        if (request.MaxOutputTokens is not null) body["max_completion_tokens"] = request.MaxOutputTokens;
        if (request.Stop is { Count: > 0 }) body["stop"] = request.Stop;
        if (request.Tools is { Count: > 0 }) body["tools"] = request.Tools.Select(MapTool).ToArray();
        if (request.ToolChoice is not null) body["tool_choice"] = MapToolChoice(request.ToolChoice, request.Tools);
        if (request.ResponseFormat is not null) body["response_format"] = MapResponseFormat(request.ResponseFormat);
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static ProviderCompletion ParseCompletion(JsonElement root, string? requestId)
    {
        var choice = root.GetProperty("choices").EnumerateArray().First();
        if (choice.GetProperty("index").GetInt32() != 0)
            throw new JsonException("OpenAI response did not contain choice zero.");
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String
            ? new[] { new ProviderContentPart(ProviderContentKind.Text, contentElement.GetString()!) }
            : [];
        var calls = message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            ? toolCalls.EnumerateArray().Select(MapToolCall).ToArray() : [];
        var refusal = GetOptionalString(message, "refusal");
        return new ProviderCompletion(root.GetProperty("id").GetString()!,
            root.GetProperty("model").GetString()!,
            new ProviderMessage("assistant", content, calls),
            GetOptionalString(choice, "finish_reason"), ParseUsage(root), requestId, refusal);
    }

    public static ProviderUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
            return null;
        var input = usage.GetProperty("prompt_tokens").GetInt32();
        var output = usage.GetProperty("completion_tokens").GetInt32();
        var cached = usage.TryGetProperty("prompt_tokens_details", out var promptDetails)
            && promptDetails.ValueKind == JsonValueKind.Object
            && promptDetails.TryGetProperty("cached_tokens", out var cachedElement)
            && cachedElement.ValueKind == JsonValueKind.Number ? cachedElement.GetInt32() : 0;
        int? reasoning = usage.TryGetProperty("completion_tokens_details", out var completionDetails)
            && completionDetails.ValueKind == JsonValueKind.Object
            && completionDetails.TryGetProperty("reasoning_tokens", out var reasoningElement)
            && reasoningElement.ValueKind == JsonValueKind.Number ? reasoningElement.GetInt32() : null;
        if (input < 0 || output < 0 || cached < 0 || cached > input || reasoning is < 0 || reasoning > output)
            throw new JsonException("OpenAI usage counts are inconsistent.");
        return new ProviderUsage(input, output, cached, reasoning);
    }

    public static string? GetOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static object MapMessage(ProviderMessage message)
    {
        if (message is null || !Roles.Contains(message.Role) || message.Content is null)
            throw new ArgumentException("Unsupported message role or content.");
        var body = new Dictionary<string, object?> { ["role"] = message.Role };
        if (message.Role == "tool")
        {
            if (string.IsNullOrWhiteSpace(message.ToolCallId) || message.Content.Count != 1
                || message.Content[0].Kind != ProviderContentKind.Text)
                throw new ArgumentException("Tool responses require a call ID and text content.");
            body["tool_call_id"] = message.ToolCallId;
            body["content"] = message.Content[0].Value;
            return body;
        }
        if (message.Content.Count == 1 && message.Content[0].Kind == ProviderContentKind.Text)
            body["content"] = message.Content[0].Value;
        else if (message.Content.Count != 0)
            body["content"] = message.Content.Select(part => MapPart(part, message.Role)).ToArray();
        else if (message.Role != "assistant" || message.ToolCalls is not { Count: > 0 })
            throw new ArgumentException("Messages require content unless an assistant calls a tool.");

        if (message.ToolCalls is { Count: > 0 })
        {
            if (message.Role != "assistant") throw new ArgumentException("Only assistant messages may call tools.");
            body["tool_calls"] = message.ToolCalls.Select(call => new
            {
                id = call.Id, type = "function", function = new { name = call.Name, arguments = call.ArgumentsJson }
            }).ToArray();
        }
        return body;
    }

    private static object MapPart(ProviderContentPart part, string role)
    {
        if (part is null || string.IsNullOrEmpty(part.Value))
            throw new ArgumentException("Content parts require values.");
        return part.Kind switch
        {
            ProviderContentKind.Text => new { type = "text", text = part.Value } as object,
            ProviderContentKind.ImageUrl when role == "user" => new
                { type = "image_url", image_url = new { url = part.Value } },
            _ => throw new ArgumentException("The content part is unsupported for this role.")
        };
    }

    private static object MapTool(ProviderToolDefinition tool)
    {
        if (tool is null || string.IsNullOrWhiteSpace(tool.Name)
            || tool.Parameters.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool definitions require a name and JSON-schema object.");
        return new { type = "function", function = new
            { name = tool.Name, description = tool.Description, parameters = tool.Parameters } };
    }

    private static object MapToolChoice(ProviderToolChoice choice,
        IReadOnlyList<ProviderToolDefinition>? tools)
    {
        if (choice.Mode == ProviderToolChoiceMode.Named)
        {
            if (string.IsNullOrWhiteSpace(choice.Name) || tools?.Any(tool => tool.Name == choice.Name) != true)
                throw new ArgumentException("Named tool choice must match a supplied tool.");
            return new { type = "function", function = new { name = choice.Name } };
        }
        if (choice.Name is not null || tools is not { Count: > 0 }
            && choice.Mode is ProviderToolChoiceMode.Required)
            throw new ArgumentException("Tool choice is inconsistent with supplied tools.");
        return choice.Mode switch
        {
            ProviderToolChoiceMode.Auto => "auto",
            ProviderToolChoiceMode.None => "none",
            ProviderToolChoiceMode.Required => "required",
            _ => throw new ArgumentException("Unsupported tool choice.")
        };
    }

    private static object MapResponseFormat(ProviderResponseFormat format) => format.Kind switch
    {
        ProviderResponseFormatKind.Text => new { type = "text" } as object,
        ProviderResponseFormatKind.JsonObject => new { type = "json_object" },
        ProviderResponseFormatKind.JsonSchema when !string.IsNullOrWhiteSpace(format.SchemaName)
            && format.Schema is { ValueKind: JsonValueKind.Object } =>
            new { type = "json_schema", json_schema = new
                { name = format.SchemaName, schema = format.Schema, strict = format.Strict } },
        _ => throw new ArgumentException("Unsupported response format.")
    };

    private static ProviderToolCall MapToolCall(JsonElement call)
    {
        var function = call.GetProperty("function");
        return new ProviderToolCall(call.GetProperty("id").GetString()!,
            function.GetProperty("name").GetString()!,
            function.GetProperty("arguments").GetString()!);
    }
}
