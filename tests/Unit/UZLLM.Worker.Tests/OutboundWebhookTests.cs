using System.Net;
using System.Text;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Worker.Tests;

public sealed class OutboundWebhookTests
{
    [Theory]
    [InlineData("http://alerts.example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://user:pass@alerts.example.com/hook")]
    [InlineData("https://alerts.example.com:8443/hook")]
    [InlineData("https://alerts.example.com/hook?token=secret")]
    [InlineData("https://alerts.example.com/hook#fragment")]
    [InlineData("https://[::1]/hook")]
    public void Endpoint_rejects_non_https_or_ambiguous_targets(string endpoint) =>
        Assert.Throws<ArgumentException>(() => WebhookEndpointPolicy.Parse(endpoint));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.18.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("203.0.113.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("192.88.99.1")]
    public void Endpoint_rejects_private_special_and_documentation_addresses(string address) =>
        Assert.False(WebhookEndpointPolicy.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task Mixed_dns_answer_is_rejected_and_sender_rechecks_dns_before_delivery()
    {
        var resolver = new TestResolver(IPAddress.Parse("1.1.1.1"));
        var policy = new WebhookEndpointPolicy(resolver);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), await policy.ResolvePublicAsync("alerts.example.com", default));
        resolver.Addresses = [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("127.0.0.1")];
        await Assert.ThrowsAsync<ArgumentException>(() => policy.ResolvePublicAsync("alerts.example.com", default));
        using var client = new HttpClient(new TestTransport(HttpStatusCode.OK));
        var sender = new HttpOutboundWebhookSender(client, policy, TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync(
            new Uri("https://alerts.example.com/hook"), new byte[32], Guid.CreateVersion7(), [], default));
    }

    [Fact]
    public async Task Sender_signs_exact_body_and_receiver_rejects_tampering_stale_time_and_bad_signature()
    {
        var transport = new TestTransport(HttpStatusCode.NoContent);
        using var client = new HttpClient(transport);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var sender = new HttpOutboundWebhookSender(client,
            new WebhookEndpointPolicy(new TestResolver(IPAddress.Parse("1.1.1.1"))), clock);
        var secret = Enumerable.Repeat((byte)7, 32).ToArray();
        var eventId = Guid.CreateVersion7();
        var body = Encoding.UTF8.GetBytes("{\"type\":\"customer.alert.triggered\"}");
        await sender.SendAsync(new Uri("https://alerts.example.com/hook"), secret, eventId, body, default);
        Assert.Equal("https://alerts.example.com/hook", transport.Url);
        Assert.Equal("application/json", transport.ContentType);
        Assert.Equal(body, transport.Body);
        Assert.Equal(eventId.ToString("D"), transport.EventId);
        var timestamp = long.Parse(transport.Timestamp!);
        Assert.True(OutboundWebhookSignature.Verify(secret, eventId, timestamp, body,
            transport.Signature!, clock.GetUtcNow()));
        Assert.False(OutboundWebhookSignature.Verify(secret, eventId, timestamp,
            Encoding.UTF8.GetBytes("{}"), transport.Signature!, clock.GetUtcNow()));
        Assert.False(OutboundWebhookSignature.Verify(secret, Guid.CreateVersion7(), timestamp,
            body, transport.Signature!, clock.GetUtcNow()));
        Assert.False(OutboundWebhookSignature.Verify(secret, eventId, timestamp,
            body, transport.Signature!, clock.GetUtcNow().AddMinutes(6)));
        Assert.False(OutboundWebhookSignature.Verify(secret, eventId, timestamp,
            body, "v1=" + new string('0', 64), clock.GetUtcNow()));
        var originalSignature = transport.Signature;
        clock.Advance(TimeSpan.FromSeconds(10));
        await sender.SendAsync(new Uri("https://alerts.example.com/hook"), secret, eventId, body, default);
        Assert.Equal(eventId.ToString("D"), transport.EventId);
        Assert.Equal(body, transport.Body);
        Assert.NotEqual(timestamp, long.Parse(transport.Timestamp!));
        Assert.NotEqual(originalSignature, transport.Signature);
        Assert.True(OutboundWebhookSignature.Verify(secret, eventId, long.Parse(transport.Timestamp!),
            body, transport.Signature!, clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(OutboundWebhookPermanentFailureException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(OutboundWebhookPermanentFailureException))]
    [InlineData(HttpStatusCode.Redirect, typeof(OutboundWebhookPermanentFailureException))]
    public async Task Sender_retries_only_transient_responses(HttpStatusCode code, Type expected)
    {
        using var client = new HttpClient(new TestTransport(code));
        var sender = new HttpOutboundWebhookSender(client,
            new WebhookEndpointPolicy(new TestResolver(IPAddress.Parse("1.1.1.1"))), TimeProvider.System);
        var failure = await Record.ExceptionAsync(() => sender.SendAsync(
            new Uri("https://alerts.example.com/hook"), new byte[32], Guid.CreateVersion7(), [], default));
        Assert.NotNull(failure);
        Assert.IsType(expected, failure);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(5, 160)]
    [InlineData(10, 3600)]
    public void Webhook_retries_back_off_without_changing_other_outbox_work(int attempt, int seconds)
    {
        var webhook = new OutboxMessage(Guid.CreateVersion7(), "customer.alert.webhook", "{}",
            DateTimeOffset.UtcNow, attempt, 10);
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxDispatchCycle.RetryDelayFor(webhook,
            TimeSpan.FromSeconds(10)));
        var ordinary = webhook with { EventType = "customer.alert.telegram" };
        Assert.Equal(TimeSpan.FromSeconds(10), OutboxDispatchCycle.RetryDelayFor(ordinary,
            TimeSpan.FromSeconds(10)));
    }

    private sealed class TestResolver(params IPAddress[] addresses) : IWebhookAddressResolver
    {
        public IPAddress[] Addresses { get; set; } = addresses;
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(Addresses);
    }

    private sealed class MutableClock(DateTimeOffset instant) : TimeProvider
    {
        private DateTimeOffset current = instant;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan delta) => current += delta;
    }

    private sealed class TestTransport(HttpStatusCode responseCode) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? ContentType { get; private set; }
        public byte[]? Body { get; private set; }
        public string? EventId { get; private set; }
        public string? Timestamp { get; private set; }
        public string? Signature { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            EventId = request.Headers.GetValues("X-UZLLM-Event-Id").Single();
            Timestamp = request.Headers.GetValues("X-UZLLM-Timestamp").Single();
            Signature = request.Headers.GetValues("X-UZLLM-Signature").Single();
            return new HttpResponseMessage(responseCode);
        }
    }
}
