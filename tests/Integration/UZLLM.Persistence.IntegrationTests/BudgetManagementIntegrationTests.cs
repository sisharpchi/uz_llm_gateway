using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.Budgets;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class BudgetManagementIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Budget_endpoints_enforce_owner_scope_csrf_and_key_project()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ApiKeys:FingerprintKey"] = Convert.ToBase64String(new byte[32])
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmApiKeys(builder.Configuration);
        builder.Services.AddUzllmUsage();
        builder.Services.AddUzllmBilling();
        builder.Services.AddScoped<IBudgetManagementService, BudgetManagementService>();
        await using var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmBudgetEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await identity.RegisterAsync("budget-owner@example.uz", "correct horse battery staple");
            var outsider = await identity.RegisterAsync("budget-outsider@example.uz", "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(outsider.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync("budget-owner@example.uz",
                "correct horse battery staple"))!;
            var outsiderSession = (await identity.AuthenticateAsync("budget-outsider@example.uz",
                "correct horse battery staple"))!;
            var organizations = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            var organization = await organizations.CreateAsync(owner.AccountId, "Budget tenant");
            var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(owner.AccountId, organization.Id, "Primary");
            var otherProject = await projects.CreateAsync(owner.AccountId, organization.Id, "Other");
            var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
            var key = await keys.CreateAsync(owner.AccountId, project.Id, "Primary key", null);
            var foreignProjectKey = await keys.CreateAsync(owner.AccountId, otherProject.Id, "Other key", null);
            var path = $"/management/v1/projects/{project.Id}/budgets";

            using (var response = await client.SendAsync(Request(HttpMethod.Get, path, outsiderSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, path, ownerSession)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Monthly",
                ownerSession, new { limitMicroUsd = 50_000, apiKeyId = (Guid?)null })))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Monthly",
                ownerSession, new { limitMicroUsd = 50_000, apiKeyId = (Guid?)null }, csrf: true)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var value = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Monthly", value.GetProperty("period").GetString());
                Assert.Equal(50_000, value.GetProperty("limitMicroUsd").GetInt64());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Daily",
                ownerSession, new { limitMicroUsd = 10_000, apiKeyId = key.ApiKey.Id }, csrf: true)))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, path, ownerSession)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var values = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2, values.GetArrayLength());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Daily",
                ownerSession, new { limitMicroUsd = 10_000, apiKeyId = foreignProjectKey.ApiKey.Id }, csrf: true)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Yearly",
                ownerSession, new { limitMicroUsd = 10_000, apiKeyId = (Guid?)null }, csrf: true)))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put, path + "/Daily",
                ownerSession, new { limitMicroUsd = -1, apiKeyId = (Guid?)null }, csrf: true)))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, bool csrf = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
