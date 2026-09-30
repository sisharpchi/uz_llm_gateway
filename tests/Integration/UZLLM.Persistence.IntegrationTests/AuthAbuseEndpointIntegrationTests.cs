using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class AuthAbuseEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Verification_and_password_reset_http_contract_consumes_one_time_tokens_without_enumeration()
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
        builder.Services.AddSingleton(new AuthAbusePolicy(100, 100, TimeSpan.FromMinutes(5), 8192,
            $"test:auth:{Guid.NewGuid():N}"));
        await using var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmAuthAbuseProtection();
        app.MapUzllmIdentityEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var registered = await client.PostAsJsonAsync("/management/v1/auth/register",
                new { email = "flow@example.uz", password = "correct horse battery staple" });
            Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
            Assert.Equal("no-store", registered.Headers.CacheControl?.ToString());

            string verificationToken;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var messages = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                    .ClaimAvailableAsync("auth-contract", 10, TimeSpan.FromMinutes(1));
                var notification = scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
                    .Unprotect(Assert.Single(messages, item => item.EventType == IdentityEmailEventTypes.Verification).Payload);
                verificationToken = notification.Token;
            }
            using var verified = await client.PostAsJsonAsync("/management/v1/auth/verify-email",
                new { token = verificationToken });
            using var replayedVerification = await client.PostAsJsonAsync("/management/v1/auth/verify-email",
                new { token = verificationToken });
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, replayedVerification.StatusCode);
            Assert.Equal("no-store", verified.Headers.CacheControl?.ToString());

            using var known = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = "flow@example.uz" });
            using var unknown = await client.PostAsJsonAsync("/management/v1/auth/recover",
                new { email = "missing@example.uz" });
            Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
            Assert.Equal(known.StatusCode, unknown.StatusCode);
            Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
            Assert.Equal("no-store", known.Headers.CacheControl?.ToString());

            string recoveryToken;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var messages = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                    .ClaimAvailableAsync("auth-contract", 10, TimeSpan.FromMinutes(1));
                var notification = scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
                    .Unprotect(Assert.Single(messages, item => item.EventType == IdentityEmailEventTypes.PasswordRecovery).Payload);
                recoveryToken = notification.Token;
            }
            using var reset = await client.PostAsJsonAsync("/management/v1/auth/reset-password",
                new { token = recoveryToken, newPassword = "new correct battery staple" });
            using var replayedReset = await client.PostAsJsonAsync("/management/v1/auth/reset-password",
                new { token = recoveryToken, newPassword = "another correct battery staple" });
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, replayedReset.StatusCode);
            Assert.Equal("no-store", reset.Headers.CacheControl?.ToString());
            using var login = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "flow@example.uz", password = "new correct battery staple" });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

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
        app.UseUzllmManagementNoStore();
        app.UseUzllmAuthAbuseProtection();
        app.MapUzllmIdentityEndpoints();
        app.MapPost("/management/v1/team/invitations/accept", () => Results.Ok());
        app.MapGet("/management/v1/cache-override", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(new { value = "sensitive" });
        });
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
            Assert.Equal("no-store", known.Headers.CacheControl?.ToString());
            Assert.Equal("no-store", unknown.Headers.CacheControl?.ToString());
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
            Assert.Equal("no-store", firstRegistration.Headers.CacheControl?.ToString());
            Assert.Equal(await firstRegistration.Content.ReadAsStringAsync(),
                await duplicateRegistration.Content.ReadAsStringAsync());

            using var wrongPassword = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "known@example.uz", password = "incorrect password" });
            using var missingAccount = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "not-registered@example.uz", password = "incorrect password" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal(wrongPassword.StatusCode, missingAccount.StatusCode);
            Assert.Equal("no-store", wrongPassword.Headers.CacheControl?.ToString());
            Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(),
                await missingAccount.Content.ReadAsStringAsync());

            using var successfulLogin = await client.PostAsJsonAsync("/management/v1/auth/login",
                new { email = "known@example.uz", password = "correct horse battery staple" });
            Assert.Equal(HttpStatusCode.NoContent, successfulLogin.StatusCode);
            Assert.Equal("no-store", successfulLogin.Headers.CacheControl?.ToString());
            Assert.Contains(successfulLogin.Headers.GetValues("Set-Cookie"), value =>
                value.StartsWith(IdentityCookieNames.Session, StringComparison.Ordinal));
            var sessionCookie = successfulLogin.Headers.GetValues("Set-Cookie").Single(value =>
                value.StartsWith(IdentityCookieNames.Session, StringComparison.Ordinal)).Split(';')[0];
            using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/management/v1/auth/session");
            sessionRequest.Headers.Add("Cookie", sessionCookie);
            using var sessionResponse = await client.SendAsync(sessionRequest);
            Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
            Assert.Equal("no-store", sessionResponse.Headers.CacheControl?.ToString());
            using var overridden = await client.GetAsync("/management/v1/cache-override");
            Assert.Equal("no-store", overridden.Headers.CacheControl?.ToString());

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
