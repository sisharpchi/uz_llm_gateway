using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Projects.Application;

public sealed class ProjectSettingsService(IProjectService projects, IProjectSettingsStore store,
    IOrganizationAuthorizationService authorization, IAuditTrail audit,
    ITransactionCoordinator transactions) : IProjectSettingsService
{
    public async Task<ProjectSettings> ReadAsync(Guid accountId, Guid organizationId, Guid projectId,
        CancellationToken cancellationToken = default) => ProjectSettingsPolicy.Read(
            (await ActiveAsync(accountId, organizationId, projectId, cancellationToken)).SettingsJson);

    public async Task<ProjectSettings> ReplaceAsync(Guid accountId, Guid organizationId, Guid projectId,
        ProjectSettings settings, CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageProjects, projectId, cancellationToken);
        await ActiveAsync(accountId, organizationId, projectId, cancellationToken);
        var normalized = ProjectSettingsPolicy.Normalize(settings);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await store.ReplaceAsync(organizationId, projectId, normalized, cancellationToken))
            throw new KeyNotFoundException();
        await audit.RecordAsync(new(organizationId, accountId, "project.settings_changed", "project",
            projectId, null, ProjectSettingsPolicy.Serialize(normalized)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return normalized;
    }

    private async Task<Project> ActiveAsync(Guid accountId, Guid organizationId, Guid projectId,
        CancellationToken ct) => await projects.FindAsync(accountId, organizationId, projectId, ct)
        is { Status: ProjectStatus.Active } project ? project : throw new KeyNotFoundException();
}
