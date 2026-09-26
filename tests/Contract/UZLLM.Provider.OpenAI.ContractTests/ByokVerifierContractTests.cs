using System.Net;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;

namespace UZLLM.Provider.OpenAI.ContractTests;

public sealed class ByokVerifierContractTests
{
    [Theory]
    [InlineData("openai", "https://api.openai.com/v1/models", "Authorization", "Bearer test-secret-123456")]
    [InlineData("anthropic", "https://api.anthropic.com/v1/models", "x-api-key", "test-secret-123456")]
    public async Task VerifyAsync_uses_only_fixed_provider_model_endpoint_and_auth_header(
        string provider, string expectedUri, string header, string expectedValue)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var verifier = new FixedEndpointByokVerifier(new HttpClient(handler));

        var result = await verifier.VerifyAsync(provider, "test-secret-123456");

        Assert.Equal(ByokTestStatus.Valid, result);
        Assert.Equal(expectedUri, handler.Uri?.ToString());
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(expectedValue, handler.Headers[header]);
        if (provider == "anthropic")
            Assert.Equal("2023-06-01", handler.Headers["anthropic-version"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ByokTestStatus.Invalid)]
    [InlineData(HttpStatusCode.Forbidden, ByokTestStatus.Invalid)]
    [InlineData(HttpStatusCode.TooManyRequests, ByokTestStatus.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ByokTestStatus.Unavailable)]
    [InlineData(HttpStatusCode.Redirect, ByokTestStatus.Unavailable)]
    public async Task VerifyAsync_classifies_status_without_echoing_provider_body(
        HttpStatusCode status, ByokTestStatus expected)
    {
        var handler = new RecordingHandler(status);
        var result = await new FixedEndpointByokVerifier(new HttpClient(handler))
            .VerifyAsync("openai", "test-secret-123456");
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task VerifyAsync_rejects_unknown_provider_before_any_network_call()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var verifier = new FixedEndpointByokVerifier(new HttpClient(handler));
        await Assert.ThrowsAsync<ArgumentException>(() => verifier.VerifyAsync(
            "https://attacker.invalid", "test-secret-123456"));
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task VerifyAsync_maps_network_failure_to_unavailable_without_disclosing_secret()
    {
        var verifier = new FixedEndpointByokVerifier(new HttpClient(new FailureHandler()));
        Assert.Equal(ByokTestStatus.Unavailable,
            await verifier.VerifyAsync("openai", "test-secret-123456"));
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated network outage");
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Method = request.Method;
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(" ", header.Value);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("provider-body-not-for-client")
            });
        }
    }
}
