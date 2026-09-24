using System.Net;

namespace UZLLM.Persistence;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ActorAccountId { get; set; }

    public string Action { get; set; } = null!;

    public string ResourceType { get; set; } = null!;

    public Guid? ResourceId { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    public OrganizationEntity Organization { get; set; } = null!;

    public IdentityAccountEntity ActorAccount { get; set; } = null!;
}
