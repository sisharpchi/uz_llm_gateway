using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Organizations.Application;

public sealed class TeamService(
    ITeamStore store,
    IOrganizationStore organizations,
    IIdentityStore identity,
    IIdentityNotificationQueue notifications,
    IAuditTrail audit,
    ITransactionCoordinator transactions,
    TimeProvider clock) : ITeamService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        return await store.ListMembersAsync(organizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamInvitation>> ListInvitationsAsync(Guid actorId,
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        return await store.ListInvitationsAsync(organizationId, cancellationToken);
    }

    public async Task<TeamInvitation> InviteAsync(Guid actorId, Guid organizationId,
        string email, OrganizationMemberRole role, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (role is OrganizationMemberRole.Owner || !Enum.IsDefined(role))
            throw new ArgumentException("Invitations require a non-owner role.", nameof(role));
        var now = clock.GetUtcNow();
        var token = CreateToken();
        var invitation = new TeamInvitation(Guid.CreateVersion7(), organizationId, normalizedEmail,
            role, actorId, now, now.Add(InvitationLifetime), null, null);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAsync(organizationId, cancellationToken);
        var actor = await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        if (actor.Role == OrganizationMemberRole.Admin && role == OrganizationMemberRole.Admin)
            throw new TenantAccessDeniedException();
        if (await store.FindActiveMemberByEmailAsync(organizationId, normalizedEmail, cancellationToken) is not null
            || await store.HasActiveInvitationAsync(organizationId, normalizedEmail, now, cancellationToken))
            throw new InvalidOperationException("An active member or invitation already exists for this email.");
        await store.CreateInvitationAsync(invitation, HashToken(token), cancellationToken);
        await notifications.QueueAsync(new IdentityEmailNotification(normalizedEmail, token,
            IdentityEmailKind.TeamInvitation, invitation.ExpiresAt), cancellationToken);
        await RecordAsync(organizationId, actorId, "team.invited", "invitation", invitation.Id,
            new { role = role.ToString() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invitation;
    }

    public async Task<TeamMember?> AcceptAsync(Guid accountId, string token,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || token is not { Length: 43 }
            || token.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_'))) return null;
        var account = await identity.FindAccountByIdAsync(accountId, cancellationToken);
        if (account is not { Status: IdentityAccountStatus.Active, IsEmailVerified: true }) return null;
        var tokenHash = HashToken(token);
        var invitation = await store.FindInvitationAsync(tokenHash, cancellationToken);
        if (invitation is null) return null;
        var now = clock.GetUtcNow();
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAsync(invitation.OrganizationId, cancellationToken);
        invitation = await store.FindInvitationAsync(tokenHash, cancellationToken);
        if (invitation is null || invitation.AcceptedAt is not null || invitation.RevokedAt is not null
            || invitation.ExpiresAt <= now || !string.Equals(account.Email, invitation.Email, StringComparison.Ordinal))
            return null;
        if (!await store.AcceptInvitationAsync(invitation.Id, accountId, now, cancellationToken)) return null;
        await RecordAsync(invitation.OrganizationId, accountId, "team.invitation.accepted", "member",
            accountId, new { role = invitation.Role.ToString() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await store.FindMemberAsync(invitation.OrganizationId, accountId, cancellationToken);
    }

    public async Task<bool> ChangeRoleAsync(Guid actorId, Guid organizationId,
        Guid accountId, OrganizationMemberRole role, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Invalid member role.", nameof(role));
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAsync(organizationId, cancellationToken);
        var actor = await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        var target = await store.FindMemberAsync(organizationId, accountId, cancellationToken);
        if (target is not { Status: OrganizationMemberStatus.Active }) return false;
        EnsureMayChange(actor, target, role);
        if (target.Role == role) return true;
        if (target.Role == OrganizationMemberRole.Owner &&
            await store.CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
            throw new InvalidOperationException("The last active owner cannot be demoted.");
        var changed = await store.SetRoleAsync(organizationId, accountId, role, cancellationToken);
        if (!changed) return true;
        await RecordAsync(organizationId, actorId, "team.role.changed", "member", accountId,
            new { from = target.Role.ToString(), to = role.ToString() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeAsync(Guid actorId, Guid organizationId,
        Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAsync(organizationId, cancellationToken);
        var actor = await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        var target = await store.FindMemberAsync(organizationId, accountId, cancellationToken);
        if (target is not { Status: OrganizationMemberStatus.Active }) return false;
        EnsureMayChange(actor, target, target.Role);
        if (target.Role == OrganizationMemberRole.Owner &&
            await store.CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
            throw new InvalidOperationException("The last active owner cannot be revoked.");
        var revoked = await store.RevokeMemberAsync(organizationId, accountId, cancellationToken);
        if (!revoked) return false;
        await RecordAsync(organizationId, actorId, "team.member.revoked", "member", accountId,
            new { role = target.Role.ToString() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetProjectGrantAsync(Guid actorId, Guid organizationId,
        Guid accountId, Guid projectId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAsync(organizationId, cancellationToken);
        _ = await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        var target = await store.FindMemberAsync(organizationId, accountId, cancellationToken);
        if (target is not { Status: OrganizationMemberStatus.Active }) return false;
        if (target.Role is not (OrganizationMemberRole.Developer or OrganizationMemberRole.ReadOnly))
            throw new InvalidOperationException("Only project-scoped roles may receive explicit grants.");
        if (!await store.ProjectBelongsAsync(organizationId, projectId, enabled, cancellationToken))
            return false;
        var changed = await store.SetProjectGrantAsync(organizationId, accountId, projectId,
            enabled, clock.GetUtcNow(), cancellationToken);
        if (!changed) return true;
        await RecordAsync(organizationId, actorId,
            enabled ? "team.project.granted" : "team.project.revoked", "project", projectId,
            new { accountId }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task LockAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || !await store.LockOrganizationAsync(organizationId, cancellationToken))
            throw new TenantAccessDeniedException();
    }

    private async Task<OrganizationMember> EnsureManagerAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken)
    {
        var actor = await organizations.FindActiveMemberAsync(organizationId, actorId, cancellationToken);
        if (actor is not { Role: OrganizationMemberRole.Owner or OrganizationMemberRole.Admin })
            throw new TenantAccessDeniedException();
        return actor;
    }

    private static void EnsureMayChange(OrganizationMember actor, TeamMember target,
        OrganizationMemberRole newRole)
    {
        if (actor.Role == OrganizationMemberRole.Admin &&
            (target.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Admin
                || newRole is OrganizationMemberRole.Owner or OrganizationMemberRole.Admin))
            throw new TenantAccessDeniedException();
    }

    private async Task RecordAsync(Guid organizationId, Guid actorId, string action,
        string resourceType, Guid resourceId, object metadata, CancellationToken cancellationToken) =>
        _ = await audit.RecordAsync(new AuditEventInput(organizationId, actorId, action, resourceType,
            resourceId, null, JsonSerializer.Serialize(metadata)), cancellationToken);

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 320 || !normalized.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Email address is invalid.", nameof(email));
        return normalized;
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
