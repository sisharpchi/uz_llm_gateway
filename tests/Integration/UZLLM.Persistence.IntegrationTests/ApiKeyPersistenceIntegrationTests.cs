using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Application;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ApiKeyPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Create_disable_and_authenticate_gateway_key_persist_only_a_fingerprint_and_audit_security_changes()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, projectId) = await SeedProjectAsync(dbContext);
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        var service = CreateService(scope.ServiceProvider, fingerprint);
        var authenticator = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(dbContext), new GatewayApiKeySecretGenerator(), fingerprint, TimeProvider.System);

        var issued = await service.CreateAsync(accountId, projectId, "Production", null);
        var authentication = await authenticator.AuthenticateAsync(issued.Secret);

        Assert.NotNull(authentication);
        Assert.Equal(projectId, authentication.ProjectId);
        var stored = await dbContext.Set<GatewayApiKeyEntity>().SingleAsync();
        Assert.NotEqual(issued.Secret, Convert.ToBase64String(stored.SecretFingerprint));
        Assert.True(await service.SetStatusAsync(accountId, issued.ApiKey.Id, GatewayApiKeyStatus.Disabled));
        Assert.Null(await authenticator.AuthenticateAsync(issued.Secret));
        var auditActions = await dbContext.Set<AuditEventEntity>()
            .Select(eventRecord => eventRecord.Action)
            .ToListAsync();

        Assert.Equal(2, auditActions.Count);
        Assert.Contains("api_key.created", auditActions);
        Assert.Contains("api_key.status_changed", auditActions);
    }

    [Fact]
    public async Task Api_key_table_enforces_project_creator_and_public_prefix_integrity()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, projectId) = await SeedProjectAsync(dbContext);
        dbContext.Set<GatewayApiKeyEntity>().Add(CreateEntity(Guid.CreateVersion7(), Guid.CreateVersion7(), accountId, "111111111111"));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.Set<GatewayApiKeyEntity>().Add(CreateEntity(Guid.CreateVersion7(), projectId, Guid.CreateVersion7(), "222222222222"));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.Set<GatewayApiKeyEntity>().Add(CreateEntity(Guid.CreateVersion7(), projectId, accountId, "333333333333"));
        await dbContext.SaveChangesAsync();
        dbContext.Set<GatewayApiKeyEntity>().Add(CreateEntity(Guid.CreateVersion7(), projectId, accountId, "333333333333"));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Authentication_rejects_an_active_key_when_its_project_is_archived()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (accountId, projectId) = await SeedProjectAsync(dbContext);
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        var service = CreateService(scope.ServiceProvider, fingerprint);
        var issued = await service.CreateAsync(accountId, projectId, "Production", null);
        await dbContext.Set<ProjectEntity>().Where(project => project.Id == projectId).ExecuteUpdateAsync(setters => setters.SetProperty(project => project.Status, "Archived"));
        var authenticator = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(dbContext), new GatewayApiKeySecretGenerator(), fingerprint, TimeProvider.System);

        Assert.Null(await authenticator.AuthenticateAsync(issued.Secret));
    }

    private static ApiKeyService CreateService(IServiceProvider services, IApiKeySecretFingerprint fingerprint)
    {
        var dbContext = services.GetRequiredService<FoundationDbContext>();
        var organizationStore = new PostgreSqlOrganizationStore(dbContext);
        var projectAccess = new ProjectAccessService(new PostgreSqlProjectStore(dbContext), new OrganizationAuthorizationService(organizationStore));
        return new ApiKeyService(
            new PostgreSqlApiKeyStore(dbContext),
            projectAccess,
            new GatewayApiKeySecretGenerator(),
            fingerprint,
            new AuditTrail(new PostgreSqlAuditEventStore(dbContext), TimeProvider.System),
            services.GetRequiredService<ITransactionCoordinator>(),
            TimeProvider.System);
    }

    private static async Task<(Guid AccountId, Guid ProjectId)> SeedProjectAsync(FoundationDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        dbContext.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity { Id = accountId, Email = $"apikey-{accountId:N}@example.uz", PasswordHash = "not-a-password", Status = "Active", CreatedAt = now, UpdatedAt = now });
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity { Id = organizationId, Name = "API key tenant", Status = "Active", CreatedAt = now });
        dbContext.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity { OrganizationId = organizationId, AccountId = accountId, Role = "Owner", Status = "Active", CreatedAt = now });
        dbContext.Set<ProjectEntity>().Add(new ProjectEntity { Id = projectId, OrganizationId = organizationId, Name = "Production", Status = "Active", SettingsJson = "{}", CreatedAt = now });
        await dbContext.SaveChangesAsync();
        return (accountId, projectId);
    }

    private static GatewayApiKeyEntity CreateEntity(Guid id, Guid projectId, Guid accountId, string prefix) => new()
    {
        Id = id,
        ProjectId = projectId,
        Name = "Production",
        Prefix = prefix,
        SecretFingerprint = RandomNumberGenerator.GetBytes(32),
        Status = "Active",
        CreatedByAccountId = accountId,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
