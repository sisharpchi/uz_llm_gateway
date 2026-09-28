using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Application;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class IdentityEmailIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Registration_commits_a_protected_email_with_the_challenge()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync("Email-Test@example.uz", "correct horse battery staple");
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var message = Assert.Single(await outbox.ClaimAvailableAsync("email-test", 10, TimeSpan.FromMinutes(1)));
        Assert.Equal(IdentityEmailEventTypes.Verification, message.EventType);
        Assert.DoesNotContain(registration.VerificationToken, message.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("email-test@example.uz", message.Payload, StringComparison.OrdinalIgnoreCase);
        var notification = scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
            .Unprotect(message.Payload);
        Assert.Equal("email-test@example.uz", notification.Email);
        Assert.Equal(registration.VerificationToken, notification.Token);
        Assert.Equal(IdentityEmailKind.Verification, notification.Kind);
        Assert.True(await identity.VerifyEmailAsync(notification.Token));
        Assert.False(await identity.VerifyEmailAsync(notification.Token));
    }

    [Fact]
    public async Task Email_handler_retries_transport_failure_then_deduplicates_replay()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var registration = await scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .RegisterAsync("retry@example.uz", "correct horse battery staple");
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var message = Assert.Single(await outbox.ClaimAvailableAsync("email-test", 10, TimeSpan.FromMinutes(1)));
        var sender = new RecordingEmailSender { FailNext = true };
        var handler = new IdentityEmailOutboxHandler(IdentityEmailEventTypes.Verification,
            scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>(), sender,
            scope.ServiceProvider.GetRequiredService<IConsumerInboxStore>(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(message, default));
        Assert.Empty(sender.Delivered);
        await handler.HandleAsync(message, default);
        await handler.HandleAsync(message, default);

        var delivered = Assert.Single(sender.Delivered);
        Assert.Equal(message.Id, delivered.Id);
        Assert.Equal(registration.VerificationToken, delivered.Notification.Token);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IConsumerInboxStore>()
            .HasProcessedAsync("identity-email", message.Id));
    }

    [Fact]
    public async Task Expired_recovery_email_is_not_sent_and_unknown_email_creates_no_work()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        Assert.Null(await identity.BeginPasswordRecoveryAsync("unknown@example.uz"));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimAvailableAsync("email-test", 10, TimeSpan.FromMinutes(1)));
        var registration = await identity.RegisterAsync("expiry@example.uz", "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var token = await identity.BeginPasswordRecoveryAsync("expiry@example.uz");
        Assert.NotNull(token);
        var messages = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimAvailableAsync("email-test", 10, TimeSpan.FromMinutes(1));
        var recovery = Assert.Single(messages, value => value.EventType == IdentityEmailEventTypes.PasswordRecovery);
        Assert.DoesNotContain(token, recovery.Payload, StringComparison.Ordinal);
        var sender = new RecordingEmailSender();
        var clock = new FixedEmailClock(DateTimeOffset.UtcNow.AddHours(1));
        var handler = new IdentityEmailOutboxHandler(IdentityEmailEventTypes.PasswordRecovery,
            scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>(), sender,
            scope.ServiceProvider.GetRequiredService<IConsumerInboxStore>(), clock);
        await handler.HandleAsync(recovery, default);
        await handler.HandleAsync(recovery, default);
        Assert.Empty(sender.Delivered);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IConsumerInboxStore>()
            .HasProcessedAsync("identity-email", recovery.Id));
    }

    [Fact]
    public async Task Notification_queue_failure_rolls_back_registration_and_recovery_challenge()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateIdentityProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IIdentityStore>();
        var failing = new IdentityService(store, services.GetRequiredService<IPasswordHasher>(),
            services.GetRequiredService<IIdentitySecretProtector>(),
            services.GetRequiredService<ITotpAuthenticator>(), TimeProvider.System,
            new FailingNotificationQueue(), services.GetRequiredService<ITransactionCoordinator>(),
            services.GetRequiredService<UZLLM.Modules.Audit.Contracts.IAuditTrail>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.RegisterAsync("rollback@example.uz", "correct horse battery staple"));
        Assert.Null(await store.FindAccountByEmailAsync("rollback@example.uz"));

        var identity = services.GetRequiredService<IIdentityService>();
        var registered = await identity.RegisterAsync("recovery-rollback@example.uz",
            "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registered.VerificationToken));
        var firstRecovery = await identity.BeginPasswordRecoveryAsync("recovery-rollback@example.uz");
        Assert.NotNull(firstRecovery);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.BeginPasswordRecoveryAsync("recovery-rollback@example.uz"));
        Assert.True(await identity.ResetPasswordAsync(firstRecovery, "another correct battery staple"));
    }

    private ServiceProvider CreateIdentityProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
        return new ServiceCollection().AddUzllmPersistence(config).AddUzllmIdentity()
            .BuildServiceProvider(validateScopes: true);
    }

    private sealed class RecordingEmailSender : IIdentityEmailSender
    {
        public bool FailNext { get; set; }
        public List<(Guid Id, IdentityEmailNotification Notification)> Delivered { get; } = [];
        public Task SendAsync(Guid messageId, IdentityEmailNotification notification,
            CancellationToken cancellationToken = default)
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Simulated transport failure."); }
            Delivered.Add((messageId, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingNotificationQueue : IIdentityNotificationQueue
    {
        public Task QueueAsync(IdentityEmailNotification notification,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated queue failure.");
    }

    private sealed class FixedEmailClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
