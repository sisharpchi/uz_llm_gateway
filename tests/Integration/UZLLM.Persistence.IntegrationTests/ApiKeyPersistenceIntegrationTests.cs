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
    public async Task Concurrent_permissions_update_and_secret_rotation_preserve_the_new_policy()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        Guid ownerId;
        IssuedGatewayApiKey issued;
        await using (var setup = fixture.CreateServiceProvider())
        await using (var setupScope = setup.CreateAsyncScope())
        {
            var seeded = await SeedProjectAsync(setupScope.ServiceProvider.GetRequiredService<FoundationDbContext>());
            ownerId = seeded.AccountId;
            issued = await CreateService(setupScope.ServiceProvider, fingerprint).CreateAsync(ownerId, seeded.ProjectId, "Production", null);
        }
        await using var first = fixture.CreateServiceProvider();
        await using var second = fixture.CreateServiceProvider();
        await using var firstScope = first.CreateAsyncScope();
        await using var secondScope = second.CreateAsyncScope();
        var gate = new ConcurrentApiKeyReadGate();
        var rotation = CreateService(firstScope.ServiceProvider, fingerprint,
            new GatedApiKeyStore(new PostgreSqlApiKeyStore(firstScope.ServiceProvider.GetRequiredService<FoundationDbContext>()), gate))
            .RotateAsync(ownerId, issued.ApiKey.Id);
        var update = CreateService(secondScope.ServiceProvider, fingerprint,
            new GatedApiKeyStore(new PostgreSqlApiKeyStore(secondScope.ServiceProvider.GetRequiredService<FoundationDbContext>()), gate))
            .SetPermissionsAsync(ownerId, issued.ApiKey.Id, new(AllowedModels: ["gpt-test"], DeniedProviders: ["anthropic"]));
        await Task.WhenAll(rotation, update);
        Assert.True(await update);
        var replacement = Assert.IsType<IssuedGatewayApiKey>(await rotation);
        var authenticator = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(firstScope.ServiceProvider.GetRequiredService<FoundationDbContext>()),
            new GatewayApiKeySecretGenerator(), fingerprint, TimeProvider.System);
        Assert.Null(await authenticator.AuthenticateAsync(issued.Secret));
        var current = Assert.IsType<GatewayApiKeyAuthentication>(await authenticator.AuthenticateAsync(replacement.Secret));
        Assert.Equal(["gpt-test"], current.Permissions!.AllowedModels);
        Assert.Equal(["anthropic"], current.Permissions.DeniedProviders);
    }

    [Fact]
    public async Task Permissions_round_trip_refresh_authentication_preserve_rotation_and_rollback_with_audit()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (ownerId, projectId) = await SeedProjectAsync(db);
        var service = CreateService(scope.ServiceProvider, fingerprint);
        var issued = await service.CreateAsync(ownerId, projectId, "Restricted", null);
        var authenticator = new ApiKeyAuthenticator(new PostgreSqlApiKeyStore(db),
            new GatewayApiKeySecretGenerator(), fingerprint, TimeProvider.System);
        Assert.Null((await authenticator.AuthenticateAsync(issued.Secret))!.Permissions!.AllowedModels);
        await Assert.ThrowsAsync<UZLLM.Modules.Organizations.Contracts.TenantAccessDeniedException>(() =>
            service.SetPermissionsAsync(Guid.NewGuid(), issued.ApiKey.Id, new(AllowedModels: [])));
        Assert.True(await service.SetPermissionsAsync(ownerId, issued.ApiKey.Id,
            new(AllowedModels: [" GPT-Test "], DeniedModels: ["other-test"],
                AllowedProviders: ["openai", "anthropic"], DeniedProviders: ["anthropic"])));
        var current = (await authenticator.AuthenticateAsync(issued.Secret))!.Permissions!;
        Assert.Equal(["gpt-test"], current.AllowedModels);
        Assert.Equal(["other-test"], current.DeniedModels);
        Assert.Equal(["anthropic", "openai"], current.AllowedProviders);
        Assert.Equal(["anthropic"], current.DeniedProviders);
        var replacement = (await service.RotateAsync(ownerId, issued.ApiKey.Id))!;
        Assert.Null(await authenticator.AuthenticateAsync(issued.Secret));
        Assert.Equal(current.AllowedModels, (await authenticator.AuthenticateAsync(replacement.Secret))!.Permissions!.AllowedModels);
        // A failure after the permission write but before audit/commit must restore both.
        var failure = CreateService(scope.ServiceProvider, fingerprint, new FailingPermissionsStore(new PostgreSqlApiKeyStore(db)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failure.SetPermissionsAsync(ownerId, issued.ApiKey.Id,
            new(AllowedModels: [])));
        Assert.Equal(current.AllowedModels, (await authenticator.AuthenticateAsync(replacement.Secret))!.Permissions!.AllowedModels);
        Assert.Single(await db.Set<AuditEventEntity>().Where(value => value.Action == "api_key.permissions_changed").ToListAsync());
        Assert.True(await service.SetPermissionsAsync(ownerId, issued.ApiKey.Id, new(AllowedModels: [])));
        Assert.Empty((await authenticator.AuthenticateAsync(replacement.Secret))!.Permissions!.AllowedModels!);
        Assert.True(await service.SetStatusAsync(ownerId, issued.ApiKey.Id, GatewayApiKeyStatus.Disabled));
        Assert.Null(await authenticator.AuthenticateAsync(replacement.Secret));
        Assert.DoesNotContain(replacement.Secret, string.Join(' ', await db.Set<AuditEventEntity>().Select(value => value.MetadataJson).ToListAsync()));
    }

    [Fact]
    public async Task Permissions_migration_preserves_legacy_access_and_refuses_unsafe_downgrade()
    {
        await fixture.ResetMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync("20261011103552_AddDurableUsageRollups");
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var (ownerId, projectId) = await SeedProjectAsync(db);
        var keyId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO gateway.api_key (id, project_id, name, key_prefix, secret_fingerprint, status, created_by, created_at)
            VALUES ({keyId}, {projectId}, {"Legacy"}, {"123456abcdef"}, {RandomNumberGenerator.GetBytes(32)},
                {"Active"}, {ownerId}, {DateTimeOffset.UtcNow});
            """);
        await migrator.MigrateAsync();
        var store = new PostgreSqlApiKeyStore(db);
        var legacy = (await store.FindByIdAsync(keyId))!;
        Assert.Null(legacy.Permissions!.AllowedModels);
        Assert.Null(legacy.Permissions.DeniedModels);
        Assert.Null(legacy.Permissions.AllowedProviders);
        Assert.Null(legacy.Permissions.DeniedProviders);
        Assert.True(await store.TrySetPermissionsAsync(keyId, new(AllowedProviders: [])));
        var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20261011103552_AddDurableUsageRollups"));
        Assert.Contains("permissions cannot be downgraded", downgrade.ToString(), StringComparison.Ordinal);
        Assert.Empty((await store.FindByIdAsync(keyId))!.Permissions!.AllowedProviders!);
    }

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
        var readGate = new ConcurrentApiKeyReadGate();
        var results = await Task.WhenAll(
            CreateService(scopeOne.ServiceProvider, fingerprint, new GatedApiKeyStore(
                new PostgreSqlApiKeyStore(scopeOne.ServiceProvider.GetRequiredService<FoundationDbContext>()), readGate))
                .RotateAsync(ownerId, original.ApiKey.Id),
            CreateService(scopeTwo.ServiceProvider, fingerprint, new GatedApiKeyStore(
                new PostgreSqlApiKeyStore(scopeTwo.ServiceProvider.GetRequiredService<FoundationDbContext>()), readGate))
                .RotateAsync(ownerId, original.ApiKey.Id));

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

    [Fact]
    public async Task Fingerprint_version_migration_backfills_v1_and_staged_v2_authenticates_old_and_new_keys()
    {
        await fixture.ResetMigrationsAsync();
        var first = RandomNumberGenerator.GetBytes(32);
        var second = RandomNumberGenerator.GetBytes(32);
        var ring = new Dictionary<int, byte[]> { [1] = first, [2] = second };
        var legacyFingerprint = new HmacApiKeySecretFingerprint(1, ring);
        var legacySecret = new GatewayApiKeySecretGenerator().Create();
        Guid ownerId;
        Guid projectId;
        Guid legacyKeyId;
        await using (var before = fixture.CreateServiceProvider())
        await using (var scope = before.CreateAsyncScope())
        {
            var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync("20260928211017_AddSettlementWalletRefunds");
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            (ownerId, projectId) = await SeedProjectAsync(db);
            legacyKeyId = Guid.CreateVersion7();
            var fingerprint = legacyFingerprint.Create(legacySecret.Value);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO gateway.api_key
                    (id, project_id, name, key_prefix, secret_fingerprint, status, created_by, created_at)
                VALUES ({legacyKeyId}, {projectId}, {"Legacy"}, {legacySecret.Prefix}, {fingerprint},
                    {"Active"}, {ownerId}, {DateTimeOffset.UtcNow});
                """);
        }

        await fixture.ApplyMigrationsAsync();
        await using var after = fixture.CreateServiceProvider();
        await using var verifyScope = after.CreateAsyncScope();
        var verified = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, (await verified.Set<GatewayApiKeyEntity>().SingleAsync(value => value.Id == legacyKeyId)).FingerprintKeyVersion);
        var activeFingerprint = new HmacApiKeySecretFingerprint(2, ring);
        var store = new PostgreSqlApiKeyStore(verified);
        var authenticator = new ApiKeyAuthenticator(store, new GatewayApiKeySecretGenerator(),
            activeFingerprint, TimeProvider.System);
        Assert.NotNull(await authenticator.AuthenticateAsync(legacySecret.Value));
        var issued = await CreateService(verifyScope.ServiceProvider, activeFingerprint)
            .CreateAsync(ownerId, projectId, "Current", null);
        var persisted = await verified.Set<GatewayApiKeyEntity>().SingleAsync(value => value.Id == issued.ApiKey.Id);
        Assert.Equal(2, persisted.FingerprintKeyVersion);
        Assert.NotEqual(issued.Secret, Convert.ToBase64String(persisted.SecretFingerprint));
        Assert.DoesNotContain(issued.Secret, string.Join(' ', await verified.Set<AuditEventEntity>()
            .Select(value => value.MetadataJson).ToListAsync()), StringComparison.Ordinal);
        Assert.NotNull(await authenticator.AuthenticateAsync(issued.Secret));
        Assert.True(await CreateService(verifyScope.ServiceProvider, activeFingerprint)
            .SetStatusAsync(ownerId, legacyKeyId, GatewayApiKeyStatus.Disabled));
        Assert.Null(await authenticator.AuthenticateAsync(legacySecret.Value));
        Assert.NotNull(await authenticator.AuthenticateAsync(issued.Secret));
        var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => verifyScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
            .MigrateAsync("20260928211017_AddSettlementWalletRefunds"));
        Assert.Contains("rotated fingerprint keys cannot be downgraded", downgrade.ToString(), StringComparison.Ordinal);
        // Newer reversible migrations may have downgraded before the historical
        // fingerprint guard fails; inspect only the guarded column at this version.
        Assert.Equal(2, await verified.Set<GatewayApiKeyEntity>().Where(value => value.Id == issued.ApiKey.Id)
            .Select(value => value.FingerprintKeyVersion).SingleAsync());
    }

    private static ApiKeyService CreateService(IServiceProvider services, IApiKeySecretFingerprint fingerprint,
        IApiKeyStore? store = null)
    {
        var dbContext = services.GetRequiredService<FoundationDbContext>();
        var organizationStore = new PostgreSqlOrganizationStore(dbContext);
        var projectAccess = new ProjectAccessService(new PostgreSqlProjectStore(dbContext), new OrganizationAuthorizationService(organizationStore));
        return new ApiKeyService(
            store ?? new PostgreSqlApiKeyStore(dbContext),
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

internal sealed class FailingPermissionsStore(IApiKeyStore inner) : IApiKeyStore
{
    public Task<bool> TryCreateAsync(StoredGatewayApiKey key, CancellationToken cancellationToken = default) => inner.TryCreateAsync(key, cancellationToken);
    public Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ListForProjectAsync(projectId, cancellationToken);
    public Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default) => inner.FindByIdAsync(apiKeyId, cancellationToken);
    public Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default) => inner.FindAuthenticationCandidateAsync(prefix, cancellationToken);
    public Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default) => inner.TrySetStatusAsync(apiKeyId, status, cancellationToken);
    public Task<ApiKeyRotationStoreResult> TryRotateAsync(Guid apiKeyId, int expectedGeneration, string newPrefix, byte[] fingerprint, int version,
        DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryRotateAsync(apiKeyId, expectedGeneration, newPrefix, fingerprint, version, now, cancellationToken);
    public async Task<bool> TrySetPermissionsAsync(Guid apiKeyId, ApiKeyPermissions permissions, CancellationToken cancellationToken = default)
    {
        Assert.True(await inner.TrySetPermissionsAsync(apiKeyId, permissions, cancellationToken));
        throw new InvalidOperationException("Simulated failure before audit/commit");
    }
}

internal sealed class ConcurrentApiKeyReadGate
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }
}

internal sealed class GatedApiKeyStore(IApiKeyStore inner, ConcurrentApiKeyReadGate gate) : IApiKeyStore
{
    public Task<bool> TrySetPermissionsAsync(Guid apiKeyId, ApiKeyPermissions permissions, CancellationToken cancellationToken = default) =>
        inner.TrySetPermissionsAsync(apiKeyId, permissions, cancellationToken);
    public Task<bool> TryCreateAsync(StoredGatewayApiKey key, CancellationToken cancellationToken = default) =>
        inner.TryCreateAsync(key, cancellationToken);
    public Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        inner.ListForProjectAsync(projectId, cancellationToken);
    public async Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var key = await inner.FindByIdAsync(apiKeyId, cancellationToken);
        await gate.WaitAsync(cancellationToken);
        return key;
    }
    public Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default) =>
        inner.FindAuthenticationCandidateAsync(prefix, cancellationToken);
    public Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default) =>
        inner.TrySetStatusAsync(apiKeyId, status, cancellationToken);
    public Task<ApiKeyRotationStoreResult> TryRotateAsync(Guid apiKeyId, int expectedGeneration, string newPrefix,
        byte[] newFingerprint, int fingerprintKeyVersion, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        inner.TryRotateAsync(apiKeyId, expectedGeneration, newPrefix, newFingerprint, fingerprintKeyVersion, now,
            cancellationToken);
}
