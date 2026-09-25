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

    [Fact]
    public async Task Rotation_replaces_secret_atomically_and_keeps_key_identity_and_generation_history()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (ownerId, projectId) = await SeedProjectAsync(db);
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        var service = CreateService(scope.ServiceProvider, fingerprint);
        var authenticator = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(db),
            new GatewayApiKeySecretGenerator(), fingerprint, TimeProvider.System);
        var original = await service.CreateAsync(ownerId, projectId, "Production", null);

        var replacement = await service.RotateAsync(ownerId, original.ApiKey.Id);

        Assert.NotNull(replacement);
        Assert.Equal(original.ApiKey.Id, replacement.ApiKey.Id);
        Assert.Equal(2, replacement.ApiKey.Generation);
        Assert.Null(await authenticator.AuthenticateAsync(original.Secret));
        Assert.Equal(original.ApiKey.Id, (await authenticator.AuthenticateAsync(replacement.Secret))!.ApiKeyId);
        var generations = await db.Set<GatewayApiKeyGenerationEntity>().AsNoTracking()
            .OrderBy(value => value.Generation).ToListAsync();
        Assert.Equal([1, 2], generations.Select(value => value.Generation));
        Assert.NotNull(generations[0].RevokedAt);
        Assert.Null(generations[1].RevokedAt);
        Assert.Equal(replacement.ApiKey.Prefix, generations[1].Prefix);
        Assert.Equal(2, await db.Set<AuditEventEntity>().CountAsync(value =>
            value.Action == "api_key.created" || value.Action == "api_key.rotated"));
    }

    [Fact]
    public async Task Two_concurrent_rotations_have_one_winner_and_old_secret_is_revoked()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        Guid ownerId;
        IssuedGatewayApiKey original;
        await using (var setup = fixture.CreateServiceProvider())
        await using (var scope = setup.CreateAsyncScope())
        {
            var seeded = await SeedProjectAsync(scope.ServiceProvider
                .GetRequiredService<FoundationDbContext>());
            ownerId = seeded.AccountId;
            original = await CreateService(scope.ServiceProvider, fingerprint)
                .CreateAsync(ownerId, seeded.ProjectId, "Production", null);
        }
        await using var one = fixture.CreateServiceProvider();
        await using var two = fixture.CreateServiceProvider();
        await using var scopeOne = one.CreateAsyncScope();
        await using var scopeTwo = two.CreateAsyncScope();
        var results = await Task.WhenAll(
            CreateService(scopeOne.ServiceProvider, fingerprint).RotateAsync(ownerId, original.ApiKey.Id),
            CreateService(scopeTwo.ServiceProvider, fingerprint).RotateAsync(ownerId, original.ApiKey.Id));

        var winner = Assert.Single(results.OfType<IssuedGatewayApiKey>());
        Assert.Equal(original.ApiKey.Id, winner.ApiKey.Id);
        await using var verifier = fixture.CreateServiceProvider();
        await using var verifyScope = verifier.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var auth = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(db), new GatewayApiKeySecretGenerator(),
            fingerprint, TimeProvider.System);
        Assert.Null(await auth.AuthenticateAsync(original.Secret));
        Assert.NotNull(await auth.AuthenticateAsync(winner.Secret));
        Assert.Equal(2, await db.Set<GatewayApiKeyGenerationEntity>().CountAsync());
        Assert.Equal(1, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "api_key.rotated"));
    }

    [Fact]
    public async Task Concurrent_disable_and_rotation_leave_no_authenticatable_secret()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        Guid ownerId;
        IssuedGatewayApiKey original;
        await using (var setup = fixture.CreateServiceProvider())
        await using (var scope = setup.CreateAsyncScope())
        {
            var seeded = await SeedProjectAsync(scope.ServiceProvider
                .GetRequiredService<FoundationDbContext>());
            ownerId = seeded.AccountId;
            original = await CreateService(scope.ServiceProvider, fingerprint)
                .CreateAsync(ownerId, seeded.ProjectId, "Production", null);
        }
        await using var one = fixture.CreateServiceProvider();
        await using var two = fixture.CreateServiceProvider();
        await using var scopeOne = one.CreateAsyncScope();
        await using var scopeTwo = two.CreateAsyncScope();
        var rotate = CreateService(scopeOne.ServiceProvider, fingerprint).RotateAsync(ownerId, original.ApiKey.Id);
        var disable = CreateService(scopeTwo.ServiceProvider, fingerprint).SetStatusAsync(
            ownerId, original.ApiKey.Id, GatewayApiKeyStatus.Disabled);
        await Task.WhenAll(rotate, disable);

        Assert.True(await disable);
        await using var verifier = fixture.CreateServiceProvider();
        await using var verifyScope = verifier.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var auth = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(db), new GatewayApiKeySecretGenerator(),
            fingerprint, TimeProvider.System);
        Assert.Null(await auth.AuthenticateAsync(original.Secret));
        if (await rotate is { } replacement) Assert.Null(await auth.AuthenticateAsync(replacement.Secret));
        Assert.Equal("Disabled", (await db.Set<GatewayApiKeyEntity>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Rotation_migration_backfills_preexisting_key_as_generation_one()
    {
        await fixture.ResetMigrationsAsync();
        await using (var before = fixture.CreateServiceProvider())
        await using (var scope = before.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
                .MigrateAsync("20260925192424_AddRecurringBudgetWindows");
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var (ownerId, projectId) = await SeedProjectAsync(db);
            var keyId = Guid.CreateVersion7();
            var prefix = "abcdef123456";
            var name = "Production";
            var status = "Active";
            var fingerprint = RandomNumberGenerator.GetBytes(32);
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO gateway.api_key
                    (id, project_id, name, key_prefix, secret_fingerprint, status, created_by, created_at)
                VALUES ({keyId}, {projectId}, {name}, {prefix}, {fingerprint}, {status}, {ownerId}, {now});
                """);
        }
        await fixture.ApplyMigrationsAsync();
        await using var after = fixture.CreateServiceProvider();
        await using var verifyScope = after.CreateAsyncScope();
        var verified = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var key = await verified.Set<GatewayApiKeyEntity>().SingleAsync();
        var history = await verified.Set<GatewayApiKeyGenerationEntity>().SingleAsync();
        Assert.Equal(1, key.Generation);
        Assert.Equal(key.Id, history.ApiKeyId);
        Assert.Equal(key.Prefix, history.Prefix);
        Assert.Null(history.RevokedAt);
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
