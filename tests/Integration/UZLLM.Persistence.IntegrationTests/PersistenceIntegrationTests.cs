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

    [Fact]
    public async Task Outbox_claims_a_message_once_when_workers_compete()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var writerProvider = fixture.CreateServiceProvider();
        await using var writerScope = writerProvider.CreateAsyncScope();
        var writerStore = writerScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var eventId = await writerStore.EnqueueAsync("payment.completed", "{}");

        await using var workerOneProvider = fixture.CreateServiceProvider();
        await using var workerOneScope = workerOneProvider.CreateAsyncScope();
        await using var workerTwoProvider = fixture.CreateServiceProvider();
        await using var workerTwoScope = workerTwoProvider.CreateAsyncScope();
        var workerOneStore = workerOneScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var workerTwoStore = workerTwoScope.ServiceProvider.GetRequiredService<IOutboxStore>();

        var claims = await Task.WhenAll(
            workerOneStore.ClaimAvailableAsync("worker-one", 10, TimeSpan.FromMinutes(1)),
            workerTwoStore.ClaimAvailableAsync("worker-two", 10, TimeSpan.FromMinutes(1)));

        var claimed = Assert.Single(claims.SelectMany(claim => claim));
        Assert.Equal(eventId, claimed.Id);
        (IOutboxStore Store, string Worker) winningWorker = claims[0].Count == 1
            ? (workerOneStore, "worker-one")
            : (workerTwoStore, "worker-two");
        (IOutboxStore Store, string Worker) losingWorker = claims[0].Count == 1
            ? (workerTwoStore, "worker-two")
            : (workerOneStore, "worker-one");

        Assert.True(await winningWorker.Store.MarkProcessedAsync(eventId, winningWorker.Worker));
        Assert.False(await losingWorker.Store.MarkProcessedAsync(eventId, losingWorker.Worker));
    }

    [Fact]
    public async Task Leased_job_claims_a_job_once_when_workers_compete()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var schedulerProvider = fixture.CreateServiceProvider();
        await using var schedulerScope = schedulerProvider.CreateAsyncScope();
        var scheduler = schedulerScope.ServiceProvider.GetRequiredService<ILeasedJobStore>();
        var jobId = await scheduler.ScheduleAsync("provider.health", "{}", "openai:primary", DateTimeOffset.UtcNow);

        await using var workerOneProvider = fixture.CreateServiceProvider();
        await using var workerOneScope = workerOneProvider.CreateAsyncScope();
        await using var workerTwoProvider = fixture.CreateServiceProvider();
        await using var workerTwoScope = workerTwoProvider.CreateAsyncScope();
        var workerOneStore = workerOneScope.ServiceProvider.GetRequiredService<ILeasedJobStore>();
        var workerTwoStore = workerTwoScope.ServiceProvider.GetRequiredService<ILeasedJobStore>();

        var claims = await Task.WhenAll(
            workerOneStore.ClaimAvailableAsync("worker-one", 10, TimeSpan.FromMinutes(1)),
            workerTwoStore.ClaimAvailableAsync("worker-two", 10, TimeSpan.FromMinutes(1)));

        var claimed = Assert.Single(claims.SelectMany(claim => claim));
        Assert.Equal(jobId, claimed.Id);
    }

    [Fact]
    public async Task Failed_outbox_lease_is_retried_by_a_new_worker_with_an_incremented_attempt()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var eventId = await firstStore.EnqueueAsync("payment.completed", "{}");
        var firstLease = Assert.Single(await firstStore.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));

        Assert.True(await firstStore.MarkFailedAsync(eventId, "worker-one", TimeSpan.Zero, "TransientFailure"));

        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var secondLease = Assert.Single(await secondStore.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));

        Assert.Equal(eventId, secondLease.Id);
        Assert.Equal(firstLease.AttemptCount + 1, secondLease.AttemptCount);
    }

    [Fact]
    public async Task Completed_leased_job_is_not_claimed_again()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILeasedJobStore>();
        var jobId = await store.ScheduleAsync("provider.health", "{}", "openai:primary", DateTimeOffset.UtcNow);
        var lease = Assert.Single(await store.ClaimAvailableAsync("worker-one", 1, TimeSpan.FromMinutes(1)));

        Assert.Equal(jobId, lease.Id);
        Assert.True(await store.MarkCompletedAsync(jobId, "worker-one"));
        Assert.Empty(await store.ClaimAvailableAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Consumer_inbox_records_an_event_once_for_the_same_consumer()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var eventId = Guid.CreateVersion7();

        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        var firstInbox = firstScope.ServiceProvider.GetRequiredService<IConsumerInboxStore>();

        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var secondInbox = secondScope.ServiceProvider.GetRequiredService<IConsumerInboxStore>();

        Assert.True(await firstInbox.TryRecordProcessedAsync("analytics", eventId));
        Assert.False(await secondInbox.TryRecordProcessedAsync("analytics", eventId));
    }

    [Fact]
    public async Task Critical_alert_publisher_persists_an_alert_and_outbox_message_together()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IOperationalAlertPublisher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();

        var alert = await publisher.RaiseAsync(
            OperationalAlertKind.SettlementFailure,
            "settlement:request-123",
            "{\"requestId\":\"request-123\"}");

        var alertCount = await dbContext.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM ops.operational_alert WHERE id = {0}", alert.Id)
            .SingleAsync();
        var outboxCount = await dbContext.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM ops.outbox WHERE event_type = 'ops.alert.raised'")
            .SingleAsync();

        Assert.Equal(1, alertCount);
        Assert.Equal(1, outboxCount);
    }

    [Fact]
    public async Task Postgres_readiness_reports_unhealthy_when_the_database_is_unreachable()
    {
        var options = new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none;Timeout=1;Command Timeout=1")
            .Options;
        await using var dbContext = new FoundationDbContext(options);
        var healthCheck = new PostgresReadinessHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Redis_readiness_reports_unhealthy_when_the_cache_is_unreachable()
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
            ConnectRetry = 0
        };
        options.EndPoints.Add("127.0.0.1", 1);
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var healthCheck = new RedisReadinessHealthCheck(multiplexer);

        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
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

    public async Task ApplyMigrationsAsync()
    {
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync();
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
