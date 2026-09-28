using Microsoft.Extensions.DependencyInjection;
using UZLLM.Persistence;

namespace UZLLM.Worker;

public interface IOperationalLeaseRenewer
{
    Task<bool> RenewOutboxAsync(OutboxMessage message, string owner, TimeSpan duration,
        CancellationToken cancellationToken);
    Task<bool> RenewJobAsync(LeasedJob job, string owner, TimeSpan duration,
        CancellationToken cancellationToken);
}

public sealed class ScopedOperationalLeaseRenewer(IServiceScopeFactory scopeFactory) : IOperationalLeaseRenewer
{
    public async Task<bool> RenewOutboxAsync(OutboxMessage message, string owner, TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .RenewLeaseAsync(message.Id, owner, message.AttemptCount, duration, cancellationToken);
    }

    public async Task<bool> RenewJobAsync(LeasedJob job, string owner, TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILeasedJobStore>()
            .RenewLeaseAsync(job.Id, owner, job.AttemptCount, duration, cancellationToken);
    }
}

internal static class OperationalLeaseHeartbeat
{
    public static async Task RunAsync(TimeSpan leaseDuration,
        Func<CancellationToken, Task<bool>> renew,
        CancellationTokenSource handlerCancellation,
        Action<Exception?> onLeaseLost,
        CancellationToken stop)
    {
        var period = TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 3));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    if (await renew(stop)) continue;
                    onLeaseLost(null);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    onLeaseLost(exception);
                }
                handlerCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
}
