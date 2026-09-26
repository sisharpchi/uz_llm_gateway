using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Google;

internal sealed class GoogleStreamParser(string? headerRequestId)
{
    private string? responseId;
    private ProviderUsage? lastUsage;
    private string? finishReason;
    private string? refusal;
    private bool finalUsageObserved;

    public string? ProviderRequestId => responseId ?? headerRequestId;

    public IReadOnlyList<ProviderStreamEvent> Parse(GoogleSseFrame frame)
    {
        if (frame.EventType is not null and not "message" and not "error")
            throw new JsonException("Google stream returned an unsupported SSE event.");
        using var document = JsonDocument.Parse(frame.Data);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Google stream chunk must be an object.");

        if (root.TryGetProperty("responseId", out var id))
        {
            if (id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                throw new JsonException("Google response ID is invalid.");
            var current = id.GetString()!;
            if (responseId is not null && responseId != current)
                throw new JsonException("Google response ID changed during streaming.");
            responseId = current;
        }

        if (frame.EventType == "error" || root.TryGetProperty("error", out _))
            return [new ProviderStreamEvent(ProviderStreamKind.Error,
                Error: GoogleErrorClassifier.Unknown(ProviderRequestId),
                ProviderRequestId: ProviderRequestId)];

        var hasUsage = root.TryGetProperty("usageMetadata", out var usage);
        if (hasUsage)
        {
            if (usage.ValueKind != JsonValueKind.Object)
                throw new JsonException("Google usage metadata is invalid.");
            var current = GoogleWireMapper.ParseUsage(usage);
            if (lastUsage is not null && (current.InputTokens < lastUsage.InputTokens
                || current.OutputTokens < lastUsage.OutputTokens
                || current.CachedInputTokens < lastUsage.CachedInputTokens
                || (current.ReasoningTokens ?? 0) < (lastUsage.ReasoningTokens ?? 0)))
                throw new JsonException("Google cumulative usage regressed.");
            lastUsage = current;
            if (finishReason is not null) finalUsageObserved = true;
        }

        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.ValueKind == JsonValueKind.Object
            && feedback.TryGetProperty("blockReason", out var blockReason))
        {
            if (finishReason is not null || blockReason.ValueKind != JsonValueKind.String
                || blockReason.GetString() is null or "BLOCK_REASON_UNSPECIFIED")
                throw new JsonException("Google prompt feedback is inconsistent.");
            finishReason = "content_filter";
            refusal = "Provider blocked the prompt.";
            if (hasUsage) finalUsageObserved = true;
        }

        if (!root.TryGetProperty("candidates", out var candidates)) return [];
        if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() > 1)
            throw new JsonException("Google stream returned unsupported candidates.");
        if (candidates.GetArrayLength() == 0) return [];
        if (finishReason is not null)
            throw new JsonException("Google stream returned a candidate after completion.");
        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object)
            throw new JsonException("Google candidate is invalid.");
        if (candidate.TryGetProperty("index", out var index)
            && (index.ValueKind != JsonValueKind.Number || index.GetInt32() != 0))
            throw new JsonException("Google candidate index is unsupported.");
        var events = new List<ProviderStreamEvent>();
        if (candidate.TryGetProperty("content", out var content))
        {
            if (content.ValueKind != JsonValueKind.Object)
                throw new JsonException("Google candidate content is invalid.");
            if (content.TryGetProperty("parts", out var parts))
            {
                if (parts.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Google candidate parts are invalid.");
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                        throw new JsonException("Google content part is invalid.");
                    if (part.TryGetProperty("thought", out var thought)
                        && thought.ValueKind == JsonValueKind.True) continue;
                    if (!part.TryGetProperty("text", out var text)
                        || text.ValueKind != JsonValueKind.String)
                        throw new JsonException("Google returned an unsupported content part.");
                    if (text.GetString() is { Length: > 0 } delta)
                        events.Add(new ProviderStreamEvent(ProviderStreamKind.TextDelta,
                            Text: delta, ProviderRequestId: ProviderRequestId));
                }
            }
        }
        if (candidate.TryGetProperty("finishReason", out var finish))
        {
            if (finish.ValueKind != JsonValueKind.String)
                throw new JsonException("Google finish reason is invalid.");
            (finishReason, refusal) = GoogleWireMapper.MapFinish(finish.GetString()!);
            if (hasUsage) finalUsageObserved = true;
        }
        return events;
    }

    public IReadOnlyList<ProviderStreamEvent> Complete()
    {
        if (finishReason is null || !finalUsageObserved || lastUsage is null)
            throw new JsonException("Google stream ended without terminal finish and verified usage.");
        var events = new List<ProviderStreamEvent>();
        if (refusal is not null)
            events.Add(new ProviderStreamEvent(ProviderStreamKind.Refusal,
                Text: refusal, ProviderRequestId: ProviderRequestId));
        events.Add(new ProviderStreamEvent(ProviderStreamKind.Finish,
            FinishReason: finishReason, ProviderRequestId: ProviderRequestId));
        events.Add(new ProviderStreamEvent(ProviderStreamKind.Usage,
            Usage: lastUsage, ProviderRequestId: ProviderRequestId));
        return events;
    }
}
