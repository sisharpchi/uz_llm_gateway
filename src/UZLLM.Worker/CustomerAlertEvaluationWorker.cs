using UZLLM.Modules.Notifications.Infrastructure;

namespace UZLLM.Worker;

public sealed class CustomerAlertEvaluationWorker(IServiceScopeFactory scopes,
    ILogger<CustomerAlertEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>()
                    .EvaluateDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Customer alert evaluation failed with {FailureKind}", exception.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
