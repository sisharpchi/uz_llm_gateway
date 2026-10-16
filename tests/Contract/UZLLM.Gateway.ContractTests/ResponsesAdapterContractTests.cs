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

    private sealed class FixtureCredentials : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("synthetic-adapter-fixture-key");
    }

    private sealed class NativeFixture(string json) : HttpMessageHandler
    {
        public int Calls;
        public string? RequestBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("x-request-id", "fixture-request-id");
            response.Headers.TryAddWithoutValidation("request-id", "fixture-request-id");
            return response;
        }
    }
}
