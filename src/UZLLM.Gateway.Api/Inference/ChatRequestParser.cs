using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public sealed record ParsedChatRequest(string Model, bool Stream, ProviderChatRequest ProviderRequest,
    IReadOnlyCollection<string> RequiredCapabilities, int EstimatedInputTokens);

public sealed class GatewayRequestException(string message, string code = "invalid_request") : Exception(message)
{
    public string Code { get; } = code;
}

public static class ChatRequestParser
{
    private static readonly HashSet<string> AllowedFields =
    ["model", "messages", "temperature", "top_p", "max_tokens", "max_completion_tokens",
        "stream", "stop", "tools", "tool_choice", "response_format", "stream_options"];

    public static ParsedChatRequest Parse(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = document.RootElement;
            RequireObject(root, "Request body");
            foreach (var property in root.EnumerateObject())
                if (!AllowedFields.Contains(property.Name))
                    throw new GatewayRequestException($"Unsupported parameter: {property.Name}.", "unsupported_parameter");
            var model = RequiredString(root, "model", 200);
            var stream = OptionalBoolean(root, "stream") ?? false;
            if (root.TryGetProperty("stream_options", out var options))
            {
                RequireObject(options, "stream_options");
                foreach (var property in options.EnumerateObject())
                    if (property.Name != "include_usage")
                        throw new GatewayRequestException("Unsupported stream_options parameter.", "unsupported_parameter");
                _ = OptionalBoolean(options, "include_usage");
            }
            if (!root.TryGetProperty("messages", out var messagesElement)
                || messagesElement.ValueKind != JsonValueKind.Array
                || messagesElement.GetArrayLength() is < 1 or > 256)
                throw new GatewayRequestException("messages must contain 1–256 entries.");
            var messages = messagesElement.EnumerateArray().Select(ParseMessage).ToArray();
            var maximum = OptionalInt(root, "max_completion_tokens");
            var legacyMaximum = OptionalInt(root, "max_tokens");
            if (maximum is not null && legacyMaximum is not null)
                throw new GatewayRequestException("Use only one output-token limit.");
            maximum ??= legacyMaximum;
            var tools = ParseTools(root);
            var choice = ParseToolChoice(root);
            var format = ParseResponseFormat(root);
            var stop = ParseStop(root);
            var request = new ProviderChatRequest(messages, OptionalDecimal(root, "temperature"),
                OptionalDecimal(root, "top_p"), maximum, stop, tools, choice, format);
            if (request.Temperature is < 0 or > 2 || request.TopP is <= 0 or > 1
                || request.MaxOutputTokens is <= 0 || request.Stop?.Any(string.IsNullOrEmpty) == true)
                throw new GatewayRequestException("Sampling or output parameters are invalid.");
            if (choice is { Mode: ProviderToolChoiceMode.Named }
                && tools?.Any(tool => tool.Name == choice.Name) != true
                || choice is { Mode: ProviderToolChoiceMode.Required } && tools is not { Count: > 0 })
                throw new GatewayRequestException("tool_choice requires a matching function tool.");
            var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Text" };
            if (messages.SelectMany(message => message.Content)
                .Any(part => part.Kind == ProviderContentKind.ImageUrl)) capabilities.Add("Vision");
            if (tools is { Count: > 0 } || messages.Any(message => message.ToolCalls is { Count: > 0 }))
                capabilities.Add("Tools");
            if (format?.Kind is ProviderResponseFormatKind.JsonObject or ProviderResponseFormatKind.JsonSchema)
                capabilities.Add("StructuredOutput");
            var textLength = messages.SelectMany(message => message.Content)
                .Where(part => part.Kind == ProviderContentKind.Text)
                .Sum(part => (long)part.Value.Length);
            var imageCount = messages.SelectMany(message => message.Content)
                .Count(part => part.Kind == ProviderContentKind.ImageUrl);
            var estimated = checked((int)Math.Min(int.MaxValue, (textLength + 1) / 2 + imageCount * 1_000L));
            return new ParsedChatRequest(model, stream, request, capabilities, estimated);
        }
        catch (JsonException)
        { throw new GatewayRequestException("Request body must be valid JSON."); }
        catch (InvalidOperationException)
        { throw new GatewayRequestException("Request fields have invalid types."); }
        catch (KeyNotFoundException)
        { throw new GatewayRequestException("A required request field is missing."); }
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        RequireObject(element, "message");
        var role = RequiredString(element, "role", 30);
        if (role is not ("system" or "developer" or "user" or "assistant" or "tool"))
            throw new GatewayRequestException("Unsupported message role.");
        var content = new List<ProviderContentPart>();
        if (element.TryGetProperty("content", out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
                content.Add(new ProviderContentPart(ProviderContentKind.Text, value.GetString()!));
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in value.EnumerateArray())
                {
                    RequireObject(part, "content part");
                    var kind = RequiredString(part, "type", 30);
                    if (kind == "text")
                        content.Add(new ProviderContentPart(ProviderContentKind.Text, RequiredString(part, "text", 1_048_576)));
                    else if (kind == "image_url" && role == "user")
                    {
                        var image = part.GetProperty("image_url");
                        RequireObject(image, "image_url");
                        var url = RequiredString(image, "url", 8192);
                        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                            throw new GatewayRequestException("Only HTTPS image URLs are supported.");
                        content.Add(new ProviderContentPart(ProviderContentKind.ImageUrl, url));
                    }
                    else throw new GatewayRequestException("Unsupported content part.", "unsupported_parameter");
                }
            }
            else if (value.ValueKind != JsonValueKind.Null)
                throw new GatewayRequestException("Invalid message content.");
        }
        IReadOnlyList<ProviderToolCall>? toolCalls = null;
        if (element.TryGetProperty("tool_calls", out var calls))
        {
            if (role != "assistant" || calls.ValueKind != JsonValueKind.Array)
                throw new GatewayRequestException("Only assistant messages may contain tool_calls.");
            toolCalls = calls.EnumerateArray().Select(call =>
            {
                RequireObject(call, "tool call");
                if (RequiredString(call, "type", 30) != "function")
                    throw new GatewayRequestException("Only function tool calls are supported.");
                var function = call.GetProperty("function");
                RequireObject(function, "function");
                return new ProviderToolCall(RequiredString(call, "id", 200),
                    RequiredString(function, "name", 100), RequiredString(function, "arguments", 100_000));
            }).ToArray();
        }
        var toolCallId = element.TryGetProperty("tool_call_id", out _)
            ? RequiredString(element, "tool_call_id", 200) : null;
        if (role == "tool" && (toolCallId is null || content.Count != 1
            || content[0].Kind != ProviderContentKind.Text))
            throw new GatewayRequestException("Tool messages need tool_call_id and text content.");
        if (role != "tool" && toolCallId is not null)
            throw new GatewayRequestException("tool_call_id is only valid on tool messages.");
        if (content.Count == 0 && role != "assistant" || content.Count == 0 && toolCalls is not { Count: > 0 })
            throw new GatewayRequestException("Message content is required.");
        return new ProviderMessage(role, content, toolCalls, toolCallId);
    }

    private static IReadOnlyList<ProviderToolDefinition>? ParseTools(JsonElement root)
    {
        if (!root.TryGetProperty("tools", out var tools)) return null;
        if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() > 64)
            throw new GatewayRequestException("tools must be an array of at most 64 functions.");
        return tools.EnumerateArray().Select(tool =>
        {
            RequireObject(tool, "tool");
            if (RequiredString(tool, "type", 30) != "function")
                throw new GatewayRequestException("Only function tools are supported.");
            var function = tool.GetProperty("function");
            RequireObject(function, "function");
            var schema = function.GetProperty("parameters");
            RequireObject(schema, "function parameters");
            return new ProviderToolDefinition(RequiredString(function, "name", 100),
                OptionalString(function, "description", 1024), schema.Clone());
        }).ToArray();
    }

    private static ProviderToolChoice? ParseToolChoice(JsonElement root)
    {
        if (!root.TryGetProperty("tool_choice", out var value)) return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() switch
            {
                "auto" => new ProviderToolChoice(ProviderToolChoiceMode.Auto),
                "none" => new ProviderToolChoice(ProviderToolChoiceMode.None),
                "required" => new ProviderToolChoice(ProviderToolChoiceMode.Required),
                _ => throw new GatewayRequestException("Unsupported tool_choice.")
            };
        RequireObject(value, "tool_choice");
        if (RequiredString(value, "type", 30) != "function")
            throw new GatewayRequestException("Unsupported tool_choice.");
        var function = value.GetProperty("function");
        RequireObject(function, "tool_choice.function");
        return new ProviderToolChoice(ProviderToolChoiceMode.Named,
            RequiredString(function, "name", 100));
    }

    private static ProviderResponseFormat? ParseResponseFormat(JsonElement root)
    {
        if (!root.TryGetProperty("response_format", out var value)) return null;
        RequireObject(value, "response_format");
        return RequiredString(value, "type", 30) switch
        {
            "text" => new ProviderResponseFormat(ProviderResponseFormatKind.Text),
            "json_object" => new ProviderResponseFormat(ProviderResponseFormatKind.JsonObject),
            "json_schema" => ParseJsonSchema(value),
            _ => throw new GatewayRequestException("Unsupported response_format.", "unsupported_parameter")
        };
    }

    private static ProviderResponseFormat ParseJsonSchema(JsonElement value)
    {
        var definition = value.GetProperty("json_schema");
        RequireObject(definition, "json_schema");
        var schema = definition.GetProperty("schema");
        RequireObject(schema, "json_schema.schema");
        return new ProviderResponseFormat(ProviderResponseFormatKind.JsonSchema,
            RequiredString(definition, "name", 100), schema.Clone(),
            OptionalBoolean(definition, "strict") ?? true);
    }

    private static IReadOnlyList<string>? ParseStop(JsonElement root)
    {
        if (!root.TryGetProperty("stop", out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return [value.GetString()!];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 4)
            throw new GatewayRequestException("stop must be a string or up to four strings.");
        return value.EnumerateArray().Select(part => part.GetString()
            ?? throw new GatewayRequestException("stop entries must be strings.")).ToArray();
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new GatewayRequestException($"{name} must be an object.");
    }

    private static string RequiredString(JsonElement parent, string name, int maximumLength)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text || string.IsNullOrWhiteSpace(text)
            || text.Length > maximumLength)
            throw new GatewayRequestException($"{name} must be a non-empty string of at most {maximumLength} characters.");
        return text;
    }

    private static string? OptionalString(JsonElement parent, string name, int maximumLength) =>
        parent.TryGetProperty(name, out _) ? RequiredString(parent, name, maximumLength) : null;

    private static bool? OptionalBoolean(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new GatewayRequestException($"{name} must be a boolean.")
        };

    private static int? OptionalInt(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new GatewayRequestException($"{name} must be an integer.");

    private static decimal? OptionalDecimal(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number : throw new GatewayRequestException($"{name} must be a decimal number.");
}
