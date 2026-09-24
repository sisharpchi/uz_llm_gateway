using System.Net;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Audit.Infrastructure;

public sealed class PostgreSqlAuditEventStore(FoundationDbContext dbContext) : IAuditEventStore
{
    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        dbContext.Set<AuditEventEntity>().Add(new AuditEventEntity
        {
            Id = auditEvent.Id,
            OrganizationId = auditEvent.OrganizationId,
            ActorAccountId = auditEvent.ActorAccountId,
            Action = auditEvent.Action,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            IpAddress = auditEvent.IpAddress is null ? null : IPAddress.Parse(auditEvent.IpAddress),
            MetadataJson = auditEvent.MetadataJson,
            OccurredAt = auditEvent.OccurredAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
