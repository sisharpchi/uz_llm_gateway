using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.DeepSeek;

internal static class DeepSeekWireMapper
{
    public static bool Supports(ProviderChatRequest request, bool stream) =>
        request.Messages is { Count: > 0 }
        && request.Temperature is not < 0 and not > 2
        // DeepSeek ignores top_p in non-thinking mode. Never silently accept a sampling policy it cannot honor.
        && request.TopP is null or 1m
        && request.Stop is not { Count: > 4 }
        && request.Stop?.Any(string.IsNullOrEmpty) != true
        && request.Tools is not { Count: > 0 }
        && request.ToolChoice is null or { Mode: ProviderToolChoiceMode.None }
        && request.Messages.Any(message => message.Role is "user" or "assistant")
        && request.Messages.SkipWhile(message => message.Role is "system" or "developer")
            .All(message => message.Role is "user" or "assistant")
        && request.Messages.All(message => message.Content is { Count: > 0 }
            && message.Content.All(part => part.Kind == ProviderContentKind.Text
                && !string.IsNullOrEmpty(part.Value))
            && message.ToolCalls is not { Count: > 0 } && message.ToolCallId is null)
        && request.ResponseFormat is null or { Kind: ProviderResponseFormatKind.Text or
            ProviderResponseFormatKind.JsonObject };

    public static byte[] BuildRequest(ProviderChatRequest request, string model, int outputLimit, bool stream)
    {
        if (!Supports(request, stream) || string.IsNullOrWhiteSpace(model) || outputLimit <= 0)
            throw new ArgumentException("DeepSeek request contains unsupported features.");
        var messages = request.Messages.Select(message => new
        {
            role = message.Role == "developer" ? "system" : message.Role,
            content = string.Concat(message.Content.Select(part => part.Value))
        }).ToArray();
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["max_tokens"] = outputLimit,
            // P1 non-stream uses non-thinking mode so sampling parameters are not silently ignored.
            ["thinking"] = new { type = "disabled" }
        };
        if (stream) body["stream_options"] = new { include_usage = true };
        if (request.Temperature is not null) body["temperature"] = request.Temperature;
        if (request.Stop is { Count: > 0 }) body["stop"] = request.Stop;
        if (request.ResponseFormat is { Kind: ProviderResponseFormatKind.JsonObject })
            body["response_format"] = new { type = "json_object" };
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static ProviderCompletion ParseCompletion(JsonElement root, string upstreamModelCode)
    {
        if (root.ValueKind != JsonValueKind.Object
            || RequiredString(root, "object") != "chat.completion")
            throw new JsonException("DeepSeek completion envelope is invalid.");
        var id = RequiredString(root, "id")
            ?? throw new JsonException("DeepSeek response ID is required.");
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
            throw new JsonException("DeepSeek must return exactly one choice.");
        var choice = choices[0];
        if (RequiredCount(choice, "index") != 0)
            throw new JsonException("DeepSeek returned a nonzero choice index.");
        var rawFinish = RequiredString(choice, "finish_reason")
            ?? throw new JsonException("DeepSeek finish reason is required.");
        var finish = rawFinish switch
        {
            "stop" or "length" or "content_filter" => rawFinish,
            _ => throw new JsonException("DeepSeek returned an incomplete or unsupported finish reason.")
        };
        if (!choice.TryGetProperty("message", out var message)
            || RequiredString(message, "role") != "assistant"
            || message.TryGetProperty("tool_calls", out var tools)
                && tools.ValueKind is not JsonValueKind.Null
                && (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() != 0))
            throw new JsonException("DeepSeek returned unsupported message content.");
        var content = message.TryGetProperty("content", out var text)
            && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        if (content is null && finish != "content_filter")
            throw new JsonException("DeepSeek returned no visible completion content.");
        var parts = content is null ? Array.Empty<ProviderContentPart>()
            : [new ProviderContentPart(ProviderContentKind.Text, content)];
        var usage = root.TryGetProperty("usage", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object ? ParseUsage(usageElement) : null;
        return new ProviderCompletion(id, upstreamModelCode,
            new ProviderMessage("assistant", parts), finish, usage, id,
            finish == "content_filter" ? "Provider refused the response." : null);
    }

    public static ProviderUsage ParseUsage(JsonElement usage)
    {
        var input = RequiredCount(usage, "prompt_tokens");
        var output = RequiredCount(usage, "completion_tokens");
        var hit = RequiredCount(usage, "prompt_cache_hit_tokens");
        var miss = RequiredCount(usage, "prompt_cache_miss_tokens");
        var total = RequiredCount(usage, "total_tokens");
        if (input < 0 || output < 0 || hit < 0 || miss < 0 || total < 0
            || (long)hit + miss != input || (long)input + output != total)
            throw new JsonException("DeepSeek cache and total token counts are inconsistent.");
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails))
        {
            if (promptDetails.ValueKind != JsonValueKind.Object)
                throw new JsonException("DeepSeek cached token details are invalid.");
            if (promptDetails.TryGetProperty("cached_tokens", out var cached)
                && (cached.ValueKind != JsonValueKind.Number || cached.GetInt32() != hit))
                throw new JsonException("DeepSeek cached token details disagree with cache hits.");
        }
        int? reasoning = null;
        if (usage.TryGetProperty("completion_tokens_details", out var completionDetails))
        {
            if (completionDetails.ValueKind != JsonValueKind.Object)
                throw new JsonException("DeepSeek completion token details are invalid.");
            if (completionDetails.TryGetProperty("reasoning_tokens", out var reasoningValue)
                && reasoningValue.ValueKind != JsonValueKind.Null)
            {
                if (reasoningValue.ValueKind != JsonValueKind.Number)
                    throw new JsonException("DeepSeek reasoning token count is invalid.");
                reasoning = reasoningValue.GetInt32();
            }
        }
        if (reasoning is < 0 || reasoning > output)
            throw new JsonException("DeepSeek reasoning tokens exceed billable output.");
        return new ProviderUsage(input, output, hit, reasoning);
    }

    private static string? RequiredString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int RequiredCount(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number ? value.GetInt32()
                : throw new JsonException($"DeepSeek {name} is required.");
}
