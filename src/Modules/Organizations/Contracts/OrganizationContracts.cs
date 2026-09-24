namespace UZLLM.Modules.Organizations.Contracts;

public enum OrganizationStatus
{
    Active,
    Suspended
}

public enum OrganizationMemberRole
{
    Owner
}

public enum OrganizationMemberStatus
{
    Active,
    Revoked
}

public sealed record Organization(
    Guid Id,
    string Name,
    OrganizationStatus Status,
    DateTimeOffset CreatedAt);

public sealed record OrganizationMember(
    Guid OrganizationId,
    Guid AccountId,
    OrganizationMemberRole Role,
    OrganizationMemberStatus Status,
    DateTimeOffset CreatedAt);

public interface IOrganizationStore
{
    Task CreateAsync(
        Organization organization,
        OrganizationMember initialOwner,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Organization>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMember?> FindActiveMemberAsync(
        Guid organizationId,
        Guid accountId,
        CancellationToken cancellationToken = default);
}

public interface IOrganizationService
{
    Task<Organization> CreateAsync(
        Guid accountId,
        string name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Organization>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}

public interface IOrganizationAuthorizationService
{
    Task EnsureOwnerAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public sealed class TenantAccessDeniedException : Exception
{
    public TenantAccessDeniedException() : base("The account does not have access to this organization.")
    {
    }
}
