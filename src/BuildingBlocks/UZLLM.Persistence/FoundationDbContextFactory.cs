using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UZLLM.Persistence;

public sealed class FoundationDbContextFactory : IDesignTimeDbContextFactory<FoundationDbContext>
{
    public FoundationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set ConnectionStrings__Postgres before running EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FoundationDbContext(options);
    }
}
