using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Worker.Tests;

public sealed class OperationalAlertDeliveryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Operator <oncall@example.uz>")]
    [InlineData("not-an-email")]
    public void Recipient_configuration_rejects_missing_or_non_mailbox_values(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Operations:AlertEmail"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => OperationalAlertRecipientOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void Recipient_configuration_accepts_one_mailbox_address()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Operations:AlertEmail"] = "oncall@example.uz" }).Build();
        Assert.Equal("oncall@example.uz", OperationalAlertRecipientOptions.FromConfiguration(configuration).Email);
    }

    [Fact]
    public void Smtp_message_contains_stable_id_and_no_alert_details()
    {
        var eventId = Guid.CreateVersion7();
        var alertId = Guid.CreateVersion7();
        var alert = new OperationalAlertDelivery(alertId, "LateExternalSpend", "critical",
            DateTimeOffset.Parse("2026-09-28T12:00:00+00:00"), eventId, null, null, "Pending");
        using var message = SmtpOperationalAlertSender.CreateMessage(eventId, alert,
            new IdentitySmtpOptions("smtp.example.uz", 587, "noreply@example.uz", null, null),
            new OperationalAlertRecipientOptions("oncall@example.uz"));
        Assert.Equal("oncall@example.uz", Assert.Single(message.To).Address);
        Assert.Equal("UZLLM critical alert: LateExternalSpend", message.Subject);
        Assert.Contains(alertId.ToString("N"), message.Body);
        Assert.Equal($"<{eventId:N}@example.uz>", message.Headers["Message-ID"]);
        Assert.DoesNotContain("exposureMicroUsd", message.Body);
        Assert.DoesNotContain("DeduplicationKey", message.Body);
    }

    [Fact]
    public void Smtp_message_rejects_unknown_persisted_kind()
    {
        var alert = new OperationalAlertDelivery(Guid.CreateVersion7(), "UnexpectedKind", "critical",
            DateTimeOffset.UtcNow, null, null, null, "Pending");
        Assert.Throws<InvalidOperationException>(() => SmtpOperationalAlertSender.CreateMessage(Guid.CreateVersion7(),
            alert, new IdentitySmtpOptions("smtp.example.uz", 587, "noreply@example.uz", null, null),
            new OperationalAlertRecipientOptions("oncall@example.uz")));
    }
}
