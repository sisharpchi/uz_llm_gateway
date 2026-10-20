using UZLLM.Persistence;

namespace UZLLM.Worker;

public sealed class RetentionSweepWorker(
    IServiceScopeFactory scopes,
    RetentionSchedule schedule,
    ILogger<RetentionSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!schedule.Enabled)
        {
            logger.LogWarning("Metadata retention minimization disabled: no approved Retention:ScheduleReference; payload expiry continues separately");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<IRetentionSweep>();
                for (var batch = 0; batch < 20; batch++)
                {
                    var result = await sweep.SweepAsync(500, stoppingToken);
                    if (result.Total > 0)
                    {
                        logger.LogInformation("Retention sweep minimized {TraceCount} traces, {OutboxCount} outbox messages, {JobCount} jobs, {AlertCount} alerts and {ObservationCount} raw SLI observations",
                            result.RequestTraces, result.OutboxMessages, result.Jobs, result.OperationalAlerts, result.SliObservations);
                    }
                    if (result.Total < 500) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Retention sweep failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
