using System.Net;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.DeepSeek;

namespace UZLLM.Provider.DeepSeek.ContractTests;

public sealed class DeepSeekAdapterContractTests
{
    private static readonly ProviderExecutionContext Context = new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "deepseek-flash", TimeSpan.FromSeconds(5));
    private static readonly ProviderChatRequest Request = new([
        new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")])
    ], MaxOutputTokens: 100);

    [Fact]
    public async Task Native_chat_completion_maps_cache_hit_miss_and_reasoning_without_double_counting()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"ds_001","object":"chat.completion","model":"deepseek-flash","choices":[{"index":0,"message":{"role":"assistant","content":"Hello!","reasoning_content":"private chain of thought"},"finish_reason":"stop"}],"usage":{"prompt_tokens":20,"prompt_cache_hit_tokens":8,"prompt_cache_miss_tokens":12,"prompt_tokens_details":{"cached_tokens":8},"completion_tokens":9,"completion_tokens_details":{"reasoning_tokens":4},"total_tokens":29}}
            """));
        var request = new ProviderChatRequest([
            new ProviderMessage("system", [new ProviderContentPart(ProviderContentKind.Text, "Be concise")]),
            new ProviderMessage("developer", [new ProviderContentPart(ProviderContentKind.Text, "Use JSON")]),
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")])
        ], Temperature: 0.2m, MaxOutputTokens: 100,
            ResponseFormat: new ProviderResponseFormat(ProviderResponseFormatKind.JsonObject));

        var completion = await Adapter(handler).CompleteAsync(request, Context);

        Assert.Equal("ds_001", completion.Id);
        Assert.Equal("ds_001", completion.ProviderRequestId);
        Assert.Equal("stop", completion.FinishReason);
        Assert.Equal("Hello!", Assert.Single(completion.Message.Content).Value);
        Assert.Equal(20, completion.Usage!.InputTokens);
        Assert.Equal(8, completion.Usage.CachedInputTokens);
        Assert.Equal(9, completion.Usage.OutputTokens);
        Assert.Equal(4, completion.Usage.ReasoningTokens);
        Assert.DoesNotContain("private chain of thought", completion.Message.Content[0].Value);
        Assert.Equal("/v1/chat/completions", handler.Path);
        Assert.Equal("Bearer secret-value", handler.Authorization);
        Assert.Equal(Context.RequestId.ToString("N"), handler.ClientRequestId);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("deepseek-flash", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(100, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("max_completion_tokens", out _));
        Assert.Equal("disabled", body.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format")
            .GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("{\"prompt_tokens\":20,\"prompt_cache_hit_tokens\":8,\"prompt_cache_miss_tokens\":11,\"completion_tokens\":9,\"total_tokens\":29}")]
    [InlineData("{\"prompt_tokens\":20,\"prompt_cache_hit_tokens\":8,\"prompt_cache_miss_tokens\":12,\"prompt_tokens_details\":{\"cached_tokens\":7},\"completion_tokens\":9,\"total_tokens\":29}")]
    [InlineData("{\"prompt_tokens\":20,\"prompt_cache_hit_tokens\":8,\"prompt_cache_miss_tokens\":12,\"completion_tokens\":9,\"total_tokens\":28}")]
    [InlineData("{\"prompt_tokens\":20,\"prompt_cache_hit_tokens\":8,\"prompt_cache_miss_tokens\":12,\"completion_tokens\":9,\"completion_tokens_details\":{\"reasoning_tokens\":10},\"total_tokens\":29}")]
    [InlineData("{\"prompt_tokens\":20,\"completion_tokens\":9,\"total_tokens\":29}")]
    public async Task Inconsistent_or_missing_cache_accounting_is_unknown(string usage)
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK,
            "{\"id\":\"ds_bad\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hi\"},\"finish_reason\":\"stop\"}],\"usage\":"
            + usage + "}"));
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.FallbackEligible);
        Assert.Equal("ds_bad", error.Error.ProviderRequestId);
    }

    [Fact]
    public async Task Missing_usage_does_not_fabricate_zero_cost()
    {
        var completion = await Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"ds_no_usage","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"Hi"},"finish_reason":"stop"}]}
            """))).CompleteAsync(Request, Context);
        Assert.Null(completion.Usage);
    }

    [Fact]
    public async Task Content_filter_refusal_retains_verified_billable_usage()
    {
        var completion = await Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"id":"ds_block","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":null},"finish_reason":"content_filter"}],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":0,"total_tokens":4}}
            """))).CompleteAsync(Request, Context);
        Assert.Equal("content_filter", completion.FinishReason);
        Assert.NotNull(completion.Refusal);
        Assert.Equal(1, completion.Usage!.CachedInputTokens);
    }

    [Theory]
    [InlineData(400, ProviderErrorCategory.InvalidRequest, ProviderExecutionCertainty.RejectedBeforeExecution, false)]
    [InlineData(401, ProviderErrorCategory.Authentication, ProviderExecutionCertainty.RejectedBeforeExecution, false)]
    [InlineData(402, ProviderErrorCategory.QuotaExhausted, ProviderExecutionCertainty.RejectedBeforeExecution, false)]
    [InlineData(422, ProviderErrorCategory.InvalidRequest, ProviderExecutionCertainty.RejectedBeforeExecution, false)]
    [InlineData(429, ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution, true)]
    [InlineData(500, ProviderErrorCategory.Upstream5xx, ProviderExecutionCertainty.Unknown, false)]
    [InlineData(503, ProviderErrorCategory.Capacity, ProviderExecutionCertainty.Unknown, false)]
    public async Task Official_http_errors_keep_safe_certainty(int status,
        ProviderErrorCategory category, ProviderExecutionCertainty certainty, bool fallback)
    {
        var handler = new FixtureHandler(_ => Response((HttpStatusCode)status,
            "{\"error\":{\"message\":\"private provider detail\"}}"));
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(category, error.Error.Category);
        Assert.Equal(certainty, error.Error.Certainty);
        Assert.Equal(fallback, error.Error.FallbackEligible);
        Assert.DoesNotContain("private provider detail", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Incomplete_finish_remains_unknown_even_with_usage()
    {
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK, """
                {"id":"ds_abort","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"partial"},"finish_reason":"aborted"}],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":0,"prompt_cache_miss_tokens":4,"completion_tokens":2,"total_tokens":6}}
                """))).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.Equal("ds_abort", error.Error.ProviderRequestId);
    }

    [Fact]
    public async Task Unsupported_vision_tools_schema_sampling_and_missing_credentials_do_not_dispatch()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("Must not dispatch"));
        var adapter = Adapter(handler);
        Assert.False(adapter.Supports(Request with { Messages = [new ProviderMessage("user", [
            new ProviderContentPart(ProviderContentKind.ImageUrl, "https://example.com/a.png")])] }, false));
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        Assert.False(adapter.Supports(Request with { ResponseFormat = new ProviderResponseFormat(
            ProviderResponseFormatKind.JsonSchema, "answer", schema.RootElement.Clone()) }, false));
        Assert.True(adapter.Supports(Request, true));
        Assert.False(adapter.Supports(Request with { TopP = 0.8m }, true));
        Assert.False(adapter.Supports(Request with { Tools = [new ProviderToolDefinition(
            "lookup", null, schema.RootElement.Clone())] }, false));
        var missing = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler, null).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, missing.Error.Certainty);
        var stream = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(handler, null).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, stream.Error.Certainty);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Client_cancellation_propagates_without_reclassifying_a_disconnect()
    {
        var handler = new AsyncFixtureHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var client = new HttpClient(handler)
        { BaseAddress = DeepSeekAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new DeepSeekChatAdapter(client, new CredentialResolver("secret-value"),
            DeepSeekAdapterOptions.Production);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.CompleteAsync(Request, Context, cancellation.Token));
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task Stream_forwards_text_only_then_reports_one_final_cache_and_reasoning_usage()
    {
        var handler = new FixtureHandler(_ => StreamResponse(
            """
            {"id":"ds_stream","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"role":"assistant","reasoning_content":"private reasoning"},"finish_reason":null}],"usage":null}
            """,
            """
            {"id":"ds_stream","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}],"usage":null}
            """,
            """
            {"id":"ds_stream","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":" world"},"finish_reason":"stop"}],"usage":null}
            """,
            """
            {"id":"ds_stream","object":"chat.completion.chunk","model":"deepseek-flash","choices":[],"usage":{"prompt_tokens":20,"prompt_cache_hit_tokens":8,"prompt_cache_miss_tokens":12,"prompt_tokens_details":{"cached_tokens":8},"completion_tokens":9,"completion_tokens_details":{"reasoning_tokens":4},"total_tokens":29}}
            """));

        var events = await CollectAsync(Adapter(handler).StreamAsync(Request, Context));

        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.TextDelta,
            ProviderStreamKind.Finish, ProviderStreamKind.Usage], events.Select(value => value.Kind));
        Assert.Equal("Hello world", string.Concat(events.Where(value => value.Kind == ProviderStreamKind.TextDelta)
            .Select(value => value.Text)));
        Assert.All(events, value => Assert.Equal("ds_stream", value.ProviderRequestId));
        Assert.Equal("stop", events[2].FinishReason);
        Assert.Equal(new ProviderUsage(20, 9, 8, 4), events[3].Usage);
        Assert.DoesNotContain(events, value => value.Text?.Contains("private reasoning") == true);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(body.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal("disabled", body.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("top_p", out _));
    }

    [Fact]
    public async Task Stream_accepts_finish_and_authoritative_usage_in_the_same_last_chunk()
    {
        var response = StreamResponse("""
            {"id":"ds_same","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"Done"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":2,"completion_tokens_details":{"reasoning_tokens":1},"total_tokens":6}}
            """);
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => response)).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], events.Select(value => value.Kind));
        Assert.Equal(new ProviderUsage(4, 2, 1, 1), events[^1].Usage);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Stream_without_done_or_final_usage_remains_unknown(bool done, bool usage)
    {
        var initial = """
            {"id":"ds_partial","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":"stop"}],"usage":null}
            """;
        var final = """
            {"id":"ds_partial","object":"chat.completion.chunk","model":"deepseek-flash","choices":[],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":2,"total_tokens":6}}
            """;
        var response = StreamResponse(done, usage ? [initial, final] : [initial]);
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(new FixtureHandler(_ => response)).StreamAsync(Request, Context))
                events.Add(item);
        });
        Assert.Single(events);
        Assert.Equal(ProviderStreamKind.TextDelta, events[0].Kind);
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.FallbackEligible);
        Assert.Equal("ds_partial", error.Error.ProviderRequestId);
    }

    [Theory]
    [InlineData("{\"prompt_tokens\":4,\"prompt_cache_hit_tokens\":1,\"prompt_cache_miss_tokens\":2,\"completion_tokens\":2,\"total_tokens\":6}")]
    [InlineData("{\"prompt_tokens\":4,\"prompt_cache_hit_tokens\":1,\"prompt_cache_miss_tokens\":3,\"completion_tokens\":2,\"completion_tokens_details\":{\"reasoning_tokens\":3},\"total_tokens\":6}")]
    public async Task Stream_with_inconsistent_usage_is_unknown_and_never_emits_success(string usage)
    {
        var finish = """
            {"id":"ds_bad","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":"stop"}],"usage":null}
            """;
        var final = "{" + "\"id\":\"ds_bad\",\"object\":\"chat.completion.chunk\",\"model\":\"deepseek-flash\",\"choices\":[],\"usage\":" + usage + "}";
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(new FixtureHandler(_ => StreamResponse(finish, final)))
                .StreamAsync(Request, Context)) events.Add(item);
        });
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.Equal([ProviderStreamKind.TextDelta], events.Select(value => value.Kind));
    }

    [Fact]
    public async Task Stream_with_changed_identity_is_unknown_not_replayed()
    {
        var response = StreamResponse(
            """
            {"id":"first","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":null}],"usage":null}
            """,
            """
            {"id":"second","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"other"},"finish_reason":"stop"}],"usage":null}
            """);
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(new FixtureHandler(_ => response)).StreamAsync(Request, Context))
                events.Add(item);
        });
        Assert.Equal([ProviderStreamKind.TextDelta], events.Select(value => value.Kind));
        Assert.Equal("first", error.Error.ProviderRequestId);
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
    }

    [Fact]
    public async Task Stream_in_band_error_and_preexecution_429_preserve_distinct_certainty()
    {
        var partial = """
            {"id":"ds_error","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":null}],"usage":null}
            """;
        var inBand = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent($"data: {partial}\n\nevent: error\ndata: {{\"error\":{{\"message\":\"private\"}}}}\n\n", Encoding.UTF8, "text/event-stream") };
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => inBand)).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.Error],
            events.Select(value => value.Kind));
        Assert.Equal(ProviderExecutionCertainty.Unknown, events[^1].Error!.Certainty);
        Assert.False(events[^1].Error!.FallbackEligible);

        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(new FixtureHandler(_ => Response(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"message\":\"private\"}}"))).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.RejectedBeforeExecution, error.Error.Certainty);
        Assert.True(error.Error.FallbackEligible);
    }

    [Fact]
    public async Task Stream_rejects_wrong_content_type_and_oversized_frame_as_unknown()
    {
        var wrong = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK,
            "{}"))).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.Unknown, wrong.Error.Certainty);
        var oversized = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("data: " + new string('x', 1_048_577) + "\n\n",
            Encoding.UTF8, "text/event-stream") };
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(new FixtureHandler(_ => oversized)).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
    }

    [Fact]
    public async Task Stream_yields_first_text_before_upstream_completion_and_cancels_blocked_read()
    {
        var first = """
            {"id":"ds_slow","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"first"},"finish_reason":null}],"usage":null}
            """;
        var stream = new TwoPartStream("data: " + first + "\n\n");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = Adapter(new FixtureHandler(_ => response))
            .StreamAsync(Request, Context, cancellation.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("first", enumerator.Current.Text);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Stream_rejects_duplicate_terminal_usage()
    {
        var finish = """
            {"id":"ds_dup","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"answer"},"finish_reason":"stop"}],"usage":null}
            """;
        var usage = """
            {"id":"ds_dup","object":"chat.completion.chunk","model":"deepseek-flash","choices":[],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":2,"total_tokens":6}}
            """;
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(new FixtureHandler(_ => StreamResponse(finish, usage, usage)))
                .StreamAsync(Request, Context)) events.Add(item);
        });
        Assert.Equal([ProviderStreamKind.TextDelta], events.Select(value => value.Kind));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
    }

    [Fact]
    public async Task Stream_timeout_after_partial_text_is_unknown_and_never_finishes()
    {
        var first = """
            {"id":"ds_timeout","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":null}],"usage":null}
            """;
        var upstream = new TwoPartStream("data: " + first + "\n\n");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(upstream) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(new FixtureHandler(_ => response))
                .StreamAsync(Request, Context with { Timeout = TimeSpan.FromMilliseconds(150) }))
                events.Add(item);
        });
        Assert.Equal([ProviderStreamKind.TextDelta], events.Select(value => value.Kind));
        Assert.Equal(ProviderErrorCategory.Timeout, error.Error.Category);
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.FallbackEligible);
    }

    [Fact]
    public async Task Stream_content_filter_emits_safe_refusal_and_verified_usage()
    {
        var response = StreamResponse("""
            {"id":"ds_refuse","object":"chat.completion.chunk","model":"deepseek-flash","choices":[{"index":0,"delta":{"content":null},"finish_reason":"content_filter"}],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":0,"total_tokens":4}}
            """);
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => response)).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.Refusal, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], events.Select(value => value.Kind));
        Assert.Equal("Provider refused the response.", events[0].Text);
        Assert.Equal("content_filter", events[1].FinishReason);
        Assert.Equal(new ProviderUsage(4, 0, 1, null), events[2].Usage);
    }

    private static async Task<List<ProviderStreamEvent>> CollectAsync(IAsyncEnumerable<ProviderStreamEvent> stream)
    {
        var events = new List<ProviderStreamEvent>();
        await foreach (var item in stream) events.Add(item);
        return events;
    }

    private static HttpResponseMessage StreamResponse(params string[] chunks) => StreamResponse(true, chunks);

    private static HttpResponseMessage StreamResponse(bool done, params string[] chunks)
    {
        var payload = ": keep-alive\n\n" + string.Concat(chunks.Select(chunk => "data: " + chunk + "\n\n"))
            + (done ? "data: [DONE]\n\n" : "");
        return new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
    }

    private static DeepSeekChatAdapter Adapter(FixtureHandler handler, string? secret = "secret-value") =>
        new(new HttpClient(handler) { BaseAddress = DeepSeekAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan }, new CredentialResolver(secret),
            DeepSeekAdapterOptions.Production);

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_header");
        return response;
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
        public string? Authorization, ClientRequestId, Path, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class AsyncFixtureHandler(Func<CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        public bool CancellationObserved;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try { return await response(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { CancellationObserved = true; throw; }
        }
    }

    private sealed class TwoPartStream(string firstPart) : Stream
    {
        private readonly byte[] first = Encoding.UTF8.GetBytes(firstPart);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool firstSent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!firstSent)
            {
                firstSent = true;
                first.CopyTo(buffer);
                return first.Length;
            }
            await release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
