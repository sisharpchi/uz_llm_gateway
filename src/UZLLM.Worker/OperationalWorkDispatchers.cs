using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UZLLM.Persistence;

namespace UZLLM.Worker;

public sealed class OutboxDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatchWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.CreateVersion7():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAvailableAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Outbox polling failed for {WorkerId} with {FailureKind}", workerId, exception.GetType().Name);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task DispatchAvailableAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatchCycle>();
        await dispatcher.DispatchAsync(workerId, BatchSize, LeaseDuration, RetryDelay, cancellationToken);
    }
}

public sealed class LeasedJobDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LeasedJobDispatchWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.CreateVersion7():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAvailableAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Leased job polling failed for {WorkerId} with {FailureKind}", workerId, exception.GetType().Name);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task DispatchAvailableAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<LeasedJobDispatchCycle>();
        await dispatcher.DispatchAsync(workerId, BatchSize, LeaseDuration, RetryDelay, cancellationToken);
    }
}

public sealed class OutboxDispatchCycle(
    IOutboxStore store,
    IEnumerable<IOutboxHandler> handlers,
    ILogger<OutboxDispatchCycle> logger)
{
    public async Task DispatchAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var messages = await store.ClaimAvailableAsync(workerId, batchSize, leaseDuration, cancellationToken);
        foreach (var message in messages)
        {
            var matchingHandlers = handlers.Where(handler => string.Equals(handler.EventType, message.EventType, StringComparison.Ordinal)).ToArray();
            if (matchingHandlers.Length == 0)
            {
                await store.MarkFailedAsync(message.Id, workerId, retryDelay, "NoRegisteredHandler", cancellationToken);
                logger.LogWarning("Outbox message has no registered handler {EventId} {EventType}", message.Id, message.EventType);
                continue;
            }

            try
            {
                foreach (var handler in matchingHandlers)
                {
                    await handler.HandleAsync(message, cancellationToken);
                }

                await store.MarkProcessedAsync(message.Id, workerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await store.MarkFailedAsync(message.Id, workerId, retryDelay, exception.GetType().Name, cancellationToken);
                logger.LogError("Outbox handler failed {EventId} {EventType} with {FailureKind}", message.Id, message.EventType, exception.GetType().Name);
            }
        }
    }
}

public sealed class LeasedJobDispatchCycle(
    ILeasedJobStore store,
    IEnumerable<ILeasedJobHandler> handlers,
    ILogger<LeasedJobDispatchCycle> logger)
{
    public async Task DispatchAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var jobs = await store.ClaimAvailableAsync(workerId, batchSize, leaseDuration, cancellationToken);
        foreach (var job in jobs)
        {
            var handler = handlers.SingleOrDefault(candidate => string.Equals(candidate.JobType, job.JobType, StringComparison.Ordinal));
            if (handler is null)
            {
                await store.MarkFailedAsync(job.Id, workerId, retryDelay, "NoRegisteredHandler", cancellationToken);
                logger.LogWarning("Leased job has no registered handler {JobId} {JobType}", job.Id, job.JobType);
                continue;
            }

            try
            {
                await handler.HandleAsync(job, cancellationToken);
                await store.MarkCompletedAsync(job.Id, workerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await store.MarkFailedAsync(job.Id, workerId, retryDelay, exception.GetType().Name, cancellationToken);
                logger.LogError("Leased job handler failed {JobId} {JobType} with {FailureKind}", job.Id, job.JobType, exception.GetType().Name);
            }
        }
    }
}
