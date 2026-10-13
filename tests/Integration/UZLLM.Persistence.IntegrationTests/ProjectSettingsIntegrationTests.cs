using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Application;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Domain;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ProjectSettingsIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Project_settings_HTTP_enforces_current_roles_tenant_scope_csrf_and_reset()
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var identity = services.GetRequiredService<IIdentityService>();
        const string password = "correct horse battery staple";
        var owner = await identity.RegisterAsync("project-owner@example.test", password);
        var other = await identity.RegisterAsync("project-other@example.test", password);
        Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
        Assert.True(await identity.VerifyEmailAsync(other.VerificationToken));
        var ownerSession = (await identity.AuthenticateAsync("project-owner@example.test", password))!;
        var otherSession = (await identity.AuthenticateAsync("project-other@example.test", password))!;
        var organizations = services.GetRequiredService<IOrganizationService>();
        var org = await organizations.CreateAsync(owner.AccountId, "Project settings tenant");
        var otherOrg = await organizations.CreateAsync(other.AccountId, "Other tenant");
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(owner.AccountId, org.Id, "Primary");
        var foreign = await projects.CreateAsync(other.AccountId, otherOrg.Id, "Foreign");
        var path = $"/management/v1/organizations/{org.Id}/projects/{project.Id}/settings";
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using (var anonymous = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var outsider = await client.SendAsync(Request(HttpMethod.Get, path, otherSession)))
            Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        using (var initial = await client.SendAsync(Request(HttpMethod.Get, path, ownerSession)))
            Assert.Equal(new ProjectSettings(), await initial.Content.ReadFromJsonAsync<ProjectSettings>());
        using (var csrf = await client.SendAsync(Request(HttpMethod.Put, path, ownerSession, new ProjectSettings(), false)))
            Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
        foreach (var invalid in new object[] { new { max_cost_usd = 1 }, new { provider_key_id = Guid.NewGuid() },
            new { allow_managed_fallback = true }, new { routing = "invalid" }, new { environment = "<prod>" },
            new { allowedModels = new[] { "*" } }, new { allowedProviders = new[] { "openai/gpt" } }, new { budget = 10 } })
        {
            using var response = await client.SendAsync(Request(HttpMethod.Put, path, ownerSession, invalid));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        var db = services.GetRequiredService<FoundationDbContext>();
        await db.Set<ProjectEntity>().Where(value => value.Id == project.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.SettingsJson, "{\"futureSibling\":42}"));
        using (var changed = await client.SendAsync(Request(HttpMethod.Put, path, ownerSession,
            new ProjectSettings("price", "Production", [" GPT-TEST ", "gpt-test"], ["openai"]))))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var value = (await changed.Content.ReadFromJsonAsync<ProjectSettings>())!;
            Assert.Equal("price", value.Routing);
            Assert.Equal(new[] { "gpt-test" }, value.AllowedModels);
            Assert.Equal("no-store", changed.Headers.CacheControl?.ToString());
        }
        var gateway = new PostgreSqlGatewayReadStore(db);
        Assert.Equal("price", (await gateway.FindTenantAsync(project.Id, default))!.Settings!.Routing);
        var stored = (await projects.FindAsync(owner.AccountId, org.Id, project.Id))!;
        using (var json = System.Text.Json.JsonDocument.Parse(stored.SettingsJson))
            Assert.Equal(42, json.RootElement.GetProperty("futureSibling").GetInt32());
        db.Set<OrganizationMemberEntity>().Add(new() { OrganizationId = org.Id, AccountId = other.AccountId,
            Role = "Developer", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
        db.Set<ProjectGrantEntity>().Add(new() { OrganizationId = org.Id, AccountId = other.AccountId,
            ProjectId = project.Id, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        foreach (var role in new[] { "Developer", "ReadOnly", "BillingViewer" })
        {
            await db.Set<OrganizationMemberEntity>().Where(value => value.OrganizationId == org.Id && value.AccountId == other.AccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Role, role));
            using var denied = await client.SendAsync(Request(HttpMethod.Put, path, otherSession, new ProjectSettings()));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        await db.Set<OrganizationMemberEntity>().Where(value => value.OrganizationId == org.Id && value.AccountId == other.AccountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Role, "Admin"));
        using (var reset = await client.SendAsync(Request(HttpMethod.Put, path, otherSession, new ProjectSettings())))
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Null((await gateway.FindTenantAsync(project.Id, default))!.Settings!.Routing);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var wrongPath = await client.SendAsync(Request(method,
                $"/management/v1/organizations/{org.Id}/projects/{foreign.Id}/settings", ownerSession,
                method == HttpMethod.Put ? new ProjectSettings() : null));
            Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);
        }
        Assert.Equal("{}", (await projects.FindAsync(other.AccountId, otherOrg.Id, foreign.Id))!.SettingsJson);
        var audit = await db.Set<AuditEventEntity>().Where(value => value.Action == "project.settings_changed").ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, value => Assert.Equal(org.Id, value.OrganizationId));
        Assert.Contains(audit, value => value.ActorAccountId == other.AccountId);
        Assert.True(await projects.ArchiveAsync(owner.AccountId, org.Id, project.Id));
        using var archived = await client.SendAsync(Request(HttpMethod.Put, path, ownerSession, new ProjectSettings("auto")));
        Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        using var archivedRead = await client.SendAsync(Request(HttpMethod.Get, path, ownerSession));
        Assert.Equal(HttpStatusCode.NotFound, archivedRead.StatusCode);
        Assert.Null(await gateway.FindTenantAsync(project.Id, default));
    }

    [Fact]
    public async Task Concurrent_project_settings_replace_atomically_and_failed_audit_rolls_back()
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var identity = services.GetRequiredService<IIdentityService>();
        var owner = await identity.RegisterAsync("project-race@example.test", "correct horse battery staple");
        var org = await services.GetRequiredService<IOrganizationService>().CreateAsync(owner.AccountId, "Race");
        var project = await services.GetRequiredService<IProjectService>().CreateAsync(owner.AccountId, org.Id, "Race");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index => {
            await using var writer = app.Services.CreateAsyncScope();
            await writer.ServiceProvider.GetRequiredService<IProjectSettingsService>().ReplaceAsync(owner.AccountId,
                org.Id, project.Id, new(index % 2 == 0 ? "price" : "priority", index % 2 == 0 ? "Production" : "Staging"));
        }));
        var service = services.GetRequiredService<IProjectSettingsService>();
        var before = await service.ReadAsync(owner.AccountId, org.Id, project.Id);
        Assert.True(before is { Routing: "price", Environment: "Production" } or { Routing: "priority", Environment: "Staging" });
        Assert.Equal(12, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "project.settings_changed"));
        var failing = new ProjectSettingsService(services.GetRequiredService<IProjectService>(),
            services.GetRequiredService<IProjectSettingsStore>(), services.GetRequiredService<IOrganizationAuthorizationService>(),
            new FailingAudit(), services.GetRequiredService<ITransactionCoordinator>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ReplaceAsync(owner.AccountId, org.Id, project.Id, new("auto", "Development")));
        Assert.Equal(before, await service.ReadAsync(owner.AccountId, org.Id, project.Id));
        Assert.Equal(12, await db.Set<AuditEventEntity>().CountAsync(value => value.Action == "project.settings_changed"));
    }

    private async Task<WebApplication> StartAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity(); builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations(); builder.Services.AddUzllmProjects();
        var app = builder.Build();
        app.UseUzllmManagementNoStore(); app.UseUzllmManagementSession(); app.MapUzllmProjectEndpoints();
        await app.StartAsync(); return app;
    }
    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
    private sealed class FailingAudit : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic audit failure");
    }
}
