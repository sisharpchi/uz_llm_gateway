namespace UZLLM.Modules.Organizations.Contracts;

public sealed record TeamMember(Guid OrganizationId, Guid AccountId, string Email,
    OrganizationMemberRole Role, OrganizationMemberStatus Status,
    DateTimeOffset CreatedAt, IReadOnlyList<Guid> ProjectIds);

public sealed record TeamInvitation(Guid Id, Guid OrganizationId, string Email,
    OrganizationMemberRole Role, Guid InvitedByAccountId,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);

public interface ITeamStore
{
    Task<bool> LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamInvitation>> ListInvitationsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<TeamMember?> FindMemberAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken = default);
    Task<TeamMember?> FindActiveMemberByEmailAsync(Guid organizationId, string email,
        CancellationToken cancellationToken = default);
    Task<TeamInvitation?> FindInvitationAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
    Task<bool> HasActiveInvitationAsync(Guid organizationId, string email, DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task CreateInvitationAsync(TeamInvitation invitation, byte[] tokenHash,
        CancellationToken cancellationToken = default);
    Task<bool> AcceptInvitationAsync(Guid invitationId, Guid accountId, DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default);
    Task<bool> SetRoleAsync(Guid organizationId, Guid accountId, OrganizationMemberRole role,
        CancellationToken cancellationToken = default);
    Task<bool> RevokeMemberAsync(Guid organizationId, Guid accountId,
        CancellationToken cancellationToken = default);
    Task<int> CountActiveOwnersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> ProjectBelongsAsync(Guid organizationId, Guid projectId, bool requireActive,
        CancellationToken cancellationToken = default);
    Task<bool> SetProjectGrantAsync(Guid organizationId, Guid accountId, Guid projectId,
        bool enabled, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface ITeamService
{
    Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamInvitation>> ListInvitationsAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<TeamInvitation> InviteAsync(Guid actorId, Guid organizationId, string email,
        OrganizationMemberRole role, CancellationToken cancellationToken = default);
    Task<TeamMember?> AcceptAsync(Guid accountId, string token,
        CancellationToken cancellationToken = default);
    Task<bool> ChangeRoleAsync(Guid actorId, Guid organizationId, Guid accountId,
        OrganizationMemberRole role, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid actorId, Guid organizationId, Guid accountId,
        CancellationToken cancellationToken = default);
    Task<bool> SetProjectGrantAsync(Guid actorId, Guid organizationId, Guid accountId,
        Guid projectId, bool enabled, CancellationToken cancellationToken = default);
}
