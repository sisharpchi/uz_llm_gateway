using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using UZLLM.Management.Api;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ApiKeyRotationEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Key_permissions_and_rotation_endpoints_enforce_current_project_roles_csrf_and_show_once_secrets()
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
        await using var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmManagementSession();
        app.MapUzllmApiKeyEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await identity.RegisterAsync("rotation-owner@example.uz", "correct horse battery staple");
            var outsider = await identity.RegisterAsync("rotation-outsider@example.uz", "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(outsider.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync("rotation-owner@example.uz",
                "correct horse battery staple"))!;
            var outsiderSession = (await identity.AuthenticateAsync("rotation-outsider@example.uz",
                "correct horse battery staple"))!;
            var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "Rotation tenant");
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(owner.AccountId, org.Id, "Production");
            var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
            var initial = await keys.CreateAsync(owner.AccountId, project.Id, "Production", null);
            var path = $"/management/v1/api-keys/{initial.ApiKey.Id}/rotate";
            var permissionsPath = $"/management/v1/api-keys/{initial.ApiKey.Id}/permissions";
            HttpRequestMessage PermissionsRequest(BrowserSessionTokens? session, bool csrf, object body)
            {
                var request = session is null ? new HttpRequestMessage(HttpMethod.Put, permissionsPath)
                    : Request(permissionsPath, session, csrf);
                request.Method = HttpMethod.Put;
                request.Content = JsonContent.Create(body);
                return request;
            }
            using (var response = await client.SendAsync(PermissionsRequest(null, false, new ApiKeyPermissions())))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(PermissionsRequest(outsiderSession, true, new ApiKeyPermissions())))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
            {
                OrganizationId = org.Id, AccountId = outsider.AccountId, Role = "Developer",
                Status = "Active", CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            using (var response = await client.SendAsync(PermissionsRequest(outsiderSession, true, new ApiKeyPermissions())))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            db.Set<ProjectGrantEntity>().Add(new ProjectGrantEntity
            {
                OrganizationId = org.Id, AccountId = outsider.AccountId, ProjectId = project.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            using (var response = await client.SendAsync(PermissionsRequest(outsiderSession, true, new ApiKeyPermissions(AllowedModels: []))))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            foreach (var role in new[] { "ReadOnly", "BillingViewer" })
            {
                await db.Set<OrganizationMemberEntity>().Where(value => value.OrganizationId == org.Id && value.AccountId == outsider.AccountId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Role, role));
                using var response = await client.SendAsync(PermissionsRequest(outsiderSession, true, new ApiKeyPermissions()));
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            using (var response = await client.SendAsync(PermissionsRequest(ownerSession, false, new ApiKeyPermissions())))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            foreach (var invalid in new object[] { new { allowedModels = new[] { "*" } },
                new { allowModels = new[] { "gpt-test" } }, new { allowedProviders = new[] { "openai/gpt-test" } } })
            {
                using var response = await client.SendAsync(PermissionsRequest(ownerSession, true, invalid));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            using (var response = await client.SendAsync(PermissionsRequest(ownerSession, true,
                new ApiKeyPermissions(AllowedModels: ["GPT-Test"], DeniedProviders: ["anthropic"]))))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            var permissions = (await scope.ServiceProvider.GetRequiredService<IApiKeyAuthenticator>()
                .AuthenticateAsync(initial.Secret))!.Permissions!;
            Assert.Equal(["gpt-test"], permissions.AllowedModels);
            Assert.Equal(["anthropic"], permissions.DeniedProviders);

            using (var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post,
                $"/management/v1/projects/{project.Id}/api-keys")
            {
                Headers = { { "Cookie", $"{IdentityCookieNames.Session}={ownerSession.SessionToken}; " +
                    $"{IdentityCookieNames.Csrf}={ownerSession.CsrfToken}" },
                    { IdentityCookieNames.CsrfHeader, ownerSession.CsrfToken } },
                Content = JsonContent.Create(new { name = "Additional", expiresAt = (DateTimeOffset?)null })
            }))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("secret").GetString()));
            }
            using (var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"/management/v1/projects/{project.Id}/api-keys")
            {
                Headers = { { "Cookie", $"{IdentityCookieNames.Session}={outsiderSession.SessionToken}" } }
            }))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            using (var response = await client.PostAsync(path, null))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(path, outsiderSession, csrf: true)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(path, ownerSession, csrf: false)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string replacementSecret;
            using (var response = await client.SendAsync(Request(path, ownerSession, csrf: true)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                replacementSecret = body.GetProperty("secret").GetString()!;
                Assert.NotEqual(initial.Secret, replacementSecret);
                Assert.Equal(initial.ApiKey.Id, body.GetProperty("apiKey").GetProperty("id").GetGuid());
                Assert.Equal(2, body.GetProperty("apiKey").GetProperty("generation").GetInt32());
                Assert.Equal("gpt-test", body.GetProperty("apiKey").GetProperty("permissions")
                    .GetProperty("allowedModels")[0].GetString());
            }
            var authenticator = scope.ServiceProvider.GetRequiredService<IApiKeyAuthenticator>();
            Assert.Null(await authenticator.AuthenticateAsync(initial.Secret));
            Assert.NotNull(await authenticator.AuthenticateAsync(replacementSecret));
            Assert.True(await keys.SetStatusAsync(owner.AccountId, initial.ApiKey.Id,
                GatewayApiKeyStatus.Disabled));
            using (var response = await client.SendAsync(Request(path, ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Null(await authenticator.AuthenticateAsync(replacementSecret));
        }
        finally { await app.StopAsync(); }
    }

    private static HttpRequestMessage Request(string path, BrowserSessionTokens session, bool csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        return request;
    }
}
