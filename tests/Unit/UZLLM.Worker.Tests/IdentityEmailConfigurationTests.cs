using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Worker.Tests;

public sealed class IdentityEmailConfigurationTests
{
    [Fact]
    public void Smtp_options_require_host_sender_and_paired_credentials()
    {
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config()));
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:Username", "mailer"))));
        Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:SmtpPort", "0"))));

        var valid = IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:Username", "mailer"), ("Email:Password", "secret")));
        Assert.Equal(587, valid.Port);
        Assert.Equal("smtp.example.uz", valid.Host);
    }

    [Fact]
    public void Verification_and_recovery_email_links_keep_tokens_in_fragments()
    {
        var options = IdentitySmtpOptions.FromConfiguration(Config(
            ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
            ("Email:DashboardBaseUrl", "https://app.example.uz")));
        var expiry = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var verification = SmtpIdentityEmailSender.BuildBody(options,
            new IdentityEmailNotification("person@example.uz", "a+/=", IdentityEmailKind.Verification, expiry));
        var recovery = SmtpIdentityEmailSender.BuildBody(options,
            new IdentityEmailNotification("person@example.uz", "reset-token", IdentityEmailKind.PasswordRecovery, expiry));
        Assert.Contains("https://app.example.uz/verify-email#token=a%2B%2F%3D", verification);
        Assert.Contains("https://app.example.uz/reset-password#token=reset-token", recovery);
        Assert.DoesNotContain("?token=", verification);
        Assert.DoesNotContain("?token=", recovery);
    }

    [Fact]
    public void Dashboard_link_configuration_rejects_insecure_remote_or_embedded_credentials()
    {
        foreach (var url in new[] { "http://app.example.uz", "https://user:pass@app.example.uz",
            "https://app.example.uz?token=bad", "https://app.example.uz#fragment" })
            Assert.Throws<InvalidOperationException>(() => IdentitySmtpOptions.FromConfiguration(Config(
                ("Email:SmtpHost", "smtp.example.uz"), ("Email:FromAddress", "noreply@example.uz"),
                ("Email:DashboardBaseUrl", url))));
    }

    [Fact]
    public void Delivery_registration_includes_team_invitation_outbox_handler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConsumerInboxStore, NoopInbox>();
        services.AddUzllmIdentityEmailDelivery(Config(
            ("Email:SmtpHost", "smtp.example.uz"),
            ("Email:FromAddress", "noreply@example.uz")));
        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<IOutboxHandler>(),
            handler => handler.EventType == IdentityEmailEventTypes.TeamInvitation);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(
            value => value.Key, value => (string?)value.Value)).Build();

    private sealed class NoopInbox : IConsumerInboxStore
    {
        public Task<bool> HasProcessedAsync(string consumer, Guid eventId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> TryRecordProcessedAsync(string consumer, Guid eventId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
