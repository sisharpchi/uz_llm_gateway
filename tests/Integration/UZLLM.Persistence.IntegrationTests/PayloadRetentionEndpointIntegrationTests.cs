using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class PayloadRetentionEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Policy_http_is_owner_only_csrf_protected_and_never_cacheable()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["PayloadSecrets:ActiveKeyVersion"] = "v1",
            ["PayloadSecrets:Keys:v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmUsage();
        builder.Services.AddUzllmPayloadRetentionPolicies();
        await using var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmPayloadRetentionEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await identity.RegisterAsync("payload-owner@example.uz",
                "correct horse battery staple");
            var outsider = await identity.RegisterAsync("payload-outsider@example.uz",
                "correct horse battery staple");
            var admin = await identity.RegisterAsync("payload-admin@example.uz",
                "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(outsider.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(admin.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync(
                "payload-owner@example.uz", "correct horse battery staple"))!;
            var outsiderSession = (await identity.AuthenticateAsync(
                "payload-outsider@example.uz", "correct horse battery staple"))!;
            var adminSession = (await identity.AuthenticateAsync(
                "payload-admin@example.uz", "correct horse battery staple"))!;
            var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "Payload policy tenant");
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(owner.AccountId, org.Id, "Production");
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
            {
                OrganizationId = org.Id, AccountId = admin.AccountId, Role = "Admin",
                Status = "Active", CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var foreignOrg = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(outsider.AccountId, "Other tenant");
            var foreignProject = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(outsider.AccountId, foreignOrg.Id, "Other project");
            var path = $"/management/v1/organizations/{org.Id}/projects/{project.Id}/payload-retention";
            var policy = new { enabled = true, retentionMinutes = 60 };

            using (var response = await client.GetAsync(path))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path,
                ownerSession, false, policy)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path,
                outsiderSession, true, policy)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path,
                adminSession, true, policy)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, path,
                adminSession, false)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var mismatchedPath = $"/management/v1/organizations/{org.Id}/projects/{foreignProject.Id}/payload-retention";
            using (var response = await client.SendAsync(Request(HttpMethod.Put, mismatchedPath,
                ownerSession, true, policy)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, mismatchedPath,
                ownerSession, false)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await db.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking().ToArrayAsync());
            Assert.Equal(0, await db.Set<AuditEventEntity>()
                .CountAsync(value => value.Action == "payload_retention.updated"));
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path,
                ownerSession, true, new { enabled = true, retentionMinutes = 59 })))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path,
                ownerSession, true, policy)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, path,
                ownerSession, false)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("\"enabled\":true", await response.Content.ReadAsStringAsync());
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                $"{path}/requests/{Guid.NewGuid()}", outsiderSession, false)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                $"{path}/requests/{Guid.NewGuid()}", ownerSession, false)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData("enable", false)]
    [InlineData("shorten", false)]
    [InlineData("disable", false)]
    [InlineData("enable", true)]
    [InlineData("shorten", true)]
    [InlineData("disable", true)]
    public async Task Policy_http_audit_failure_or_request_cancellation_rolls_back_all_effects(
        string change, bool cancel)
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var fault = new AuditFault();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["PayloadSecrets:ActiveKeyVersion"] = "v1",
            ["PayloadSecrets:Keys:v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmUsage();
        builder.Services.AddUzllmPayloadRetentionPolicies();
        builder.Services.AddScoped<IAuditEventStore>(services => new FaultingAuditStore(
            services.GetRequiredService<FoundationDbContext>(), fault));
        await using var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.Use(async (context, next) =>
        {
            var observe = fault.Enabled;
            try { await next(context); }
            finally { if (observe) fault.RequestFinished.TrySetResult(true); }
        });
        app.UseUzllmManagementSession();
        app.MapUzllmPayloadRetentionEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            Guid organizationId;
            Guid projectId;
            BrowserSessionTokens session;
            Guid accountId;
            await using (var seed = app.Services.CreateAsyncScope())
            {
                var identity = seed.ServiceProvider.GetRequiredService<IIdentityService>();
                var owner = await identity.RegisterAsync("retention-atomic@example.test",
                    "correct horse battery staple");
                accountId = owner.AccountId;
                Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
                session = (await identity.AuthenticateAsync("retention-atomic@example.test",
                    "correct horse battery staple"))!;
                organizationId = (await seed.ServiceProvider.GetRequiredService<IOrganizationService>()
                    .CreateAsync(accountId, "Atomic retention")).Id;
                projectId = (await seed.ServiceProvider.GetRequiredService<IProjectService>()
                    .CreateAsync(accountId, organizationId, "Project")).Id;
            }
            var path = $"/management/v1/organizations/{organizationId}/projects/{projectId}/payload-retention";
            var expectedPayloads = new Dictionary<Guid, DateTimeOffset>();
            if (change != "enable")
            {
                using var enabled = await client.SendAsync(Request(HttpMethod.Put, path, session,
                    true, new { enabled = true, retentionMinutes = 120 }));
                Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
                await using var seed = app.Services.CreateAsyncScope();
                var db = seed.ServiceProvider.GetRequiredService<FoundationDbContext>();
                var retention = seed.ServiceProvider.GetRequiredService<IPayloadRetentionService>();
                foreach (var age in new[] { 0, 90 })
                {
                    var requestId = await SeedRequestAsync(db, organizationId, projectId, accountId);
                    Assert.True(await retention.CaptureRequestAsync(organizationId, projectId,
                        requestId, Encoding.UTF8.GetBytes("synthetic private prompt"), default));
                    var created = DateTimeOffset.UtcNow.AddMinutes(-age);
                    var expires = created.AddMinutes(120);
                    await db.Set<UsagePayloadEntity>().Where(value => value.RequestId == requestId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.CreatedAt, created)
                            .SetProperty(value => value.ExpiresAt, expires));
                    expectedPayloads.Add(requestId, await db.Set<UsagePayloadEntity>().AsNoTracking()
                        .Where(value => value.RequestId == requestId).Select(value => value.ExpiresAt).SingleAsync());
                }
            }
            var enabledAfter = change != "disable";
            var minutesAfter = change == "disable" ? 120 : 60;
            var command = new { enabled = enabledAfter, retentionMinutes = minutesAfter };
            var baselineAuditCount = change == "enable" ? 0 : 1;
            fault.Enabled = true;
            fault.Cancel = cancel;
            using var abort = new CancellationTokenSource();
            using var request = Request(HttpMethod.Put, path, session, true, command);
            var pending = client.SendAsync(request, abort.Token);
            await fault.AuditReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (cancel)
            {
                abort.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else
            {
                using var failed = await pending;
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal("no-store", failed.Headers.CacheControl?.ToString());
                var errorBody = await failed.Content.ReadAsStringAsync();
                Assert.Contains("Unable to update payload retention.", errorBody);
                Assert.DoesNotContain("Injected", errorBody);
                Assert.DoesNotContain("synthetic private prompt", errorBody);
            }
            await fault.RequestFinished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            fault.Enabled = false;

            // A fresh context observes committed state, not tracked pre-rollback objects.
            await using (var verify = app.Services.CreateAsyncScope())
            {
                var db = verify.ServiceProvider.GetRequiredService<FoundationDbContext>();
                var policy = await db.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking()
                    .SingleOrDefaultAsync(value => value.ProjectId == projectId);
                if (change == "enable") Assert.Null(policy);
                else
                {
                    Assert.True(policy!.Enabled);
                    Assert.Equal(120, policy.RetentionMinutes);
                }
                var payloads = await db.Set<UsagePayloadEntity>().AsNoTracking().ToArrayAsync();
                Assert.Equal(expectedPayloads.Count, payloads.Length);
                foreach (var payload in payloads)
                {
                    Assert.Equal(expectedPayloads[payload.RequestId], payload.ExpiresAt);
                    Assert.DoesNotContain("synthetic private prompt", Encoding.UTF8.GetString(payload.EncryptedRequestPayload));
                }
                Assert.Equal(baselineAuditCount, await db.Set<AuditEventEntity>()
                    .CountAsync(value => value.Action == "payload_retention.updated"));
            }
            // Retry the same replacement after failure: exactly one committed audit/effect.
            using var succeeded = await client.SendAsync(Request(HttpMethod.Put, path, session, true, command));
            Assert.Equal(HttpStatusCode.OK, succeeded.StatusCode);
            Assert.Equal("no-store", succeeded.Headers.CacheControl?.ToString());
            Assert.Equal(new PayloadRetentionPolicy(enabledAfter, minutesAfter),
                await succeeded.Content.ReadFromJsonAsync<PayloadRetentionPolicy>());
            await using var final = app.Services.CreateAsyncScope();
            var committed = final.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var finalPolicy = await committed.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking().SingleAsync();
            Assert.Equal(enabledAfter, finalPolicy.Enabled);
            Assert.Equal(minutesAfter, finalPolicy.RetentionMinutes);
            var events = await committed.Set<AuditEventEntity>().AsNoTracking()
                .Where(value => value.Action == "payload_retention.updated").OrderBy(value => value.OccurredAt).ToArrayAsync();
            Assert.Equal(baselineAuditCount + 1, events.Length);
            var audit = events[^1];
            Assert.Equal(accountId, audit.ActorAccountId);
            Assert.Equal(organizationId, audit.OrganizationId);
            Assert.Equal(projectId, audit.ResourceId);
            Assert.Equal("project", audit.ResourceType);
            Assert.NotNull(audit.IpAddress);
            using var metadata = JsonDocument.Parse(audit.MetadataJson);
            Assert.Equal(enabledAfter, metadata.RootElement.GetProperty("Enabled").GetBoolean());
            Assert.Equal(minutesAfter, metadata.RootElement.GetProperty("RetentionMinutes").GetInt32());
            Assert.Equal(2, metadata.RootElement.EnumerateObject().Count());
            var finalPayloads = await committed.Set<UsagePayloadEntity>().AsNoTracking().ToArrayAsync();
            if (change == "shorten")
            {
                var remaining = Assert.Single(finalPayloads);
                Assert.Equal(remaining.CreatedAt.AddMinutes(60), remaining.ExpiresAt);
            }
            else Assert.Empty(finalPayloads);
            Assert.Equal(expectedPayloads.Count, await committed.Set<UsageRequestEntity>()
                .CountAsync(value => value.ProjectId == projectId));
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Policy_store_rejects_mutation_without_an_enclosing_transaction()
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var configuration = new ConfigurationBuilder().Build();
        var store = new PostgreSqlPayloadRetentionService(db,
            new PayloadEnvelopeProtector(configuration), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetPolicyAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, 60, default));
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static async Task<Guid> SeedRequestAsync(FoundationDbContext db, Guid organizationId,
        Guid projectId, Guid accountId)
    {
        var now = DateTimeOffset.UtcNow;
        var key = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();
        var request = Guid.CreateVersion7();
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = key, ProjectId = projectId, Name = "Retention fixture", Prefix = Guid.NewGuid().ToString("N")[..12],
            SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
            CreatedByAccountId = accountId, CreatedAt = now
        });
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = model, CanonicalCode = $"retention-{model:N}", DisplayName = "Retention fixture",
            ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]",
            Status = "Active", CreatedAt = now
        });
        db.Set<UsageRequestEntity>().Add(new UsageRequestEntity
        {
            Id = request, OrganizationId = organizationId, ProjectId = projectId, ApiKeyId = key,
            CanonicalModelId = model, StartedAt = now, ExecutionState = "Prepared",
            DeliveryState = "NotStarted", FinancialState = "PendingAdmission", Operation = "chat.completions"
        });
        await db.SaveChangesAsync();
        return request;
    }

    private sealed class AuditFault
    {
        public bool Enabled;
        public bool Cancel;
        public TaskCompletionSource<bool> AuditReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RequestFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FaultingAuditStore(FoundationDbContext db, AuditFault fault) : IAuditEventStore
    {
        public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await new PostgreSqlAuditEventStore(db).AppendAsync(auditEvent, cancellationToken);
            if (!fault.Enabled || auditEvent.Action != "payload_retention.updated") return;
            // Fail after the audit insert was flushed, not before any database work.
            fault.AuditReached.TrySetResult(true);
            if (fault.Cancel) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Injected retention audit failure.");
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path,
        BrowserSessionTokens session, bool csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
