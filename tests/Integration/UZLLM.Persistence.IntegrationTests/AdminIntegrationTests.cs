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
using UZLLM.Management.Api;
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
        app.UseUzllmManagementNoStore();
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

            using (var response = await client.GetAsync("/management/v1/admin/access"))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.GetAsync("/management/v1/admin/providers"))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/access", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/financial/risk", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            using var enroll = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/enroll",
                operatorSession, new { password = "correct horse battery staple" }, true));
            Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
            Assert.Equal("no-store", enroll.Headers.CacheControl?.ToString());
            var secret = (await enroll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedSecret").GetString()!;
            var code = new TotpAuthenticator().CreateCode(secret, clock.GetUtcNow());
            using (var pending = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.Forbidden, pending.StatusCode);
            using (var premature = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/verify",
                operatorSession, new { code }, true)))
                Assert.Equal(HttpStatusCode.Unauthorized, premature.StatusCode);
            using var verified = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/confirm",
                operatorSession, new { code }, true));
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
            using (var replay = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/verify",
                operatorSession, new { code }, true)))
                Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/financial/risk", operatorSession)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                var body = await response.Content.ReadAsStringAsync();
                Assert.Contains("dataAsOf", body);
                Assert.DoesNotContain("do-not-return", body);
            }

            var providerRequest = new { code = "auth-test", name = "Auth Test", reason = "operator test" };
            using (var response = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/admin/providers",
                operatorSession, providerRequest)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/admin/providers",
                operatorSession, providerRequest, true)))
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var deadLetterId = await outbox.EnqueueAsync("test.dead-letter", "{\"secret\":\"do-not-return\"}", 1);
            var claimed = Assert.Single(await outbox.ClaimAvailableAsync("admin-dead-letter-test", 50,
                TimeSpan.FromMinutes(1)), item => item.Id == deadLetterId);
            Assert.True(await outbox.MarkFailedAsync(deadLetterId, "admin-dead-letter-test",
                claimed.AttemptCount, TimeSpan.Zero, "PermanentFailure"));
            using (var denied = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/work/dead-letters", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var visible = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/work/dead-letters", operatorSession)))
            {
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
                var body = await visible.Content.ReadAsStringAsync();
                Assert.Contains(deadLetterId.ToString(), body);
                Assert.Contains("PermanentFailure", body);
                Assert.DoesNotContain("do-not-return", body);
            }
            var operationalAlert = await scope.ServiceProvider.GetRequiredService<IOperationalAlertPublisher>()
                .RaiseAsync(OperationalAlertKind.SettlementFailure, "admin-alert-test",
                    "{\"secret\":\"do-not-return\"}");
            using (var denied = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/work/alerts", customerSession)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using (var visible = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/work/alerts", operatorSession)))
            {
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
                var body = await visible.Content.ReadAsStringAsync();
                Assert.Contains(operationalAlert.Id.ToString(), body);
                Assert.Contains("Pending", body);
                Assert.DoesNotContain("do-not-return", body);
            }
            var notificationEventId = (await scope.ServiceProvider.GetRequiredService<IOperationalAlertDeliveryStore>()
                .FindAsync(operationalAlert.Id))!.NotificationEventId!.Value;
            await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE ops.outbox SET dead_lettered_at = now(), last_error = 'TestFailure' WHERE id = {0}",
                notificationEventId);
            using (var deadAlert = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/work/alerts", operatorSession)))
            {
                Assert.Equal(HttpStatusCode.OK, deadAlert.StatusCode);
                var body = await deadAlert.Content.ReadAsStringAsync();
                Assert.Contains("DeadLettered", body);
                Assert.Contains(notificationEventId.ToString(), body);
                Assert.DoesNotContain("do-not-return", body);
            }
            using (var invalidLimit = await client.SendAsync(Request(HttpMethod.Get,
                "/management/v1/admin/work/alerts?limit=101", operatorSession)))
                Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);

            var secondOperator = await identity.RegisterAsync("admin-second-operator@example.uz", "correct horse battery staple");
            Assert.True(await identity.VerifyEmailAsync(secondOperator.VerificationToken));
            await identity.GrantOperatorAccessAsync(secondOperator.AccountId);
            var secondSession = (await identity.AuthenticateAsync("admin-second-operator@example.uz", "correct horse battery staple"))!;
            using var secondEnroll = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/enroll",
                secondSession, new { password = "correct horse battery staple" }, true));
            Assert.Equal(HttpStatusCode.OK, secondEnroll.StatusCode);
            var secondSecret = (await secondEnroll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedSecret").GetString()!;
            var secondCode = new TotpAuthenticator().CreateCode(secondSecret, clock.GetUtcNow());
            using (var secondConfirm = await client.SendAsync(Request(HttpMethod.Post, "/management/v1/auth/operator/mfa/confirm",
                secondSession, new { code = secondCode }, true)))
                Assert.Equal(HttpStatusCode.NoContent, secondConfirm.StatusCode);

            var resetPath = $"/management/v1/admin/operators/{operatorAccount.AccountId}/mfa/reset";
            var resetBody = new { reason = "Lost authenticator during operator recovery" };
            using (var missingCsrf = await client.SendAsync(Request(HttpMethod.Post, resetPath, secondSession, resetBody)))
                Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
            using (var selfReset = await client.SendAsync(Request(HttpMethod.Post,
                $"/management/v1/admin/operators/{secondOperator.AccountId}/mfa/reset", secondSession, resetBody, true)))
                Assert.Equal(HttpStatusCode.Forbidden, selfReset.StatusCode);
            using (var reset = await client.SendAsync(Request(HttpMethod.Post, resetPath, secondSession, resetBody, true)))
                Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            using (var revoked = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", operatorSession)))
                Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            using (var replayReset = await client.SendAsync(Request(HttpMethod.Post, resetPath, secondSession, resetBody, true)))
                Assert.Equal(HttpStatusCode.NotFound, replayReset.StatusCode);

            clock.Advance(TimeSpan.FromMinutes(16));
            using (var response = await client.SendAsync(Request(HttpMethod.Get, "/management/v1/admin/providers", secondSession)))
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
            var thirdStart = future.AddDays(1);
            var thirdId = await service.SchedulePriceAsync(actor,
                new CreateAdminPriceRequest(mappingId, thirdStart, 5000, 6000, 1000,
                    "second future tariff window"), default);
            prices = await service.ListPricesAsync(mappingId, default);
            Assert.Equal(3, prices.Count);
            Assert.InRange(Math.Abs((thirdStart - Assert.Single(prices, item => item.Id == secondId).EffectiveTo!.Value).Ticks), 0, 9);
            Assert.Null(Assert.Single(prices, item => item.Id == thirdId).EffectiveTo);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchedulePriceAsync(actor,
                new CreateAdminPriceRequest(mappingId, future.AddHours(12), 7000, 8000, null,
                    "out of order future price"), default));
            Assert.Equal(3, (await service.ListPricesAsync(mappingId, default)).Count);

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
            Id = id,
            Email = $"admin-test-{id:N}@example.uz",
            PasswordHash = "test-only",
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
