using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Contracts;

namespace UZLLM.Audit.Tests;

public sealed class AuditTrailTests
{
    [Fact]
    public async Task RecordAsync_appends_a_tenant_scoped_event_with_normalized_metadata()
    {
        var store = new InMemoryAuditEventStore();
        var trail = new AuditTrail(store, new FixedAuditTimeProvider());
        var organizationId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();

        var auditEvent = await trail.RecordAsync(new AuditEventInput(
            organizationId,
            accountId,
            " project.archived ",
            " project ",
            projectId,
            "127.0.0.1",
            null));

        Assert.Equal(organizationId, auditEvent.OrganizationId);
        Assert.Equal(accountId, auditEvent.ActorAccountId);
        Assert.Equal("project.archived", auditEvent.Action);
        Assert.Equal("project", auditEvent.ResourceType);
        Assert.Equal(projectId, auditEvent.ResourceId);
        Assert.Equal("127.0.0.1", auditEvent.IpAddress);
        Assert.Equal("{}", auditEvent.MetadataJson);
        Assert.Equal(auditEvent, Assert.Single(store.Events));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task RecordAsync_rejects_non_object_or_invalid_metadata(string metadataJson)
    {
        var trail = new AuditTrail(new InMemoryAuditEventStore(), new FixedAuditTimeProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => trail.RecordAsync(CreateInput(metadataJson: metadataJson)));
    }

    [Fact]
    public async Task RecordAsync_rejects_invalid_actor_and_ip_address()
    {
        var trail = new AuditTrail(new InMemoryAuditEventStore(), new FixedAuditTimeProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => trail.RecordAsync(CreateInput(actorAccountId: Guid.Empty)));
        await Assert.ThrowsAsync<ArgumentException>(() => trail.RecordAsync(CreateInput(ipAddress: "not-an-ip")));
    }

    [Theory]
    [InlineData("", "project")]
    [InlineData("project.archived", "")]
    public async Task RecordAsync_rejects_blank_action_or_resource_type(string action, string resourceType)
    {
        var trail = new AuditTrail(new InMemoryAuditEventStore(), new FixedAuditTimeProvider());
        var input = CreateInput() with { Action = action, ResourceType = resourceType };

        await Assert.ThrowsAsync<ArgumentException>(() => trail.RecordAsync(input));
    }

    private static AuditEventInput CreateInput(
        Guid? actorAccountId = null,
        string? metadataJson = "{}",
        string? ipAddress = null) => new(
        Guid.CreateVersion7(),
        actorAccountId ?? Guid.CreateVersion7(),
        "project.archived",
        "project",
        Guid.CreateVersion7(),
        ipAddress,
        metadataJson);
}

internal sealed class InMemoryAuditEventStore : IAuditEventStore
{
    public List<AuditEvent> Events { get; } = [];

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal sealed class FixedAuditTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 14, 0, 0, TimeSpan.Zero);
}
