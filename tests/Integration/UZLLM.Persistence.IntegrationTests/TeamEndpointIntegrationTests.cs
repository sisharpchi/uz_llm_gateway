using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class TeamEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Team_http_requires_session_csrf_and_rechecks_grants_without_session_refresh()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmTeam();
        builder.Services.AddUzllmProjects();
        await using var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmTeamEndpoints();
        app.MapUzllmProjectEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await identity.RegisterAsync("http-team-owner@example.uz",
                "correct horse battery staple");
            var developer = await identity.RegisterAsync("http-team-dev@example.uz",
                "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(owner.VerificationToken));
            Assert.True(await identity.VerifyEmailAsync(developer.VerificationToken));
            var ownerSession = (await identity.AuthenticateAsync("http-team-owner@example.uz",
                "correct horse battery staple"))!;
            var developerSession = (await identity.AuthenticateAsync("http-team-dev@example.uz",
                "correct horse battery staple"))!;
            var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "HTTP team tenant");
            var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
                .CreateAsync(owner.AccountId, org.Id, "Production");
            var teamPath = $"/management/v1/organizations/{org.Id}/team";
            var projectsPath = $"/management/v1/organizations/{org.Id}/projects";

            using (var response = await client.PostAsJsonAsync(teamPath + "/invitations",
                new { email = "http-team-dev@example.uz", role = "Developer" }))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                teamPath + "/invitations", ownerSession, csrf: false,
                new { email = "http-team-dev@example.uz", role = "Developer" })))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                teamPath + "/invitations", developerSession, csrf: true,
                new { email = "http-team-dev@example.uz", role = "Developer" })))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                teamPath + "/invitations", ownerSession, csrf: true,
                new { email = "http-team-dev@example.uz", role = "2" })))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string inviteBody;
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                teamPath + "/invitations", ownerSession, csrf: true,
                new { email = "http-team-dev@example.uz", role = "Developer" })))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.NotNull(response.Headers.Location);
                inviteBody = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("token", inviteBody, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Developer", inviteBody, StringComparison.Ordinal);
            }
            var eventMessage = (await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .ClaimAvailableAsync("team-http-test", 20, TimeSpan.FromMinutes(1)))
                .Single(value => value.EventType == IdentityEmailEventTypes.TeamInvitation);
            var token = scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
                .Unprotect(eventMessage.Payload).Token;
            Assert.DoesNotContain(token, inviteBody, StringComparison.Ordinal);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                "/management/v1/team/invitations/accept", developerSession, csrf: true,
                new { token = "not-a-valid-token" })))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                "/management/v1/team/invitations/accept", developerSession, csrf: false,
                new { token })))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                "/management/v1/team/invitations/accept", developerSession, csrf: true,
                new { token })))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post,
                "/management/v1/team/invitations/accept", developerSession, csrf: true,
                new { token })))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                projectsPath, developerSession, csrf: false)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Put,
                $"{teamPath}/members/{developer.AccountId}/projects/{project.Id}", ownerSession,
                csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                projectsPath, developerSession, csrf: false)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(project.Id, (await response.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().Single().GetProperty("id").GetGuid());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Delete,
                $"{teamPath}/members/{developer.AccountId}", ownerSession, csrf: true)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get,
                projectsPath, developerSession, csrf: false)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
