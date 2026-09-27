using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Notifications.Infrastructure;

namespace UZLLM.Worker.Tests;

public sealed class TelegramAlertDeliveryTests
{
    [Fact]
    public async Task Sender_uses_documented_sendMessage_json_and_requires_ok_true()
    {
        var transport = new RecordingTransport(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":7}}""");
        using var client = new HttpClient(transport);
        var sender = new TelegramBotMessageSender(client, Options());
        await sender.SendAsync(12345, "UZLLM low balance");
        Assert.Equal("https://api.telegram.org/bot123456:test_token/sendMessage", transport.Url);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Equal(12345, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("UZLLM low balance", body.RootElement.GetProperty("text").GetString());
        Assert.Equal("application/json", transport.ContentType);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}", typeof(TelegramDestinationUnavailableException))]
    [InlineData(HttpStatusCode.BadRequest, "{}", typeof(TelegramDestinationUnavailableException))]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.InternalServerError, "{}", typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.OK, "{\"ok\":false}", typeof(InvalidOperationException))]
    public async Task Sender_classifies_permanent_destination_and_retryable_transport_failures(
        HttpStatusCode status, string responseBody, Type expected)
    {
        using var client = new HttpClient(new RecordingTransport(status, responseBody));
        var sender = new TelegramBotMessageSender(client, Options());
        var failure = await Record.ExceptionAsync(() => sender.SendAsync(12345, "alert"));
        Assert.NotNull(failure);
        Assert.IsType(expected, failure);
    }

    [Theory]
    [InlineData("Daily", "2026-09-27T00:00:00+00:00")]
    [InlineData("Weekly", "2026-09-21T00:00:00+00:00")]
    [InlineData("Monthly", "2026-09-01T00:00:00+00:00")]
    [InlineData("Lifetime", "1970-01-01T00:00:00+00:00")]
    public void Budget_alert_window_matches_utc_budget_period(string period, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), CustomerAlertEvaluator.WindowStart(period,
            new DateTimeOffset(2026, 9, 27, 14, 30, 0, TimeSpan.Zero)));
    }

    private static TelegramAlertOptions Options() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telegram:BotUsername"] = "UZLLMTestBot",
            ["Telegram:BotToken"] = "123456:test_token",
            ["Telegram:WebhookSecret"] = "test-webhook-secret-0123456789",
            ["Telegram:ActiveChatKeyVersion"] = "v1",
            ["Telegram:ChatKeys:v1"] = Convert.ToBase64String(new byte[32])
        }).Build());

    private sealed class RecordingTransport(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(status)
            { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
