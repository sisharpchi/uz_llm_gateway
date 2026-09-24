using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class BillingReconciliationJobHandler(IFinancialService financial, string jobType) : ILeasedJobHandler
{
    public string JobType { get; } = jobType;

    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(job.Payload);
        var reservationId = document.RootElement.GetProperty("reservationId").GetGuid();
        await financial.ReconcileAsync(reservationId, cancellationToken);
    }
}

public sealed class BillingUsageEvidenceHandler(IFinancialService financial, string eventType) : IOutboxHandler
{
    public string EventType { get; } = eventType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message.Payload);
        var requestId = document.RootElement.GetProperty("requestId").GetGuid();
        var reservationId = await financial.FindReservationIdAsync(requestId, cancellationToken);
        if (reservationId is not null)
        {
            var result = await financial.FinalizeAsync(reservationId.Value, cancellationToken);
            if (result.Status == FinalizationStatus.AlreadyFinalized && EventType == "usage.evidence.verified")
                await financial.RecordLateExposureAsync(
                    document.RootElement.GetProperty("evidenceId").GetGuid(), cancellationToken);
        }
    }
}

public sealed class BillingFinancialAlertHandler(IOperationalAlertPublisher alerts,
    ITransactionCoordinator transactions, IConsumerInboxStore inbox, string eventType) : IOutboxHandler
{
    public string EventType { get; } = eventType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await inbox.TryRecordProcessedAsync("billing-financial-alerts", message.Id, cancellationToken)) return;
        using var document = JsonDocument.Parse(message.Payload);
        if (EventType == "billing.reservation.finalized")
        {
            var unresolved = document.RootElement.GetProperty("UnresolvedUsage").GetBoolean();
            var exposure = document.RootElement.GetProperty("exposureMicroUsd").GetInt64();
            if (unresolved || exposure > 0)
                await alerts.RaiseAsync(OperationalAlertKind.FinancialExposure,
                    document.RootElement.GetProperty("reservationId").GetGuid().ToString("N"),
                    JsonSerializer.Serialize(new { unresolved, exposureMicroUsd = exposure }), cancellationToken);
        }
        else if (EventType == "billing.reversal.applied")
        {
            var debt = document.RootElement.GetProperty("debtMicroUsd").GetInt64();
            if (debt > 0)
                await alerts.RaiseAsync(OperationalAlertKind.RecoveryDebt,
                    document.RootElement.GetProperty("externalReferenceId").GetGuid().ToString("N"),
                    JsonSerializer.Serialize(new { debtMicroUsd = debt }), cancellationToken);
        }
        else if (EventType == "billing.late_exposure.recorded")
        {
            var exposure = document.RootElement.GetProperty("exposureMicroUsd").GetInt64();
            await alerts.RaiseAsync(OperationalAlertKind.FinancialExposure,
                document.RootElement.GetProperty("evidenceId").GetGuid().ToString("N"),
                JsonSerializer.Serialize(new { lateEvidence = true, exposureMicroUsd = exposure }), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
