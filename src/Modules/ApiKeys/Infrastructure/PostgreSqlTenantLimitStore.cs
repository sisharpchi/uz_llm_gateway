using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public sealed class PostgreSqlTenantLimitStore(FoundationDbContext db) : ITenantLimitStore
{
    public Task<TenantLimitSnapshot?> FindAsync(LimitScope scope, CancellationToken cancellationToken = default) =>
        db.Set<GatewayApiKeyEntity>().AsNoTracking().Where(key => key.Id == scope.ApiKeyId && key.ProjectId == scope.ProjectId
            && key.Project.OrganizationId == scope.OrganizationId && key.Status == "Active"
            && key.Project.Status == "Active")
            .Select(key => new TenantLimitSnapshot(
                new(key.Project.RequestsPerMinute, key.Project.Concurrency), new(key.RequestsPerMinute, key.Concurrency)))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<TenantLimits?> ReadAsync(Guid projectId, Guid? apiKeyId, CancellationToken cancellationToken = default) =>
        apiKeyId is null
            ? db.Set<ProjectEntity>().AsNoTracking().Where(project => project.Id == projectId)
                .Select(project => new TenantLimits(project.RequestsPerMinute, project.Concurrency))
                .SingleOrDefaultAsync(cancellationToken)
            : db.Set<GatewayApiKeyEntity>().AsNoTracking().Where(key => key.Id == apiKeyId && key.ProjectId == projectId)
                .Select(key => new TenantLimits(key.RequestsPerMinute, key.Concurrency)).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ReplaceAsync(Guid projectId, Guid? apiKeyId, TenantLimits limits,
        CancellationToken cancellationToken = default) => (apiKeyId is null
            ? await db.Set<ProjectEntity>().Where(project => project.Id == projectId).ExecuteUpdateAsync(setters => setters
                .SetProperty(project => project.RequestsPerMinute, limits.RequestsPerMinute)
                .SetProperty(project => project.Concurrency, limits.Concurrency), cancellationToken)
            : await db.Set<GatewayApiKeyEntity>().Where(key => key.Id == apiKeyId && key.ProjectId == projectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.RequestsPerMinute, limits.RequestsPerMinute)
                    .SetProperty(key => key.Concurrency, limits.Concurrency), cancellationToken)) == 1;
}
