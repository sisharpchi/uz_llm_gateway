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
}
