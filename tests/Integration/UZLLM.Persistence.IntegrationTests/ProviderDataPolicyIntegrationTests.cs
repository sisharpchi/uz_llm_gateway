using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Management.Api;
using UZLLM.Management.Api.Administration;
using UZLLM.Management.Api.Catalog;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ProviderDataPolicyIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private const string Password = "correct horse battery staple";
    private static PublishProviderDataPolicyRequest Publication(DateTimeOffset observed) => new(
        "MayRetain", "NoTrainingReported", "ProviderReported", ["US"], "Synthetic public API scope",
        "Conditional on endpoint and account settings; not a gateway guarantee.",
        "https://example.com/provider-policy", observed, "Document synthetic provider policy evidence");

    [Fact]
    public async Task Customer_policy_returns_explicit_unknown_and_enforces_tenant_project_boundaries()
    {
        await ResetAsync();
        var clock = new PolicyClock();
        await using var app = App(clock);
        await app.StartAsync();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var owner = await RegisterAsync(services, "policy-owner@example.uz");
            var outsider = await RegisterAsync(services, "policy-outsider@example.uz");
            var org = await services.GetRequiredService<IOrganizationService>().CreateAsync(owner.Id, "Policy tenant");
            var project = await services.GetRequiredService<IProjectService>().CreateAsync(owner.Id, org.Id, "Policy project");
            var provider = await services.GetRequiredService<ICatalogService>().AddProviderAsync("openai", "OpenAI");
            var path = CustomerPath(org.Id, project.Id);
            using var client = Client(app);
            using (var anonymous = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using (var denied = await client.SendAsync(Request(HttpMethod.Get, path, outsider.Session)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var wrongOrg = await client.SendAsync(Request(HttpMethod.Get, CustomerPath(Guid.NewGuid(), project.Id), owner.Session)))
                Assert.Equal(HttpStatusCode.Forbidden, wrongOrg.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, path, owner.Session)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                var policy = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(provider.Id, policy.GetProperty("providerId").GetGuid());
                foreach (var name in new[] { "retention", "training", "residency" })
                    Assert.Equal("Unknown", policy.GetProperty(name).GetString());
                foreach (var name in new[] { "versionId", "sourceUrl", "sourceObservedAt", "publishedAt", "scope", "conditions" })
                    Assert.Equal(JsonValueKind.Null, policy.GetProperty(name).ValueKind);
                Assert.Empty(policy.GetProperty("regions").EnumerateArray());
                Assert.False(policy.TryGetProperty("zdr", out _));
                Assert.False(policy.TryGetProperty("publishedByAccountId", out _));
            }
            using (var missing = await client.SendAsync(Request(HttpMethod.Get,
                path.Replace("openai", "missing"), owner.Session))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await services.GetRequiredService<IProjectService>().ArchiveAsync(owner.Id, org.Id, project.Id);
            using (var archived = await client.SendAsync(Request(HttpMethod.Get, path, owner.Session)))
                Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Publication_requires_operator_recent_mfa_csrf_and_records_dated_audit()
    {
        await ResetAsync();
        var clock = new PolicyClock();
        await using var app = App(clock);
        await app.StartAsync();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var identity = services.GetRequiredService<IIdentityService>();
            var op = await RegisterAsync(services, "policy-operator@example.uz", true);
            var customer = await RegisterAsync(services, "policy-customer@example.uz");
            var org = await services.GetRequiredService<IOrganizationService>().CreateAsync(customer.Id, "Policy reader");
            var project = await services.GetRequiredService<IProjectService>().CreateAsync(customer.Id, org.Id, "Reader");
            var provider = await services.GetRequiredService<ICatalogService>().AddProviderAsync("openai", "OpenAI");
            var path = $"/management/v1/admin/providers/{provider.Id}/data-policies";
            var body = Publication(clock.Now.AddDays(-1));
            using var client = Client(app);
            using (var anonymous = await client.PostAsJsonAsync(path, body)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using (var denied = await client.SendAsync(Request(HttpMethod.Post, path, customer.Session, body, true)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var noMfa = await client.SendAsync(Request(HttpMethod.Post, path, op.Session, body, true)))
                Assert.Equal(HttpStatusCode.Forbidden, noMfa.StatusCode);
            var enrollment = await identity.EnrollOperatorMfaAsync(op.Id, Password);
            Assert.True(await identity.ConfirmOperatorMfaAsync(op.Session.SessionToken,
                new TotpAuthenticator().CreateCode(enrollment.SharedSecret, clock.Now)));
            using (var noCsrf = await client.SendAsync(Request(HttpMethod.Post, path, op.Session, body)))
                Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
            using (var invalid = await client.SendAsync(Request(HttpMethod.Post, path, op.Session,
                body with { Retention = "ZDR" }, true))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            foreach (var invalidReason in new[] { "short", "\nDocument synthetic provider policy evidence" })
                using (var invalid = await client.SendAsync(Request(HttpMethod.Post, path, op.Session,
                    body with { Reason = invalidReason }, true))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var missing = await client.SendAsync(Request(HttpMethod.Post,
                $"/management/v1/admin/providers/{Guid.NewGuid()}/data-policies", op.Session, body, true)))
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var published = await client.SendAsync(Request(HttpMethod.Post, path, op.Session, body, true));
            Assert.Equal(HttpStatusCode.Created, published.StatusCode);
            Assert.Equal("no-store", published.Headers.CacheControl?.ToString());
            var version = (await published.Content.ReadFromJsonAsync<AdminProviderDataPolicyResponse>())!;
            Assert.Equal(op.Id, version.PublishedByAccountId);
            Assert.Equal(body.SourceObservedAt.Ticks / 10, version.Policy.SourceObservedAt!.Value.Ticks / 10);
            using (var exact = await client.SendAsync(Request(HttpMethod.Get, published.Headers.Location!.ToString(), op.Session)))
            {
                var saved = (await exact.Content.ReadFromJsonAsync<AdminProviderDataPolicyResponse>())!;
                Assert.Equal(version.Policy.VersionId, saved.Policy.VersionId);
                Assert.Equal(version.Policy.PublishedAt, saved.Policy.PublishedAt);
                Assert.Equal(version.Policy.SourceObservedAt, saved.Policy.SourceObservedAt);
            }
            using (var duplicate = await client.SendAsync(Request(HttpMethod.Post, path, op.Session, body, true)))
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using (var limit = await client.SendAsync(Request(HttpMethod.Get, path + "?limit=101", op.Session)))
                Assert.Equal(HttpStatusCode.BadRequest, limit.StatusCode);
            using (var denied = await client.SendAsync(Request(HttpMethod.Get, path, customer.Session)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var history = await client.SendAsync(Request(HttpMethod.Get, path, op.Session)))
                Assert.Single((await history.Content.ReadFromJsonAsync<AdminProviderDataPolicyResponse[]>())!);
            using (var read = await client.SendAsync(Request(HttpMethod.Get, CustomerPath(org.Id, project.Id), customer.Session)))
            {
                var policy = await read.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("MayRetain", policy.GetProperty("retention").GetString());
                Assert.Equal("NoTrainingReported", policy.GetProperty("training").GetString());
                Assert.Equal(body.Conditions, policy.GetProperty("conditions").GetString());
                Assert.False(policy.TryGetProperty("publishedByAccountId", out _));
                Assert.False(policy.TryGetProperty("zdr", out _));
            }
            var db = services.GetRequiredService<FoundationDbContext>();
            var audit = await db.Set<AuditEventEntity>().AsNoTracking()
                .SingleAsync(value => value.Action == "provider-data-policy.published");
            Assert.Equal(op.Id, audit.ActorAccountId);
            Assert.Equal(version.Policy.VersionId, audit.ResourceId);
            Assert.Null(audit.OrganizationId);
            Assert.Contains(body.Reason, audit.MetadataJson);
            clock.Now = clock.Now.AddMinutes(16);
            using (var expired = await client.SendAsync(Request(HttpMethod.Post, path, op.Session,
                body with { SourceObservedAt = clock.Now }, true))) Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
            Assert.Equal(1, await db.Set<CatalogProviderDataPolicyEntity>().CountAsync());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Concurrent_publication_preserves_history_unknown_correction_and_append_only_evidence()
    {
        await ResetAsync();
        var clock = new PolicyClock();
        await using var app = App(clock);
        await using var scope = app.Services.CreateAsyncScope();
        var op = await RegisterAsync(scope.ServiceProvider, "policy-concurrent@example.uz", true);
        var provider = await scope.ServiceProvider.GetRequiredService<ICatalogService>().AddProviderAsync("openai", "OpenAI");
        var body = Publication(clock.Now.AddDays(-2));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var attempt = app.Services.CreateAsyncScope();
            try
            {
                await attempt.ServiceProvider.GetRequiredService<IProviderDataPolicyAdminService>()
                    .PublishAsync(op.Id, provider.Id, body, default);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Equal(1, attempts.Count(value => value));
        var service = scope.ServiceProvider.GetRequiredService<IProviderDataPolicyAdminService>();
        var original = Assert.Single(await service.ListAsync(provider.Id, 100, default));
        var correction = await service.PublishAsync(op.Id, provider.Id, body with
        {
            Retention = "Unknown", Training = "Unknown", Residency = "Unknown", Regions = [],
            Conditions = "Prior conditions could no longer be verified; all claims are unknown.",
            SourceObservedAt = clock.Now.AddDays(-1)
        }, default);
        var policyService = scope.ServiceProvider.GetRequiredService<IProviderDataPolicyService>();
        Assert.Equal(correction.Policy.VersionId, (await policyService.FindCurrentAsync(provider.Id))!.Id);
        Assert.Equal("Unknown", (await policyService.FindCurrentAsync(provider.Id))!.Retention);
        Assert.Equal("MayRetain", (await policyService.FindVersionAsync(original.Policy.VersionId!.Value))!.Retention);
        Assert.Equal(2, (await service.ListAsync(provider.Id, 100, default)).Count);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        var constraints = await db.Database.SqlQueryRaw<string>("""
            SELECT conname AS "Value" FROM pg_constraint
            WHERE conrelid = 'catalog.provider_data_policy'::regclass
            """).ToListAsync();
        Assert.Contains("PK_provider_data_policy", constraints);
        Assert.Contains("FK_provider_data_policy_provider_provider_id", constraints);
        Assert.Contains("FK_provider_data_policy_user_published_by_account_id", constraints);
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT has_table_privilege(current_user, 'catalog.provider_data_policy', 'TRUNCATE') AS "Value"
            """).SingleAsync());
        Assert.True(db.GetService<IMigrationsIdGenerator>().IsValidId("20261017010000_AddProviderDataPolicies"));
        Assert.Equal(2, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "provider-data-policy.published"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE catalog.provider_data_policy SET retention = 'NoRetentionReported';"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM catalog.provider_data_policy;"));
        await using var migrator = fixture.CreateServiceProvider();
        await using var migratorScope = migrator.CreateAsyncScope();
        await Assert.ThrowsAsync<PostgresException>(() => migratorScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
            .MigrateAsync("20261017000000_AddEmbeddingModelLimits"));
        Assert.Equal(2, await db.Set<CatalogProviderDataPolicyEntity>().CountAsync());
    }

    [Fact]
    public async Task Audit_failure_rolls_back_policy_and_requires_transaction()
    {
        await ResetAsync();
        var clock = new PolicyClock();
        await using var app = App(clock);
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var op = await RegisterAsync(services, "policy-rollback@example.uz", true);
        var provider = await services.GetRequiredService<ICatalogService>().AddProviderAsync("openai", "OpenAI");
        var policies = services.GetRequiredService<IProviderDataPolicyService>();
        var input = Publication(clock.Now.AddDays(-1));
        var admin = new ProviderDataPolicyAdminService(policies, new FailingAudit(), services.GetRequiredService<ITransactionCoordinator>());
        await Assert.ThrowsAsync<IOException>(() => admin.PublishAsync(op.Id, provider.Id, input, default));
        var db = services.GetRequiredService<FoundationDbContext>();
        db.ChangeTracker.Clear();
        Assert.Empty(await policies.ListAsync(provider.Id, 100));
        Assert.Empty(await db.Set<AuditEventEntity>().Where(value => value.Action == "provider-data-policy.published").ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => policies.PublishAsync(provider.Id, op.Id,
            new(input.Retention, input.Training, input.Residency, input.Regions, input.Scope,
                input.Conditions, input.SourceUrl, input.SourceObservedAt)));
    }

    private async Task ResetAsync() { await fixture.ResetMigrationsAsync(); await fixture.ApplyMigrationsAsync(); }

    private WebApplication App(PolicyClock clock)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = "test",
            ["ProviderSecrets:Keys:test"] = Convert.ToBase64String(new byte[32]),
            ["Payments:Payme:MerchantId"] = "policy-synthetic-merchant", ["Payments:Payme:Key"] = "policy-synthetic-key"
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity(); builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations(); builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmBilling(); builder.Services.AddUzllmCatalog();
        builder.Services.AddUzllmProviders(builder.Configuration); builder.Services.AddUzllmPayments(builder.Configuration);
        builder.Services.AddUzllmAdministration();
        builder.Services.AddScoped<ICustomerCatalogService, CustomerCatalogService>();
        var app = builder.Build();
        app.UseUzllmManagementNoStore(); app.UseUzllmManagementSession();
        app.MapUzllmAdminEndpoints(); app.MapUzllmCustomerCatalogEndpoints();
        return app;
    }

    private static async Task<(Guid Id, BrowserSessionTokens Session)> RegisterAsync(IServiceProvider services,
        string email, bool isOperator = false)
    {
        var identity = services.GetRequiredService<IIdentityService>();
        var account = await identity.RegisterAsync(email, Password);
        Assert.True(await identity.VerifyEmailAsync(account.VerificationToken));
        if (isOperator) await identity.GrantOperatorAccessAsync(account.AccountId);
        return (account.AccountId, (await identity.AuthenticateAsync(email, Password))!);
    }

    private static string CustomerPath(Guid organization, Guid project) =>
        $"/management/v1/organizations/{organization}/projects/{project}/catalog/providers/openai/data-policy";
    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, bool csrf = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
    private sealed class PolicyClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FailingAudit : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic audit persistence failure");
    }
}
