using System.Net;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.Anthropic;

namespace UZLLM.Provider.Anthropic.ContractTests;

public sealed class AnthropicAdapterContractTests
{
    private static readonly ProviderExecutionContext Context = new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "claude-test", TimeSpan.FromSeconds(5));
    private static readonly ProviderChatRequest Request = new([
        new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")])
    ], MaxOutputTokens: 100);

    [Fact]
    public async Task Messages_request_and_response_preserve_tools_usage_and_request_id()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"msg_01","type":"message","role":"assistant","model":"claude-test","content":[{"type":"text","text":"I will check"},{"type":"tool_use","id":"toolu_01","name":"lookup","input":{"x":1}}],"stop_reason":"tool_use","usage":{"input_tokens":15,"cache_read_input_tokens":5,"cache_creation_input_tokens":0,"output_tokens":8}}
            """));
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""");
        var request = new ProviderChatRequest([
            new ProviderMessage("system", [new ProviderContentPart(ProviderContentKind.Text, "Be concise")]),
            new ProviderMessage("assistant", [], [new ProviderToolCall("old", "lookup", "{}")]),
            new ProviderMessage("tool", [new ProviderContentPart(ProviderContentKind.Text, "ok")], ToolCallId: "old"),
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Again")])
        ], MaxOutputTokens: 100, Tools: [new ProviderToolDefinition("lookup", "Look up", schema.RootElement.Clone())],
            ToolChoice: new ProviderToolChoice(ProviderToolChoiceMode.Named, "lookup"));

        var completion = await Adapter(handler).CompleteAsync(request, Context);

        Assert.Equal("msg_01", completion.Id);
        Assert.Equal("req_01", completion.ProviderRequestId);
        Assert.Equal(20, completion.Usage!.InputTokens);
        Assert.Equal(5, completion.Usage.CachedInputTokens);
        Assert.Equal(8, completion.Usage.OutputTokens);
        Assert.Equal("tool_calls", completion.FinishReason);
        Assert.Equal("lookup", Assert.Single(completion.Message.ToolCalls!).Name);
        Assert.Equal("secret-value", handler.ApiKey);
        Assert.Equal("2023-06-01", handler.Version);
        Assert.Equal(Context.RequestId.ToString("N"), handler.ClientRequestId);
        Assert.Equal("/v1/messages", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Be concise", body.RootElement.GetProperty("system")[0].GetProperty("text").GetString());
        Assert.Equal(100, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("tool", body.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("old", body.RootElement.GetProperty("messages")[1].GetProperty("content")[0]
            .GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task Vision_and_json_schema_use_native_messages_shape()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"msg_02","model":"claude-test","content":[{"type":"text","text":"{\"ok\":true}"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":4}}
            """));
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}}}""");
        var request = new ProviderChatRequest([
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.ImageUrl,
                "https://example.com/image.png")])
        ], MaxOutputTokens: 50, ResponseFormat: new ProviderResponseFormat(
            ProviderResponseFormatKind.JsonSchema, "answer", schema.RootElement.Clone()));
        var completion = await Adapter(handler).CompleteAsync(request, Context);
        Assert.Equal("stop", completion.FinishReason);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("url", body.RootElement.GetProperty("messages")[0].GetProperty("content")[0]
            .GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("output_config")
            .GetProperty("format").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(429, "rate_limit_error", ProviderErrorCategory.RateLimited, true)]
    [InlineData(529, "overloaded_error", ProviderErrorCategory.Capacity, true)]
    [InlineData(500, "api_error", ProviderErrorCategory.Upstream5xx, false)]
    [InlineData(504, "timeout_error", ProviderErrorCategory.Timeout, false)]
    [InlineData(401, "authentication_error", ProviderErrorCategory.Authentication, false)]
    [InlineData(402, "billing_error", ProviderErrorCategory.QuotaExhausted, false)]
    public async Task Official_error_shape_classifies_safe_failover(int status, string type,
        ProviderErrorCategory category, bool eligible)
    {
        var handler = new FixtureHandler(_ => Response((HttpStatusCode)status,
            "{\"type\":\"error\",\"error\":{\"type\":\"" + type
            + "\",\"message\":\"private provider detail\"},\"request_id\":\"req_01\"}"));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(category, exception.Error.Category);
        Assert.Equal(eligible, exception.Error.FallbackEligible);
        Assert.Equal(eligible ? ProviderExecutionCertainty.RejectedBeforeExecution
            : status >= 500 ? ProviderExecutionCertainty.Unknown
                : ProviderExecutionCertainty.RejectedBeforeExecution, exception.Error.Certainty);
        Assert.DoesNotContain("private provider detail", exception.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Official_stream_event_sequence_maps_text_tool_finish_and_cumulative_usage()
    {
        const string frames = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_01","usage":{"input_tokens":10,"cache_read_input_tokens":2,"cache_creation_input_tokens":0,"output_tokens":1}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"lookup","input":{}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"x\":1}"}}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":9}}

            event: message_stop
            data: {"type":"message_stop"}

            """;
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => StreamResponse(frames)))
            .StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.ToolCallDelta,
            ProviderStreamKind.ToolCallDelta, ProviderStreamKind.Finish, ProviderStreamKind.Usage],
            events.Select(value => value.Kind));
        Assert.Equal("Hello", events[0].Text);
        Assert.Equal("toolu_01", events[1].ToolCallId);
        Assert.Equal("{\"x\":1}", events[2].ArgumentsDelta);
        Assert.Equal("tool_calls", events[3].FinishReason);
        Assert.Equal(12, events[4].Usage!.InputTokens);
        Assert.Equal(9, events[4].Usage!.OutputTokens);
    }

    [Fact]
    public async Task Partial_stream_without_message_stop_is_unknown_not_retryable()
    {
        var adapter = Adapter(new FixtureHandler(_ => StreamResponse("""
            event: message_start
            data: {"type":"message_start","message":{"usage":{"input_tokens":2,"output_tokens":1}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"partial"}}

            """)));
        var events = new List<ProviderStreamEvent>();
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var item in adapter.StreamAsync(Request, Context)) events.Add(item); });
        Assert.Equal("partial", Assert.Single(events).Text);
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
        Assert.False(exception.Error.FallbackEligible);
    }

    [Fact]
    public async Task Unexpected_cache_creation_is_not_mispriced_as_verified_usage()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"msg_03","model":"claude-test","content":[{"type":"text","text":"Hi"}],"stop_reason":"end_turn","usage":{"input_tokens":4,"cache_creation_input_tokens":5,"cache_read_input_tokens":0,"output_tokens":2}}
            """));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
    }

    [Fact]
    public async Task Refusal_is_a_billed_success_not_a_retryable_provider_error()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"msg_refusal","model":"claude-test","content":[],"stop_reason":"refusal","stop_details":{"type":"safety_refusal"},"usage":{"input_tokens":3,"output_tokens":1}}
            """));
        var completion = await Adapter(handler).CompleteAsync(Request, Context);
        Assert.Equal("content_filter", completion.FinishReason);
        Assert.NotNull(completion.Refusal);
        Assert.Equal(3, completion.Usage!.InputTokens);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_credentials_and_unsupported_request_do_not_dispatch()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("Must not be called"));
        var credentialError = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler, null).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, credentialError.Error.Certainty);
        var bad = Request with { Messages = [new ProviderMessage("user", [
            new ProviderContentPart(ProviderContentKind.Text, "Hello")]),
            new ProviderMessage("developer", [new ProviderContentPart(ProviderContentKind.Text, "Late")])] };
        var requestError = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(bad, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, requestError.Error.Certainty);
        Assert.Equal(0, handler.Calls);
    }

    private static AnthropicMessagesAdapter Adapter(FixtureHandler handler, string? secret = "secret-value") =>
        new(new HttpClient(handler) { BaseAddress = AnthropicAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan }, new CredentialResolver(secret),
            AnthropicAdapterOptions.Production);

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("request-id", "req_01");
        return response;
    }

    private static HttpResponseMessage StreamResponse(string frames)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(frames, Encoding.UTF8, "text/event-stream") };
        response.Headers.TryAddWithoutValidation("request-id", "req_stream");
        return response;
    }

    private static async Task<List<ProviderStreamEvent>> CollectAsync(
        IAsyncEnumerable<ProviderStreamEvent> stream)
    {
        var events = new List<ProviderStreamEvent>();
        await foreach (var item in stream) events.Add(item);
        return events;
    }

    private sealed class CredentialResolver(string? secret) : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId,
            CancellationToken cancellationToken = default) => Task.FromResult(secret);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int Calls;
        public string? ApiKey, Version, ClientRequestId, Path, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            ApiKey = request.Headers.GetValues("x-api-key").Single();
            Version = request.Headers.GetValues("anthropic-version").Single();
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
