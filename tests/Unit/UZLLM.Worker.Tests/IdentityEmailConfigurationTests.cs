using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
