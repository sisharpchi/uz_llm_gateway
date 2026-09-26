using System.Net;
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
    public async Task Missing_credentials_unsupported_features_and_stream_never_dispatch()
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
        Assert.False(Adapter(handler).Supports(Request, true));
        var stream = await Assert.ThrowsAsync<ProviderExecutionException>(async () =>
        { await foreach (var _ in Adapter(handler).StreamAsync(Request, Context)) { } });
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

    private sealed class CredentialResolver(string? secret) : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId,
            CancellationToken cancellationToken = default) => Task.FromResult(secret);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int Calls;
        public string? ApiKey, ClientRequestId, Path, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
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
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
