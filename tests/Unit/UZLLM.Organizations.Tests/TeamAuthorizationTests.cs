using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Organizations.Tests;

public sealed class TeamAuthorizationTests
{
    [Theory]
    [InlineData(OrganizationMemberRole.Owner, OrganizationPermission.ManageTeam, true)]
    [InlineData(OrganizationMemberRole.Admin, OrganizationPermission.ManageTeam, true)]
    [InlineData(OrganizationMemberRole.Developer, OrganizationPermission.ManageTeam, false)]
    [InlineData(OrganizationMemberRole.ReadOnly, OrganizationPermission.ManageTeam, false)]
    [InlineData(OrganizationMemberRole.BillingViewer, OrganizationPermission.ManageTeam, false)]
    [InlineData(OrganizationMemberRole.BillingViewer, OrganizationPermission.ReadBilling, true)]
    [InlineData(OrganizationMemberRole.BillingViewer, OrganizationPermission.ManageBilling, false)]
    [InlineData(OrganizationMemberRole.ReadOnly, OrganizationPermission.ReadBilling, false)]
    [InlineData(OrganizationMemberRole.Developer, OrganizationPermission.ManageApiKeys, true)]
    [InlineData(OrganizationMemberRole.ReadOnly, OrganizationPermission.ManageApiKeys, false)]
    public async Task Permission_matrix_applies_current_role_and_project_grant(
        OrganizationMemberRole role, OrganizationPermission permission, bool expected)
    {
        var store = new InMemoryOrganizationStore();
        var organizationId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        store.SetMember(new OrganizationMember(organizationId, accountId, role,
            OrganizationMemberStatus.Active, DateTimeOffset.UtcNow));
        store.SetGrant(organizationId, accountId, projectId, true);
        var authorization = new OrganizationAuthorizationService(store);

        if (expected)
            await authorization.EnsurePermissionAsync(accountId, organizationId, permission, projectId);
        else
            await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
                authorization.EnsurePermissionAsync(accountId, organizationId, permission, projectId));
    }

    [Fact]
    public async Task Grant_role_and_membership_changes_take_effect_without_a_new_session()
    {
        var store = new InMemoryOrganizationStore();
        var organizationId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var allowedProject = Guid.CreateVersion7();
        var deniedProject = Guid.CreateVersion7();
        var authorization = new OrganizationAuthorizationService(store);
        store.SetMember(new OrganizationMember(organizationId, accountId,
            OrganizationMemberRole.Developer, OrganizationMemberStatus.Active, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(accountId, organizationId,
                OrganizationPermission.ManageApiKeys, allowedProject));
        store.SetGrant(organizationId, accountId, allowedProject, true);
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageApiKeys, allowedProject);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(accountId, organizationId,
                OrganizationPermission.ManageApiKeys, deniedProject));

        store.SetMember(new OrganizationMember(organizationId, accountId,
            OrganizationMemberRole.ReadOnly, OrganizationMemberStatus.Active, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(accountId, organizationId,
                OrganizationPermission.ManageApiKeys, allowedProject));
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ReadProjects, allowedProject);
        store.SetGrant(organizationId, accountId, allowedProject, false);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(accountId, organizationId,
                OrganizationPermission.ReadProjects, allowedProject));
        store.SetMember(new OrganizationMember(organizationId, accountId,
            OrganizationMemberRole.ReadOnly, OrganizationMemberStatus.Revoked, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.GetReadableProjectIdsAsync(accountId, organizationId));
    }
}
