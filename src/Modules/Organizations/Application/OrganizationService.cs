using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Modules.Organizations.Application;

public sealed class OrganizationService(IOrganizationStore store, TimeProvider timeProvider) : IOrganizationService
{
    public async Task<Organization> CreateAsync(
        Guid accountId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account is required.", nameof(accountId));
        }

        var organization = new Organization(Guid.CreateVersion7(), NormalizeName(name), OrganizationStatus.Active, timeProvider.GetUtcNow());
        var owner = new OrganizationMember(
            organization.Id,
            accountId,
            OrganizationMemberRole.Owner,
            OrganizationMemberStatus.Active,
            organization.CreatedAt);
        await store.CreateAsync(organization, owner, cancellationToken);
        return organization;
    }

    public Task<IReadOnlyList<Organization>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        accountId == Guid.Empty
            ? throw new ArgumentException("An account is required.", nameof(accountId))
            : store.ListForAccountAsync(accountId, cancellationToken);

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
        {
            throw new ArgumentException("Organization name must be between 2 and 120 characters.", nameof(name));
        }

        return normalized;
    }
}

public sealed class OrganizationAuthorizationService(IOrganizationStore store) : IOrganizationAuthorizationService
{
    public async Task EnsureOwnerAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || organizationId == Guid.Empty)
        {
            throw new TenantAccessDeniedException();
        }

        var membership = await store.FindActiveMemberAsync(organizationId, accountId, cancellationToken);
        if (membership is not { Role: OrganizationMemberRole.Owner, Status: OrganizationMemberStatus.Active })
        {
            throw new TenantAccessDeniedException();
        }
    }

    public async Task EnsurePermissionAsync(Guid accountId, Guid organizationId,
        OrganizationPermission permission, Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || organizationId == Guid.Empty)
            throw new TenantAccessDeniedException();
        var member = await store.FindActiveMemberAsync(organizationId, accountId, cancellationToken)
            ?? throw new TenantAccessDeniedException();
        if (member.Role == OrganizationMemberRole.Owner) return;
        var allowed = (member.Role, permission) switch
        {
            (OrganizationMemberRole.Admin, _) => true,
            (OrganizationMemberRole.BillingViewer, OrganizationPermission.ReadBilling or
                OrganizationPermission.ReadUsage or OrganizationPermission.ReadProjects) => true,
            (OrganizationMemberRole.Developer, OrganizationPermission.ReadProjects or
                OrganizationPermission.ReadUsage or OrganizationPermission.ManageApiKeys) =>
                projectId is { } id && await store.HasProjectGrantAsync(organizationId, accountId, id, cancellationToken),
            (OrganizationMemberRole.ReadOnly, OrganizationPermission.ReadProjects or
                OrganizationPermission.ReadUsage) =>
                projectId is { } id && await store.HasProjectGrantAsync(organizationId, accountId, id, cancellationToken),
            _ => false
        };
        if (!allowed) throw new TenantAccessDeniedException();
    }

    public async Task<IReadOnlyList<Guid>?> GetReadableProjectIdsAsync(Guid accountId,
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || organizationId == Guid.Empty)
            throw new TenantAccessDeniedException();
        var member = await store.FindActiveMemberAsync(organizationId, accountId, cancellationToken)
            ?? throw new TenantAccessDeniedException();
        return member.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Admin or
            OrganizationMemberRole.BillingViewer
            ? null
            : await store.ListProjectGrantsAsync(organizationId, accountId, cancellationToken);
    }
}
