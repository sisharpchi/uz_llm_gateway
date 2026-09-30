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
using UZLLM.Management.Api;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ManagementSecurityEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Payment_routes_return_explicit_denials_without_exposing_another_tenants_billing()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["Payments:FeeBasisPoints"] = "100",
            ["Payments:FixedFeeTiyin"] = "0",
            ["Payments:Payme:MerchantId"] = "test-merchant",
            ["Payments:Payme:Key"] = "test-key"
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmBilling();
        builder.Services.AddUzllmPayments(builder.Configuration);
        await using var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmManagementSession();
        app.MapUzllmPaymentEndpoints();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            await using var scope = app.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var owner = await RegisterVerifiedAsync(identity, "payment-owner@example.uz");
            var outsider = await RegisterVerifiedAsync(identity, "payment-outsider@example.uz");
            var viewer = await RegisterVerifiedAsync(identity, "payment-viewer@example.uz");
            var organization = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
                .CreateAsync(owner.AccountId, "Billing security tenant");
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
            {
                OrganizationId = organization.Id, AccountId = viewer.AccountId,
                Role = "BillingViewer", Status = "Active", CreatedAt = DateTimeOffset.UtcNow
            });
            db.Set<BillingFxRateSnapshotEntity>().Add(new BillingFxRateSnapshotEntity
            {
                Id = Guid.CreateVersion7(), Source = "security-test", UzsTiyinPerUsd = 1_000_000m,
                ObservedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var path = $"/management/v1/organizations/{organization.Id}/billing";
            using (var response = await client.GetAsync($"{path}/wallet"))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            using var ownerQuote = await client.SendAsync(Request(HttpMethod.Post, $"{path}/quotes", owner,
                new { provider = "Payme", amountTiyin = "100000" }));
            Assert.Equal(HttpStatusCode.OK, ownerQuote.StatusCode);
            var quoteId = (await ownerQuote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            using var ownerTopup = await client.SendAsync(Request(HttpMethod.Post, $"{path}/topups", owner,
                new { quoteId }, "owner-topup"));
            Assert.Equal(HttpStatusCode.Created, ownerTopup.StatusCode);
            Assert.Equal("no-store", ownerTopup.Headers.CacheControl?.ToString());
            var topupBody = await ownerTopup.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrEmpty(topupBody.GetProperty("checkoutUrl").GetString()));
            var intentId = topupBody.GetProperty("intent").GetProperty("id").GetGuid();

            var denied = new[]
            {
                Request(HttpMethod.Post, $"{path}/quotes", outsider, new { provider = "Payme", amountTiyin = "100000" }),
                Request(HttpMethod.Post, $"{path}/topups", outsider, new { quoteId }, "outsider-topup"),
                Request(HttpMethod.Get, $"{path}/topups", outsider),
                Request(HttpMethod.Get, $"{path}/refunds", outsider),
                Request(HttpMethod.Get, $"{path}/topups/{intentId}", outsider),
                Request(HttpMethod.Get, $"{path}/wallet", outsider),
                Request(HttpMethod.Post, $"{path}/quotes", viewer, new { provider = "Payme", amountTiyin = "100000" }),
                Request(HttpMethod.Post, $"{path}/topups", viewer, new { quoteId }, "viewer-topup")
            };
            foreach (var request in denied)
            {
                using (request)
                using (var response = await client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                    Assert.DoesNotContain(intentId.ToString("D"), await response.Content.ReadAsStringAsync());
                }
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, $"{path}/wallet", viewer)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var wallet = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("0", wallet.GetProperty("recoveryDebtMicroUsd").GetString());
                Assert.False(wallet.GetProperty("spendingHeld").GetBoolean());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, $"{path}/topups", viewer)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var item = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
                Assert.False(item.GetProperty("hasCredit").GetBoolean());
                Assert.False(item.GetProperty("hasReversal").GetBoolean());
                Assert.False(item.GetProperty("hasOpenReconciliationCase").GetBoolean());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, $"{path}/refunds", viewer)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
            }
            using (var response = await client.SendAsync(Request(HttpMethod.Get, $"{path}/topups/{intentId}", viewer)))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, await db.Set<PaymentIntentEntity>().CountAsync());
            Assert.Equal(1, await db.Set<PaymentQuoteEntity>().CountAsync());
        }
        finally { await app.StopAsync(); }
    }

    private static async Task<SessionActor> RegisterVerifiedAsync(IIdentityService identity, string email)
    {
        var registration = await identity.RegisterAsync(email, "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        return new SessionActor(registration.AccountId,
            (await identity.AuthenticateAsync(email, "correct horse battery staple"))!);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, SessionActor actor,
        object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={actor.Session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={actor.Session.CsrfToken}");
        if (method == HttpMethod.Post) request.Headers.Add(IdentityCookieNames.CsrfHeader, actor.Session.CsrfToken);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed record SessionActor(Guid AccountId, BrowserSessionTokens Session);
}
