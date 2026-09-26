using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ByokEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Byok_http_requires_session_csrf_manager_role_and_never_returns_secret()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = "v1",
            ["ProviderSecrets:Keys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmCatalog();
        builder.Services.AddUzllmProviders(builder.Configuration);
        builder.Services.AddSingleton<IByokCredentialVerifier, InvalidCredentialVerifier>();
        await using var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmByokEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await identity.RegisterAsync("byok-http-owner@example.uz",
                "correct horse battery staple");
            var outsider = await identity.RegisterAsync("byok-http-outsider@example.uz",
                "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(outsider.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync(
                "byok-http-owner@example.uz", "correct horse battery staple"))!;
            var outsiderSession = (await identity.AuthenticateAsync(
                "byok-http-outsider@example.uz", "correct horse battery staple"))!;
            var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "BYOK HTTP tenant");
            var upstream = await scope.ServiceProvider.GetRequiredService<ICatalogService>()
                .AddProviderAsync("openai", "OpenAI");
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(owner.AccountId, org.Id, "Production");
            var path = $"/management/v1/organizations/{org.Id}/provider-keys";
            const string secret = "sk-http-private-key-123456";
            var payload = new { providerId = upstream.Id, name = "Production", secret };

            using (var response = await client.PostAsJsonAsync(path, payload))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post, path,
                ownerSession, csrf: false, payload)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post, path,
                outsiderSession, csrf: true, payload)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Guid credentialId;
            using (var response = await client.SendAsync(Request(HttpMethod.Post, path,
                ownerSession, csrf: true, payload)))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.NotNull(response.Headers.Location);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(secret, body);
                Assert.Contains("••••3456", body);
                credentialId = (await response.Content.ReadFromJsonAsync<ByokHttpResponse>())!.Id;
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                path, ownerSession, csrf: false)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                $"{path}/{credentialId}", outsiderSession, csrf: false)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put,
                $"{path}/{credentialId}/projects/{project.Id}", outsiderSession, csrf: true)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Put,
                $"{path}/{credentialId}/projects/{project.Id}", ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                $"{path}/{credentialId}", ownerSession, csrf: false)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(project.Id.ToString(), await response.Content.ReadAsStringAsync());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                $"{path}/{credentialId}/test", ownerSession, csrf: true)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("Invalid", await response.Content.ReadAsStringAsync());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Patch,
                $"{path}/{credentialId}", ownerSession, csrf: true,
                new { name = "Renamed", secret = "sk-http-replaced-key-654321" })))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("sk-http-replaced-key-654321", body);
                Assert.Contains("Renamed", body);
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Delete,
                $"{path}/{credentialId}/projects/{project.Id}", ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                $"{path}/{credentialId}/disable", ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Delete,
                $"{path}/{credentialId}", ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                $"{path}/{credentialId}", ownerSession, csrf: false)))
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

    private sealed record ByokHttpResponse(Guid Id);

    private sealed class InvalidCredentialVerifier : IByokCredentialVerifier
    {
        public Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
            CancellationToken cancellationToken = default) => Task.FromResult(ByokTestStatus.Invalid);
    }
}
