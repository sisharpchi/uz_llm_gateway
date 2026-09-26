using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Worker;

public sealed class ExpiredPayloadDeletionWorker(IServiceScopeFactory scopes,
    ILogger<ExpiredPayloadDeletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPayloadRetentionService>();
                for (var batch = 0; batch < 20; batch++)
                {
                    var deleted = await service.DeleteExpiredAsync(500, stoppingToken);
                    if (deleted < 500) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Expired payload deletion failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
