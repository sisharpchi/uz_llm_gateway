using System.Net;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.OpenAI;

namespace UZLLM.Provider.OpenAI.ContractTests;

public sealed class OpenAiAdapterContractTests
{
    private static readonly Guid ProviderId = Guid.NewGuid();
    private static readonly Guid CredentialId = Guid.NewGuid();
    private static readonly ProviderExecutionContext Context = new(Guid.NewGuid(), ProviderId,
        Guid.NewGuid(), CredentialId, "gpt-4o-mini", TimeSpan.FromSeconds(5));
    private static readonly ProviderChatRequest Request = new(
        [new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")])]);

    [Fact]
    public async Task Nonstream_request_and_response_preserve_tools_usage_and_request_id()
    {
        var handler = new FixtureHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"id":"chatcmpl-1","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{\"x\":1}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":20,"completion_tokens":8,"prompt_tokens_details":{"cached_tokens":5},"completion_tokens_details":{"reasoning_tokens":2}}}
            """));
        var adapter = CreateAdapter(handler);
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""");
        var request = new ProviderChatRequest([
            new ProviderMessage("assistant", [], [new ProviderToolCall("call_old", "lookup", "{}")]),
            new ProviderMessage("tool", [new ProviderContentPart(ProviderContentKind.Text, "ok")], ToolCallId: "call_old"),
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "again")])
        ], MaxOutputTokens: 100, Tools: [new ProviderToolDefinition("lookup", "Look up", schema.RootElement.Clone())],
            ToolChoice: new ProviderToolChoice(ProviderToolChoiceMode.Named, "lookup"),
            ResponseFormat: new ProviderResponseFormat(ProviderResponseFormatKind.JsonSchema, "result", schema.RootElement.Clone()));

        var completion = await adapter.CompleteAsync(request, Context);

        Assert.Equal("chatcmpl-1", completion.Id);
        Assert.Equal("req_provider_1", completion.ProviderRequestId);
        Assert.Equal(20, completion.Usage!.InputTokens);
        Assert.Equal(5, completion.Usage.CachedInputTokens);
        Assert.Equal(2, completion.Usage.ReasoningTokens);
        Assert.Equal("lookup", Assert.Single(completion.Message.ToolCalls!).Name);
        Assert.Equal("tool_calls", completion.FinishReason);
        Assert.Equal("Bearer secret-value", handler.Authorization);
        Assert.Equal(Context.RequestId.ToString("N"), handler.ClientRequestId);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("gpt-4o-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(100, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("lookup", body.RootElement.GetProperty("tool_choice").GetProperty("function")
            .GetProperty("name").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("call_old", body.RootElement.GetProperty("messages")[1].GetProperty("tool_call_id").GetString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_provider_usage_is_not_invented()
    {
        var handler = new FixtureHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"id":"chatcmpl-2","model":"gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"Hi"},"finish_reason":"stop"}]}
            """));
        var completion = await CreateAdapter(handler).CompleteAsync(Request, Context);
        Assert.Null(completion.Usage);
        Assert.Equal("Hi", Assert.Single(completion.Message.Content).Value);
    }

    [Theory]
    [InlineData(429, "rate_limit_exceeded", ProviderErrorCategory.RateLimited, true)]
    [InlineData(429, "insufficient_quota", ProviderErrorCategory.QuotaExhausted, false)]
    [InlineData(400, "context_length_exceeded", ProviderErrorCategory.ContextExceeded, false)]
    [InlineData(401, "invalid_api_key", ProviderErrorCategory.Authentication, false)]
    [InlineData(503, "server_is_overloaded", ProviderErrorCategory.Capacity, false)]
    [InlineData(500, "internal_error", ProviderErrorCategory.Upstream5xx, false)]
    public async Task Error_fixtures_classify_retry_without_automatic_reexecution(int status,
        string code, ProviderErrorCategory expected, bool fallback)
    {
        var handler = new FixtureHandler(_ => JsonResponse((HttpStatusCode)status,
            "{\"error\":{\"code\":\"" + code + "\",\"message\":\"upstream secret detail\"}}"));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            CreateAdapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(expected, exception.Error.Category);
        Assert.Equal(fallback, exception.Error.FallbackEligible);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("upstream secret detail", exception.Message);
        Assert.Equal(status >= 500 ? ProviderExecutionCertainty.Unknown
            : ProviderExecutionCertainty.RejectedBeforeExecution, exception.Error.Certainty);
    }

    [Fact]
    public async Task Stream_yields_text_tool_refusal_finish_and_usage_before_done()
    {
        const string frames = """
            data: {"choices":[{"index":0,"delta":{"content":"Hi","tool_calls":[{"index":0,"id":"call_1","function":{"name":"lookup","arguments":"{\"x\":"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"content":"!","refusal":"No"},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":3,"prompt_tokens_details":{"cached_tokens":2}}}

            data: [DONE]

            """;
        var handler = new FixtureHandler(_ => StreamResponse(frames));
        var events = await CollectAsync(CreateAdapter(handler).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.ToolCallDelta,
            ProviderStreamKind.TextDelta, ProviderStreamKind.Refusal, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], events.Select(value => value.Kind));
        Assert.Equal("Hi", events[0].Text);
        Assert.Equal("lookup", events[1].ToolName);
        Assert.Equal("tool_calls", events[4].FinishReason);
        Assert.Equal(2, events[5].Usage!.CachedInputTokens);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(body.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
    }

    [Fact]
    public async Task Stream_without_usage_but_with_done_remains_valid_with_unknown_usage()
    {
        var events = await CollectAsync(CreateAdapter(new FixtureHandler(_ => StreamResponse(
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"x\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")))
            .StreamAsync(Request, Context));
        Assert.DoesNotContain(events, value => value.Kind == ProviderStreamKind.Usage);
        Assert.Contains(events, value => value.Kind == ProviderStreamKind.Finish);
    }

    [Fact]
    public async Task Stream_interrupted_after_partial_output_has_unknown_execution_outcome()
    {
        var adapter = CreateAdapter(new FixtureHandler(_ => StreamResponse(
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"partial\"},\"finish_reason\":null}]}\n\n")));
        var events = new List<ProviderStreamEvent>();
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in adapter.StreamAsync(Request, Context)) events.Add(item);
        });
        Assert.Equal("partial", Assert.Single(events).Text);
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
        Assert.False(exception.Error.FallbackEligible);
    }

    [Fact]
    public async Task Stream_error_event_is_safe_and_terminal()
    {
        var events = await CollectAsync(CreateAdapter(new FixtureHandler(_ => StreamResponse(
            "event: error\ndata: {\"error\":{\"message\":\"private upstream detail\"}}\n\n")))
            .StreamAsync(Request, Context));
        var error = Assert.Single(events);
        Assert.Equal(ProviderStreamKind.Error, error.Kind);
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error!.Certainty);
        Assert.DoesNotContain("private upstream detail", error.Error.SafeMessage);
    }

    [Fact]
    public async Task Malformed_nonstream_response_is_unknown_not_success()
    {
        var handler = new FixtureHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"choices\":[]}"));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            CreateAdapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
    }

    [Fact]
    public async Task Missing_credential_prevents_upstream_dispatch()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("Must not be called"));
        var adapter = CreateAdapter(handler, secret: null);
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() => adapter.CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, error.Error.Certainty);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reclassified_as_provider_error()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var handler = new FixtureHandler(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateAdapter(handler).CompleteAsync(Request, Context, cancelled.Token));
    }

    private static OpenAiChatAdapter CreateAdapter(FixtureHandler handler, string? secret = "secret-value") =>
        new(new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan }, new CredentialResolver(secret), OpenAiAdapterOptions.Production);

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string body)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_provider_1");
        return response;
    }

    private static HttpResponseMessage StreamResponse(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_provider_1");
        return response;
    }

    private static async Task<List<ProviderStreamEvent>> CollectAsync(IAsyncEnumerable<ProviderStreamEvent> source)
    {
        var events = new List<ProviderStreamEvent>();
        await foreach (var item in source) events.Add(item);
        return events;
    }

    private sealed class CredentialResolver(string? secret) : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId,
            CancellationToken cancellationToken = default) => Task.FromResult(secret);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }
        public string? ClientRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri?.ToString());
            return respond(request);
        }
    }
}
