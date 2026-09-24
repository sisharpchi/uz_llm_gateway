using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UZLLM.Persistence;

public interface IDatabaseMigrator
{
    Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default);
}

internal sealed class DatabaseMigrator(FoundationDbContext dbContext) : IDatabaseMigrator
{
    public async Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
    {
        await EnsureMigrationPermissionsAsync(cancellationToken);

        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, cancellationToken);
    }

    private async Task EnsureMigrationPermissionsAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT has_database_privilege(current_user, current_database(), 'CREATE');";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is not true)
            {
                throw new InvalidOperationException("The migration principal must have CREATE permission on the database.");
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
