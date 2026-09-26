using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Google;

internal static class GoogleWireMapper
{
    public static bool Supports(ProviderChatRequest request, bool stream) =>
        request.Messages is { Count: > 0 }
        && request.Temperature is not < 0 and not > 1
        && request.TopP is not <= 0 and not > 1
        && request.Stop is not { Count: > 5 }
        && request.Tools is not { Count: > 0 }
        && request.ToolChoice is null or { Mode: ProviderToolChoiceMode.None }
        && request.Messages.Any(message => message.Role is "user" or "assistant")
        && request.Messages.SkipWhile(message => message.Role is "system" or "developer")
            .All(message => message.Role is "user" or "assistant")
        && request.Messages.All(message => message.Content.Count > 0
            && message.Content.All(part => part.Kind == ProviderContentKind.Text)
            && message.ToolCalls is not { Count: > 0 } && message.ToolCallId is null)
        && request.ResponseFormat is null or { Kind: ProviderResponseFormatKind.Text or
            ProviderResponseFormatKind.JsonObject } or
            { Kind: ProviderResponseFormatKind.JsonSchema, Schema: { ValueKind: JsonValueKind.Object } };

    public static byte[] BuildRequest(ProviderChatRequest request, int outputLimit)
    {
        if (!Supports(request, false) || outputLimit <= 0)
            throw new ArgumentException("Google request contains unsupported features.");
        var systemParts = new List<object>();
        var contents = new List<object>();
        var dialogueStarted = false;
        foreach (var message in request.Messages)
        {
            if (message.Role is "system" or "developer")
            {
                if (dialogueStarted) throw new ArgumentException("System instruction must precede dialogue.");
                systemParts.AddRange(message.Content.Select(part => new { text = part.Value }));
                continue;
            }
            dialogueStarted = true;
            var role = message.Role switch
            {
                "user" => "user", "assistant" => "model",
                _ => throw new ArgumentException("Google supports only text user/assistant dialogue.")
            };
            contents.Add(new { role, parts = message.Content.Select(part => new { text = part.Value }).ToArray() });
        }
        if (contents.Count == 0) throw new ArgumentException("Google requires a dialogue turn.");
        var generation = new Dictionary<string, object?> { ["maxOutputTokens"] = outputLimit };
        if (request.Temperature is not null) generation["temperature"] = request.Temperature;
        if (request.TopP is not null) generation["topP"] = request.TopP;
        if (request.Stop is { Count: > 0 }) generation["stopSequences"] = request.Stop;
        if (request.ResponseFormat is { Kind: ProviderResponseFormatKind.JsonObject or
            ProviderResponseFormatKind.JsonSchema } format)
        {
            generation["responseMimeType"] = "application/json";
            if (format.Kind == ProviderResponseFormatKind.JsonSchema)
                generation["responseJsonSchema"] = format.Schema;
        }
        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["generationConfig"] = generation
        };
        if (systemParts.Count != 0) body["systemInstruction"] = new { parts = systemParts };
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static ProviderCompletion ParseCompletion(JsonElement root, string? headerRequestId,
        string upstreamModelCode)
    {
        var responseId = RequiredString(root, "responseId");
        var providerRequestId = responseId ?? headerRequestId;
        var usage = root.TryGetProperty("usageMetadata", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object ? ParseUsage(usageElement) : null;
        var hasCandidates = root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array;
        if (!hasCandidates || candidates.GetArrayLength() == 0)
        {
            if (!root.TryGetProperty("promptFeedback", out var feedback)
                || RequiredString(feedback, "blockReason") is null)
                throw new JsonException("Google returned no candidate or block reason.");
            return new ProviderCompletion(responseId ?? throw new JsonException("Response ID is required."),
                upstreamModelCode, new ProviderMessage("assistant", []), "content_filter", usage,
                providerRequestId, "Provider blocked the prompt.");
        }
        if (candidates.GetArrayLength() != 1)
            throw new JsonException("Google returned multiple candidates.");
        var candidate = candidates[0];
        var finish = RequiredString(candidate, "finishReason")
            ?? throw new JsonException("Google finish reason is required.");
        var (finishReason, refusal) = MapFinish(finish);
        var content = new List<ProviderContentPart>();
        if (candidate.TryGetProperty("content", out var body) && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    continue;
                if (!part.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new JsonException("Google returned an unsupported content part.");
                content.Add(new ProviderContentPart(ProviderContentKind.Text, text.GetString()!));
            }
        }
        if (content.Count == 0 && refusal is null && finishReason != "length")
            throw new JsonException("Google returned no visible content.");
        return new ProviderCompletion(responseId ?? throw new JsonException("Response ID is required."),
            upstreamModelCode, new ProviderMessage("assistant", content), finishReason, usage,
            providerRequestId, refusal);
    }

    public static ProviderUsage ParseUsage(JsonElement usage)
    {
        var input = RequiredCount(usage, "promptTokenCount");
        var candidates = OptionalCount(usage, "candidatesTokenCount");
        var thoughts = OptionalCount(usage, "thoughtsTokenCount");
        var cached = OptionalCount(usage, "cachedContentTokenCount");
        var toolUse = OptionalCount(usage, "toolUsePromptTokenCount");
        var total = usage.TryGetProperty("totalTokenCount", out var totalValue)
            && totalValue.ValueKind != JsonValueKind.Null
            ? RequiredCount(usage, "totalTokenCount") : (int?)null;
        if (input < 0 || candidates < 0 || thoughts < 0 || cached < 0
            || cached > input || toolUse != 0 || total is not null
                && total != (long)input + candidates + thoughts)
            throw new JsonException("Google usage has unsupported token dimensions.");
        var output = checked(candidates + thoughts);
        return new ProviderUsage(input, output, cached, thoughts == 0 ? null : thoughts);
    }

    internal static (string Finish, string? Refusal) MapFinish(string reason) => reason switch
    {
        "STOP" => ("stop", null),
        "MAX_TOKENS" => ("length", null),
        "SAFETY" or "RECITATION" or "LANGUAGE" or "BLOCKLIST" or
            "PROHIBITED_CONTENT" or "SPII" or "IMAGE_SAFETY" =>
            ("content_filter", "Provider refused the response."),
        _ => throw new JsonException("Google finish reason is unsupported.")
    };

    private static string? RequiredString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int RequiredCount(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : throw new JsonException($"Google {name} is required.");

    private static int OptionalCount(JsonElement usage, string name) =>
        !usage.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? 0
            : value.ValueKind == JsonValueKind.Number ? value.GetInt32()
                : throw new JsonException($"Google {name} must be numeric.");
}
