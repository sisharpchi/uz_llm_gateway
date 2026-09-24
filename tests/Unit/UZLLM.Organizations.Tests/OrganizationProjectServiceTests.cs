using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Application;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Organizations.Tests;

public sealed class OrganizationProjectServiceTests
{
    [Fact]
    public async Task CreateAsync_creates_an_active_organization_with_the_requesting_account_as_owner()
    {
        var fixture = new OrganizationProjectFixture();
        var accountId = Guid.CreateVersion7();

        var organization = await fixture.Organizations.CreateAsync(accountId, "Acme AI");

        Assert.Equal(OrganizationStatus.Active, organization.Status);
        var membership = await fixture.OrganizationStore.FindActiveMemberAsync(organization.Id, accountId);
        Assert.NotNull(membership);
        Assert.Equal(OrganizationMemberRole.Owner, membership.Role);
        Assert.Equal(OrganizationMemberStatus.Active, membership.Status);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_organization_name()
    {
        var fixture = new OrganizationProjectFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Organizations.CreateAsync(Guid.CreateVersion7(), " "));
    }

    [Fact]
    public async Task ListForAccountAsync_returns_only_the_requesting_accounts_organizations()
    {
        var fixture = new OrganizationProjectFixture();
        var firstAccountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var firstOrganization = await fixture.Organizations.CreateAsync(firstAccountId, "First tenant");
        _ = await fixture.Organizations.CreateAsync(secondAccountId, "Second tenant");

        var organizations = await fixture.Organizations.ListForAccountAsync(firstAccountId);

        var organization = Assert.Single(organizations);
        Assert.Equal(firstOrganization.Id, organization.Id);
    }

    [Fact]
    public async Task CreateAsync_creates_an_active_project_at_the_organization_scope()
    {
        var fixture = new OrganizationProjectFixture();
        var accountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(accountId, "Acme AI");

        var project = await fixture.Projects.CreateAsync(accountId, organization.Id, "Production");

        Assert.Equal(organization.Id, project.OrganizationId);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal("{}", project.SettingsJson);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_project_name()
    {
        var fixture = new OrganizationProjectFixture();
        var accountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(accountId, "Acme AI");

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Projects.CreateAsync(accountId, organization.Id, " "));
    }

    [Fact]
    public async Task CreateAsync_allows_the_same_project_name_in_different_organizations()
    {
        var fixture = new OrganizationProjectFixture();
        var firstAccountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var firstOrganization = await fixture.Organizations.CreateAsync(firstAccountId, "First tenant");
        var secondOrganization = await fixture.Organizations.CreateAsync(secondAccountId, "Second tenant");

        var firstProject = await fixture.Projects.CreateAsync(firstAccountId, firstOrganization.Id, "Production");
        var secondProject = await fixture.Projects.CreateAsync(secondAccountId, secondOrganization.Id, "Production");

        Assert.NotEqual(firstProject.Id, secondProject.Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_project_name_within_the_organization()
    {
        var fixture = new OrganizationProjectFixture();
        var accountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(accountId, "Acme AI");
        _ = await fixture.Projects.CreateAsync(accountId, organization.Id, "Production");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Projects.CreateAsync(accountId, organization.Id, "Production"));
    }

    [Fact]
    public async Task ArchiveAsync_marks_an_active_project_archived_once()
    {
        var fixture = new OrganizationProjectFixture();
        var accountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(accountId, "Acme AI");
        var project = await fixture.Projects.CreateAsync(accountId, organization.Id, "Staging");

        Assert.True(await fixture.Projects.ArchiveAsync(accountId, organization.Id, project.Id));
        Assert.False(await fixture.Projects.ArchiveAsync(accountId, organization.Id, project.Id));

        var archived = await fixture.Projects.FindAsync(accountId, organization.Id, project.Id);
        Assert.NotNull(archived);
        Assert.Equal(ProjectStatus.Archived, archived.Status);
        Assert.NotNull(archived.ArchivedAt);
    }

    [Fact]
    public async Task Project_operations_reject_an_account_outside_the_organization()
    {
        var fixture = new OrganizationProjectFixture();
        var ownerAccountId = Guid.CreateVersion7();
        var outsiderAccountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(ownerAccountId, "Acme AI");

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            fixture.Projects.CreateAsync(outsiderAccountId, organization.Id, "Production"));
    }

    [Fact]
    public async Task Project_read_and_archive_operations_reject_an_account_outside_the_organization()
    {
        var fixture = new OrganizationProjectFixture();
        var ownerAccountId = Guid.CreateVersion7();
        var outsiderAccountId = Guid.CreateVersion7();
        var organization = await fixture.Organizations.CreateAsync(ownerAccountId, "Acme AI");
        var project = await fixture.Projects.CreateAsync(ownerAccountId, organization.Id, "Production");

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            fixture.Projects.ListAsync(outsiderAccountId, organization.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            fixture.Projects.FindAsync(outsiderAccountId, organization.Id, project.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            fixture.Projects.ArchiveAsync(outsiderAccountId, organization.Id, project.Id));
    }

    [Fact]
    public async Task FindAsync_does_not_return_a_project_from_another_organization_even_to_an_authorized_owner()
    {
        var fixture = new OrganizationProjectFixture();
        var firstAccountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var firstOrganization = await fixture.Organizations.CreateAsync(firstAccountId, "First tenant");
        var secondOrganization = await fixture.Organizations.CreateAsync(secondAccountId, "Second tenant");
        var secondProject = await fixture.Projects.CreateAsync(secondAccountId, secondOrganization.Id, "Production");

        var result = await fixture.Projects.FindAsync(firstAccountId, firstOrganization.Id, secondProject.Id);

        Assert.Null(result);
        Assert.Empty(await fixture.Projects.ListAsync(firstAccountId, firstOrganization.Id));
    }
}

internal sealed class OrganizationProjectFixture
{
    public InMemoryOrganizationStore OrganizationStore { get; } = new();

    public InMemoryProjectStore ProjectStore { get; } = new();

    public IOrganizationService Organizations { get; }

    public IProjectService Projects { get; }

    public OrganizationProjectFixture()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        Organizations = new OrganizationService(OrganizationStore, clock);
        Projects = new ProjectService(ProjectStore, new OrganizationAuthorizationService(OrganizationStore), clock);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly Dictionary<Guid, Organization> organizations = [];
    private readonly Dictionary<(Guid OrganizationId, Guid AccountId), OrganizationMember> memberships = [];

    public Task CreateAsync(Organization organization, OrganizationMember initialOwner, CancellationToken cancellationToken = default)
    {
        organizations.Add(organization.Id, organization);
        memberships.Add((initialOwner.OrganizationId, initialOwner.AccountId), initialOwner);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Organization>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Organization>>(memberships.Values
            .Where(member => member.AccountId == accountId && member.Status == OrganizationMemberStatus.Active)
            .Select(member => organizations[member.OrganizationId])
            .OrderBy(organization => organization.CreatedAt)
            .ToList());

    public Task<OrganizationMember?> FindActiveMemberAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(memberships.GetValueOrDefault((organizationId, accountId)) is { Status: OrganizationMemberStatus.Active } membership
            ? membership
            : null);
}

internal sealed class InMemoryProjectStore : IProjectStore
{
    private readonly Dictionary<Guid, Project> projects = [];

    public Task<bool> TryCreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (projects.Values.Any(existing => existing.OrganizationId == project.OrganizationId && existing.Name == project.Name))
        {
            return Task.FromResult(false);
        }

        projects.Add(project.Id, project);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Project>>(projects.Values
            .Where(project => project.OrganizationId == organizationId)
            .OrderBy(project => project.CreatedAt)
            .ToList());

    public Task<Project?> FindAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(projects.GetValueOrDefault(projectId) is { OrganizationId: var projectOrganizationId } project
            && projectOrganizationId == organizationId
                ? project
                : null);

    public Task<bool> ArchiveAsync(Guid organizationId, Guid projectId, DateTimeOffset archivedAt, CancellationToken cancellationToken = default)
    {
        if (projects.GetValueOrDefault(projectId) is not { OrganizationId: var projectOrganizationId, Status: ProjectStatus.Active } project
            || projectOrganizationId != organizationId)
        {
            return Task.FromResult(false);
        }

        projects[projectId] = project with { Status = ProjectStatus.Archived, ArchivedAt = archivedAt };
        return Task.FromResult(true);
    }
}
