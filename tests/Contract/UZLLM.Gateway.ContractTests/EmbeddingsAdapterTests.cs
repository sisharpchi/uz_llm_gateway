using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.OpenAI;

namespace UZLLM.Gateway.ContractTests;

public sealed class EmbeddingsAdapterTests
{
    private const string Valid = """{"object":"list","model":"upstream","data":[{"object":"embedding","index":1,"embedding":[-0.5,0.25]},{"object":"embedding","index":0,"embedding":[0.25,-0.5]}],"usage":{"prompt_tokens":5,"total_tokens":5}}""";

    [Fact]
    public async Task Embeddings_native_request_maps_text_float_credentials_and_reorders_vectors_without_chat()
    {
        using var handler = new Fixture(Valid);
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        var result = await Adapter(client).EmbedAsync(new(["Hello", "World"]), Context());
        Assert.Equal("https://api.openai.com/v1/embeddings", handler.Url);
        Assert.Equal("Bearer fixture-only", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("upstream", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("float", body.RootElement.GetProperty("encoding_format").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("input").GetArrayLength());
        Assert.False(body.RootElement.TryGetProperty("messages", out _));
        Assert.Equal(new[] { 0, 1 }, result.Data.Select(v => v.Index));
        Assert.Equal(new ProviderUsage(5, 0, 0, null), result.Usage);
        Assert.Equal("native-id", result.ProviderRequestId);
        var http = new DefaultHttpContext(); http.Response.Body = new MemoryStream();
        await OpenAiEmbeddingsWriter.WriteAsync(http, result, "canonical", CancellationToken.None);
        http.Response.Body.Position = 0;
        using var wire = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("canonical", wire.RootElement.GetProperty("model").GetString());
        Assert.Equal(5, wire.RootElement.GetProperty("usage").GetProperty("prompt_tokens").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"prompt_tokens\":\"5\",\"total_tokens\":5}")]
    [InlineData("{\"prompt_tokens\":5,\"total_tokens\":null}")]
    [InlineData("{\"prompt_tokens\":5,\"total_tokens\":6}")]
    [InlineData("{\"prompt_tokens\":-1,\"total_tokens\":-1}")]
    [InlineData("{\"prompt_tokens\":0,\"total_tokens\":0}")]
    [InlineData("{\"prompt_tokens\":5,\"total_tokens\":5,\"prompt_tokens\":1}")]
    public async Task Embeddings_invalid_or_missing_usage_remains_unknown(string usage)
    {
        var json = Valid[..Valid.IndexOf(",\"usage\"", StringComparison.Ordinal)] + ",\"usage\":" + usage + "}";
        using var handler = new Fixture(json);
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        var result = await Adapter(client).EmbedAsync(new(["Hello", "World"]), Context());
        Assert.Null(result.Usage);
        Assert.Equal(2, result.Data.Count);
    }

    [Theory]
    [InlineData("index\":1", "index\":0")]
    [InlineData("index\":1", "index\":2")]
    [InlineData("[-0.5,0.25]", "null")]
    [InlineData("[-0.5,0.25]", "[]")]
    [InlineData("[-0.5,0.25]", "[0.1]")]
    [InlineData("[-0.5,0.25]", "\"YmFk\"")]
    [InlineData("[-0.5,0.25]", "[1e100,0]")]
    [InlineData("\"model\":\"upstream\"", "\"model\":\"other\"")]
    public async Task Embeddings_malformed_vector_response_is_unknown_and_not_retryable(string from, string to)
    {
        using var handler = new Fixture(Valid.Replace(from, to, StringComparison.Ordinal));
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() => Adapter(client).EmbedAsync(new(["Hello", "World"]), Context()));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.FallbackEligible);
        Assert.Equal("native-id", error.Error.ProviderRequestId);
    }

    [Theory]
    [InlineData(429, ProviderExecutionCertainty.RejectedBeforeExecution)]
    [InlineData(500, ProviderExecutionCertainty.Unknown)]
    public async Task Embeddings_http_errors_reuse_execution_certainty_not_status_only_retries(int status, ProviderExecutionCertainty certainty)
    {
        using var handler = new Fixture("""{"error":{"code":"rate_limit_exceeded"}}""", (HttpStatusCode)status);
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() => Adapter(client).EmbedAsync(new(["Hello"]), Context()));
        Assert.Equal(certainty, error.Error.Certainty);
        Assert.Equal(status, error.Error.ProviderStatusCode);
        Assert.Equal("native-id", error.Error.ProviderRequestId);
    }

    [Fact]
    public async Task Embeddings_cardinality_mismatch_does_not_return_a_partial_batch()
    {
        using var handler = new Fixture("""{"object":"list","model":"upstream","data":[],"usage":{"prompt_tokens":5,"total_tokens":5}}""");
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        var error = await Assert.ThrowsAsync<ProviderExecutionException>(() => Adapter(client).EmbedAsync(new(["Hello"]), Context()));
        Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
        Assert.False(error.Error.Retryable);
    }

    private static OpenAiChatAdapter Adapter(HttpClient client) => new(client, new Credentials(), OpenAiAdapterOptions.Production);

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    public async Task Embeddings_vector_dimension_bound_is_enforced_without_truncation(int dimensions)
    {
        var json = JsonSerializer.Serialize(new { @object = "list", model = "upstream",
            data = new[] { new { @object = "embedding", index = 0, embedding = Enumerable.Repeat(0.25f, dimensions).ToArray() } },
            usage = new { prompt_tokens = 2, total_tokens = 2 } });
        using var handler = new Fixture(json);
        using var client = new HttpClient(handler) { BaseAddress = OpenAiAdapterOptions.Production.BaseAddress };
        if (dimensions == 4096)
        {
            var result = await Adapter(client).EmbedAsync(new(["Hello"]), Context());
            Assert.Equal(4096, Assert.Single(result.Data).Values.Count);
            Assert.Equal(2, result.Usage!.InputTokens);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ProviderExecutionException>(() => Adapter(client).EmbedAsync(new(["Hello"]), Context()));
            Assert.Equal(ProviderExecutionCertainty.Unknown, error.Error.Certainty);
            Assert.False(error.Error.FallbackEligible);
        }
    }
    private static ProviderExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "upstream", TimeSpan.FromSeconds(5));
    private sealed class Credentials : IProviderCredentialResolver
    {
        public Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("fixture-only");
    }
    private sealed class Fixture(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body, Url, Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization!.ToString();
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "native-id");
            return response;
        }
    }
}
