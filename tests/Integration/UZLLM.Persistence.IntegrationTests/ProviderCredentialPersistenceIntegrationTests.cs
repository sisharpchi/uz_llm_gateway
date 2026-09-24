using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Providers.Application;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ProviderCredentialPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Platform_secret_is_persisted_encrypted_and_disabled_secret_cannot_be_resolved()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var catalog = new CatalogService(new PostgreSqlCatalogStore(db), TimeProvider.System);
        var catalogProvider = await catalog.AddProviderAsync("openai", "OpenAI");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecrets:ActiveKeyVersion"] = "v1",
            ["ProviderSecrets:Keys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var protector = new ProviderEnvelopeSecretProtector(config);
        var store = new PostgreSqlProviderCredentialStore(db);
        var service = new PlatformCredentialService(store, protector, TimeProvider.System);
        var resolver = new ProviderCredentialResolver(store, protector);

        var credential = await service.CreateAsync(catalogProvider.Id, "sk-sensitive-value");

        var row = await db.Set<ProviderCredentialEntity>().AsNoTracking().SingleAsync();
        Assert.Equal("Platform", row.CredentialType);
        Assert.Null(row.OrganizationId);
        Assert.NotEqual("sk-sensitive-value", System.Text.Encoding.UTF8.GetString(row.EncryptedSecret));
        Assert.Equal("sk-sensitive-value", await resolver.ResolvePlatformSecretAsync(credential.Id, catalogProvider.Id));
        Assert.Null(await resolver.ResolvePlatformSecretAsync(credential.Id, Guid.NewGuid()));
        Assert.True(await service.SetStatusAsync(credential.Id, ProviderCredentialStatus.Disabled));
        Assert.Null(await resolver.ResolvePlatformSecretAsync(credential.Id, catalogProvider.Id));
    }
}
