using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[CollectionDefinition(nameof(PersistenceIntegrationCollection), DisableParallelization = true)]
public sealed class PersistenceIntegrationCollection : ICollectionFixture<PersistenceIntegrationFixture>;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class PersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly string[] SchemaNames = ["iam", "org", "gateway", "catalog", "billing", "payment", "usage", "ops", "audit"];

    [Fact]
    public async Task Migrator_creates_all_foundation_schemas()
    {
        await fixture.ResetMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync();

        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var schemaName in SchemaNames)
        {
            var exists = await dbContext.Database
                .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = {schemaName}) AS \"Value\"")
                .SingleAsync();

            Assert.True(exists, $"Schema '{schemaName}' was not created.");
        }
    }

    [Fact]
    public async Task Migrator_rolls_back_foundation_schemas_to_zero()
    {
        await fixture.ResetMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync();
        await migrator.MigrateAsync("0");

        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var opsSchemaExists = await dbContext.Database
            .SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'ops') AS \"Value\"")
            .SingleAsync();

        Assert.False(opsSchemaExists);
    }

    [Fact]
    public async Task Transaction_scope_rolls_back_uncommitted_postgresql_work()
    {
        await fixture.ResetMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var transactionCoordinator = scope.ServiceProvider.GetRequiredService<ITransactionCoordinator>();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();

        await migrator.MigrateAsync();

        await using (await transactionCoordinator.BeginAsync())
        {
            await dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE ops.transaction_rollback_probe (id integer PRIMARY KEY);");
        }

        var probeExists = await dbContext.Database
            .SqlQueryRaw<string?>("SELECT to_regclass('ops.transaction_rollback_probe') AS \"Value\"")
            .SingleAsync();

        Assert.Null(probeExists);
    }

    [Fact]
    public async Task Runtime_database_role_cannot_create_schema_objects()
    {
        await fixture.ResetMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ops.runtime_must_not_create (id integer PRIMARY KEY);";

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Redis_registration_connects_to_the_configured_instance()
    {
        await using var provider = fixture.CreateServiceProvider();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        var latency = await multiplexer.GetDatabase().PingAsync();

        Assert.True(latency >= TimeSpan.Zero);
    }
}

public sealed class PersistenceIntegrationFixture : IAsyncLifetime
{
    private const string DatabaseName = "uzllm";
    private const string MigratorUser = "uzllm_migrator";
    private const string MigratorPassword = "uzllm_migrator_test_password";
    private const string RuntimeUser = "uzllm_runtime";
    private const string RuntimePassword = "uzllm_runtime_test_password";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername(MigratorUser)
        .WithPassword(MigratorPassword)
        .Build();

    private readonly RedisContainer redis = new RedisBuilder("redis:8-alpine")
        .Build();

    public string RuntimeConnectionString => new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
    {
        Username = RuntimeUser,
        Password = RuntimePassword
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE ROLE {RuntimeUser} LOGIN PASSWORD '{RuntimePassword}';";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), redis.DisposeAsync().AsTask());
    }

    public async Task ResetMigrationsAsync()
    {
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync("0");
    }

    public ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = redis.GetConnectionString()
            })
            .Build();

        return new ServiceCollection()
            .AddUzllmPersistence(configuration)
            .AddUzllmRedis(configuration)
            .BuildServiceProvider(validateScopes: true);
    }
}
