namespace UZLLM.Modules.Projects.Contracts;

public enum ProjectStatus
{
    Active,
    Archived
}

public sealed record Project(
    Guid Id,
    Guid OrganizationId,
    string Name,
    ProjectStatus Status,
    string SettingsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt);

public interface IProjectStore
{
    Task<bool> TryCreateAsync(Project project, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<Project?> FindAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default);

    Task<Project?> FindByIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> ArchiveAsync(
        Guid organizationId,
        Guid projectId,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken = default);
}

public interface IProjectService
{
    Task<Project> CreateAsync(
        Guid accountId,
        Guid organizationId,
        string name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(
        Guid accountId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<Project?> FindAsync(
        Guid accountId,
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveAsync(
        Guid accountId,
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public interface IProjectAccessService
{
    Task<Project?> GetOwnedAsync(Guid accountId, Guid projectId, CancellationToken cancellationToken = default);
}
