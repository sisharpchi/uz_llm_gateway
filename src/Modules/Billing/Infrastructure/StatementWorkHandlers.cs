using System.Text.Json;
using Microsoft.Extensions.Logging;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class StatementBuildJobHandler(IStatementPublicationStore store) : ILeasedJobHandler
{
    public string JobType => PostgreSqlStatementPublicationStore.JobType;
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(job.Payload);
        await store.BuildAsync(payload.RootElement.GetProperty("operationId").GetGuid(), job.AttemptCount, cancellationToken);
    }
}

/// <summary>Committed lifecycle acknowledgement, not a promise of customer notification delivery.</summary>
public sealed class StatementPublishedEventHandler(ITransactionCoordinator transactions, IConsumerInboxStore inbox,
    ILogger<StatementPublishedEventHandler> logger) : IOutboxHandler
{
    public string EventType => PostgreSqlStatementPublicationStore.PublishedEvent;
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var tx = await transactions.BeginAsync(cancellationToken);
        if (!await inbox.TryRecordProcessedAsync("statement-publications", message.Id, cancellationToken)) return;
        await tx.CommitAsync(cancellationToken);
        logger.LogInformation("Statement publication lifecycle acknowledged {EventId}", message.Id);
    }
}
