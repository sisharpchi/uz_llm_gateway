using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace UZLLM.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<FoundationDbContext>(options =>
            options.UseNpgsql(GetRequiredConnectionString(configuration)));
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<ITransactionCoordinator, TransactionCoordinator>();
        services.AddScoped<IRuntimeDatabasePermissionVerifier, RuntimeDatabasePermissionVerifier>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOutboxStore, PostgreSqlOutboxStore>();
        services.AddScoped<IConsumerInboxStore, PostgreSqlConsumerInboxStore>();
        services.AddScoped<ILeasedJobStore, PostgreSqlLeasedJobStore>();
        services.AddScoped<IOperationalAlertPublisher, PostgreSqlOperationalAlertPublisher>();

        return services;
    }

    public static IServiceCollection AddUzllmRedis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connectionString = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Redis must be configured before Redis is used.");
            }

            // The admission limiter reads INFO SERVER run_id to detect Redis restarts.
            // Redis ACLs should grant INFO only to the gateway runtime identity.
            var options = ConfigurationOptions.Parse(connectionString);
            options.AllowAdmin = true;
            return ConnectionMultiplexer.Connect(options);
        });

        return services;
    }

    private static string GetRequiredConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres must be configured before persistence is used.");
        }

        return connectionString;
    }
}
