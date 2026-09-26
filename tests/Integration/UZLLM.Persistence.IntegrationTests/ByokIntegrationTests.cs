using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ByokIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Restrictions_accept_only_provider_models_and_audited_lifetime_spend_cap()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-policy-owner@example.uz");
        var outsider = await RegisterAsync(scope, "byok-policy-outsider@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK policy");
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var upstream = await catalog.AddProviderAsync("openai", "OpenAI");
        var model = await catalog.AddModelAsync("policy-model", "Policy model", 1000, 100,
            [CatalogCapability.Text]);
        await catalog.AddProviderModelAsync(upstream.Id, model.Id, "upstream-model", null, null);
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var key = await byok.CreateAsync(owner, org.Id, upstream.Id,
            "Restricted", "sk-restricted-example-1234");
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.SetRestrictionsAsync(
            outsider, org.Id, key.Id, ["policy-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["unmapped-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["policy-model", "policy-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["policy-model"], -1));
        var restricted = await byok.SetRestrictionsAsync(owner, org.Id,
            key.Id, ["policy-model"], 50_000);
        Assert.Equal(["policy-model"], restricted!.AllowedModels);
        Assert.Equal(50_000, restricted.SpendLimitMicroUsd);
        Assert.Equal(0, restricted.ExternalSpentMicroUsd);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.Set<AuditEventEntity>().AsNoTracking().Where(value =>
            value.ResourceId == key.Id && value.Action == "byok.restrictions.updated").ToListAsync());
        Assert.True(await byok.DisableAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.SetRestrictionsAsync(owner, org.Id, key.Id, null, null));
    }

    [Fact]
    public async Task Org_credential_lifecycle_is_masked_encrypted_audited_and_disabled()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-owner@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK tenant");
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var upstream = await catalog.AddProviderAsync("openai", "OpenAI");
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IByokCredentialResolver>();
        const string firstSecret = "sk-first-secret-value-123456";
        await Assert.ThrowsAsync<ArgumentException>(() => byok.CreateAsync(owner,
            org.Id, upstream.Id, "Short", "too-short"));
        var unsupported = await catalog.AddProviderAsync("untrusted", "Untrusted");
        await Assert.ThrowsAsync<ArgumentException>(() => byok.CreateAsync(owner,
            org.Id, unsupported.Id, "Unknown", "sk-valid-length-123456"));

        var key = await byok.CreateAsync(owner, org.Id, upstream.Id, "Primary", firstSecret);
        Assert.Equal("••••3456", key.MaskedKey);
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(key), StringComparison.Ordinal);
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(await byok.ListAsync(owner, org.Id)),
            StringComparison.Ordinal);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == key.Id);
        Assert.Equal(org.Id, row.OrganizationId);
        Assert.Equal("BYOK", row.CredentialType);
        Assert.DoesNotContain(firstSecret, Encoding.UTF8.GetString(row.EncryptedSecret));
        Assert.NotEqual(firstSecret, Encoding.UTF8.GetString(row.WrappedDataKey));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, Guid.NewGuid(), key.Id, upstream.Id));
        Assert.Equal(ByokTestStatus.Valid, await byok.TestAsync(owner, org.Id, key.Id));
        Assert.Equal("Valid", (await byok.FindAsync(owner, org.Id, key.Id))!.LastTestStatus);

        const string nextSecret = "sk-next-secret-value-987654";
        var rotated = await byok.UpdateAsync(owner, org.Id, key.Id, "Rotated", nextSecret);
        Assert.Equal("Rotated", rotated!.Name);
        Assert.Equal("••••7654", rotated.MaskedKey);
        Assert.Null(rotated.LastTestStatus);
        Assert.True(await byok.DisableAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.TestAsync(owner, org.Id, key.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => byok.UpdateAsync(owner,
            org.Id, key.Id, "Cannot edit", null));
        Assert.True(await byok.DeleteAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.FindAsync(owner, org.Id, key.Id));
        Assert.Empty(await byok.ListAsync(owner, org.Id));
        var deletedRow = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == key.Id);
        Assert.Equal("deleted", deletedRow.KeyVersion);
        Assert.NotEqual(row.EncryptedSecret, deletedRow.EncryptedSecret);
        Assert.NotNull(deletedRow.DeletedAt);
        var auditRows = await db.Set<AuditEventEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id && value.ResourceId == key.Id)
            .ToArrayAsync();
        Assert.Equal(5, auditRows.Length);
        Assert.Contains(auditRows, value => value.Action == "byok.created");
        Assert.Contains(auditRows, value => value.Action == "byok.tested");
        Assert.Contains(auditRows, value => value.Action == "byok.updated");
        Assert.Contains(auditRows, value => value.Action == "byok.disabled");
        Assert.Contains(auditRows, value => value.Action == "byok.deleted");
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(auditRows));
        Assert.DoesNotContain(nextSecret, JsonSerializer.Serialize(auditRows));
    }

    [Fact]
    public async Task Credential_grants_require_same_tenant_active_project_and_prevent_cross_project_resolution()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-grants@example.uz");
        var outsider = await RegisterAsync(scope, "byok-outsider@example.uz");
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var org = await orgs.CreateAsync(owner, "BYOK grants");
        var otherOrg = await orgs.CreateAsync(owner, "Other grants");
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var allowed = await projects.CreateAsync(owner, org.Id, "Allowed");
        var denied = await projects.CreateAsync(owner, org.Id, "Denied");
        var foreign = await projects.CreateAsync(owner, otherOrg.Id, "Foreign");
        var upstream = await scope.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync("anthropic", "Anthropic");
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IByokCredentialResolver>();
        const string secret = "sk-antropic-test-value-12345";
        var key = await byok.CreateAsync(owner, org.Id, upstream.Id, "Anthropic", secret);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.ListAsync(outsider, org.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            byok.SetProjectGrantAsync(outsider, org.Id, key.Id, allowed.Id, true));
        db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = org.Id, AccountId = outsider, Role = "Developer",
            Status = "Active", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.ListAsync(outsider, org.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            byok.SetProjectGrantAsync(outsider, org.Id, key.Id, allowed.Id, true));
        Assert.False(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, foreign.Id, true));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.Equal([allowed.Id], (await byok.FindAsync(owner, org.Id, key.Id))!.ProjectIds);
        Assert.Equal(secret, await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        var executionResolver = scope.ServiceProvider.GetRequiredService<IProviderCredentialResolver>();
        var execution = new ProviderExecutionContext(Guid.NewGuid(), upstream.Id, Guid.NewGuid(),
            key.Id, "upstream-model", TimeSpan.FromSeconds(5), org.Id, allowed.Id);
        Assert.Equal(secret, await executionResolver.ResolveSecretAsync(execution));
        Assert.Null(await executionResolver.ResolveSecretAsync(execution with { ProjectId = denied.Id }));
        Assert.Null(await executionResolver.ResolveSecretAsync(execution with { OrganizationId = otherOrg.Id,
            ProjectId = foreign.Id }));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, denied.Id,
            key.Id, upstream.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(otherOrg.Id, foreign.Id,
            key.Id, upstream.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, Guid.NewGuid()));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO gateway.provider_credential_project_grant (organization_id, credential_id, project_id, created_at) VALUES ({otherOrg.Id}, {key.Id}, {foreign.Id}, {DateTimeOffset.UtcNow})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, false));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.True(await projects.ArchiveAsync(owner, org.Id, allowed.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        Assert.False(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
    }

    [Fact]
    public async Task Failed_audit_rolls_back_credential_creation_and_never_persists_secret()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var seedProvider = CreateProvider();
        await using var seed = seedProvider.CreateAsyncScope();
        var owner = await RegisterAsync(seed, "byok-rollback@example.uz");
        var org = await seed.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK rollback");
        var upstream = await seed.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync("openai", "OpenAI");
        await using var failingProvider = CreateProvider(failAudit: true);
        await using var failingScope = failingProvider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingScope.ServiceProvider
            .GetRequiredService<IByokCredentialService>().CreateAsync(owner, org.Id,
                upstream.Id, "Should rollback", "sk-rollback-secret-123456"));
        var db = seed.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Empty(await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id).ToArrayAsync());
    }

    private ServiceProvider CreateProvider(bool failAudit = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = "v1",
            ["ProviderSecrets:Keys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var services = new ServiceCollection().AddUzllmPersistence(configuration).AddUzllmIdentity()
            .AddUzllmAudit().AddUzllmOrganizations().AddUzllmProjects()
            .AddUzllmCatalog().AddUzllmProviders(configuration)
            .AddScoped<IByokCredentialVerifier, NoTransactionVerifier>();
        if (failAudit) services.AddScoped<IAuditEventStore, FailingAuditStore>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<Guid> RegisterAsync(AsyncServiceScope scope, string email)
    {
        var registration = await scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .RegisterAsync(email, "correct horse battery staple");
        Assert.True(await scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .VerifyEmailAsync(registration.VerificationToken));
        return registration.AccountId;
    }

    private sealed class NoTransactionVerifier(FoundationDbContext db) : IByokCredentialVerifier
    {
        public Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
            CancellationToken cancellationToken = default)
        {
            Assert.Null(db.Database.CurrentTransaction);
            return Task.FromResult(ByokTestStatus.Valid);
        }
    }

    private sealed class FailingAuditStore : IAuditEventStore
    {
        public Task AppendAsync(AuditEvent auditEvent,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit storage failure.");
    }
}
