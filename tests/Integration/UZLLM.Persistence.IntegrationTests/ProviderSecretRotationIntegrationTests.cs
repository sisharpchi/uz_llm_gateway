using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Providers.Application;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ProviderSecretRotationIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Operator_keyring_rotation_requires_mfa_and_records_start_and_completion()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var services = CreateServices("v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync("keyring-operator@example.uz", "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var directory = Path.Combine(Path.GetTempPath(), "uzllm-keyring-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "keyring.pfx");
        const string password = "test-only-keyring-password";
        try
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=UZLLM rotation test", rsa,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(2));
                File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pkcs12, password));
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = directory,
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = password
            }).Build();
            using var keyRing = new ServiceCollection().AddUzllmOperatorKeyRing(config, true)
                .BuildServiceProvider();
            var keyManager = keyRing.GetRequiredService<IKeyManager>();
            var rotation = new OperatorKeyRingRotation(db, keyManager,
                scope.ServiceProvider.GetRequiredService<IAuditTrail>(), TimeProvider.System);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rotation.RotateAsync(registration.AccountId));
            Assert.Empty(await db.Set<AuditEventEntity>().AsNoTracking().Where(value =>
                value.Action.StartsWith("identity.keyring.rotation.")).ToArrayAsync());
            db.Set<IdentityOperatorAccessEntity>().Add(new IdentityOperatorAccessEntity
            {
                AccountId = registration.AccountId, IsActive = true,
                MfaEnabledAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var keyId = await rotation.RotateAsync(registration.AccountId);
            Assert.Contains(keyManager.GetAllKeys(), key => key.KeyId == keyId);
            var events = await db.Set<AuditEventEntity>().AsNoTracking().Where(value =>
                value.Action.StartsWith("identity.keyring.rotation.")).OrderBy(value => value.OccurredAt)
                .ToArrayAsync();
            Assert.Equal(["identity.keyring.rotation.started", "identity.keyring.rotation.completed"],
                events.Select(value => value.Action));
            Assert.All(events, value => Assert.Equal(registration.AccountId, value.ActorAccountId));
            Assert.Equal(keyId, events[1].ResourceId);
            var protectedSecret = new DataProtectionIdentitySecretProtector(
                keyRing.GetRequiredService<IDataProtectionProvider>()).Protect("new-mfa-secret");
            Assert.Equal("new-mfa-secret", new DataProtectionIdentitySecretProtector(
                keyRing.GetRequiredService<IDataProtectionProvider>()).Unprotect(protectedSecret));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Bounded_rotation_and_rollback_preserve_platform_and_tenant_secrets_with_audit()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var firstKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var nextKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var services = CreateServices("v1", firstKey, nextKey);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync("rotation-operator@example.uz", "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        db.Set<IdentityOperatorAccessEntity>().Add(new IdentityOperatorAccessEntity
        {
            AccountId = registration.AccountId, IsActive = true,
            MfaEnabledAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var tenant = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(registration.AccountId, "Rotation tenant");
        var provider = await scope.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync("openai", "OpenAI");
        var platform = await scope.ServiceProvider.GetRequiredService<IPlatformCredentialService>()
            .CreateAsync(provider.Id, "sk-platform-rotation-123456");
        var disabledPlatform = await scope.ServiceProvider.GetRequiredService<IPlatformCredentialService>()
            .CreateAsync(provider.Id, "sk-disabled-rotation-123456");
        Assert.True(await scope.ServiceProvider.GetRequiredService<IPlatformCredentialService>()
            .SetStatusAsync(disabledPlatform.Id, ProviderCredentialStatus.Disabled));
        var byok = await scope.ServiceProvider.GetRequiredService<IByokCredentialService>()
            .CreateAsync(registration.AccountId, tenant.Id, provider.Id,
                "Rotating key", "sk-byok-rotation-123456");
        var deleted = await scope.ServiceProvider.GetRequiredService<IByokCredentialService>()
            .CreateAsync(registration.AccountId, tenant.Id, provider.Id,
                "Deleted key", "sk-deleted-rotation-123456");
        Assert.True(await scope.ServiceProvider.GetRequiredService<IByokCredentialService>()
            .DeleteAsync(registration.AccountId, tenant.Id, deleted.Id));

        var second = CreateProtector("v2", firstKey, nextKey);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        var invalid = new ProviderCredentialKeyRotation(db,
            CreateProtector("v2", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), nextKey), audit);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ProviderCredentialKeyRotation(db, second, audit).RotateBatchAsync(Guid.NewGuid(), 1));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => invalid.RotateBatchAsync(registration.AccountId, 1));
        Assert.Empty(await RotationAudit(db));
        Assert.Equal(3, await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .CountAsync(row => row.DeletedAt == null && row.KeyVersion == "v1"));

        var rotator = new ProviderCredentialKeyRotation(db, second, audit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rotator.RotateBatchAsync(registration.AccountId, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rotator.RotateBatchAsync(registration.AccountId, 501));
        Assert.Equal(1, await rotator.RotateBatchAsync(registration.AccountId, 1));
        Assert.Single(await RotationAudit(db));
        Assert.Equal(1, await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .CountAsync(row => row.DeletedAt == null && row.KeyVersion == "v2"));
        Assert.Equal(2, await rotator.RotateBatchAsync(registration.AccountId, 500));
        Assert.Equal(0, await rotator.RotateBatchAsync(registration.AccountId, 500));
        Assert.Equal("deleted", (await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(row => row.Id == deleted.Id)).KeyVersion);

        var rows = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(row => row.Id == platform.Id || row.Id == byok.Id).ToArrayAsync();
        var platformRow = rows.Single(row => row.Id == platform.Id);
        var byokRow = rows.Single(row => row.Id == byok.Id);
        Assert.Equal("sk-platform-rotation-123456", second.Unprotect(platform.Id, provider.Id,
            new ProtectedProviderSecret(platformRow.EncryptedSecret, platformRow.WrappedDataKey, platformRow.KeyVersion)));
        var byokCipher = new ProtectedProviderSecret(byokRow.EncryptedSecret, byokRow.WrappedDataKey, byokRow.KeyVersion);
        Assert.Equal("sk-byok-rotation-123456", second.UnprotectForOrganization(tenant.Id, byok.Id,
            provider.Id, byokCipher));
        Assert.ThrowsAny<CryptographicException>(() => second.UnprotectForOrganization(Guid.NewGuid(),
            byok.Id, provider.Id, byokCipher));

        var rollback = new ProviderCredentialKeyRotation(db, CreateProtector("v1", firstKey, nextKey), audit);
        Assert.Equal(3, await rollback.RotateBatchAsync(registration.AccountId, 500));
        Assert.Equal(6, (await RotationAudit(db)).Length);
        Assert.All(await RotationAudit(db), item =>
        {
            Assert.Equal(registration.AccountId, item.ActorAccountId);
            Assert.DoesNotContain("sk-", item.MetadataJson);
            Assert.Contains("Version", item.MetadataJson);
        });
        Assert.Equal(3, await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .CountAsync(row => row.DeletedAt == null && row.KeyVersion == "v1"));
        Assert.Equal("Disabled", (await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(row => row.Id == disabledPlatform.Id)).Status);
        Assert.Equal("sk-platform-rotation-123456", await scope.ServiceProvider
            .GetRequiredService<IProviderCredentialResolver>()
            .ResolvePlatformSecretAsync(platform.Id, provider.Id));
    }

    private ServiceProvider CreateServices(string active, string first, string next)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = active,
            ["ProviderSecrets:Keys:v1"] = first,
            ["ProviderSecrets:Keys:v2"] = next
        }).Build();
        return new ServiceCollection().AddUzllmPersistence(config).AddUzllmIdentity()
            .AddUzllmAudit().AddUzllmOrganizations().AddUzllmCatalog()
            .AddUzllmProviders(config).BuildServiceProvider(validateScopes: true);
    }

    private static ProviderEnvelopeSecretProtector CreateProtector(string active, string first, string next) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecrets:ActiveKeyVersion"] = active,
            ["ProviderSecrets:Keys:v1"] = first,
            ["ProviderSecrets:Keys:v2"] = next
        }).Build());

    private static Task<AuditEventEntity[]> RotationAudit(FoundationDbContext db) =>
        db.Set<AuditEventEntity>().AsNoTracking()
            .Where(item => item.Action == "provider_credential.key_rotated").ToArrayAsync();
}
