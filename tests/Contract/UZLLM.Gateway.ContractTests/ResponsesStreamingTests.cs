using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class GatewayExecutionTests
{
    private const string StreamingResponseRequest = """{"model":"gpt-test","input":"Hello","stream":true,"max_output_tokens":32}""";
    private static ProviderStreamEvent[] ResponsesStream(string finish = "stop", bool usage = true) => [
        new(ProviderStreamKind.TextDelta, Text: "Hello ", ProviderRequestId: "provider-stream"),
        new(ProviderStreamKind.TextDelta, Text: "world", ProviderRequestId: "provider-stream"),
        new(ProviderStreamKind.Finish, FinishReason: finish),
        ..usage ? new[] { new ProviderStreamEvent(ProviderStreamKind.Usage, Usage: new(12, 5, 4, 2)) } : []];

    internal static JsonElement[] ResponsesEvents(string body) => body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(frame => {
            var lines = frame.Split('\n');
            var value = JsonSerializer.Deserialize<JsonElement>(lines.Single(line => line.StartsWith("data: "))[6..]);
            Assert.Equal("event: " + value.GetProperty("type").GetString(), lines[0]);
            return value;
        }).ToArray();

    [Theory]
    [InlineData("stop", "completed", true)]
    [InlineData("length", "incomplete", true)]
    [InlineData("content_filter", "incomplete", true)]
    [InlineData("stop", "completed", false)]
    public async Task Responses_SSE_flushes_deltas_before_terminal_and_financial_cleanup(string finish, string status, bool knownUsage)
    {
        var scenario = new Scenario();
        scenario.Adapter.StreamItems = ResponsesStream(finish, knownUsage);
        scenario.Finance.NextFinalization = knownUsage ? FinalizationStatus.Settled : FinalizationStatus.PendingEvidence;
        scenario.Adapter.BeforeStreamEvent = item => {
            if (item.Kind != ProviderStreamKind.Finish) return;
            var partial = Encoding.UTF8.GetString(((MemoryStream)scenario.Context.Response.Body).ToArray());
            Assert.Contains("response.output_text.delta", partial);
            Assert.DoesNotContain("response.completed", partial);
            Assert.Equal(0, scenario.Finance.Finalizes);
        };
        scenario.Finance.BeforeFinalize = () => Assert.DoesNotContain("event: response." + status + "\n",
            Encoding.UTF8.GetString(((MemoryStream)scenario.Context.Response.Body).ToArray()));
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        var body = await ResponseBodyAsync(scenario.Context);
        var events = ResponsesEvents(body);
        Assert.Equal(Enumerable.Range(0, events.Length), events.Select(value => value.GetProperty("sequence_number").GetInt32()));
        Assert.Equal("response.created", events[0].GetProperty("type").GetString());
        Assert.Empty(events[0].GetProperty("response").GetProperty("output").EnumerateArray());
        Assert.Equal("response." + status, events[^1].GetProperty("type").GetString());
        var response = events[^1].GetProperty("response");
        Assert.Equal("Hello world", response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("resp_" + scenario.RequestId.ToString("N"), response.GetProperty("id").GetString());
        if (knownUsage) Assert.Equal(17, response.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        else Assert.Equal(JsonValueKind.Null, response.GetProperty("usage").ValueKind);
        if (status == "incomplete") Assert.Equal(finish == "length" ? "max_output_tokens" : "content_filter",
            response.GetProperty("incomplete_details").GetProperty("reason").GetString());
        Assert.DoesNotContain("[DONE]", body);
        Assert.Equal("text/event-stream", scenario.Context.Response.ContentType);
        Assert.Equal("no-store", scenario.Context.Response.Headers.CacheControl);
        Assert.Equal("no", scenario.Context.Response.Headers["X-Accel-Buffering"]);
        Assert.Equal(1, scenario.Finance.Reserves); Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Equal(knownUsage ? 1 : 0, scenario.Usage.VerifiedEvidence);
        Assert.Equal(knownUsage ? 0 : 1, scenario.Usage.UnknownEvidence);
        Assert.Equal(DeliveryState.Completed, scenario.Usage.Delivery);
        Assert.Equal(1, scenario.Limiter.Releases);
    }

    [Theory]
    [InlineData("missing-finish")]
    [InlineData("missing-finish-known")]
    [InlineData("transport")]
    [InlineData("transport-known")]
    [InlineData("in-band")]
    [InlineData("tool")]
    [InlineData("late-rejection")]
    [InlineData("settlement")]
    public async Task Responses_SSE_partial_failure_never_falls_back_or_fabricates_success(string failure)
    {
        var scenario = new Scenario(twoProviders: true);
        var knownUsage = failure is "settlement" or "missing-finish-known" or "transport-known";
        scenario.Finance.NextFinalization = knownUsage ? FinalizationStatus.Settled : FinalizationStatus.PendingEvidence;
        scenario.Adapter.StreamItems = failure == "settlement" ? ResponsesStream() : [new(ProviderStreamKind.TextDelta, Text: "partial", ProviderRequestId: "partial-request")];
        if (failure is "missing-finish-known" or "transport-known") scenario.Adapter.StreamItems = [..scenario.Adapter.StreamItems,
            new(ProviderStreamKind.Usage, Usage: new(12, 5, 4, 2))];
        if (failure is "transport" or "transport-known") scenario.Adapter.FailAfterStreamItems = true;
        if (failure is "in-band" or "late-rejection") scenario.Adapter.StreamItems = [..scenario.Adapter.StreamItems,
            new(ProviderStreamKind.Error, Error: new(ProviderErrorCategory.Capacity, ProviderExecutionCertainty.RejectedBeforeExecution,
                true, true, 429, "p", "Provider unavailable."))];
        if (failure == "tool") scenario.Adapter.StreamItems = [..scenario.Adapter.StreamItems, new(ProviderStreamKind.ToolCallDelta, ToolName: "not-supported")];
        if (failure == "settlement") scenario.Finance.FinalizationFailure = new InvalidOperationException("database unavailable");
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        var body = await ResponseBodyAsync(scenario.Context);
        var events = ResponsesEvents(body);
        Assert.Equal("response.failed", events[^1].GetProperty("type").GetString());
        Assert.Equal(failure == "settlement" ? "finalization_pending" : "provider_error",
            events[^1].GetProperty("response").GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("event: response.completed", body);
        Assert.DoesNotContain("response.output_text.done", body);
        Assert.Equal(0, scenario.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, scenario.Finance.Finalizes); Assert.Equal(0, scenario.Finance.Releases);
        Assert.Equal(knownUsage ? 1 : 0, scenario.Usage.VerifiedEvidence);
        Assert.Equal(knownUsage ? 0 : 1, scenario.Usage.UnknownEvidence);
        Assert.Equal(DeliveryState.Partial, scenario.Usage.Delivery);
        Assert.True(scenario.Adapter.StreamDisposed);
        if (failure is "missing-finish" or "missing-finish-known" or "transport" or "transport-known" or "tool")
            Assert.Equal("partial-request", scenario.Usage.FinishedProviderRequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Responses_SSE_disconnect_cancels_transport_but_preserves_evidence_and_settlement(bool knownUsage)
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.Adapter.StreamItems = [new(ProviderStreamKind.TextDelta, Text: "partial"),
            ..knownUsage ? new[] { new ProviderStreamEvent(ProviderStreamKind.Usage, Usage: new(12, 5, 4, 2)) } : [],
            new(ProviderStreamKind.Finish, FinishReason: "stop")];
        scenario.Adapter.BeforeStreamEvent = item => {
            if (item.Kind == ProviderStreamKind.Finish) scenario.Writer.AbortSource.Cancel();
        };
        scenario.Finance.NextFinalization = knownUsage ? FinalizationStatus.Settled : FinalizationStatus.PendingEvidence;
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        Assert.True(scenario.Adapter.ObservedStreamToken.IsCancellationRequested);
        Assert.True(scenario.Adapter.StreamDisposed);
        Assert.Equal(1, scenario.Finance.Finalizes); Assert.Equal(0, scenario.Finance.Releases);
        Assert.False(scenario.Finance.FinalizationToken.IsCancellationRequested);
        Assert.False(scenario.Usage.EvidenceToken.IsCancellationRequested);
        Assert.Equal(knownUsage ? 1 : 0, scenario.Usage.VerifiedEvidence);
        Assert.Equal(knownUsage ? 0 : 1, scenario.Usage.UnknownEvidence);
        Assert.Equal(DeliveryState.ClientDisconnected, scenario.Usage.Delivery);
        Assert.Equal(0, scenario.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, scenario.Limiter.Releases);
        Assert.DoesNotContain("response.completed", await ResponseBodyAsync(scenario.Context));
    }

    [Fact]
    public async Task Responses_SSE_preexecution_rejection_can_failover_before_any_event()
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.Adapter.Error = new(ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution,
            true, true, 429, "rejected", "Rate limited.");
        scenario.AnthropicAdapter.StreamItems = ResponsesStream();
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        Assert.Equal("anthropic", scenario.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal(2, scenario.Usage.Attempts); Assert.Equal(1, scenario.Finance.Reserves);
        Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Single(ResponsesEvents(await ResponseBodyAsync(scenario.Context)), value => value.GetProperty("type").GetString() == "response.created");
    }

    [Fact]
    public async Task Responses_SSE_empty_upstream_is_JSON_error_not_a_successful_empty_stream()
    {
        var scenario = new Scenario();
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        Assert.Equal(502, scenario.Context.Response.StatusCode);
        Assert.Contains("\"error\":", await ResponseBodyAsync(scenario.Context));
        Assert.Equal(1, scenario.Usage.UnknownEvidence); Assert.Equal(1, scenario.Finance.Finalizes);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("negative")]
    [InlineData("cached")]
    [InlineData("reasoning")]
    public async Task Responses_SSE_ambiguous_usage_is_unknown_not_a_settleable_snapshot(string failure)
    {
        var scenario = new Scenario(); scenario.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        ProviderUsage? invalid = failure switch { "null" => null, "negative" => new(-1, 5, 0, null),
            "cached" => new(12, 5, 13, null), "reasoning" => new(12, 5, 0, 6), _ => new(1, 1, 0, null) };
        scenario.Adapter.StreamItems = [new(ProviderStreamKind.TextDelta, Text: "partial"),
            ..failure == "duplicate" ? new[] { new ProviderStreamEvent(ProviderStreamKind.Usage, Usage: new(12, 5, 4, 2)) } : [],
            new(ProviderStreamKind.Usage, Usage: invalid)];
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        var events = ResponsesEvents(await ResponseBodyAsync(scenario.Context));
        Assert.Equal("response.failed", events[^1].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, events[^1].GetProperty("response").GetProperty("usage").ValueKind);
        Assert.Equal(0, scenario.Usage.VerifiedEvidence); Assert.Equal(1, scenario.Usage.UnknownEvidence);
        Assert.Equal(0, scenario.Finance.Releases); Assert.Equal(1, scenario.Finance.Finalizes);
    }

    [Theory]
    [InlineData("duplicate-finish")]
    [InlineData("after-finish")]
    [InlineData("unsupported-finish")]
    [InlineData("null-text")]
    public async Task Responses_SSE_writer_rejects_invalid_event_order_or_shape(string failure)
    {
        var scenario = new Scenario();
        scenario.Adapter.StreamItems = failure switch {
            "duplicate-finish" => [new(ProviderStreamKind.Finish, FinishReason: "stop"), new(ProviderStreamKind.Finish, FinishReason: "stop")],
            "after-finish" => [new(ProviderStreamKind.Finish, FinishReason: "stop"), new(ProviderStreamKind.TextDelta, Text: "late")],
            "unsupported-finish" => [new(ProviderStreamKind.Finish, FinishReason: "tool_calls")],
            _ => [new(ProviderStreamKind.TextDelta)] };
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        Assert.DoesNotContain("response.completed", await ResponseBodyAsync(scenario.Context));
        Assert.Equal(1, scenario.Usage.UnknownEvidence); Assert.Equal(1, scenario.Finance.Finalizes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Responses_SSE_snapshot_bound_is_exact_and_never_truncates_as_success(int offset)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var parsed = ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(StreamingResponseRequest));
        var writer = new OpenAiResponsesWriter(context, parsed);
        var write = () => writer.WriteStreamEventAsync(new(ProviderStreamKind.TextDelta,
            Text: new string('x', OpenAiResponsesWriter.MaximumStreamCharacters + offset)), "m", Guid.NewGuid(), CancellationToken.None);
        if (offset > 0) { await Assert.ThrowsAsync<ProviderExecutionException>(write); Assert.False(writer.Started); }
        else { await write(); Assert.True(writer.Started); }
    }

    [Fact]
    public async Task Responses_SSE_snapshot_bound_counts_all_deltas_and_content_parts()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var writer = new OpenAiResponsesWriter(context, ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(StreamingResponseRequest)));
        var id = Guid.NewGuid();
        await writer.WriteStreamEventAsync(new(ProviderStreamKind.TextDelta, Text: new string('x', OpenAiResponsesWriter.MaximumStreamCharacters - 1)), "m", id, CancellationToken.None);
        await writer.WriteStreamEventAsync(new(ProviderStreamKind.Refusal, Text: "y"), "m", id, CancellationToken.None);
        await Assert.ThrowsAsync<ProviderExecutionException>(() => writer.WriteStreamEventAsync(
            new(ProviderStreamKind.TextDelta, Text: "z"), "m", id, CancellationToken.None));
        await writer.WriteStreamErrorAsync("Stream too large.", "provider_error", CancellationToken.None);
        var events = ResponsesEvents(Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Equal(2, events.Count(value => value.GetProperty("type").GetString()!.EndsWith(".delta")));
        Assert.Equal("response.failed", events[^1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Responses_SSE_refusal_and_text_keep_content_indices_and_terminal_snapshot()
    {
        var scenario = new Scenario();
        scenario.Adapter.StreamItems = [new(ProviderStreamKind.Refusal, Text: "No"), new(ProviderStreamKind.TextDelta, Text: "Explanation"),
            new(ProviderStreamKind.Refusal, Text: " thanks"), new(ProviderStreamKind.Finish, FinishReason: "stop")];
        await scenario.RunResponsesAsync(StreamingResponseRequest);
        var events = ResponsesEvents(await ResponseBodyAsync(scenario.Context));
        Assert.Equal(new[] { 0, 1, 0 }, events.Where(value => value.GetProperty("type").GetString()!.EndsWith(".delta"))
            .Select(value => value.GetProperty("content_index").GetInt32()));
        var content = events[^1].GetProperty("response").GetProperty("output")[0].GetProperty("content");
        Assert.Equal("No thanks", content[0].GetProperty("refusal").GetString());
        Assert.Equal("Explanation", content[1].GetProperty("text").GetString());
        Assert.Contains(events, value => value.GetProperty("type").GetString() == "response.refusal.done");
    }
}
