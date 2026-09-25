using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.Administration;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Providers.Application;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class AdminIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Admin_routes_require_operator_recent_Mfa_and_Csrf_for_mutations()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var clock = new ShiftableClock(DateTimeOffset.UtcNow);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = "test",
            ["ProviderSecrets:Keys:test"] = Convert.ToBase64String(new byte[32])
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmCatalog();
        builder.Services.AddUzllmProviders(builder.Configuration);
        builder.Services.AddUzllmAdministration();
        await using var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmIdentityEndpoints();
        app.MapUzllmAdminEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var customer = await identity.RegisterAsync("admin-customer@example.uz", "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(customer.VerificationToken));
            var customerSession = (await identity.AuthenticateAsync("admin-customer@example.uz", "correct horse battery staple"))!;
            var operatorAccount = await identity.RegisterAsync("admin-operator@example.uz", "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(operatorAccount.VerificationToken));
            await identity.GrantOperatorAccessAsync(operatorAccount.AccountId);
            var operatorSession = (await identity.AuthenticateAsync("admin-operator@example.uz", "correct horse battery staple"))!;

            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var enroll = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/enroll",
                operatorSession, new { password = "correct horse battery staple" }, true));
            Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
            var secret = (await enroll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedSecret").GetString()!;
            var code = new TotpAuthenticator().CreateCode(secret, clock.GetUtcNow());
            using var verified = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/verify",
                operatorSession, new { code }, true));
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var providerRequest = new { code = "auth-test", name = "Auth Test", reason = "operator test" };
            using (var response = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/admin/providers",
                operatorSession, providerRequest)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/admin/providers",
                operatorSession, providerRequest, true)))
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            clock.Advance(TimeSpan.FromMinutes(16));
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await using var provider = fixture.CreateServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
                .Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit.audit_event");
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        object? body = null, bool csrf = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; {IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed class ShiftableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current = current.Add(amount);
    }

    [Fact]
    public async Task Operator_changes_are_audited_and_price_history_is_preserved()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var actor = await SeedActorAsync(db);
        var service = CreateService(db, scope.ServiceProvider);

        try
        {
            Assert.All(await service.ListControlsAsync(default), item => Assert.True(item.Enabled));
            Assert.True(await service.SetPlatformEnabledAsync(actor, "ManagedTraffic",
                new SetAdminStatusRequest(false, "incident INC-42"), default));
            Assert.False(await new PostgreSqlPlatformControlStore(db).IsEnabledAsync(PlatformFeature.ManagedTraffic));

            var providerId = await service.CreateProviderAsync(actor,
                new CreateAdminProviderRequest("admin-test", "Admin Test", "catalog setup"), default);
            var modelId = await service.CreateModelAsync(actor,
                new CreateAdminModelRequest("admin-model", "Admin Model", 8192, 2048,
                    ["Text"], "catalog setup"), default);
            var mappingId = await service.CreateMappingAsync(actor,
                new CreateAdminMappingRequest(providerId, modelId, "upstream-model", null, "catalog setup"), default);
            var catalog = new CatalogService(new PostgreSqlCatalogStore(db), TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            var first = await catalog.AddPriceAsync(mappingId, now.AddDays(-1), null,
                1000, 2000, null, null);
            var future = now.AddDays(1);
            var secondId = await service.SchedulePriceAsync(actor,
                new CreateAdminPriceRequest(mappingId, future, 3000, 4000, null, "supplier new price"), default);
            var prices = await service.ListPricesAsync(mappingId, default);
            Assert.Equal(2, prices.Count);
            Assert.InRange(Math.Abs((future - Assert.Single(prices, item => item.Id == first.Id).EffectiveTo!.Value).Ticks), 0, 9);
            Assert.Null(Assert.Single(prices, item => item.Id == secondId).EffectiveTo);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchedulePriceAsync(actor,
                new CreateAdminPriceRequest(mappingId, future.AddDays(1), 5000, 6000, null, "second future price"), default));
            Assert.Equal(2, (await service.ListPricesAsync(mappingId, default)).Count);

            var credentialId = await service.CreateCredentialAsync(actor,
                new CreateAdminCredentialRequest(providerId, "platform-secret-123", "credential rotation"), default);
            var credential = await db.Set<ProviderCredentialEntity>().AsNoTracking().SingleAsync(item => item.Id == credentialId);
            Assert.DoesNotContain("platform-secret-123", Encoding.UTF8.GetString(credential.EncryptedSecret));
            Assert.Single((await service.ListProvidersAsync(default)).Single(item => item.Id == providerId).Credentials);
            Assert.True(await service.SetCredentialEnabledAsync(actor, credentialId,
                new SetAdminStatusRequest(false, "credential expired"), default));
            Assert.True(await service.SetProviderEnabledAsync(actor, providerId,
                new SetAdminStatusRequest(false, "upstream incident"), default));

            var audit = await service.ListAuditAsync(50, default);
            Assert.Contains(audit, item => item.Action == "platform.status" && item.OrganizationId is null && item.ActorAccountId == actor);
            Assert.Contains(audit, item => item.Action == "price.scheduled" && item.ResourceId == secondId);
            Assert.Contains(audit, item => item.Action == "credential.status" && item.ResourceId == credentialId);
            Assert.Contains(await service.SearchAccountsAsync("admin-test", default), item => item.Id == actor);
            Assert.Empty(await service.ListPaymentsAsync(null, 50, default));
        }
        finally
        {
            // The isolated Testcontainers database is reset between tests. Its production audit trigger
            // is append-only, so test teardown uses TRUNCATE after all assertions.
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit.audit_event");
        }
    }

    private static async Task<Guid> SeedActorAsync(FoundationDbContext db)
    {
        var id = Guid.CreateVersion7();
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = id, Email = $"admin-test-{id:N}@example.uz", PasswordHash = "test-only",
            Status = "Active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AdminService CreateService(FoundationDbContext db, IServiceProvider services)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecrets:ActiveKeyVersion"] = "test",
            ["ProviderSecrets:Keys:test"] = Convert.ToBase64String(new byte[32])
        }).Build();
        return new AdminService(new PostgreSqlAdminReadStore(db),
            new CatalogService(new PostgreSqlCatalogStore(db), TimeProvider.System),
            new PlatformCredentialService(new PostgreSqlProviderCredentialStore(db),
                new ProviderEnvelopeSecretProtector(config), TimeProvider.System),
            new PostgreSqlPlatformControlStore(db),
            new AuditTrail(new PostgreSqlAuditEventStore(db), TimeProvider.System),
            services.GetRequiredService<ITransactionCoordinator>(), TimeProvider.System);
    }
}
