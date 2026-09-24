using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Application;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class AuditPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task ArchiveAsync_commits_project_state_and_audit_event_together()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, organizationId, projectId) = await SeedProjectAsync(dbContext);
        var projectService = CreateProjectService(dbContext, scope.ServiceProvider);

        Assert.True(await projectService.ArchiveAsync(accountId, organizationId, projectId));

        dbContext.ChangeTracker.Clear();
        var project = await dbContext.Set<ProjectEntity>().SingleAsync(candidate => candidate.Id == projectId);
        var auditEvent = await dbContext.Set<AuditEventEntity>().SingleAsync();
        Assert.Equal("Archived", project.Status);
        Assert.NotNull(project.ArchivedAt);
        Assert.Equal("project.archived", auditEvent.Action);
        Assert.Equal(projectId, auditEvent.ResourceId);
        Assert.Equal(organizationId, auditEvent.OrganizationId);
        Assert.Equal(accountId, auditEvent.ActorAccountId);
    }

    [Fact]
    public async Task ArchiveAsync_rolls_back_the_project_when_audit_write_fails()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, organizationId, projectId) = await SeedProjectAsync(dbContext);
        var projectService = CreateProjectService(dbContext, scope.ServiceProvider, new ThrowingAuditTrail());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            projectService.ArchiveAsync(accountId, organizationId, projectId));

        dbContext.ChangeTracker.Clear();
        var project = await dbContext.Set<ProjectEntity>().SingleAsync(candidate => candidate.Id == projectId);
        Assert.Equal("Active", project.Status);
        Assert.Null(project.ArchivedAt);
        Assert.Empty(await dbContext.Set<AuditEventEntity>().ToListAsync());
    }

    [Fact]
    public async Task Audit_event_database_trigger_rejects_updates_and_deletes()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, organizationId, projectId) = await SeedProjectAsync(dbContext);
        var trail = new AuditTrail(new PostgreSqlAuditEventStore(dbContext), TimeProvider.System);
        var auditEvent = await trail.RecordAsync(new AuditEventInput(
            organizationId,
            accountId,
            "project.archived",
            "project",
            projectId,
            null,
            "{}"));

        var updateException = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit.audit_event SET action = {"changed"} WHERE id = {auditEvent.Id}"));
        var deleteException = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit.audit_event WHERE id = {auditEvent.Id}"));

        Assert.Equal(PostgresErrorCodes.RaiseException, updateException.SqlState);
        Assert.Equal(PostgresErrorCodes.RaiseException, deleteException.SqlState);
    }

    [Fact]
    public async Task Audit_event_foreign_keys_reject_unknown_organization_and_actor()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        dbContext.Set<AuditEventEntity>().Add(CreateAuditEvent(Guid.CreateVersion7(), Guid.CreateVersion7()));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        var organizationId = Guid.CreateVersion7();
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organizationId,
            Name = "Audit tenant",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        dbContext.Set<AuditEventEntity>().Add(CreateAuditEvent(organizationId, Guid.CreateVersion7()));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static ProjectService CreateProjectService(
        FoundationDbContext dbContext,
        IServiceProvider services,
        IAuditTrail? auditTrail = null) =>
        new(
            new PostgreSqlProjectStore(dbContext),
            new OrganizationAuthorizationService(new PostgreSqlOrganizationStore(dbContext)),
            auditTrail ?? new AuditTrail(new PostgreSqlAuditEventStore(dbContext), TimeProvider.System),
            services.GetRequiredService<ITransactionCoordinator>(),
            TimeProvider.System);

    private static async Task<(Guid AccountId, Guid OrganizationId, Guid ProjectId)> SeedProjectAsync(FoundationDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        dbContext.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = accountId,
            Email = $"audit-{accountId:N}@example.uz",
            PasswordHash = "not-a-password",
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organizationId,
            Name = "Audit tenant",
            Status = "Active",
            CreatedAt = now
        });
        dbContext.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = organizationId,
            AccountId = accountId,
            Role = "Owner",
            Status = "Active",
            CreatedAt = now
        });
        dbContext.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = projectId,
            OrganizationId = organizationId,
            Name = "Production",
            Status = "Active",
            SettingsJson = "{}",
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return (accountId, organizationId, projectId);
    }

    private static AuditEventEntity CreateAuditEvent(Guid organizationId, Guid accountId) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = organizationId,
        ActorAccountId = accountId,
        Action = "project.archived",
        ResourceType = "project",
        MetadataJson = "{}",
        OccurredAt = DateTimeOffset.UtcNow
    };

    private sealed class ThrowingAuditTrail : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Audit persistence failed.");
    }
}
