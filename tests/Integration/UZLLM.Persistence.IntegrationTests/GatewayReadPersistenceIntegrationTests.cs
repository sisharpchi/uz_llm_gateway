using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class GatewayReadPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Gateway_scope_rejects_suspended_tenants_and_credential_selection_requires_active_platform_scope()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var services = fixture.CreateServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var organization = new OrganizationEntity
        {
            Id = Guid.NewGuid(), Name = "Gateway Tenant", Status = "Active", CreatedAt = now
        };
        var project = new ProjectEntity
        {
            Id = Guid.NewGuid(), OrganizationId = organization.Id, Name = "Production",
            Status = "Active", CreatedAt = now
        };
        var upstream = new CatalogProviderEntity
        {
            Id = Guid.NewGuid(), Code = "openai", Name = "OpenAI", Status = "Active", CreatedAt = now
        };
        db.AddRange(organization, project, upstream);
        await db.SaveChangesAsync();
        var read = new PostgreSqlGatewayReadStore(db);
        Assert.Equal(organization.Id, (await read.FindTenantAsync(project.Id, CancellationToken.None))!.OrganizationId);
        Assert.Null(await read.FindTenantAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await read.FindPlatformCredentialAsync(upstream.Id, CancellationToken.None));

        db.Set<ProviderCredentialEntity>().Add(new ProviderCredentialEntity
        {
            Id = Guid.NewGuid(), ProviderId = upstream.Id, CredentialType = "Platform",
            Status = "Active", EncryptedSecret = new byte[40], WrappedDataKey = new byte[60],
            KeyVersion = "v1", CreatedAt = now
        });
        await db.SaveChangesAsync();
        Assert.NotNull(await read.FindPlatformCredentialAsync(upstream.Id, CancellationToken.None));
        await db.Set<ProviderCredentialEntity>().ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.Status, "Disabled"));
        Assert.Null(await read.FindPlatformCredentialAsync(upstream.Id, CancellationToken.None));

        await db.Set<OrganizationEntity>().Where(value => value.Id == organization.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Suspended"));
        Assert.Null(await read.FindTenantAsync(project.Id, CancellationToken.None));
    }
}
