using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Worker;

public sealed class BillingRecoverySweepWorker(IServiceScopeFactory scopes,
    ILogger<BillingRecoverySweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RecoveryCursor? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                for (var page = 0; page < 20; page++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IFinancialRecoverySweep>()
                        .SweepPageAsync(cursor, 100, stoppingToken);
                    cursor = result.Examined == 100 ? result.NextCursor : null;
                    if (cursor is null) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Financial recovery sweep failed with {FailureKind}", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
