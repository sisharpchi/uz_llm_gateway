using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public interface IRuntimeDatabasePermissionVerifier
{
    Task VerifyNoSchemaCreatePrivilegeAsync(CancellationToken cancellationToken = default);
}

internal sealed class RuntimeDatabasePermissionVerifier(FoundationDbContext dbContext) : IRuntimeDatabasePermissionVerifier
{
    public async Task VerifyNoSchemaCreatePrivilegeAsync(CancellationToken cancellationToken = default)
    {
        var canCreate = await dbContext.Database
            .SqlQueryRaw<bool>("SELECT has_schema_privilege(current_user, 'ops', 'CREATE');")
            .SingleAsync(cancellationToken);

        if (canCreate)
        {
            throw new InvalidOperationException("The runtime principal must not have CREATE permission in the ops schema.");
        }
    }
}
