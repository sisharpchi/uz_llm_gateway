using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Organizations.Infrastructure;

public sealed class PostgreSqlTeamStore(FoundationDbContext dbContext) : ITeamStore
{
    public async Task<bool> LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await dbContext.Set<OrganizationEntity>()
            .FromSqlInterpolated($"SELECT * FROM org.organization WHERE id = {organizationId} AND status = 'Active' FOR UPDATE")
            .AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var members = await dbContext.Set<OrganizationMemberEntity>().AsNoTracking()
            .Include(member => member.Account).Include(member => member.ProjectGrants)
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.CreatedAt).ToListAsync(cancellationToken);
        return members.Select(MapMember).ToList();
    }

    public async Task<IReadOnlyList<TeamInvitation>> ListInvitationsAsync(Guid organizationId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Set<OrganizationInvitationEntity>().AsNoTracking()
            .Where(invitation => invitation.OrganizationId == organizationId)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken)).Select(MapInvitation).ToList();

    public async Task<TeamMember?> FindMemberAsync(Guid organizationId, Guid accountId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Set<OrganizationMemberEntity>().AsNoTracking()
            .Include(member => member.Account).Include(member => member.ProjectGrants)
            .SingleOrDefaultAsync(member => member.OrganizationId == organizationId
                && member.AccountId == accountId, cancellationToken)) is { } member
            ? MapMember(member) : null;

    public async Task<TeamMember?> FindActiveMemberByEmailAsync(Guid organizationId,
        string email, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<OrganizationMemberEntity>().AsNoTracking()
            .Include(member => member.Account)
            .SingleOrDefaultAsync(member => member.OrganizationId == organizationId
                && member.Account.Email == email
                && member.Status == nameof(OrganizationMemberStatus.Active), cancellationToken))
            is { } member ? MapMember(member) : null;

    public async Task<TeamInvitation?> FindInvitationAsync(byte[] tokenHash,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Set<OrganizationInvitationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(invitation => invitation.TokenHash == tokenHash,
                cancellationToken)) is { } invitation ? MapInvitation(invitation) : null;

    public Task<bool> HasActiveInvitationAsync(Guid organizationId, string email, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<OrganizationInvitationEntity>().AsNoTracking()
            .AnyAsync(invitation => invitation.OrganizationId == organizationId
                && invitation.Email == email && invitation.AcceptedAt == null
                && invitation.RevokedAt == null && invitation.ExpiresAt > now, cancellationToken);

    public async Task CreateInvitationAsync(TeamInvitation invitation, byte[] tokenHash,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Set<OrganizationInvitationEntity>()
            .Where(existing => existing.OrganizationId == invitation.OrganizationId
                && existing.Email == invitation.Email && existing.AcceptedAt == null
                && existing.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(existing => existing.RevokedAt,
                invitation.CreatedAt), cancellationToken);
        dbContext.Set<OrganizationInvitationEntity>().Add(new OrganizationInvitationEntity
        {
            Id = invitation.Id,
            OrganizationId = invitation.OrganizationId,
            Email = invitation.Email,
            Role = invitation.Role.ToString(),
            TokenHash = tokenHash,
            InvitedByAccountId = invitation.InvitedByAccountId,
            CreatedAt = invitation.CreatedAt,
            ExpiresAt = invitation.ExpiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> AcceptInvitationAsync(Guid invitationId, Guid accountId,
        DateTimeOffset acceptedAt, CancellationToken cancellationToken = default)
    {
        var invitation = await dbContext.Set<OrganizationInvitationEntity>()
            .SingleOrDefaultAsync(value => value.Id == invitationId && value.AcceptedAt == null
                && value.RevokedAt == null && value.ExpiresAt > acceptedAt, cancellationToken);
        if (invitation is null) return false;
        var member = await dbContext.Set<OrganizationMemberEntity>()
            .SingleOrDefaultAsync(value => value.OrganizationId == invitation.OrganizationId
                && value.AccountId == accountId, cancellationToken);
        if (member is { Status: nameof(OrganizationMemberStatus.Active) }) return false;
        if (member is null)
        {
            dbContext.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
            {
                OrganizationId = invitation.OrganizationId,
                AccountId = accountId,
                Role = invitation.Role,
                Status = nameof(OrganizationMemberStatus.Active),
                CreatedAt = acceptedAt
            });
        }
        else
        {
            member.Role = invitation.Role;
            member.Status = nameof(OrganizationMemberStatus.Active);
        }
        invitation.AcceptedAt = acceptedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetRoleAsync(Guid organizationId, Guid accountId,
        OrganizationMemberRole role, CancellationToken cancellationToken = default)
    {
        var member = await dbContext.Set<OrganizationMemberEntity>()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.AccountId == accountId && value.Status == nameof(OrganizationMemberStatus.Active),
                cancellationToken);
        if (member is null) return false;
        member.Role = role.ToString();
        if (role is not (OrganizationMemberRole.Developer or OrganizationMemberRole.ReadOnly))
            await dbContext.Set<ProjectGrantEntity>()
                .Where(grant => grant.OrganizationId == organizationId && grant.AccountId == accountId)
                .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeMemberAsync(Guid organizationId, Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.Set<OrganizationMemberEntity>()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.AccountId == accountId && value.Status == nameof(OrganizationMemberStatus.Active),
                cancellationToken);
        if (member is null) return false;
        member.Status = nameof(OrganizationMemberStatus.Revoked);
        await dbContext.Set<ProjectGrantEntity>()
            .Where(grant => grant.OrganizationId == organizationId && grant.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<int> CountActiveOwnersAsync(Guid organizationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<OrganizationMemberEntity>().AsNoTracking()
            .CountAsync(member => member.OrganizationId == organizationId
                && member.Role == nameof(OrganizationMemberRole.Owner)
                && member.Status == nameof(OrganizationMemberStatus.Active), cancellationToken);

    public Task<bool> ProjectBelongsAsync(Guid organizationId, Guid projectId, bool requireActive,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<ProjectEntity>().AsNoTracking()
            .AnyAsync(project => project.OrganizationId == organizationId && project.Id == projectId
                && (!requireActive || project.Status == "Active"), cancellationToken);

    public async Task<bool> SetProjectGrantAsync(Guid organizationId, Guid accountId,
        Guid projectId, bool enabled, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var grants = dbContext.Set<ProjectGrantEntity>();
        if (!enabled)
            return await grants.Where(grant => grant.OrganizationId == organizationId
                && grant.AccountId == accountId && grant.ProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken) > 0;
        if (await grants.AnyAsync(grant => grant.OrganizationId == organizationId
            && grant.AccountId == accountId && grant.ProjectId == projectId, cancellationToken)) return false;
        grants.Add(new ProjectGrantEntity
        {
            OrganizationId = organizationId,
            AccountId = accountId,
            ProjectId = projectId,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static TeamMember MapMember(OrganizationMemberEntity member) => new(
        member.OrganizationId, member.AccountId, member.Account.Email,
        Enum.Parse<OrganizationMemberRole>(member.Role),
        Enum.Parse<OrganizationMemberStatus>(member.Status),
        member.CreatedAt, member.ProjectGrants.Select(grant => grant.ProjectId).ToList());

    private static TeamInvitation MapInvitation(OrganizationInvitationEntity invitation) => new(
        invitation.Id, invitation.OrganizationId, invitation.Email,
        Enum.Parse<OrganizationMemberRole>(invitation.Role),
        invitation.InvitedByAccountId, invitation.CreatedAt, invitation.ExpiresAt,
        invitation.AcceptedAt, invitation.RevokedAt);
}
