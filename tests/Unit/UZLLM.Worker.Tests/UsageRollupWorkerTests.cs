using UZLLM.Modules.Usage.Contracts;
using UZLLM.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UZLLM.Worker.Tests;

public sealed class UsageRollupWorkerTests
{
    [Fact]
    public async Task Cycle_drains_full_pages_then_stops_at_partial_page()
    {
        var store = new RecordingStore(new UsageRollupBatchResult(100, 7), new UsageRollupBatchResult(13, 2));
        Assert.Equal(9, await new UsageRollupCycle(store).RunAsync());
        Assert.Equal([100, 100], store.BatchSizes);
    }

    [Fact]
    public async Task Cycle_bounds_work_to_twenty_pages_and_leaves_remainder_for_next_tick()
    {
        var store = new RecordingStore(new UsageRollupBatchResult(100, 1));
        Assert.Equal(20, await new UsageRollupCycle(store).RunAsync());
        Assert.Equal(20, store.BatchSizes.Count);
        Assert.All(store.BatchSizes, size => Assert.Equal(100, size));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cycle_stops_on_empty_or_other_worker_ownership(bool busy)
    {
        var store = new RecordingStore(new UsageRollupBatchResult(0, 0, busy));
        Assert.Equal(0, await new UsageRollupCycle(store).RunAsync());
        Assert.Single(store.BatchSizes);
    }

    [Fact]
    public async Task Hosted_worker_retries_failed_cycle_in_a_new_scope_and_redacts_error_details()
    {
        var store = new RecordingStore(new UsageRollupBatchResult(1, 1))
            { Failure = new InvalidOperationException("fixture-secret-must-not-be-logged") };
        var scopesCreated = 0;
        await using var services = new ServiceCollection()
            .AddScoped<IUsageRollupStore>(_ => { Interlocked.Increment(ref scopesCreated); return store; })
            .AddScoped<UsageRollupCycle>().BuildServiceProvider(validateScopes: true);
        var logger = new RecordingLogger();
        using var worker = new UsageRollupWorker(services.GetRequiredService<IServiceScopeFactory>(), logger);
        await worker.StartAsync(CancellationToken.None);
        await store.Faulted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Failure = null;
        await store.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, scopesCreated);
        Assert.Equal([100, 100], store.BatchSizes);
        Assert.Contains(logger.Messages, value => value.Contains(nameof(InvalidOperationException)));
        Assert.DoesNotContain(logger.Messages, value => value.Contains("fixture-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cycle_propagates_failure_for_retry_and_preserves_cancellation()
    {
        var store = new RecordingStore(new UsageRollupBatchResult(100, 1)) { Failure = new InvalidOperationException("fixture") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UsageRollupCycle(store).RunAsync());
        store.Failure = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UsageRollupCycle(store).RunAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, store.LastToken);
    }

    private sealed class RecordingStore(params UsageRollupBatchResult[] results) : IUsageRollupStore
    {
        public List<int> BatchSizes { get; } = [];
        public Exception? Failure { get; set; }
        public CancellationToken LastToken { get; private set; }
        public TaskCompletionSource Faulted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<UsageRollupBatchResult> RebuildBatchAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            BatchSizes.Add(batchSize);
            if (Failure is { } failure) { Faulted.TrySetResult(); throw failure; }
            Succeeded.TrySetResult();
            return Task.FromResult(results[Math.Min(BatchSizes.Count - 1, results.Length - 1)]);
        }
    }

    private sealed class RecordingLogger : ILogger<UsageRollupWorker>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
