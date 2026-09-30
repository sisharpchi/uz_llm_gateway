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
using UZLLM.Management.Api.Catalog;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class CustomerCatalogEndpointIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Catalog_endpoint_returns_effective_customer_rates_and_only_published_chat_mappings()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var app = CreateApp();
        await app.StartAsync();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var owner = await RegisterOwnerAsync(scope.ServiceProvider, "catalog-owner@example.uz");
            var (organization, project) = await CreateProjectAsync(scope.ServiceProvider, owner.AccountId);
            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var pricing = scope.ServiceProvider.GetRequiredService<IPricingHistoryService>();
            var now = DateTimeOffset.UtcNow;
            var fee = await pricing.AddFeePolicyVersionAsync("default", 2500,
                new UsdMicroAmount(100), now.AddDays(-1), now.AddDays(1));
            await pricing.AddFeePolicyVersionAsync("default", 5000,
                new UsdMicroAmount(900), now.AddDays(1), null);
            var provider = await catalog.AddProviderAsync("openai", "OpenAI");
            var model = await catalog.AddModelAsync("gpt-catalog", "Catalog Chat", 8192, 1024,
                [CatalogCapability.Text, CatalogCapability.Tools]);
            var mapping = await catalog.AddProviderModelAsync(provider.Id, model.Id,
                "gpt-catalog", null, [CatalogCapability.Vision]);
            var current = await catalog.AddPriceAsync(mapping.Id, now.AddDays(-1), now.AddDays(1),
                1_000_000, 2_000_000, 500_000, null);
            await catalog.AddPriceAsync(mapping.Id, now.AddDays(1), null,
                9_000_000, 9_000_000, 9_000_000, null);
            var disabledModel = await catalog.AddModelAsync("disabled-chat", "Disabled", 4096, 512,
                [CatalogCapability.Text]);
            var disabledMapping = await catalog.AddProviderModelAsync(provider.Id, disabledModel.Id,
                "disabled-chat", null, null);
            await catalog.AddPriceAsync(disabledMapping.Id, now.AddDays(-1), null, 1, 1, null, null);
            await catalog.SetModelStatusAsync(disabledModel.Id, CatalogStatus.Disabled);
            var unsupported = await catalog.AddModelAsync("audio-only", "Audio", 4096, 512,
                [CatalogCapability.Audio]);
            var unsupportedProvider = await catalog.AddProviderAsync("future-provider", "Future");
            var unsupportedMapping = await catalog.AddProviderModelAsync(unsupportedProvider.Id,
                unsupported.Id, "audio-only", null, null);
            await catalog.AddPriceAsync(unsupportedMapping.Id, now.AddDays(-1), null, 1, 1, null, null);

            using var client = Client(app);
            using var response = await client.SendAsync(Request(Path(organization.Id, project.Id), owner.Session));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(fee.Id, body.GetProperty("feePolicyVersionId").GetGuid());
            Assert.Equal(2500, body.GetProperty("markupBasisPoints").GetInt32());
            Assert.Equal("100", body.GetProperty("fixedFeeMicroUsdPerRequest").GetString());
            var published = Assert.Single(body.GetProperty("models").EnumerateArray());
            Assert.Equal("gpt-catalog", published.GetProperty("id").GetString());
            Assert.Equal("Published", published.GetProperty("status").GetString());
            var rate = Assert.Single(published.GetProperty("providers").EnumerateArray());
            Assert.Equal(mapping.Id, rate.GetProperty("mappingId").GetGuid());
            Assert.Equal("openai", rate.GetProperty("provider").GetString());
            Assert.Equal(current.Id, rate.GetProperty("priceVersionId").GetGuid());
            Assert.Equal("1250000", rate.GetProperty("inputPriceMicroUsdPerMillion").GetString());
            Assert.Equal("2500000", rate.GetProperty("outputPriceMicroUsdPerMillion").GetString());
            Assert.Equal("625000", rate.GetProperty("cachedInputPriceMicroUsdPerMillion").GetString());
            Assert.Contains(rate.GetProperty("capabilities").EnumerateArray(),
                value => value.GetString() == "Vision");
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Catalog_endpoint_rejects_unauthorized_cross_tenant_archived_project_and_suspended_org_access()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var app = CreateApp();
        await app.StartAsync();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var owner = await RegisterOwnerAsync(scope.ServiceProvider, "catalog-owner-two@example.uz");
            var outsider = await RegisterOwnerAsync(scope.ServiceProvider, "catalog-outsider@example.uz");
            var (organization, project) = await CreateProjectAsync(scope.ServiceProvider, owner.AccountId);
            using var client = Client(app);
            var path = Path(organization.Id, project.Id);
            using (var response = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using (var response = await client.SendAsync(Request(path, outsider.Session)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await client.SendAsync(Request(Path(Guid.NewGuid(), project.Id), owner.Session)))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
            Assert.True(await projects.ArchiveAsync(owner.AccountId, organization.Id, project.Id));
            using (var response = await client.SendAsync(Request(path, owner.Session)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var secondProject = await projects.CreateAsync(owner.AccountId, organization.Id, "Another project");
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            await db.Set<OrganizationEntity>().Where(value => value.Id == organization.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Suspended"));
            using (var response = await client.SendAsync(Request(Path(organization.Id, secondProject.Id), owner.Session)))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Catalog_endpoint_fails_closed_when_managed_fee_is_not_published()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var app = CreateApp();
        await app.StartAsync();
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var owner = await RegisterOwnerAsync(scope.ServiceProvider, "catalog-no-fee@example.uz");
            var (organization, project) = await CreateProjectAsync(scope.ServiceProvider, owner.AccountId);
            using var client = Client(app);
            using var response = await client.SendAsync(Request(Path(organization.Id, project.Id), owner.Session));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(503, body.GetProperty("status").GetInt32());
        }
        finally { await app.StopAsync(); }
    }

    private WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString
        });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmAudit();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmProjects();
        builder.Services.AddUzllmCatalog();
        builder.Services.AddUzllmBilling();
        builder.Services.AddScoped<ICustomerCatalogService, CustomerCatalogService>();
        var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmManagementSession();
        app.MapUzllmCustomerCatalogEndpoints();
        return app;
    }

    private static async Task<(Guid AccountId, BrowserSessionTokens Session)> RegisterOwnerAsync(
        IServiceProvider services, string email)
    {
        var identity = services.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync(email, "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var session = await identity.AuthenticateAsync(email, "correct horse battery staple");
        return (registration.AccountId, session!);
    }

    private static async Task<(UZLLM.Modules.Organizations.Contracts.Organization, Project)> CreateProjectAsync(
        IServiceProvider services, Guid accountId)
    {
        var organizations = services.GetRequiredService<IOrganizationService>();
        var organization = await organizations.CreateAsync(accountId, "Catalog tenant");
        var project = await services.GetRequiredService<IProjectService>()
            .CreateAsync(accountId, organization.Id, "Catalog project");
        return (organization, project);
    }

    private static HttpClient Client(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static string Path(Guid organizationId, Guid projectId) =>
        $"/management/v1/organizations/{organizationId}/projects/{projectId}/catalog/models";

    private static HttpRequestMessage Request(string path, BrowserSessionTokens session)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={session.CsrfToken}");
        return request;
    }
}
