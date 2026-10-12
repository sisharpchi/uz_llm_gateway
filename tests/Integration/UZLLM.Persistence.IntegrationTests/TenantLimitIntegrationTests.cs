using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using UZLLM.Management.Api;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Domain;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;
using IServer = Microsoft.AspNetCore.Hosting.Server.IServer;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class TenantLimitIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Limit_HTTP_controls_enforce_current_roles_csrf_tenant_bounds_and_reset()
    {
        await using var app = await StartAsync();
        var seed = await SeedAsync(app.Services);
        using var client = Client(app);
        var projectPath = $"/management/v1/projects/{seed.Project.Id}/limits";
        var keyPath = $"/management/v1/api-keys/{seed.Key.ApiKey.Id}/limits";
        foreach (var path in new[] { projectPath, keyPath })
        {
            using var anonymous = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var outsider = await client.SendAsync(Request(HttpMethod.Get, path, seed.OtherSession));
            Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
            using var noCsrf = await client.SendAsync(Request(HttpMethod.Put, path, seed.OwnerSession, new TenantLimits(10, 2), false));
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
            foreach (var invalid in new object[] { new { requestsPerMinute = 0 }, new { concurrency = -1 },
                new { requestsPerMinute = int.MaxValue }, new { concurrency = 65 }, new { requestsPerMinute = 601 },
                new { leaseSeconds = 1 }, new { requestPerMinute = 1 }, new { concurrency = 1.5 } })
            {
                using var bad = await client.SendAsync(Request(HttpMethod.Put, path, seed.OwnerSession, invalid));
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
                Assert.Equal("no-store", bad.Headers.CacheControl?.ToString());
            }
            using var updated = await client.SendAsync(Request(HttpMethod.Put, path, seed.OwnerSession, new TenantLimits(10, 2)));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var view = (await updated.Content.ReadFromJsonAsync<TenantLimitView>())!;
            Assert.Equal(new TenantLimits(10, 2), view.Effective);
            using var read = await client.SendAsync(Request(HttpMethod.Get, path, seed.OwnerSession));
            Assert.Equal(view, await read.Content.ReadFromJsonAsync<TenantLimitView>());
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.Set<OrganizationMemberEntity>().Add(new() { OrganizationId = seed.Project.OrganizationId,
            AccountId = seed.OtherId, Role = "Developer", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        using (var ungranted = await client.SendAsync(Request(HttpMethod.Put, keyPath, seed.OtherSession, new TenantLimits(5, 1))))
            Assert.Equal(HttpStatusCode.Forbidden, ungranted.StatusCode);
        db.Set<ProjectGrantEntity>().Add(new() { OrganizationId = seed.Project.OrganizationId,
            AccountId = seed.OtherId, ProjectId = seed.Project.Id, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        using (var granted = await client.SendAsync(Request(HttpMethod.Put, keyPath, seed.OtherSession, new TenantLimits(5, 1))))
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using (var projectDenied = await client.SendAsync(Request(HttpMethod.Put, projectPath, seed.OtherSession, new TenantLimits(5, 1))))
            Assert.Equal(HttpStatusCode.Forbidden, projectDenied.StatusCode);
        foreach (var role in new[] { "ReadOnly", "BillingViewer" })
        {
            await db.Set<OrganizationMemberEntity>().Where(member => member.OrganizationId == seed.Project.OrganizationId
                && member.AccountId == seed.OtherId).ExecuteUpdateAsync(setters => setters.SetProperty(member => member.Role, role));
            using var denied = await client.SendAsync(Request(HttpMethod.Put, keyPath, seed.OtherSession, new TenantLimits()));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        await db.Set<OrganizationMemberEntity>().Where(member => member.OrganizationId == seed.Project.OrganizationId
            && member.AccountId == seed.OtherId).ExecuteUpdateAsync(setters => setters.SetProperty(member => member.Role, "Admin"));
        using (var reset = await client.SendAsync(Request(HttpMethod.Put, projectPath, seed.OtherSession, new TenantLimits())))
        {
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            var view = (await reset.Content.ReadFromJsonAsync<TenantLimitView>())!;
            Assert.Equal(new TenantLimits(), view.Configured);
            Assert.Equal(new TenantLimits(600, 64), view.Effective);
        }
        using (var missing = await client.SendAsync(Request(HttpMethod.Get,
            $"/management/v1/api-keys/{Guid.NewGuid()}/limits", seed.OwnerSession)))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var audit = await db.Set<AuditEventEntity>().Where(value => value.Action.EndsWith(".limits_changed")).ToListAsync();
        Assert.Equal(4, audit.Count);
        Assert.Contains(audit, value => value.ActorAccountId == seed.OtherId && value.Action == "project.limits_changed");
        Assert.All(audit, value => Assert.Equal(seed.Project.OrganizationId, value.OrganizationId));
    }

    [Fact]
    public async Task Concurrent_policy_replacement_is_atomic_and_failed_audit_rolls_back_changes()
    {
        await using var app = await StartAsync();
        var seed = await SeedAsync(app.Services);
        await Task.WhenAll(Enumerable.Range(1, 20).Select(async number =>
        {
            await using var writer = app.Services.CreateAsyncScope();
            await writer.ServiceProvider.GetRequiredService<ITenantLimitService>()
                .ReplaceAsync(seed.OwnerId, seed.Project.Id, false, new(number, number));
        }));
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var policy = await services.GetRequiredService<ITenantLimitService>().ReadAsync(seed.OwnerId, seed.Project.Id, false);
        Assert.Equal(policy.Configured.RequestsPerMinute, policy.Configured.Concurrency);
        Assert.InRange(policy.Configured.Concurrency!.Value, 1, 20);
        Assert.Equal(20, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "project.limits_changed"));
        var failing = new TenantLimitService(services.GetRequiredService<ITenantLimitStore>(),
            services.GetRequiredService<IApiKeyStore>(), services.GetRequiredService<IProjectAccessService>(),
            services.GetRequiredService<TenantLimitCeilings>(), new FailingAudit(), services.GetRequiredService<ITransactionCoordinator>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ReplaceAsync(seed.OwnerId, seed.Project.Id, false, new(40, 40)));
        Assert.Equal(policy, await services.GetRequiredService<ITenantLimitService>().ReadAsync(seed.OwnerId, seed.Project.Id, false));
        Assert.Equal(20, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "project.limits_changed"));
    }

    [Fact]
    public async Task Two_nodes_enforce_live_PG_limits_without_resetting_RPM_or_existing_leases()
    {
        await using var app = await StartAsync();
        var seed = await SeedAsync(app.Services);
        using var first = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        using var second = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var epoch = new Epoch(Guid.NewGuid().ToString("N"));
        var recovery = new LimitRecoveryOptions(TimeSpan.FromSeconds(15));
        var nodeA = new RedisAdmissionLimiter(first, epoch, recovery);
        var nodeB = new RedisAdmissionLimiter(second, epoch, recovery);
        var policy = new LimitPolicy(60, 600, 8, 64, TimeSpan.FromSeconds(14));
        var keyScope = new LimitScope(seed.Project.OrganizationId, seed.Project.Id, seed.Key.ApiKey.Id);
        async Task<LimitDecision> AdmitAsync(bool a, LimitScope? target = null)
        {
            await using var scope = app.Services.CreateAsyncScope();
            return await new TenantAdmissionLimiter(scope.ServiceProvider.GetRequiredService<ITenantLimitStore>(), a ? nodeA : nodeB)
                .TryAcquireAsync(target ?? keyScope, policy, Guid.NewGuid());
        }
        async Task SetAsync(bool key, TenantLimits value)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITenantLimitService>()
                .ReplaceAsync(seed.OwnerId, key ? seed.Key.ApiKey.Id : seed.Project.Id, key, value);
        }
        Assert.Equal(LimitOutcome.RecoveryWindow, (await AdmitAsync(true)).Outcome);
        await Task.Delay(15_100);
        await SetAsync(false, new(null, 3));
        var decisions = await Task.WhenAll(Enumerable.Range(0, 50).Select(index => AdmitAsync(index % 2 == 0)));
        var holds = decisions.Where(value => value.Outcome == LimitOutcome.Admitted).ToArray();
        Assert.Equal(3, holds.Length);
        Assert.Equal(47, decisions.Count(value => value.Outcome == LimitOutcome.ConcurrencyLimited));
        await SetAsync(false, new(null, 1));
        Assert.Equal(LimitOutcome.ConcurrencyLimited, (await AdmitAsync(false)).Outcome);
        foreach (var hold in holds)
            Assert.Equal(LimitReleaseOutcome.Released, await nodeB.ReleaseAsync(hold.Lease!));
        var next = await AdmitAsync(false);
        Assert.Equal(LimitOutcome.Admitted, next.Outcome);
        await nodeA.ReleaseAsync(next.Lease!);
        await SetAsync(false, new());
        await SetAsync(true, new(4, null));
        Assert.Equal(LimitOutcome.RateLimited, (await AdmitAsync(false)).Outcome); // Four earlier admissions still counted.
        await SetAsync(true, new());
        next = await AdmitAsync(true);
        Assert.Equal(LimitOutcome.Admitted, next.Outcome);
        await nodeA.ReleaseAsync(next.Lease!);
        await SetAsync(false, new(5, null));
        await using var creation = app.Services.CreateAsyncScope();
        var otherKey = await creation.ServiceProvider.GetRequiredService<IApiKeyService>()
            .CreateAsync(seed.OwnerId, seed.Project.Id, "Second", null);
        Assert.Equal(LimitOutcome.RateLimited, (await AdmitAsync(false, keyScope with { ApiKeyId = otherKey.ApiKey.Id })).Outcome);
        Assert.Equal(LimitOutcome.DependencyUnavailable,
            (await AdmitAsync(true, keyScope with { OrganizationId = Guid.NewGuid() })).Outcome);
        var independent = await creation.ServiceProvider.GetRequiredService<IProjectService>()
            .CreateAsync(seed.OwnerId, seed.Project.OrganizationId, "Key concurrency");
        var limitedKey = await creation.ServiceProvider.GetRequiredService<IApiKeyService>()
            .CreateAsync(seed.OwnerId, independent.Id, "Limited", null);
        var settings = creation.ServiceProvider.GetRequiredService<ITenantLimitService>();
        await settings.ReplaceAsync(seed.OwnerId, limitedKey.ApiKey.Id, true, new(null, 1));
        var independentScope = keyScope with { ProjectId = independent.Id, ApiKeyId = limitedKey.ApiKey.Id };
        var keyRace = await Task.WhenAll(Enumerable.Range(0, 10).Select(index => AdmitAsync(index % 2 == 0, independentScope)));
        var keyHold = Assert.Single(keyRace, value => value.Outcome == LimitOutcome.Admitted);
        Assert.Equal(9, keyRace.Count(value => value.Outcome == LimitOutcome.ConcurrencyLimited));
        await settings.ReplaceAsync(seed.OwnerId, limitedKey.ApiKey.Id, true, new(1, null));
        Assert.Equal(LimitOutcome.RateLimited, (await AdmitAsync(false, independentScope)).Outcome);
        await settings.ReplaceAsync(seed.OwnerId, limitedKey.ApiKey.Id, true, new());
        var afterReset = await AdmitAsync(false, independentScope);
        Assert.Equal(LimitOutcome.Admitted, afterReset.Outcome);
        Assert.Equal(LimitReleaseOutcome.Released, await nodeA.ReleaseAsync(keyHold.Lease!));
        Assert.Equal(LimitReleaseOutcome.Released, await nodeB.ReleaseAsync(afterReset.Lease!));
        // Restart has a full recovery horizon, independently of tenant policy reset.
        epoch.Value = Guid.NewGuid().ToString("N");
        Assert.Equal(LimitOutcome.RecoveryWindow, (await AdmitAsync(false)).Outcome);
        using var lost = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var unavailable = new RedisAdmissionLimiter(lost, new RedisServerEpochSource(lost), recovery);
        lost.Dispose();
        await using var failedScope = app.Services.CreateAsyncScope();
        var wrapped = new TenantAdmissionLimiter(failedScope.ServiceProvider.GetRequiredService<ITenantLimitStore>(), unavailable);
        Assert.Equal(LimitOutcome.DependencyUnavailable, (await wrapped.TryAcquireAsync(keyScope, policy, Guid.NewGuid())).Outcome);
        var unavailablePostgres = new NpgsqlConnectionStringBuilder(fixture.RuntimeConnectionString)
            { Host = "127.0.0.1", Port = 1, Timeout = 1, Pooling = false };
        await using var lostDb = new FoundationDbContext(new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(unavailablePostgres.ConnectionString).Options);
        var pgUnavailable = new TenantAdmissionLimiter(new PostgreSqlTenantLimitStore(lostDb), nodeA);
        Assert.Equal(LimitOutcome.DependencyUnavailable,
            (await pgUnavailable.TryAcquireAsync(keyScope, policy, Guid.NewGuid())).Outcome);
    }

    [Fact]
    public async Task Migration_preserves_existing_defaults_enforces_bounds_and_refuses_lossy_downgrade()
    {
        await using var app = await StartAsync();
        var seed = await SeedAsync(app.Services);
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ITenantLimitStore>();
        var db = services.GetRequiredService<FoundationDbContext>();
        var keyScope = new LimitScope(seed.Project.OrganizationId, seed.Project.Id, seed.Key.ApiKey.Id);
        Assert.Equal(new TenantLimitSnapshot(new(), new()), await store.FindAsync(keyScope));
        foreach (var key in new[] { false, true })
        {
            await services.GetRequiredService<ITenantLimitService>().ReplaceAsync(seed.OwnerId,
                key ? seed.Key.ApiKey.Id : seed.Project.Id, key, new(1, 1));
            await using var migration = fixture.CreateServiceProvider();
            await using var migrationScope = migration.CreateAsyncScope();
            var migrator = migrationScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
            await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261012120511_AddApiKeyRoutePermissions"));
            Assert.Equal(new TenantLimits(1, 1), await store.ReadAsync(seed.Project.Id, key ? seed.Key.ApiKey.Id : null));
            await store.ReplaceAsync(seed.Project.Id, key ? seed.Key.ApiKey.Id : null, new());
        }
        foreach (var key in new[] { false, true })
        {
            foreach (var invalid in new TenantLimits[] { new(0, 1), new(1_000_001, 1), new(1, 0), new(1, 10_001) })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => store.ReplaceAsync(seed.Project.Id,
                    key ? seed.Key.ApiKey.Id : null, invalid));
                Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            }
        }
        await using var provider = fixture.CreateServiceProvider();
        await using var migratorScope = provider.CreateAsyncScope();
        var migrationService = migratorScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrationService.MigrateAsync("20261012120511_AddApiKeyRoutePermissions");
        await migrationService.MigrateAsync();
        Assert.Equal(new TenantLimitSnapshot(new(), new()), await store.FindAsync(keyScope));
        Assert.NotNull(await services.GetRequiredService<IApiKeyAuthenticator>().AuthenticateAsync(seed.Key.Secret));
    }

    private async Task<WebApplication> StartAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ConnectionStrings:Redis"] = fixture.RedisConnectionString,
            ["ApiKeys:FingerprintKey"] = Convert.ToBase64String(new byte[32]) });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmRedis(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmApiKeys(builder.Configuration);
        var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmManagementSession();
        app.MapUzllmApiKeyEndpoints();
        await app.StartAsync();
        return app;
    }

    private static async Task<Seed> SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        const string password = "correct horse battery staple";
        var owner = await identity.RegisterAsync("limits-owner@example.test", password);
        var other = await identity.RegisterAsync("limits-other@example.test", password);
        Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
        Assert.True(await identity.VerifyEmailAsync(other.VerificationToken));
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>().CreateAsync(owner.AccountId, "Limits tenant");
        var project = await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(owner.AccountId, org.Id, "Primary");
        var key = await scope.ServiceProvider.GetRequiredService<IApiKeyService>().CreateAsync(owner.AccountId, project.Id, "Primary", null);
        return new(owner.AccountId, other.AccountId, project, key,
            (await identity.AuthenticateAsync("limits-owner@example.test", password))!,
            (await identity.AuthenticateAsync("limits-other@example.test", password))!);
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Services
        .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
    private sealed record Seed(Guid OwnerId, Guid OtherId, Project Project, IssuedGatewayApiKey Key,
        BrowserSessionTokens OwnerSession, BrowserSessionTokens OtherSession);
    private sealed class FailingAudit : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic audit failure");
    }
    private sealed class Epoch(string value) : IRedisEpochSource
    {
        public string Value { get; set; } = value;
        public Task<string> GetCurrentEpochAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }
}
