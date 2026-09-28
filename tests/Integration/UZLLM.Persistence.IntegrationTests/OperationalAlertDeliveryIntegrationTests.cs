using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Persistence;
using UZLLM.Worker;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class OperationalAlertDeliveryIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Late_external_spend_replay_creates_one_distinct_alert_and_one_notification()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var sender = new RecordingAlertSender();
        await using var services = CreateServices(sender);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var outbox = provider.GetRequiredService<IOutboxStore>();
        var evidenceId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var originalId = await outbox.EnqueueAsync("billing.late_external_spend.recorded",
            JsonSerializer.Serialize(new { evidenceId, reservationId, exposureMicroUsd = 731L }));
        var original = Assert.Single(await outbox.ClaimAvailableAsync("billing-worker", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(originalId, original.Id);
        var financialHandler = new BillingFinancialAlertHandler(
            provider.GetRequiredService<IOperationalAlertPublisher>(),
            provider.GetRequiredService<ITransactionCoordinator>(),
            provider.GetRequiredService<IConsumerInboxStore>(), "billing.late_external_spend.recorded");

        await financialHandler.HandleAsync(original, default);
        await financialHandler.HandleAsync(original, default);
        Assert.True(await outbox.MarkProcessedAsync(original.Id, "billing-worker", original.AttemptCount));

        var alerts = await provider.GetRequiredService<IOperationalAlertDeliveryStore>().ListAsync();
        var alert = Assert.Single(alerts);
        Assert.Equal("LateExternalSpend", alert.Kind);
        Assert.Equal("Pending", alert.DeliveryStatus);
        var db = provider.GetRequiredService<FoundationDbContext>();
        var notificationEvents = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.outbox WHERE event_type = 'ops.alert.raised'")
            .SingleAsync();
        Assert.Equal(1, notificationEvents);

        var notification = Assert.Single(await outbox.ClaimAvailableAsync("operator-worker", 1,
            TimeSpan.FromMinutes(1)));
        Assert.Equal("ops.alert.raised", notification.EventType);
        Assert.Equal(notification.Id, alert.NotificationEventId);
        var deliveryHandler = CreateDeliveryHandler(provider, sender);
        await deliveryHandler.HandleAsync(notification, default);
        await deliveryHandler.HandleAsync(notification, default);
        Assert.True(await outbox.MarkProcessedAsync(notification.Id, "operator-worker", notification.AttemptCount));
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(notification.Id, Assert.Single(sender.EventIds));
        Assert.Equal("Delivered", (await provider.GetRequiredService<IOperationalAlertDeliveryStore>()
            .FindAsync(alert.Id))!.DeliveryStatus);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.consumer_inbox WHERE consumer = 'operator-alert-email' AND event_id = {0}",
            notification.Id).SingleAsync());
    }

    [Fact]
    public async Task Failed_operator_delivery_retries_to_visible_dead_letter_without_false_delivery()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var sender = new RecordingAlertSender { Fail = true };
        await using var services = CreateServices(sender);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var alert = await provider.GetRequiredService<IOperationalAlertPublisher>().RaiseAsync(
            OperationalAlertKind.SettlementFailure, Guid.CreateVersion7().ToString("N"),
            "{\"secret\":\"must-not-leak\"}");
        var notificationId = (await provider.GetRequiredService<IOperationalAlertDeliveryStore>()
            .FindAsync(alert.Id))!.NotificationEventId!.Value;
        var cycle = new OutboxDispatchCycle(provider.GetRequiredService<IOutboxStore>(),
            [CreateDeliveryHandler(provider, sender)],
            new ScopedOperationalLeaseRenewer(services.GetRequiredService<IServiceScopeFactory>()),
            NullLogger<OutboxDispatchCycle>.Instance);
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.True(await cycle.DispatchAsync($"worker-{attempt}", TimeSpan.FromMinutes(1), TimeSpan.Zero));
        Assert.False(await cycle.DispatchAsync("worker-final", TimeSpan.FromMinutes(1), TimeSpan.Zero));

        var dead = Assert.Single(await provider.GetRequiredService<IOperationalWorkMonitor>().ListDeadLettersAsync());
        Assert.Equal(notificationId, dead.Id);
        Assert.Equal("ops.alert.raised", dead.WorkType);
        Assert.Equal(10, dead.AttemptCount);
        Assert.Equal("InvalidOperationException", dead.LastError);
        var status = (await provider.GetRequiredService<IOperationalAlertDeliveryStore>().FindAsync(alert.Id))!;
        Assert.Equal("DeadLettered", status.DeliveryStatus);
        Assert.Null(status.NotifiedAt);
        Assert.Equal(10, sender.SendCount);
        Assert.DoesNotContain("must-not-leak", JsonSerializer.Serialize(status));
    }

    [Fact]
    public async Task Malformed_late_external_spend_event_dead_letters_without_creating_an_alert()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var services = CreateServices(new RecordingAlertSender());
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var outbox = provider.GetRequiredService<IOutboxStore>();
        var eventId = await outbox.EnqueueAsync("billing.late_external_spend.recorded", "{}", 1);
        var handler = new BillingFinancialAlertHandler(
            provider.GetRequiredService<IOperationalAlertPublisher>(),
            provider.GetRequiredService<ITransactionCoordinator>(),
            provider.GetRequiredService<IConsumerInboxStore>(), "billing.late_external_spend.recorded");
        var cycle = new OutboxDispatchCycle(outbox, [handler],
            new ScopedOperationalLeaseRenewer(services.GetRequiredService<IServiceScopeFactory>()),
            NullLogger<OutboxDispatchCycle>.Instance);
        Assert.True(await cycle.DispatchAsync("billing-worker", TimeSpan.FromMinutes(1), TimeSpan.Zero));
        var dead = Assert.Single(await provider.GetRequiredService<IOperationalWorkMonitor>().ListDeadLettersAsync());
        Assert.Equal(eventId, dead.Id);
        Assert.Equal("billing.late_external_spend.recorded", dead.WorkType);
        Assert.Equal("KeyNotFoundException", dead.LastError);
        Assert.Empty(await provider.GetRequiredService<IOperationalAlertDeliveryStore>().ListAsync());
    }

    [Fact]
    public async Task Operational_alert_rejects_an_event_id_that_does_not_match_the_persisted_alert()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var sender = new RecordingAlertSender();
        await using var services = CreateServices(sender);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var alert = await provider.GetRequiredService<IOperationalAlertPublisher>().RaiseAsync(
            OperationalAlertKind.PaymentCallbackFailure, Guid.CreateVersion7().ToString("N"), "{}");
        var impostor = new OutboxMessage(Guid.CreateVersion7(), "ops.alert.raised",
            JsonSerializer.Serialize(new { alert.Id }), DateTimeOffset.UtcNow, 1, 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateDeliveryHandler(provider, sender).HandleAsync(impostor, default));
        Assert.Equal(0, sender.SendCount);
        Assert.Equal("Pending", (await provider.GetRequiredService<IOperationalAlertDeliveryStore>()
            .FindAsync(alert.Id))!.DeliveryStatus);
    }

    private ServiceProvider CreateServices(RecordingAlertSender sender)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
        return new ServiceCollection().AddUzllmPersistence(configuration).AddSingleton<IOperationalAlertSender>(sender)
            .BuildServiceProvider(validateScopes: true);
    }

    private static OperationalAlertOutboxHandler CreateDeliveryHandler(IServiceProvider provider,
        IOperationalAlertSender sender) => new(provider.GetRequiredService<IOperationalAlertDeliveryStore>(),
        sender, provider.GetRequiredService<IConsumerInboxStore>(),
        provider.GetRequiredService<ITransactionCoordinator>(), provider.GetRequiredService<TimeProvider>());

    private sealed class RecordingAlertSender : IOperationalAlertSender
    {
        public bool Fail { get; set; }
        public int SendCount { get; private set; }
        public List<Guid> EventIds { get; } = [];

        public Task SendAsync(Guid eventId, OperationalAlertDelivery alert,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            EventIds.Add(eventId);
            if (Fail) throw new InvalidOperationException("Simulated SMTP outage");
            return Task.CompletedTask;
        }
    }
}
