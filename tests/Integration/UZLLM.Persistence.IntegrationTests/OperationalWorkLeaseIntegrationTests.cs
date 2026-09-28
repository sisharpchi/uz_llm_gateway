using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Persistence;
using UZLLM.Worker;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class OperationalWorkLeaseIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Outbox_crash_after_final_claim_dead_letters_once_and_fences_stale_owner()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var clock = new WorkClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        await using var services = CreateServices(clock);
        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IOutboxStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IOutboxStore>();
        var id = await firstStore.EnqueueAsync("test.once", "{\"secret\":\"never-expose\"}", maxAttempts: 2);

        var firstClaim = Assert.Single(await firstStore.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.False(await firstStore.RenewLeaseAsync(id, "worker-one", 0, TimeSpan.FromMinutes(1)));
        Assert.True(await firstStore.RenewLeaseAsync(id, "worker-one", firstClaim.AttemptCount, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Empty(await secondStore.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromSeconds(21));
        var finalClaim = Assert.Single(await secondStore.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(2, finalClaim.AttemptCount);
        Assert.False(await firstStore.MarkProcessedAsync(id, "worker-one", firstClaim.AttemptCount));
        Assert.False(await secondStore.MarkProcessedAsync(id, "worker-two", firstClaim.AttemptCount));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await firstStore.ClaimAvailableAsync("worker-three", 1, TimeSpan.FromMinutes(1)));
        var deadLetter = Assert.Single(await first.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>()
            .ListDeadLettersAsync());
        Assert.Equal(id, deadLetter.Id);
        Assert.Equal("Outbox", deadLetter.Kind);
        Assert.Equal("test.once", deadLetter.WorkType);
        Assert.Equal(2, deadLetter.AttemptCount);
        Assert.Equal("LeaseExpired", deadLetter.LastError);
        Assert.DoesNotContain("never-expose", System.Text.Json.JsonSerializer.Serialize(deadLetter));
    }

    [Fact]
    public async Task Job_failure_retries_only_to_ceiling_and_dead_letter_is_visible_without_payload()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var clock = new WorkClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        await using var services = CreateServices(clock);
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILeasedJobStore>();
        var id = await store.ScheduleAsync("test.job", "{\"secret\":\"never-expose\"}", "dedupe", clock.GetUtcNow(), 2);

        var first = Assert.Single(await store.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.True(await store.RenewLeaseAsync(id, "worker-one", first.AttemptCount, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Empty(await store.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        Assert.True(await store.MarkFailedAsync(id, "worker-one", first.AttemptCount, TimeSpan.Zero, "TransientFailure"));
        var second = Assert.Single(await store.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(2, second.AttemptCount);
        Assert.True(await store.MarkFailedAsync(id, "worker-two", second.AttemptCount, TimeSpan.Zero, "PermanentFailure"));
        Assert.Empty(await store.ClaimAvailableAsync("worker-three", 1, TimeSpan.FromMinutes(1)));
        Assert.False(await store.MarkCompletedAsync(id, "worker-two", second.AttemptCount));
        var deadLetter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>()
            .ListDeadLettersAsync());
        Assert.Equal((id, "Job", "test.job", 2, "PermanentFailure"),
            (deadLetter.Id, deadLetter.Kind, deadLetter.WorkType, deadLetter.AttemptCount, deadLetter.LastError));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scope.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>().ListDeadLettersAsync(101));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scope.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>().ListDeadLettersAsync(0));
    }

    [Fact]
    public async Task Job_final_attempt_crash_becomes_dead_letter_on_next_poll()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var clock = new WorkClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        await using var services = CreateServices(clock);
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILeasedJobStore>();
        var id = await store.ScheduleAsync("test.crash", "{}", "crash", clock.GetUtcNow(), 1);
        var claim = Assert.Single(await store.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(1, claim.AttemptCount);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(await store.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        var deadLetter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>()
            .ListDeadLettersAsync());
        Assert.Equal(id, deadLetter.Id);
        Assert.Equal("Job", deadLetter.Kind);
        Assert.Equal("LeaseExpired", deadLetter.LastError);
    }

    [Fact]
    public async Task Outbox_explicit_failure_at_ceiling_dead_letters_without_another_claim()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var services = CreateServices(TimeProvider.System);
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var id = await store.EnqueueAsync("test.permanent", "{}", 1);
        var claim = Assert.Single(await store.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));
        Assert.True(await store.MarkFailedAsync(id, "worker-one", claim.AttemptCount,
            TimeSpan.Zero, "PermanentFailure"));
        Assert.Empty(await store.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        var deadLetter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IOperationalWorkMonitor>()
            .ListDeadLettersAsync());
        Assert.Equal(id, deadLetter.Id);
        Assert.Equal("Outbox", deadLetter.Kind);
        Assert.Equal("PermanentFailure", deadLetter.LastError);
    }

    [Fact]
    public async Task Outbox_heartbeat_prevents_another_node_from_claiming_a_slow_handler()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var services = CreateServices(TimeProvider.System);
        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IOutboxStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IOutboxStore>();
        var id = await firstStore.EnqueueAsync("test.slow", "{}");
        var handler = new PausingHandler();
        var renewer = new ScopedOperationalLeaseRenewer(services.GetRequiredService<IServiceScopeFactory>());
        var cycle = new OutboxDispatchCycle(firstStore, [handler], renewer, NullLogger<OutboxDispatchCycle>.Instance);

        var dispatch = cycle.DispatchAsync("worker-one", TimeSpan.FromSeconds(2), TimeSpan.Zero);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Empty(await secondStore.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromSeconds(2)));
        handler.Release.SetResult();
        Assert.True(await dispatch.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Empty(await secondStore.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromSeconds(2)));
        var completed = await first.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM ops.outbox WHERE id = {0} AND processed_at IS NOT NULL", id)
            .SingleAsync();
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task Inbox_deduplicates_a_durable_side_effect_after_outbox_reclaim()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var clock = new WorkClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        await using var services = CreateServices(clock);
        await using var first = services.CreateAsyncScope();
        await using var second = services.CreateAsyncScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IOutboxStore>();
        var evidenceId = Guid.CreateVersion7();
        var id = await firstStore.EnqueueAsync("billing.late_exposure.recorded",
            System.Text.Json.JsonSerializer.Serialize(new { evidenceId, exposureMicroUsd = 123L }));
        var firstClaim = Assert.Single(await firstStore.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));
        await CreateAlertHandler(first.ServiceProvider).HandleAsync(firstClaim, default);
        clock.Advance(TimeSpan.FromMinutes(1));
        var secondStore = second.ServiceProvider.GetRequiredService<IOutboxStore>();
        var reclaimed = Assert.Single(await secondStore.ClaimAvailableAsync("worker-two", 1,
            TimeSpan.FromMinutes(1)));
        await CreateAlertHandler(second.ServiceProvider).HandleAsync(reclaimed, default);
        Assert.True(await secondStore.MarkProcessedAsync(id, "worker-two", reclaimed.AttemptCount));
        var db = first.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var count = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.operational_alert WHERE deduplication_key = {0}",
            evidenceId.ToString("N"))
            .SingleAsync();
        Assert.Equal(1, count);
        var inboxCount = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.consumer_inbox WHERE consumer = 'billing-financial-alerts' AND event_id = {0}", id)
            .SingleAsync();
        Assert.Equal(1, inboxCount);
    }

    private static BillingFinancialAlertHandler CreateAlertHandler(IServiceProvider services) => new(
        services.GetRequiredService<IOperationalAlertPublisher>(),
        services.GetRequiredService<ITransactionCoordinator>(),
        services.GetRequiredService<IConsumerInboxStore>(), "billing.late_exposure.recorded");

    private ServiceProvider CreateServices(TimeProvider clock)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
        return new ServiceCollection().AddUzllmPersistence(configuration).AddSingleton(clock)
            .BuildServiceProvider(validateScopes: true);
    }

    private sealed class PausingHandler : IOutboxHandler
    {
        public string EventType => "test.slow";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class WorkClock(DateTimeOffset initial) : TimeProvider
    {
        private long milliseconds = initial.ToUnixTimeMilliseconds();
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref milliseconds));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref milliseconds, (long)duration.TotalMilliseconds);
    }
}
