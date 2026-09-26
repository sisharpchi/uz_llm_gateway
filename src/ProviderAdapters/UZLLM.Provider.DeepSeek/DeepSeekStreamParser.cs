using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.DeepSeek;

internal sealed class DeepSeekStreamParser(string? headerRequestId)
{
    private string? responseId;
    private string? responseModel;
    private string? finishReason;
    private ProviderUsage? finalUsage;
    private bool sawUsage;

    public string? ProviderRequestId => responseId ?? headerRequestId;

    public IReadOnlyList<ProviderStreamEvent> Parse(DeepSeekSseFrame frame)
    {
        if (frame.EventType == "error")
            return [Error()];
        if (frame.EventType is not null and not "message")
            throw new JsonException("DeepSeek returned an unsupported SSE event.");
        if (sawUsage)
            throw new JsonException("DeepSeek sent data after final usage.");
        using var document = JsonDocument.Parse(frame.Data);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("DeepSeek stream chunk is invalid.");
        if (root.TryGetProperty("error", out _)) return [Error()];
        if (RequiredString(root, "object") != "chat.completion.chunk")
            throw new JsonException("DeepSeek stream envelope is invalid.");
        var id = RequiredString(root, "id");
        var model = RequiredString(root, "model");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(model)
            || responseId is not null && responseId != id
            || responseModel is not null && responseModel != model)
            throw new JsonException("DeepSeek stream identity changed or is missing.");
        responseId = id;
        responseModel = model;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 1)
            throw new JsonException("DeepSeek stream choices are invalid.");

        var events = new List<ProviderStreamEvent>();
        if (choices.GetArrayLength() == 1)
        {
            if (finishReason is not null)
                throw new JsonException("DeepSeek sent a choice after finish.");
            var choice = choices[0];
            if (RequiredCount(choice, "index") != 0
                || !choice.TryGetProperty("delta", out var delta)
                || delta.ValueKind != JsonValueKind.Object)
                throw new JsonException("DeepSeek stream delta is invalid.");
            if (delta.TryGetProperty("role", out var role)
                && role.ValueKind != JsonValueKind.Null
                && (role.ValueKind != JsonValueKind.String || role.GetString() != "assistant"))
                throw new JsonException("DeepSeek stream role is invalid.");
            if (delta.TryGetProperty("tool_calls", out var tools)
                && tools.ValueKind is not JsonValueKind.Null
                && (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() != 0))
                throw new JsonException("DeepSeek stream returned unsupported tool calls.");
            // Chain-of-thought stays private. Its billable tokens are accounted for only in final usage.
            OptionalString(delta, "reasoning_content");
            var content = OptionalString(delta, "content");
            if (content is { Length: > 0 })
                events.Add(new ProviderStreamEvent(ProviderStreamKind.TextDelta,
                    Text: content, ProviderRequestId: ProviderRequestId));
            var finish = OptionalString(choice, "finish_reason");
            if (finish is not null)
            {
                if (finish is not ("stop" or "length" or "content_filter"))
                    throw new JsonException("DeepSeek stream finish is incomplete or unsupported.");
                finishReason = finish;
            }
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind != JsonValueKind.Null)
        {
            if (finishReason is null || usage.ValueKind != JsonValueKind.Object)
                throw new JsonException("DeepSeek final usage arrived before finish or is malformed.");
            finalUsage = DeepSeekWireMapper.ParseUsage(usage);
            sawUsage = true;
        }
        else if (choices.GetArrayLength() == 0)
            throw new JsonException("DeepSeek sent an empty chunk without final usage.");
        return events;
    }

    public IReadOnlyList<ProviderStreamEvent> Complete()
    {
        if (responseId is null || finishReason is null || !sawUsage || finalUsage is null)
            throw new JsonException("DeepSeek stream ended without finish and authoritative usage.");
        var events = new List<ProviderStreamEvent>();
        if (finishReason == "content_filter")
            events.Add(new ProviderStreamEvent(ProviderStreamKind.Refusal,
                Text: "Provider refused the response.", ProviderRequestId: ProviderRequestId));
        events.Add(new ProviderStreamEvent(ProviderStreamKind.Finish,
            FinishReason: finishReason, ProviderRequestId: ProviderRequestId));
        events.Add(new ProviderStreamEvent(ProviderStreamKind.Usage,
            Usage: finalUsage, ProviderRequestId: ProviderRequestId));
        return events;
    }

    private ProviderStreamEvent Error() => new(ProviderStreamKind.Error,
        Error: DeepSeekErrorClassifier.Unknown(ProviderRequestId),
        ProviderRequestId: ProviderRequestId);

    private static string? RequiredString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()
            : throw new JsonException($"DeepSeek {name} is invalid.");
    }

    private static int RequiredCount(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number ? value.GetInt32()
                : throw new JsonException($"DeepSeek {name} is required.");
}
