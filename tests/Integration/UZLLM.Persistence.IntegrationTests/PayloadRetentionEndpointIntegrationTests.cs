using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Infrastructure;
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
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(outsider.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync(
                "payload-owner@example.uz", "correct horse battery staple"))!;
            var outsiderSession = (await identity.AuthenticateAsync(
                "payload-outsider@example.uz", "correct horse battery staple"))!;
            var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "Payload policy tenant");
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(owner.AccountId, org.Id, "Production");
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
