using System.Text.Json;
using Microsoft.Extensions.Logging;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Infrastructure;

public sealed class PaymentReconciliationJobHandler(IPaymentService payments) : ILeasedJobHandler
{
    public string JobType => "payment.reconcile";

    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(job.Payload);
        await payments.ReconcileAsync(document.RootElement.GetProperty("intentId").GetGuid(), cancellationToken);
    }
}

public sealed class PaymentEventLogHandler(ILogger<PaymentEventLogHandler> logger,
    ITransactionCoordinator transactions, IConsumerInboxStore inbox, string eventType) : IOutboxHandler
{
    public string EventType { get; } = eventType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await inbox.TryRecordProcessedAsync("payment-events", message.Id, cancellationToken)) return;
        using var document = JsonDocument.Parse(message.Payload);
        var intentId = document.RootElement.GetProperty("intentId").GetGuid();
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Payment event {EventType} for intent {IntentId}", EventType, intentId);
    }
}
