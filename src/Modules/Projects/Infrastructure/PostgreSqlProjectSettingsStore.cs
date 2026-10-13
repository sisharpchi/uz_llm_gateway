using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Projects.Infrastructure;

public sealed class PostgreSqlProjectSettingsStore(FoundationDbContext db) : IProjectSettingsStore
{
    public async Task<bool> ReplaceAsync(Guid organizationId, Guid projectId, ProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        var json = ProjectSettingsPolicy.Serialize(settings);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE gateway.project SET settings_json = jsonb_set(settings_json, {"{defaults}"}::text[], {json}::jsonb, true)
            WHERE id = {projectId} AND organization_id = {organizationId} AND status = 'Active'
            """, cancellationToken) == 1;
    }
}
