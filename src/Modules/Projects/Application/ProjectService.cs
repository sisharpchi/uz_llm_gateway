using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Modules.Projects.Application;

public sealed class ProjectService(
    IProjectStore store,
    IOrganizationAuthorizationService organizationAuthorization,
    TimeProvider timeProvider) : IProjectService
{
    public async Task<Project> CreateAsync(
        Guid accountId,
        Guid organizationId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await organizationAuthorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        var project = new Project(
            Guid.CreateVersion7(),
            organizationId,
            NormalizeName(name),
            ProjectStatus.Active,
            "{}",
            timeProvider.GetUtcNow(),
            null);
        if (!await store.TryCreateAsync(project, cancellationToken))
        {
            throw new InvalidOperationException("A project with that name already exists in this organization.");
        }

        return project;
    }

    public async Task<IReadOnlyList<Project>> ListAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await organizationAuthorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        return await store.ListAsync(organizationId, cancellationToken);
    }

    public async Task<Project?> FindAsync(
        Guid accountId,
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await organizationAuthorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        return await store.FindAsync(organizationId, projectId, cancellationToken);
    }

    public async Task<bool> ArchiveAsync(
        Guid accountId,
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await organizationAuthorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        return await store.ArchiveAsync(organizationId, projectId, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
        {
            throw new ArgumentException("Project name must be between 2 and 120 characters.", nameof(name));
        }

        return normalized;
    }
}
