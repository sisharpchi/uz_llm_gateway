namespace UZLLM.Modules.Audit.Contracts;

public sealed record AuditEventInput(
    Guid OrganizationId,
    Guid ActorAccountId,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string? IpAddress,
    string? MetadataJson);

public sealed record AuditEvent(
    Guid Id,
    Guid OrganizationId,
    Guid ActorAccountId,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string? IpAddress,
    string MetadataJson,
    DateTimeOffset OccurredAt);

public interface IAuditEventStore
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAuditTrail
{
    Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default);
}
