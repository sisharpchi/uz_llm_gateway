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
    public async Task Unsupported_vision_tools_schema_stream_and_missing_credentials_do_not_dispatch()
    {
        var handler = new FixtureHandler(_ => throw new InvalidOperationException("Must not dispatch"));
        var adapter = Adapter(handler);
        Assert.False(adapter.Supports(Request with { Messages = [new ProviderMessage("user", [
            new ProviderContentPart(ProviderContentKind.ImageUrl, "https://example.com/a.png")])] }, false));
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        Assert.False(adapter.Supports(Request with { ResponseFormat = new ProviderResponseFormat(
            ProviderResponseFormatKind.JsonSchema, "answer", schema.RootElement.Clone()) }, false));
        Assert.False(adapter.Supports(Request, true));
        Assert.False(adapter.Supports(Request with { Tools = [new ProviderToolDefinition(
            "lookup", null, schema.RootElement.Clone())] }, false));
        var missing = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            Adapter(handler, null).CompleteAsync(Request, Context));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, missing.Error.Certainty);
        var stream = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in adapter.StreamAsync(Request, Context)) { } });
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
}
