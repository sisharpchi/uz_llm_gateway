using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.Anthropic;
using UZLLM.Provider.DeepSeek;
using UZLLM.Provider.Google;
using UZLLM.Provider.OpenAI;

namespace UZLLM.Gateway.ContractTests;

public sealed class ResponsesAdapterContractTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("google")]
    [InlineData("deepseek")]
    public async Task Responses_text_translates_through_real_native_adapter_and_response_writer(string provider)
    {
        var parsed = ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes("""
            {"model":"example-chat","instructions":"Be concise","input":"Hello","max_output_tokens":32,"store":false}
            """));
        var nativeJson = provider switch
        {
            "anthropic" => """{"id":"upstream-id","type":"message","role":"assistant","model":"upstream-model","content":[{"type":"text","text":"Hello"}],"stop_reason":"end_turn","usage":{"input_tokens":2,"output_tokens":1}}""",
            "google" => """{"responseId":"upstream-id","candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":2,"cachedContentTokenCount":0,"candidatesTokenCount":1,"thoughtsTokenCount":0,"totalTokenCount":3}}""",
            _ => """{"id":"upstream-id","object":"chat.completion","model":"upstream-model","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":2,"prompt_cache_hit_tokens":0,"prompt_cache_miss_tokens":2,"completion_tokens":1,"total_tokens":3}}"""
        };
        using var handler = new NativeFixture(nativeJson);
        var address = provider switch {
            "openai" => OpenAiAdapterOptions.Production.BaseAddress,
            "anthropic" => AnthropicAdapterOptions.Production.BaseAddress,
            "google" => GoogleAdapterOptions.Production.BaseAddress,
            _ => DeepSeekAdapterOptions.Production.BaseAddress };
        using var client = new HttpClient(handler) { BaseAddress = address, Timeout = Timeout.InfiniteTimeSpan };
        var credentials = new FixtureCredentials();
        ILlmProviderAdapter adapter = provider switch
        {
            "openai" => new OpenAiChatAdapter(client, credentials, OpenAiAdapterOptions.Production),
            "anthropic" => new AnthropicMessagesAdapter(client, credentials, AnthropicAdapterOptions.Production),
            "google" => new GoogleContentAdapter(client, credentials, GoogleAdapterOptions.Production),
            _ => new DeepSeekChatAdapter(client, credentials, DeepSeekAdapterOptions.Production)
        };
        var requestId = Guid.NewGuid();
        Assert.True(adapter.Supports(parsed.ProviderRequest, false));
        var completion = await adapter.CompleteAsync(parsed.ProviderRequest, new(requestId,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "upstream-model", TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("Be concise", handler.RequestBody!);
        using var nativeRequest = JsonDocument.Parse(handler.RequestBody!);
        var nativeLimit = provider switch { "openai" => "max_completion_tokens", "anthropic" or "deepseek" => "max_tokens", _ => "generationConfig" };
        Assert.Equal(32, provider == "google"
            ? nativeRequest.RootElement.GetProperty(nativeLimit).GetProperty("maxOutputTokens").GetInt32()
            : nativeRequest.RootElement.GetProperty(nativeLimit).GetInt32());
        Assert.False(nativeRequest.RootElement.TryGetProperty("input", out _));
        Assert.False(nativeRequest.RootElement.TryGetProperty("uzllm", out _));
        Assert.Equal(2, completion.Usage!.InputTokens);
        Assert.Equal(1, completion.Usage.OutputTokens);
        Assert.NotNull(completion.ProviderRequestId);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await new OpenAiResponsesWriter(context, parsed).WriteCompletionAsync(completion, "example-chat", requestId, CancellationToken.None);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("response", response.RootElement.GetProperty("object").GetString());
        Assert.Equal("completed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal("Hello", response.RootElement.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(3, response.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt32());
    }

    [Theory]
    [InlineData("openai", false)]
    [InlineData("anthropic", false)]
    [InlineData("google", false)]
    [InlineData("deepseek", false)]
    [InlineData("openai", true)]
    [InlineData("anthropic", true)]
    [InlineData("google", true)]
    [InlineData("deepseek", true)]
    public async Task Responses_SSE_native_adapters_translate_incrementally_and_reject_truncated_protocol(string provider, bool truncated)
    {
        var parsed = ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes("""{"model":"m","input":"Hello","stream":true,"max_output_tokens":32}"""));
        var first = provider switch {
            "anthropic" => "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":2,\"output_tokens\":0}}}\n\n"
                + "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n\n",
            "google" => "data: {\"responseId\":\"up\",\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hello\"}]}}]}\n\n",
            _ => "data: {\"id\":\"up\",\"model\":\"model\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n" };
        var last = provider switch {
            "anthropic" => "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":1}}\n\n"
                + "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n",
            "google" => "data: {\"responseId\":\"up\",\"candidates\":[{\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":2,\"candidatesTokenCount\":1,\"totalTokenCount\":3}}\n\n",
            _ => "data: {\"id\":\"up\",\"model\":\"model\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
                + "data: {\"id\":\"up\",\"model\":\"model\",\"object\":\"chat.completion.chunk\",\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"prompt_cache_hit_tokens\":0,\"prompt_cache_miss_tokens\":2,\"completion_tokens\":1,\"total_tokens\":3}}\n\ndata: [DONE]\n\n" };
        using var handler = new NativeFixture(first + (truncated ? "" : last), true);
        var address = provider switch { "openai" => OpenAiAdapterOptions.Production.BaseAddress,
            "anthropic" => AnthropicAdapterOptions.Production.BaseAddress, "google" => GoogleAdapterOptions.Production.BaseAddress,
            _ => DeepSeekAdapterOptions.Production.BaseAddress };
        using var client = new HttpClient(handler) { BaseAddress = address, Timeout = Timeout.InfiniteTimeSpan };
        var credentials = new FixtureCredentials();
        ILlmProviderAdapter adapter = provider switch {
            "openai" => new OpenAiChatAdapter(client, credentials, new(client.BaseAddress)),
            "anthropic" => new AnthropicMessagesAdapter(client, credentials, new(client.BaseAddress)),
            "google" => new GoogleContentAdapter(client, credentials, new(client.BaseAddress)),
            _ => new DeepSeekChatAdapter(client, credentials, new(client.BaseAddress)) };
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var writer = new OpenAiResponsesWriter(context, parsed); var id = Guid.NewGuid();
        async Task ExecuteAsync()
        {
            await foreach (var item in adapter.StreamAsync(parsed.ProviderRequest,
                new(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "model", TimeSpan.FromSeconds(5))))
            {
                if (item.Kind == ProviderStreamKind.Finish)
                    Assert.Contains("response.output_text.delta", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
                await writer.WriteStreamEventAsync(item, "m", id, CancellationToken.None);
            }
            writer.ValidateStreamEnd();
        }
        if (truncated)
        {
            var error = await Assert.ThrowsAsync<ProviderExecutionException>(ExecuteAsync);
            Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
            await writer.WriteStreamErrorAsync("Provider stream failed.", "provider_error", CancellationToken.None);
        }
        else { await ExecuteAsync(); await writer.FinishStreamAsync(CancellationToken.None); }
        var events = GatewayExecutionTests.ResponsesEvents(Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Contains(events, value => value.GetProperty("type").GetString() == "response.output_text.delta");
        Assert.Equal(truncated ? "response.failed" : "response.completed", events[^1].GetProperty("type").GetString());
        if (!truncated) Assert.Equal(3, events[^1].GetProperty("response").GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.True(adapter.Supports(parsed.ProviderRequest, true));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class FixtureCredentials : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("synthetic-adapter-fixture-key");
    }

    private sealed class NativeFixture(string json, bool stream = false) : HttpMessageHandler
    {
        public int Calls;
        public string? RequestBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
            response.Headers.TryAddWithoutValidation("x-request-id", "fixture-request-id");
            response.Headers.TryAddWithoutValidation("request-id", "fixture-request-id");
            return response;
        }
    }
}
