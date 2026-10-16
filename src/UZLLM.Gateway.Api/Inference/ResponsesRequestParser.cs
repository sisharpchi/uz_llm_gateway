using System.Text.Json;
using System.Text.Json.Nodes;

namespace UZLLM.Gateway.Api.Inference;

/// <summary>Translates only the documented stateless text Responses subset into provider-neutral chat.</summary>
public static class ResponsesRequestParser
{
    public static ParsedChatRequest Parse(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = document.RootElement;
            CheckFields(root, "model", "input", "instructions", "max_output_tokens", "temperature", "top_p",
                "stream", "store", "uzllm");
            foreach (var field in new[] { "stream", "store" })
                if (root.TryGetProperty(field, out var value))
                {
                    if (value.ValueKind == JsonValueKind.True)
                        throw new GatewayRequestException($"{field}=true is not supported.", "unsupported_parameter");
                    if (value.ValueKind != JsonValueKind.False)
                        throw new GatewayRequestException($"{field} must be false or omitted.");
                }
            var converted = new JsonObject();
            foreach (var field in new[] { "model", "temperature", "top_p", "uzllm" })
                if (root.TryGetProperty(field, out var value))
                    converted[field] = JsonNode.Parse(value.GetRawText());
            if (root.TryGetProperty("uzllm", out var extension))
                CheckFields(extension, "provider_key_id", "allow_managed_fallback", "routing", "fallback_models");
            if (root.TryGetProperty("max_output_tokens", out var maximum))
            {
                if (maximum.ValueKind != JsonValueKind.Number || !maximum.TryGetInt32(out var count) || count < 16)
                    throw new GatewayRequestException("max_output_tokens must be an integer of at least 16.");
                converted["max_completion_tokens"] = count;
            }
            var messages = new JsonArray();
            if (root.TryGetProperty("instructions", out var instructions))
                messages.Add(Message("system", Text(instructions)));
            if (!root.TryGetProperty("input", out var input))
                throw new GatewayRequestException("input is required.");
            if (input.ValueKind == JsonValueKind.String)
                messages.Add(Message("user", Text(input)));
            else if (input.ValueKind == JsonValueKind.Array && input.GetArrayLength() is >= 1 and <= 256)
                foreach (var item in input.EnumerateArray())
                {
                    CheckFields(item, "type", "role", "content");
                    if (item.TryGetProperty("type", out var type) && type.GetString() != "message")
                        throw new GatewayRequestException("Only message input items are supported.", "unsupported_parameter");
                    var role = item.GetProperty("role").GetString();
                    if (role is not ("system" or "developer" or "user" or "assistant"))
                        throw new GatewayRequestException("Unsupported input message role.", "unsupported_parameter");
                    var content = item.GetProperty("content");
                    if (content.ValueKind == JsonValueKind.String) messages.Add(Message(role, Text(content)));
                    else if (content.ValueKind == JsonValueKind.Array && content.GetArrayLength() is >= 1 and <= 64)
                    {
                        var parts = new JsonArray();
                        foreach (var part in content.EnumerateArray())
                        {
                            CheckFields(part, "type", "text");
                            if (part.GetProperty("type").GetString() != "input_text")
                                throw new GatewayRequestException("Only input_text content is supported.", "unsupported_parameter");
                            parts.Add(new JsonObject { ["type"] = "text", ["text"] = Text(part.GetProperty("text")) });
                        }
                        messages.Add(new JsonObject { ["role"] = role, ["content"] = parts });
                    }
                    else throw new GatewayRequestException("Message content must be text or 1–64 input_text parts.");
                }
            else throw new GatewayRequestException("input must be text or 1–256 message items.");
            if (messages.Count > 256)
                throw new GatewayRequestException("instructions and input together may contain at most 256 messages.");
            converted["messages"] = messages;
            return ChatRequestParser.Parse(JsonSerializer.SerializeToUtf8Bytes(converted)) with
            { Operation = "responses", Instructions = root.TryGetProperty("instructions", out var guidance) ? guidance.GetString() : null };
        }
        catch (JsonException) { throw new GatewayRequestException("Request body must be valid JSON."); }
        catch (InvalidOperationException) { throw new GatewayRequestException("Request fields have invalid types."); }
        catch (KeyNotFoundException) { throw new GatewayRequestException("A required request field is missing."); }
    }

    private static JsonObject Message(string role, string text) => new() { ["role"] = role, ["content"] = text };

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 and <= 1_048_576 } text && !string.IsNullOrWhiteSpace(text)
        ? text : throw new GatewayRequestException("Text must be a non-empty string of at most 1048576 characters.");

    private static void CheckFields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new GatewayRequestException("Expected a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new GatewayRequestException("Duplicate JSON properties are not supported.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new GatewayRequestException($"Unsupported parameter: {property.Name}.", "unsupported_parameter");
        }
    }
}
