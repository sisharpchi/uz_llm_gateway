using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Organizations.Infrastructure;

public sealed class PostgreSqlOrganizationStore(FoundationDbContext dbContext) : IOrganizationStore
{
    public async Task CreateAsync(
        Organization organization,
        OrganizationMember initialOwner,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organization.Id,
            Name = organization.Name,
            Status = organization.Status.ToString(),
            CreatedAt = organization.CreatedAt
        });
        dbContext.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = initialOwner.OrganizationId,
            AccountId = initialOwner.AccountId,
            Role = initialOwner.Role.ToString(),
            Status = initialOwner.Status.ToString(),
            CreatedAt = initialOwner.CreatedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Organization>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<OrganizationMemberEntity>()
            .AsNoTracking()
            .Where(member => member.AccountId == accountId && member.Status == OrganizationMemberStatus.Active.ToString())
            .Select(member => member.Organization)
            .OrderBy(organization => organization.CreatedAt)
            .Select(organization => new Organization(
                organization.Id,
                organization.Name,
                Enum.Parse<OrganizationStatus>(organization.Status, false),
                organization.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<OrganizationMember?> FindActiveMemberAsync(
        Guid organizationId,
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Set<OrganizationMemberEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(member => member.OrganizationId == organizationId
                && member.AccountId == accountId
                && member.Status == OrganizationMemberStatus.Active.ToString(), cancellationToken)) is { } member
            ? new OrganizationMember(
                member.OrganizationId,
                member.AccountId,
                Enum.Parse<OrganizationMemberRole>(member.Role, false),
                Enum.Parse<OrganizationMemberStatus>(member.Status, false),
                member.CreatedAt)
            : null;
}
