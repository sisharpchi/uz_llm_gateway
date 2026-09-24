using Microsoft.EntityFrameworkCore;
using Npgsql;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Projects.Infrastructure;

public sealed class PostgreSqlProjectStore(FoundationDbContext dbContext) : IProjectStore
{
    public async Task<bool> TryCreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        dbContext.Set<ProjectEntity>().Add(ToEntity(project));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await dbContext.Set<ProjectEntity>()
            .AsNoTracking()
            .Where(project => project.OrganizationId == organizationId)
            .OrderBy(project => project.CreatedAt)
            .Select(project => ToContract(project))
            .ToListAsync(cancellationToken);

    public async Task<Project?> FindAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<ProjectEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(project => project.Id == projectId && project.OrganizationId == organizationId, cancellationToken)) is { } project
            ? ToContract(project)
            : null;

    public async Task<bool> ArchiveAsync(
        Guid organizationId,
        Guid projectId,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<ProjectEntity>()
            .Where(project => project.Id == projectId
                && project.OrganizationId == organizationId
                && project.Status == ProjectStatus.Active.ToString())
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(project => project.Status, ProjectStatus.Archived.ToString())
                .SetProperty(project => project.ArchivedAt, archivedAt), cancellationToken) == 1;

    private static ProjectEntity ToEntity(Project project) => new()
    {
        Id = project.Id,
        OrganizationId = project.OrganizationId,
        Name = project.Name,
        Status = project.Status.ToString(),
        SettingsJson = project.SettingsJson,
        CreatedAt = project.CreatedAt,
        ArchivedAt = project.ArchivedAt
    };

    private static Project ToContract(ProjectEntity project) => new(
        project.Id,
        project.OrganizationId,
        project.Name,
        Enum.Parse<ProjectStatus>(project.Status, false),
        project.SettingsJson,
        project.CreatedAt,
        project.ArchivedAt);
}
