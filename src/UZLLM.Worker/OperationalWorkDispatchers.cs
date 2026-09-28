using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UZLLM.Persistence;

namespace UZLLM.Worker;

public sealed class OutboxDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatchWorker> logger) : BackgroundService
{
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
                if (await DispatchAvailableAsync(stoppingToken)) continue;
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

    private async Task<bool> DispatchAvailableAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatchCycle>();
        return await dispatcher.DispatchAsync($"{workerId}:{Guid.CreateVersion7():N}",
            LeaseDuration, RetryDelay, cancellationToken);
    }
}

public sealed class LeasedJobDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LeasedJobDispatchWorker> logger) : BackgroundService
{
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
                if (await DispatchAvailableAsync(stoppingToken)) continue;
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

    private async Task<bool> DispatchAvailableAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<LeasedJobDispatchCycle>();
        return await dispatcher.DispatchAsync($"{workerId}:{Guid.CreateVersion7():N}",
            LeaseDuration, RetryDelay, cancellationToken);
    }
}

public sealed class OutboxDispatchCycle(
    IOutboxStore store,
    IEnumerable<IOutboxHandler> handlers,
    IOperationalLeaseRenewer renewer,
    ILogger<OutboxDispatchCycle> logger)
{
    public async Task<bool> DispatchAsync(
        string workerId,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var messages = await store.ClaimAvailableAsync(workerId, 1, leaseDuration, cancellationToken);
        foreach (var message in messages)
        {
            var matchingHandlers = handlers.Where(handler => string.Equals(handler.EventType, message.EventType, StringComparison.Ordinal)).ToArray();
            if (matchingHandlers.Length == 0)
            {
                var recorded = await store.MarkFailedAsync(message.Id, workerId, message.AttemptCount, retryDelay,
                    "NoRegisteredHandler", cancellationToken);
                logger.LogWarning("Outbox message has no registered handler {EventId} {EventType}", message.Id, message.EventType);
                if (!recorded) logger.LogError("Outbox failure could not be recorded {EventId}", message.Id);
                else if (message.AttemptCount >= message.MaxAttempts)
                    logger.LogError("Outbox message dead-lettered {EventId} {EventType}", message.Id, message.EventType);
                continue;
            }

            using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var heartbeatStop = new CancellationTokenSource();
            var leaseLost = 0;
            var heartbeat = OperationalLeaseHeartbeat.RunAsync(leaseDuration,
                token => renewer.RenewOutboxAsync(message, workerId, leaseDuration, token),
                handlerCancellation, exception =>
                {
                    Interlocked.Exchange(ref leaseLost, 1);
                    logger.LogError("Outbox lease lost {EventId} {EventType} with {FailureKind}",
                        message.Id, message.EventType, exception?.GetType().Name ?? "LeaseReclaimed");
                }, heartbeatStop.Token);
            Exception? failure = null;
            try
            {
                foreach (var handler in matchingHandlers)
                {
                    await handler.HandleAsync(message, handlerCancellation.Token);
                    handlerCancellation.Token.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                await heartbeatStop.CancelAsync();
                await heartbeat;
            }

            if (Volatile.Read(ref leaseLost) != 0) continue;
            if (failure is null)
            {
                if (!await store.MarkProcessedAsync(message.Id, workerId, message.AttemptCount, cancellationToken))
                    logger.LogError("Outbox completion lost its lease {EventId} {EventType}", message.Id, message.EventType);
            }
            else
            {
                var recorded = await store.MarkFailedAsync(message.Id, workerId, message.AttemptCount,
                    RetryDelayFor(message, retryDelay), failure.GetType().Name, cancellationToken);
                logger.LogError("Outbox handler failed {EventId} {EventType} with {FailureKind}",
                    message.Id, message.EventType, failure.GetType().Name);
                if (!recorded) logger.LogError("Outbox failure could not be recorded {EventId}", message.Id);
                else if (message.AttemptCount >= message.MaxAttempts)
                    logger.LogError("Outbox message dead-lettered {EventId} {EventType}", message.Id, message.EventType);
            }
        }
        return messages.Count > 0;
    }

    public static TimeSpan RetryDelayFor(OutboxMessage message, TimeSpan defaultDelay) =>
        message.EventType == "customer.alert.webhook"
            ? TimeSpan.FromSeconds(Math.Min(3600, 10 * (1 << Math.Min(9, Math.Max(0, message.AttemptCount - 1)))))
            : defaultDelay;
}

public sealed class LeasedJobDispatchCycle(
    ILeasedJobStore store,
    IEnumerable<ILeasedJobHandler> handlers,
    IOperationalLeaseRenewer renewer,
    ILogger<LeasedJobDispatchCycle> logger)
{
    public async Task<bool> DispatchAsync(
        string workerId,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var jobs = await store.ClaimAvailableAsync(workerId, 1, leaseDuration, cancellationToken);
        foreach (var job in jobs)
        {
            var handler = handlers.SingleOrDefault(candidate => string.Equals(candidate.JobType, job.JobType, StringComparison.Ordinal));
            if (handler is null)
            {
                var recorded = await store.MarkFailedAsync(job.Id, workerId, job.AttemptCount, retryDelay,
                    "NoRegisteredHandler", cancellationToken);
                logger.LogWarning("Leased job has no registered handler {JobId} {JobType}", job.Id, job.JobType);
                if (!recorded) logger.LogError("Leased job failure could not be recorded {JobId}", job.Id);
                else if (job.AttemptCount >= job.MaxAttempts)
                    logger.LogError("Leased job dead-lettered {JobId} {JobType}", job.Id, job.JobType);
                continue;
            }

            using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var heartbeatStop = new CancellationTokenSource();
            var leaseLost = 0;
            var heartbeat = OperationalLeaseHeartbeat.RunAsync(leaseDuration,
                token => renewer.RenewJobAsync(job, workerId, leaseDuration, token),
                handlerCancellation, exception =>
                {
                    Interlocked.Exchange(ref leaseLost, 1);
                    logger.LogError("Leased job lost its lease {JobId} {JobType} with {FailureKind}",
                        job.Id, job.JobType, exception?.GetType().Name ?? "LeaseReclaimed");
                }, heartbeatStop.Token);
            Exception? failure = null;
            try
            {
                await handler.HandleAsync(job, handlerCancellation.Token);
                handlerCancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                await heartbeatStop.CancelAsync();
                await heartbeat;
            }

            if (Volatile.Read(ref leaseLost) != 0) continue;
            if (failure is null)
            {
                if (!await store.MarkCompletedAsync(job.Id, workerId, job.AttemptCount, cancellationToken))
                    logger.LogError("Leased job completion lost its lease {JobId} {JobType}", job.Id, job.JobType);
            }
            else
            {
                var recorded = await store.MarkFailedAsync(job.Id, workerId, job.AttemptCount,
                    retryDelay, failure.GetType().Name, cancellationToken);
                logger.LogError("Leased job handler failed {JobId} {JobType} with {FailureKind}",
                    job.Id, job.JobType, failure.GetType().Name);
                if (!recorded) logger.LogError("Leased job failure could not be recorded {JobId}", job.Id);
                else if (job.AttemptCount >= job.MaxAttempts)
                    logger.LogError("Leased job dead-lettered {JobId} {JobType}", job.Id, job.JobType);
            }
        }
        return jobs.Count > 0;
    }
}
