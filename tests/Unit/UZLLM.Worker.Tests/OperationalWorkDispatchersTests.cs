using Microsoft.Extensions.Logging.Abstractions;
using UZLLM.Persistence;
using UZLLM.Worker;

namespace UZLLM.Worker.Tests;

public sealed class OperationalWorkDispatchersTests
{
    [Fact]
    public async Task Outbox_dispatch_marks_message_processed_after_matching_handler_succeeds()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 10);
        var store = new FakeOutboxStore(message);
        var handler = new RecordingOutboxHandler("payment.completed");
        var dispatcher = new OutboxDispatchCycle(store, [handler], new AlwaysRenew(), NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMinutes(1), TimeSpan.Zero);

        Assert.Equal(message.Id, Assert.Single(handler.HandledMessageIds));
        Assert.Equal(message.Id, Assert.Single(store.ProcessedMessageIds));
        Assert.Empty(store.FailedMessageIds);
    }

    [Fact]
    public async Task Outbox_dispatch_releases_message_when_no_matching_handler_is_registered()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 10);
        var store = new FakeOutboxStore(message);
        var dispatcher = new OutboxDispatchCycle(store, [], new AlwaysRenew(), NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

        var failed = Assert.Single(store.FailedMessageIds);
        Assert.Equal(message.Id, failed.Id);
        Assert.Equal("NoRegisteredHandler", failed.FailureKind);
        Assert.Empty(store.ProcessedMessageIds);
    }

    [Fact]
    public async Task Outbox_dispatch_releases_message_when_handler_throws()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 10);
        var store = new FakeOutboxStore(message);
        var dispatcher = new OutboxDispatchCycle(store, [new ThrowingOutboxHandler("payment.completed")],
            new AlwaysRenew(), NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

        var failed = Assert.Single(store.FailedMessageIds);
        Assert.Equal(message.Id, failed.Id);
        Assert.Equal(nameof(InvalidOperationException), failed.FailureKind);
        Assert.Empty(store.ProcessedMessageIds);
    }

    [Fact]
    public async Task Leased_job_dispatch_marks_job_completed_after_matching_handler_succeeds()
    {
        var job = new LeasedJob(Guid.CreateVersion7(), "provider.health", "{}", "openai:primary", 1, 10);
        var store = new FakeLeasedJobStore(job);
        var handler = new RecordingJobHandler("provider.health");
        var dispatcher = new LeasedJobDispatchCycle(store, [handler], new AlwaysRenew(), NullLogger<LeasedJobDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMinutes(1), TimeSpan.Zero);

        Assert.Equal(job.Id, Assert.Single(handler.HandledJobIds));
        Assert.Equal(job.Id, Assert.Single(store.CompletedJobIds));
        Assert.Empty(store.FailedJobIds);
    }

    [Fact]
    public async Task Leased_job_dispatch_releases_job_when_no_matching_handler_is_registered()
    {
        var job = new LeasedJob(Guid.CreateVersion7(), "provider.health", "{}", "openai:primary", 1, 10);
        var store = new FakeLeasedJobStore(job);
        var dispatcher = new LeasedJobDispatchCycle(store, [], new AlwaysRenew(), NullLogger<LeasedJobDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

        var failed = Assert.Single(store.FailedJobIds);
        Assert.Equal(job.Id, failed.Id);
        Assert.Equal("NoRegisteredHandler", failed.FailureKind);
        Assert.Empty(store.CompletedJobIds);
    }

    [Fact]
    public async Task Outbox_dispatch_renews_a_slow_handler_without_preclaiming_more_work()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 3);
        var store = new FakeOutboxStore(message);
        var handler = new PausingOutboxHandler();
        var renewer = new CountingRenewer();
        var dispatcher = new OutboxDispatchCycle(store, [handler], renewer, NullLogger<OutboxDispatchCycle>.Instance);

        var dispatch = dispatcher.DispatchAsync("worker-one", TimeSpan.FromMilliseconds(90), TimeSpan.Zero);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await renewer.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, store.LastClaimMaxCount);
        Assert.Empty(store.ProcessedMessageIds);
        handler.Release.SetResult();
        Assert.True(await dispatch.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(message.Id, Assert.Single(store.ProcessedMessageIds));
    }

    [Fact]
    public async Task Outbox_dispatch_does_not_complete_after_lease_renewal_is_lost()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 3);
        var store = new FakeOutboxStore(message);
        var handler = new CancelableOutboxHandler();
        var renewer = new CountingRenewer { LoseOutboxLease = true };
        var dispatcher = new OutboxDispatchCycle(store, [handler], renewer, NullLogger<OutboxDispatchCycle>.Instance);

        Assert.True(await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMilliseconds(90),
            TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(handler.Canceled);
        Assert.Empty(store.ProcessedMessageIds);
        Assert.Empty(store.FailedMessageIds);
    }

    [Fact]
    public async Task Leased_job_dispatch_does_not_complete_after_lease_renewal_is_lost()
    {
        var job = new LeasedJob(Guid.CreateVersion7(), "provider.health", "{}", "openai:primary", 1, 3);
        var store = new FakeLeasedJobStore(job);
        var handler = new CancelableJobHandler();
        var renewer = new CountingRenewer { LoseJobLease = true };
        var dispatcher = new LeasedJobDispatchCycle(store, [handler], renewer,
            NullLogger<LeasedJobDispatchCycle>.Instance);

        Assert.True(await dispatcher.DispatchAsync("worker-one", TimeSpan.FromMilliseconds(90),
            TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(handler.Canceled);
        Assert.Empty(store.CompletedJobIds);
        Assert.Empty(store.FailedJobIds);
    }

    [Fact]
    public async Task Leased_job_dispatch_renews_a_slow_handler_without_preclaiming_more_work()
    {
        var job = new LeasedJob(Guid.CreateVersion7(), "provider.health", "{}", "openai:primary", 1, 3);
        var store = new FakeLeasedJobStore(job);
        var handler = new PausingJobHandler();
        var renewer = new CountingRenewer();
        var dispatcher = new LeasedJobDispatchCycle(store, [handler], renewer,
            NullLogger<LeasedJobDispatchCycle>.Instance);

        var dispatch = dispatcher.DispatchAsync("worker-one", TimeSpan.FromMilliseconds(90), TimeSpan.Zero);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await renewer.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, store.LastClaimMaxCount);
        Assert.Empty(store.CompletedJobIds);
        handler.Release.SetResult();
        Assert.True(await dispatch.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(job.Id, Assert.Single(store.CompletedJobIds));
    }

    private sealed class RecordingOutboxHandler(string eventType) : IOutboxHandler
    {
        public List<Guid> HandledMessageIds { get; } = [];

        public string EventType { get; } = eventType;

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            HandledMessageIds.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingJobHandler(string jobType) : ILeasedJobHandler
    {
        public List<Guid> HandledJobIds { get; } = [];

        public string JobType { get; } = jobType;

        public Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
        {
            HandledJobIds.Add(job.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOutboxHandler(string eventType) : IOutboxHandler
    {
        public string EventType { get; } = eventType;

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class PausingOutboxHandler : IOutboxHandler
    {
        public string EventType => "payment.completed";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CancelableOutboxHandler : IOutboxHandler
    {
        public string EventType => "payment.completed";
        public bool Canceled { get; private set; }

        public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
    }

    private sealed class CancelableJobHandler : ILeasedJobHandler
    {
        public string JobType => "provider.health";
        public bool Canceled { get; private set; }

        public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
    }

    private sealed class PausingJobHandler : ILeasedJobHandler
    {
        public string JobType => "provider.health";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FakeOutboxStore(OutboxMessage message) : IOutboxStore
    {
        public int LastClaimMaxCount { get; private set; }
        public List<Guid> ProcessedMessageIds { get; } = [];

        public List<(Guid Id, string FailureKind)> FailedMessageIds { get; } = [];

        public Task<Guid> EnqueueAsync(string eventType, string payload, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.CreateVersion7());

        public Task<IReadOnlyList<OutboxMessage>> ClaimAvailableAsync(string workerId, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            LastClaimMaxCount = maxCount;
            return Task.FromResult<IReadOnlyList<OutboxMessage>>([message]);
        }

        public Task<bool> RenewLeaseAsync(Guid eventId, string workerId, int attemptCount,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkProcessedAsync(Guid eventId, string workerId, int attemptCount,
            CancellationToken cancellationToken = default)
        {
            ProcessedMessageIds.Add(eventId);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid eventId, string workerId, int attemptCount,
            TimeSpan retryDelay, string failureKind, CancellationToken cancellationToken = default)
        {
            FailedMessageIds.Add((eventId, failureKind));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeLeasedJobStore(LeasedJob job) : ILeasedJobStore
    {
        public int LastClaimMaxCount { get; private set; }
        public List<Guid> CompletedJobIds { get; } = [];

        public List<(Guid Id, string FailureKind)> FailedJobIds { get; } = [];

        public Task<Guid> ScheduleAsync(string jobType, string payload, string deduplicationKey, DateTimeOffset availableAt, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.CreateVersion7());

        public Task<IReadOnlyList<LeasedJob>> ClaimAvailableAsync(string workerId, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            LastClaimMaxCount = maxCount;
            return Task.FromResult<IReadOnlyList<LeasedJob>>([job]);
        }

        public Task<bool> RenewLeaseAsync(Guid jobId, string workerId, int attemptCount,
            TimeSpan leaseDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> MarkCompletedAsync(Guid jobId, string workerId, int attemptCount,
            CancellationToken cancellationToken = default)
        {
            CompletedJobIds.Add(jobId);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid jobId, string workerId, int attemptCount,
            TimeSpan retryDelay, string failureKind, CancellationToken cancellationToken = default)
        {
            FailedJobIds.Add((jobId, failureKind));
            return Task.FromResult(true);
        }
    }

    private sealed class AlwaysRenew : IOperationalLeaseRenewer
    {
        public Task<bool> RenewOutboxAsync(OutboxMessage message, string owner, TimeSpan duration,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> RenewJobAsync(LeasedJob job, string owner, TimeSpan duration,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class CountingRenewer : IOperationalLeaseRenewer
    {
        public bool LoseOutboxLease { get; init; }
        public bool LoseJobLease { get; init; }
        public TaskCompletionSource Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> RenewOutboxAsync(OutboxMessage message, string owner, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            Renewed.TrySetResult();
            return Task.FromResult(!LoseOutboxLease);
        }

        public Task<bool> RenewJobAsync(LeasedJob job, string owner, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            Renewed.TrySetResult();
            return Task.FromResult(!LoseJobLease);
        }
    }
}
