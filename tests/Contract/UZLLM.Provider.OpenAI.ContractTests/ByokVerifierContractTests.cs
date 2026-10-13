using System.Net;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;

namespace UZLLM.Provider.OpenAI.ContractTests;

public sealed class ByokVerifierContractTests
{
    [Theory]
    [InlineData("openai", "https://api.openai.com/v1/models", "Authorization", "Bearer test-secret-123456")]
    [InlineData("anthropic", "https://api.anthropic.com/v1/models", "x-api-key", "test-secret-123456")]
    [InlineData("google", "https://generativelanguage.googleapis.com/v1beta/models", "x-goog-api-key", "test-secret-123456")]
    [InlineData("deepseek", "https://api.deepseek.com/models", "Authorization", "Bearer test-secret-123456")]
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
        Assert.Equal(string.Empty, handler.Uri!.Query);
        Assert.DoesNotContain("test-secret", handler.Uri.AbsoluteUri);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(provider == "anthropic" ? 2 : 1, handler.Headers.Count);
        if (provider == "anthropic")
            Assert.Equal("2023-06-01", handler.Headers["anthropic-version"]);
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task VerifyAsync_classifies_status_without_echoing_provider_body(
        string provider, HttpStatusCode status, ByokTestStatus expected)
    {
        var handler = new RecordingHandler(status);
        var result = await new FixedEndpointByokVerifier(new HttpClient(handler))
            .VerifyAsync(provider, "test-secret-123456");
        Assert.Equal(expected, result);
        Assert.Equal(1, handler.Calls);
    }

    public static IEnumerable<object[]> StatusCases =>
        from provider in new[] { "openai", "anthropic", "google", "deepseek" }
        from status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
            HttpStatusCode.PaymentRequired, HttpStatusCode.TooManyRequests,
            HttpStatusCode.InternalServerError, HttpStatusCode.Redirect }
        select new object[] { provider, status, status is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden ? ByokTestStatus.Invalid : ByokTestStatus.Unavailable };

    [Theory]
    [InlineData("{\"error\":{\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\"}]}}", ByokTestStatus.Invalid)]
    [InlineData("{\"error\":{\"status\":\"FAILED_PRECONDITION\"}}", ByokTestStatus.Unavailable)]
    [InlineData("{\"error\":{\"message\":\"API_KEY_INVALID\"}}", ByokTestStatus.Unavailable)]
    [InlineData("{\"error\":{\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}", ByokTestStatus.Unavailable)]
    [InlineData("{\"error\":{\"details\":[null,\"API_KEY_INVALID\",{\"@type\":5,\"reason\":true}]}}", ByokTestStatus.Unavailable)]
    [InlineData("[]", ByokTestStatus.Unavailable)]
    [InlineData("not-json", ByokTestStatus.Unavailable)]
    public async Task Google_400_requires_structured_invalid_key_evidence(string body, ByokTestStatus expected)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, body);
        var result = await new FixedEndpointByokVerifier(new HttpClient(handler))
            .VerifyAsync("google", "test-secret-123456");
        Assert.Equal(expected, result);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(16384, ByokTestStatus.Invalid)]
    [InlineData(16385, ByokTestStatus.Unavailable)]
    public async Task Google_error_body_is_bounded_and_never_treated_as_valid(int length, ByokTestStatus expected)
    {
        var body = """
            {"error":{"details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID"}]}}
            """.PadRight(length);
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, body);
        Assert.Equal(expected, await new FixedEndpointByokVerifier(new HttpClient(handler))
            .VerifyAsync("google", "test-secret-123456"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Google_error_json_does_not_trust_an_unknown_response_charset()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest,
            """{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID"}]}}""",
            "unknown-provider-charset");
        Assert.Equal(ByokTestStatus.Invalid, await new FixedEndpointByokVerifier(new HttpClient(handler))
            .VerifyAsync("google", "test-secret-123456"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Google_error_body_read_obeys_total_deadline_not_just_header_timeout()
    {
        using var governor = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var verifier = new FixedEndpointByokVerifier(new HttpClient(new StalledBodyHandler()));
        Assert.Equal(ByokTestStatus.Unavailable,
            await verifier.VerifyAsync("google", "test-secret-123456", governor.Token));
        Assert.False(governor.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verification_distinguishes_timeout_from_caller_cancellation(bool callerCancelled)
    {
        using var cancellation = new CancellationTokenSource();
        if (callerCancelled) cancellation.Cancel();
        var verifier = new FixedEndpointByokVerifier(new HttpClient(new CancellationHandler()));
        if (callerCancelled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync("google",
                "test-secret-123456", cancellation.Token));
        else Assert.Equal(ByokTestStatus.Unavailable, await verifier.VerifyAsync("google", "test-secret-123456"));
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

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new TaskCanceledException();
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StalledContent() });
    }

    private sealed class StalledContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body = "provider-body-not-for-client",
        string? charset = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Method = request.Method;
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(" ", header.Value);
            var content = new StringContent(body);
            if (charset is not null) content.Headers.ContentType!.CharSet = charset;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }
}
