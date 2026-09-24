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
        var dispatcher = new OutboxDispatchCycle(store, [handler], NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        Assert.Equal(message.Id, Assert.Single(handler.HandledMessageIds));
        Assert.Equal(message.Id, Assert.Single(store.ProcessedMessageIds));
        Assert.Empty(store.FailedMessageIds);
    }

    [Fact]
    public async Task Outbox_dispatch_releases_message_when_no_matching_handler_is_registered()
    {
        var message = new OutboxMessage(Guid.CreateVersion7(), "payment.completed", "{}", DateTimeOffset.UtcNow, 1, 10);
        var store = new FakeOutboxStore(message);
        var dispatcher = new OutboxDispatchCycle(store, [], NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", 10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

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
        var dispatcher = new OutboxDispatchCycle(store, [new ThrowingOutboxHandler("payment.completed")], NullLogger<OutboxDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", 10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

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
        var dispatcher = new LeasedJobDispatchCycle(store, [handler], NullLogger<LeasedJobDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        Assert.Equal(job.Id, Assert.Single(handler.HandledJobIds));
        Assert.Equal(job.Id, Assert.Single(store.CompletedJobIds));
        Assert.Empty(store.FailedJobIds);
    }

    [Fact]
    public async Task Leased_job_dispatch_releases_job_when_no_matching_handler_is_registered()
    {
        var job = new LeasedJob(Guid.CreateVersion7(), "provider.health", "{}", "openai:primary", 1, 10);
        var store = new FakeLeasedJobStore(job);
        var dispatcher = new LeasedJobDispatchCycle(store, [], NullLogger<LeasedJobDispatchCycle>.Instance);

        await dispatcher.DispatchAsync("worker-one", 10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));

        var failed = Assert.Single(store.FailedJobIds);
        Assert.Equal(job.Id, failed.Id);
        Assert.Equal("NoRegisteredHandler", failed.FailureKind);
        Assert.Empty(store.CompletedJobIds);
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

    private sealed class FakeOutboxStore(OutboxMessage message) : IOutboxStore
    {
        public List<Guid> ProcessedMessageIds { get; } = [];

        public List<(Guid Id, string FailureKind)> FailedMessageIds { get; } = [];

        public Task<Guid> EnqueueAsync(string eventType, string payload, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.CreateVersion7());

        public Task<IReadOnlyList<OutboxMessage>> ClaimAvailableAsync(string workerId, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>([message]);

        public Task<bool> MarkProcessedAsync(Guid eventId, string workerId, CancellationToken cancellationToken = default)
        {
            ProcessedMessageIds.Add(eventId);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid eventId, string workerId, TimeSpan retryDelay, string failureKind, CancellationToken cancellationToken = default)
        {
            FailedMessageIds.Add((eventId, failureKind));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeLeasedJobStore(LeasedJob job) : ILeasedJobStore
    {
        public List<Guid> CompletedJobIds { get; } = [];

        public List<(Guid Id, string FailureKind)> FailedJobIds { get; } = [];

        public Task<Guid> ScheduleAsync(string jobType, string payload, string deduplicationKey, DateTimeOffset availableAt, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.CreateVersion7());

        public Task<IReadOnlyList<LeasedJob>> ClaimAvailableAsync(string workerId, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LeasedJob>>([job]);

        public Task<bool> MarkCompletedAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default)
        {
            CompletedJobIds.Add(jobId);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid jobId, string workerId, TimeSpan retryDelay, string failureKind, CancellationToken cancellationToken = default)
        {
            FailedJobIds.Add((jobId, failureKind));
            return Task.FromResult(true);
        }
    }
}
