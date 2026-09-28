using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class AuthAbuseEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Recovery_and_registration_hide_account_existence_while_attempts_and_bodies_are_bounded()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ConnectionStrings:Redis"] = fixture.RedisConnectionString
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmRedis(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddSingleton(new AuthAbusePolicy(20, 2, TimeSpan.FromMinutes(5), 8192,
            $"test:auth:{Guid.NewGuid():N}"));
        await using var app = builder.Build();
        app.UseUzllmAuthAbuseProtection();
        app.MapUzllmIdentityEndpoints();
        app.MapPost("/management/v1/team/invitations/accept", () => Results.Ok());
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var registration = await scope.ServiceProvider.GetRequiredService<IIdentityService>()
                    .RegisterAsync("known@example.uz", "correct horse battery staple");
                Assert.True(await scope.ServiceProvider.GetRequiredService<IIdentityService>()
                    .VerifyEmailAsync(registration.VerificationToken));
            }

            using var known = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = "known@example.uz" });
            using var unknown = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = "missing@example.uz" });
            Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
            Assert.Equal(known.StatusCode, unknown.StatusCode);
            Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

            using var secondKnown = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = " KNOWN@EXAMPLE.UZ " });
            Assert.Equal(HttpStatusCode.Accepted, secondKnown.StatusCode);
            using var limited = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = "known@example.uz" });
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

            using var firstRegistration = await client.PostAsJsonAsync("/management/v1/auth/register",
                new { email = "fresh@example.uz", password = "correct horse battery staple" });
            using var duplicateRegistration = await client.PostAsJsonAsync("/management/v1/auth/register",
                new { email = "fresh@example.uz", password = "correct horse battery staple" });
            Assert.Equal(HttpStatusCode.Accepted, firstRegistration.StatusCode);
            Assert.Equal(firstRegistration.StatusCode, duplicateRegistration.StatusCode);
            Assert.Equal(await firstRegistration.Content.ReadAsStringAsync(),
                await duplicateRegistration.Content.ReadAsStringAsync());

            using var wrongPassword = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "known@example.uz", password = "incorrect password" });
            using var missingAccount = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "not-registered@example.uz", password = "incorrect password" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal(wrongPassword.StatusCode, missingAccount.StatusCode);
            Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(),
                await missingAccount.Content.ReadAsStringAsync());

            using var oversized = await client.PostAsync("/management/v1/auth/login",
                new StringContent(new string('x', 8193)));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            using var oversizedInvitation = await client.PostAsync("/management/v1/team/invitations/accept",
                new StringContent(new string('x', 8193)));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedInvitation.StatusCode);
            Assert.Equal("no-store", oversizedInvitation.Headers.CacheControl?.ToString());
        }
        finally { await app.StopAsync(); }
    }
}
