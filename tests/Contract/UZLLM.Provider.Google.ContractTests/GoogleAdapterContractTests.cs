using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.Google;

namespace UZLLM.Provider.Google.ContractTests;

public sealed class GoogleAdapterContractTests
{
    private static readonly ProviderExecutionContext Context = new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "gemini-2.5-flash", TimeSpan.FromSeconds(5));
    private static readonly ProviderChatRequest Request = new([
        new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")])
    ], MaxOutputTokens: 100);

    [Fact]
    public async Task Native_generate_content_maps_system_dialogue_json_schema_and_billable_usage()
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"responseId":"resp_01","candidates":[{"content":{"parts":[{"text":"{\"ok\":true}"}],"role":"model"},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":20,"cachedContentTokenCount":8,"candidatesTokenCount":5,"thoughtsTokenCount":3,"totalTokenCount":28}}
            """));
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"ok":{"type":"boolean"}}}""");
        var request = new ProviderChatRequest([
            new ProviderMessage("system", [new ProviderContentPart(ProviderContentKind.Text, "Be precise")]),
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Hello")]),
            new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "Previous")]),
            new ProviderMessage("user", [new ProviderContentPart(ProviderContentKind.Text, "Again")])
        ], MaxOutputTokens: 100, ResponseFormat: new ProviderResponseFormat(
            ProviderResponseFormatKind.JsonSchema, "answer", schema.RootElement.Clone()));

        var completion = await Adapter(handler).CompleteAsync(request, Context);

        Assert.Equal("resp_01", completion.Id);
        Assert.Equal("resp_01", completion.ProviderRequestId);
        Assert.Equal(20, completion.Usage!.InputTokens);
        Assert.Equal(8, completion.Usage.CachedInputTokens);
        Assert.Equal(8, completion.Usage.OutputTokens);
        Assert.Equal(3, completion.Usage.ReasoningTokens);
        Assert.Equal("stop", completion.FinishReason);
        Assert.Equal("{\"ok\":true}", Assert.Single(completion.Message.Content).Value);
        Assert.Equal("secret-value", handler.ApiKey);
        Assert.Equal(Context.RequestId.ToString("N"), handler.ClientRequestId);
        Assert.Equal("/v1beta/models/gemini-2.5-flash:generateContent", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Be precise", body.RootElement.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("model", body.RootElement.GetProperty("contents")[1].GetProperty("role").GetString());
        Assert.Equal(100, body.RootElement.GetProperty("generationConfig")
            .GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig")
            .GetProperty("responseMimeType").GetString());
        Assert.Equal("object", body.RootElement.GetProperty("generationConfig")
            .GetProperty("responseJsonSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Missing_optional_total_and_output_counts_preserve_zero_usage()
    {
        var completion = await Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"responseId":"resp_02","candidates":[{"content":{"parts":[{"text":"Hi"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":4}}
            """))).CompleteAsync(Request, Context);
        Assert.Equal(4, completion.Usage!.InputTokens);
        Assert.Equal(0, completion.Usage.OutputTokens);
    }

    [Fact]
    public async Task Missing_usage_remains_unknown_not_zero_billable_usage()
    {
        var completion = await Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK, """
            {"responseId":"resp_no_usage","candidates":[{"content":{"parts":[{"text":"Hi"}]},"finishReason":"STOP"}]}
            """))).CompleteAsync(Request, Context);
        Assert.Null(completion.Usage);
    }

    [Theory]
    [InlineData(",\"candidates\":[]")]
    [InlineData("")]
    public async Task Prompt_safety_block_is_a_billed_refusal(string candidates)
    {
        var completion = await Adapter(new FixtureHandler(_ => Response(HttpStatusCode.OK,
            "{\"responseId\":\"resp_block\",\"promptFeedback\":{\"blockReason\":\"SAFETY\"}"
            + candidates
            + ",\"usageMetadata\":{\"promptTokenCount\":9,\"totalTokenCount\":9}}")))
            .CompleteAsync(Request, Context);
        Assert.Equal("content_filter", completion.FinishReason);
        Assert.NotNull(completion.Refusal);
        Assert.Equal(9, completion.Usage!.InputTokens);
    }

    [Theory]
    [InlineData("{\"promptTokenCount\":2,\"cachedContentTokenCount\":3,\"candidatesTokenCount\":1}")]
    [InlineData("{\"promptTokenCount\":2,\"candidatesTokenCount\":1,\"totalTokenCount\":2}")]
    [InlineData("{\"promptTokenCount\":2,\"toolUsePromptTokenCount\":1,\"candidatesTokenCount\":1}")]
    [InlineData("{\"promptTokenCount\":-1,\"candidatesTokenCount\":1}")]
    [InlineData("{\"promptTokenCount\":1.5,\"candidatesTokenCount\":1}")]
    public async Task Unsupported_usage_dimensions_are_unknown_not_verified(string usage)
    {
        var handler = new FixtureHandler(_ => Response(HttpStatusCode.OK,
            "{\"responseId\":\"resp\",\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":" + usage + "}"));
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.FallbackEligible);
        Assert.Equal("req_header", error.Error.ProviderRequestId);
    }

    [Theory]
    [InlineData(400, "INVALID_ARGUMENT", ProviderErrorCategory.InvalidRequest, false)]
    [InlineData(401, "UNAUTHENTICATED", ProviderErrorCategory.Authentication, false)]
    [InlineData(402, "PAYMENT_REQUIRED", ProviderErrorCategory.QuotaExhausted, false)]
    [InlineData(429, "RESOURCE_EXHAUSTED", ProviderErrorCategory.RateLimited, true)]
    [InlineData(503, "UNAVAILABLE", ProviderErrorCategory.Capacity, false)]
    [InlineData(504, "DEADLINE_EXCEEDED", ProviderErrorCategory.Timeout, false)]
    public async Task Official_error_shape_preserves_safe_execution_certainty(int status,
        string googleStatus, ProviderErrorCategory category, bool eligible)
    {
        var handler = new FixtureHandler(_ => Response((HttpStatusCode)status,
            "{\"error\":{\"code\":" + status + ",\"status\":\"" + googleStatus
            + "\",\"message\":\"private provider detail\"}}"));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context));
        Assert.Equal(category, exception.Error.Category);
        Assert.Equal(eligible, exception.Error.FallbackEligible);
        Assert.Equal(status >= 500 ? ProviderExecutionCertainty.Unknown
            : ProviderExecutionCertainty.RejectedBeforeExecution, exception.Error.Certainty);
        Assert.DoesNotContain("private provider detail", exception.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_credentials_and_unsupported_features_never_dispatch()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("Must not dispatch"));
        var credential = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler, null).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, credential.Error.Certainty);
        var vision = Request with { Messages = [new ProviderMessage("user", [
            new ProviderContentPart(ProviderContentKind.ImageUrl, "https://example.com/image.png")])] };
        Assert.False(Adapter(handler).Supports(vision, false));
        var invalid = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(vision, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, invalid.Error.Certainty);
        Assert.True(Adapter(handler).Supports(Request, true));
        var stream = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(handler, null).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, stream.Error.Certainty);
        var invalidMapping = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler).CompleteAsync(Request, Context with { UpstreamModelCode = "models/x/unsafe" }));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, invalidMapping.Error.Certainty);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Client_cancellation_propagates_to_upstream_without_reclassification()
    {
        var handler = new AsyncFixtureHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var client = new HttpClient(handler) { BaseAddress = GoogleAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan };
        var adapter = new GoogleContentAdapter(client, new CredentialResolver("secret-value"),
            GoogleAdapterOptions.Production);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.CompleteAsync(Request, Context, cancellation.Token));
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task Official_native_sse_chunks_normalize_text_terminal_finish_and_cumulative_usage()
    {
        var handler = new FixtureHandler(_ => StreamResponse("""
            : provider heartbeat

            data: {"responseId":"resp_stream","candidates":[{"index":0,"content":{"role":"model","parts":[{"text":"Hel"}]}}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":0,"totalTokenCount":12}}

            data: {"responseId":"resp_stream","candidates":[{"index":0,"content":{"role":"model","parts":[{"text":"hidden","thought":true},{"text":"lo"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"cachedContentTokenCount":4,"candidatesTokenCount":3,"thoughtsTokenCount":2,"totalTokenCount":17}}

            """));

        var events = await CollectAsync(Adapter(handler).StreamAsync(Request, Context));

        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.TextDelta,
            ProviderStreamKind.Finish, ProviderStreamKind.Usage], events.Select(item => item.Kind));
        Assert.Equal("Hello", events[0].Text + events[1].Text);
        Assert.Equal("stop", events[2].FinishReason);
        Assert.Equal(12, events[3].Usage!.InputTokens);
        Assert.Equal(5, events[3].Usage!.OutputTokens);
        Assert.Equal(4, events[3].Usage!.CachedInputTokens);
        Assert.Equal(2, events[3].Usage!.ReasoningTokens);
        Assert.All(events, item => Assert.Equal("resp_stream", item.ProviderRequestId));
        Assert.Equal("/v1beta/models/gemini-2.5-flash:streamGenerateContent", handler.Path);
        Assert.Equal("?alt=sse", handler.Query);
        Assert.Equal("text/event-stream", handler.Accept);
        Assert.Equal("secret-value", handler.ApiKey);
    }

    [Fact]
    public async Task Terminal_usage_only_chunk_after_finish_completes_once()
    {
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => StreamResponse("""
            data: {"responseId":"resp_1","candidates":[{"content":{"parts":[{"text":"Hi"}]},"finishReason":"STOP"}]}

            data: {"responseId":"resp_1","usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":2,"totalTokenCount":6}}

            """))).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], events.Select(item => item.Kind));
        Assert.Equal(2, events[2].Usage!.OutputTokens);
    }

    [Fact]
    public async Task Native_prompt_block_stream_is_billed_refusal_without_text()
    {
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => StreamResponse("""
            data: {"responseId":"resp_block","promptFeedback":{"blockReason":"SAFETY"},"usageMetadata":{"promptTokenCount":5,"totalTokenCount":5}}

            """))).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.Refusal, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], events.Select(item => item.Kind));
        Assert.Equal("content_filter", events[1].FinishReason);
        Assert.Equal(5, events[2].Usage!.InputTokens);
    }

    [Theory]
    [InlineData("data: {\"responseId\":\"resp\",\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial\"}]}}]}\n\n")]
    [InlineData("data: {\"responseId\":\"resp\",\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial\"}]}}],\"usageMetadata\":{\"promptTokenCount\":4}}\n\n")]
    public async Task Partial_stream_without_terminal_usage_is_unknown_and_not_replayed(string frames)
    {
        var handler = new FixtureHandler(_ => StreamResponse(frames));
        var events = new List<ProviderStreamEvent>();
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var item in Adapter(handler).StreamAsync(Request, Context)) events.Add(item); });
        Assert.Equal("partial", Assert.Single(events).Text);
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
        Assert.False(exception.Error.FallbackEligible);
        Assert.Equal("resp", exception.Error.ProviderRequestId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task In_band_error_after_text_is_terminal_unknown_event()
    {
        var events = await CollectAsync(Adapter(new FixtureHandler(_ => StreamResponse("""
            data: {"responseId":"resp_error","candidates":[{"content":{"parts":[{"text":"partial"}]}}]}

            event: error
            data: {"error":{"code":503,"status":"UNAVAILABLE","message":"private provider detail"}}

            """))).StreamAsync(Request, Context));
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.Error],
            events.Select(item => item.Kind));
        Assert.Equal(ProviderExecutionCertainty.Unknown, events[1].Error!.Certainty);
        Assert.False(events[1].Error!.FallbackEligible);
        Assert.DoesNotContain("private provider detail", events[1].Error!.SafeMessage);
    }

    [Theory]
    [InlineData(429, ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution)]
    [InlineData(503, ProviderErrorCategory.Capacity, ProviderExecutionCertainty.Unknown)]
    public async Task Stream_http_rejection_preserves_execution_certainty(int status,
        ProviderErrorCategory category, ProviderExecutionCertainty certainty)
    {
        var handler = new FixtureHandler(_ => Response((HttpStatusCode)status,
            "{\"error\":{\"code\":" + status + ",\"status\":\"RESOURCE_EXHAUSTED\"}}"));
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(handler).StreamAsync(Request, Context)) { } });
        Assert.Equal(category, exception.Error.Category);
        Assert.Equal(certainty, exception.Error.Certainty);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task First_text_chunk_is_delivered_before_upstream_stream_completes()
    {
        using var stream = new TwoStageStream("""
            data: {"responseId":"resp_prompt","candidates":[{"content":{"parts":[{"text":"first"}]}}]}

            """, """
            data: {"responseId":"resp_prompt","candidates":[{"content":{"parts":[{"text":" last"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":2,"totalTokenCount":6}}

            """);
        var handler = new FixtureHandler(_ => StreamResponse(stream));
        await using var enumerator = Adapter(handler).StreamAsync(Request, Context).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("first", enumerator.Current.Text);
        Assert.False(stream.Released);

        stream.Release();
        var remaining = new List<ProviderStreamEvent>();
        while (await enumerator.MoveNextAsync()) remaining.Add(enumerator.Current);
        Assert.Equal([ProviderStreamKind.TextDelta, ProviderStreamKind.Finish,
            ProviderStreamKind.Usage], remaining.Select(item => item.Kind));
    }

    [Fact]
    public async Task Timeout_after_partial_stream_is_unknown_and_preserves_upstream_id()
    {
        using var stream = new TwoStageStream("""
            data: {"responseId":"resp_timeout","candidates":[{"content":{"parts":[{"text":"partial"}]}}]}

            """, "");
        var handler = new FixtureHandler(_ => StreamResponse(stream));
        var events = new List<ProviderStreamEvent>();
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        {
            await foreach (var item in Adapter(handler).StreamAsync(Request,
                Context with { Timeout = TimeSpan.FromMilliseconds(200) })) events.Add(item);
        });
        Assert.Equal("partial", Assert.Single(events).Text);
        Assert.Equal(ProviderErrorCategory.Timeout, exception.Error.Category);
        Assert.Equal(ProviderExecutionCertainty.Unknown, exception.Error.Certainty);
        Assert.Equal("resp_timeout", exception.Error.ProviderRequestId);
        Assert.False(exception.Error.FallbackEligible);
    }

    [Fact]
    public async Task Client_disconnect_cancels_upstream_without_fabricating_terminal_usage()
    {
        using var stream = new TwoStageStream("""
            data: {"responseId":"resp_disconnect","candidates":[{"content":{"parts":[{"text":"partial"}]}}]}

            """, "");
        using var cancellation = new CancellationTokenSource();
        var handler = new FixtureHandler(_ => StreamResponse(stream));
        await using var enumerator = Adapter(handler).StreamAsync(Request, Context,
            cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial", enumerator.Current.Text);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enumerator.MoveNextAsync().AsTask());
        Assert.False(stream.Released);
    }

    [Theory]
    [InlineData("{\"responseId\":\"changed\",\"candidates\":[{\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":4,\"candidatesTokenCount\":2,\"totalTokenCount\":6}}")]
    [InlineData("{\"responseId\":\"resp\",\"candidates\":[{\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":4,\"candidatesTokenCount\":1,\"totalTokenCount\":5}}")]
    public async Task Changed_response_id_or_regressed_usage_is_unknown(string terminal)
    {
        var frames = """
            data: {"responseId":"resp","candidates":[{"content":{"parts":[{"text":"partial"}]}}],"usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":2,"totalTokenCount":6}}

            """ + "\n\ndata: " + terminal + "\n\n";
        var events = new List<ProviderStreamEvent>();
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var item in Adapter(new FixtureHandler(_ => StreamResponse(frames)))
            .StreamAsync(Request, Context)) events.Add(item); });
        Assert.Equal("partial", Assert.Single(events).Text);
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.Equal("resp", error.Error.ProviderRequestId);
    }

    [Fact]
    public async Task Oversized_sse_line_is_bounded_and_unknown()
    {
        var handler = new FixtureHandler(_ => StreamResponse("data: "
            + new string('a', 1_048_577) + "\n\n"));
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(handler).StreamAsync(Request, Context)) { } });
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
    }

    private static async Task<List<ProviderStreamEvent>> CollectAsync(IAsyncEnumerable<ProviderStreamEvent> stream)
    {
        var items = new List<ProviderStreamEvent>();
        await foreach (var item in stream) items.Add(item);
        return items;
    }

    private static GoogleContentAdapter Adapter(FixtureHandler handler, string? secret = "secret-value") =>
        new(new HttpClient(handler) { BaseAddress = GoogleAdapterOptions.Production.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan }, new CredentialResolver(secret),
            GoogleAdapterOptions.Production);

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_header");
        return response;
    }

    private static HttpResponseMessage StreamResponse(string frames)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(frames, Encoding.UTF8, "text/event-stream") };
        response.Headers.TryAddWithoutValidation("x-request-id", "req_header");
        return response;
    }

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
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
        public string? ApiKey, ClientRequestId, Path, Query, Accept, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            Path = request.RequestUri?.AbsolutePath;
            Query = request.RequestUri?.Query;
            Accept = request.Headers.Accept.SingleOrDefault()?.MediaType;
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
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class TwoStageStream(string first, string second) : Stream
    {
        private readonly MemoryStream firstPart = new(Encoding.UTF8.GetBytes(first + "\n\n"));
        private readonly MemoryStream secondPart = new(Encoding.UTF8.GetBytes(second + "\n\n"));
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Released => release.Task.IsCompleted;
        public void Release() => release.TrySetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await firstPart.ReadAsync(buffer, cancellationToken);
            if (read != 0) return read;
            await release.Task.WaitAsync(cancellationToken);
            return await secondPart.ReadAsync(buffer, cancellationToken);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
