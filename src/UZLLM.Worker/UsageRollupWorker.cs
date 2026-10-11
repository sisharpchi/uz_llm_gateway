using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Worker;

public sealed class UsageRollupCycle(IUsageRollupStore store)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var rebuilt = 0;
        for (var page = 0; page < 20; page++)
        {
            var batch = await store.RebuildBatchAsync(100, cancellationToken);
            rebuilt += batch.BucketsRebuilt;
            if (batch.Busy || batch.RequestsExamined < 100) break;
        }
        return rebuilt;
    }
}

public sealed class UsageRollupWorker(IServiceScopeFactory scopes,
    ILogger<UsageRollupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<UsageRollupCycle>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Queue and old snapshot survive; do not print SQL, payloads or connection details.
                logger.LogError("Usage rollup cycle failed with {FailureKind}", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
