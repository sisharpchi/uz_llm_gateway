using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class OrganizationPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Member_foreign_key_rejects_an_account_that_does_not_exist()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var organizationId = Guid.CreateVersion7();
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organizationId,
            Name = "Acme AI",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        dbContext.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = organizationId,
            AccountId = Guid.CreateVersion7(),
            Role = "Owner",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Project_foreign_key_rejects_an_organization_that_does_not_exist()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        dbContext.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.CreateVersion7(),
            Name = "Production",
            Status = "Active",
            SettingsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }
}
