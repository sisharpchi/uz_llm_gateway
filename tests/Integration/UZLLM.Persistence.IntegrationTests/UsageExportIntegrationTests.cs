using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using UZLLM.Management.Api;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Worker;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class UsageReadIntegrationTests
{
    [Fact]
    public async Task Csv_activity_matches_paged_api_and_utc_filters_without_other_tenant_rows()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);
        var queries = new[]
        {
            Query(), Query() with { From = Start.ToOffset(TimeSpan.FromHours(5)), To = Start.AddDays(1).ToOffset(TimeSpan.FromHours(5)) },
            Query() with { ProjectId = seed.ProjectId, ApiKeyId = seed.ApiKeyId, ModelId = seed.MainModelId, Status = "succeeded", IsStream = true },
            Query() with { ProviderId = seed.AnthropicProviderId }, Query() with { RequestId = UnknownRequestId },
            Query() with { From = Start.AddMicroseconds(1), To = Start.AddDays(1).AddMicroseconds(1) },
            Query() with { ProjectId = seed.ForeignProjectId }, Query() with { RequestId = seed.ForeignRequestId }
        };
        foreach (var query in queries)
        {
            var api = new List<UsageActivityItem>();
            string? cursor = null;
            do
            {
                var page = await read.GetActivityAsync(seed.OrganizationId, query, 1, cursor);
                api.AddRange(page.Items); cursor = page.NextCursor;
            } while (cursor is not null);
            var csv = await read.ExportAsync(seed.OrganizationId, query);
            var rows = ParseCsv(csv.Content);
            Assert.Equal(api.Select(value => value.RequestId.ToString("D")), rows.Select(value => value["requestId"]));
            for (var index = 0; index < api.Count; index++)
            {
                Assert.Equal(api[index].ChargedMicroUsd ?? "", rows[index]["chargedMicroUsd"]);
                Assert.Equal(api[index].InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "", rows[index]["inputTokens"]);
                Assert.Equal(api[index].OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "", rows[index]["outputTokens"]);
                Assert.Equal(api[index].StartedAt, DateTimeOffset.Parse(rows[index]["startedAt"], CultureInfo.InvariantCulture));
                Assert.Equal(api[index].ExecutionState, rows[index]["executionState"]);
                Assert.Equal(api[index].ProviderCode ?? "", rows[index]["providerCode"]);
            }
            Assert.DoesNotContain(seed.ForeignRequestId.ToString("D"), Encoding.UTF8.GetString(csv.Content));
        }
        var full = ParseCsv((await read.ExportAsync(seed.OrganizationId, Query())).Content);
        Assert.Equal(3500, full.Sum(value => value["chargedMicroUsd"] == "" ? 0 : long.Parse(value["chargedMicroUsd"], CultureInfo.InvariantCulture)));
        Assert.Equal(120, full.Sum(value => value["inputTokens"] == "" ? 0 : long.Parse(value["inputTokens"], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Csv_daily_matches_api_lag_and_original_day_after_refund_publication()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var read = Read(scope.ServiceProvider);
        var settlement = await db.Set<BillingSettlementEntity>().SingleAsync(value => value.RequestId == FirstRequestId);
        db.Set<BillingSettlementRefundEntity>().Add(new BillingSettlementRefundEntity
        {
            Id = Guid.CreateVersion7(), SettlementId = settlement.Id, OrganizationId = seed.OrganizationId,
            ActorAccountId = seed.OwnerAccountId, RefundKey = "csv-refund", AmountMicroUsd = 1000,
            Reason = "CSV correction fixture", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var before = await CompareAsync();
        Assert.Equal(1, before.PendingBucketCount);
        Assert.Equal("0", ParseCsv(before.Content)[0]["refundedMicroUsd"]);
        await new UsageRollupCycle(new PostgreSqlUsageRollupStore(db)).RunAsync();
        var after = await CompareAsync();
        Assert.Equal(0, after.PendingBucketCount);
        Assert.Equal("2026-09-20", ParseCsv(after.Content)[0]["day"]);
        Assert.Equal("1000", ParseCsv(after.Content)[0]["refundedMicroUsd"]);
        Assert.Equal("2500", ParseCsv(after.Content)[0]["netChargedMicroUsd"]);

        async Task<UsageCsvExport> CompareAsync()
        {
            var api = await read.GetTimeSeriesAsync(seed.OrganizationId, Query());
            var csv = await read.ExportAsync(seed.OrganizationId, Query(), "daily");
            var rows = ParseCsv(csv.Content);
            Assert.Equal(api.DataAsOf, csv.DataAsOf);
            Assert.Equal(api.Aggregation.PendingBucketCount, csv.PendingBucketCount);
            Assert.Equal(api.Items.Count, rows.Count);
            for (var index = 0; index < api.Items.Count; index++)
            {
                var item = api.Items[index];
                Assert.Equal(item.Day.ToString("yyyy-MM-dd"), rows[index]["day"]);
                Assert.Equal(item.RequestCount.ToString(CultureInfo.InvariantCulture), rows[index]["requestCount"]);
                Assert.Equal(item.ErrorCount.ToString(CultureInfo.InvariantCulture), rows[index]["errorCount"]);
                Assert.Equal(item.InputTokens.ToString(CultureInfo.InvariantCulture), rows[index]["inputTokens"]);
                Assert.Equal(item.OutputTokens.ToString(CultureInfo.InvariantCulture), rows[index]["outputTokens"]);
                Assert.Equal(item.ChargedMicroUsd, rows[index]["chargedMicroUsd"]);
                Assert.Equal(item.RefundedMicroUsd, rows[index]["refundedMicroUsd"]);
                Assert.Equal(item.NetChargedMicroUsd, rows[index]["netChargedMicroUsd"]);
            }
            return csv;
        }
    }

    [Fact]
    public async Task Csv_http_enforces_session_current_project_grants_bounds_and_safe_download_headers()
    {
        var seed = await ResetAndSeedAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString });
        builder.Services.AddUzllmPersistence(builder.Configuration);
        builder.Services.AddUzllmIdentity();
        builder.Services.AddUzllmOrganizations();
        builder.Services.AddUzllmUsage();
        await using var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.UseUzllmManagementSession();
        app.MapUzllmUsageReadEndpoints();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var owner = await Actor("Owner");
            var developer = await Actor("Developer");
            var viewer = await Actor("BillingViewer"); // Existing ReadUsage policy deliberately allows this role.
            var outsider = await Actor(null);
            db.Set<ProjectGrantEntity>().Add(new() { OrganizationId = seed.OrganizationId, AccountId = developer.Id, ProjectId = seed.ProjectId, CreatedAt = DateTimeOffset.UtcNow });
            await db.Set<ProjectEntity>().Where(value => value.Id == seed.ProjectId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.Name, "=1+2\";,bad\r\nToshkent"));
            await db.SaveChangesAsync();
            var path = $"/management/v1/organizations/{seed.OrganizationId}/usage/export?from={Start.AddHours(-1):O}&to={AsOf:O}"
                .Replace("+00:00", "Z", StringComparison.Ordinal);
            using (var anonymous = await client.GetAsync(path))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            foreach (var (actor, suffix) in new[] { (outsider, ""), (developer, ""), (developer, $"&projectId={seed.ForeignProjectId}") })
            {
                using var denied = await client.SendAsync(Request(actor.Session, path + suffix));
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
                Assert.DoesNotContain(FirstRequestId.ToString("D"), await denied.Content.ReadAsStringAsync());
            }
            foreach (var (actor, suffix) in new[] { (owner, ""), (viewer, ""), (developer, $"&projectId={seed.ProjectId}") })
            {
                using var success = await client.SendAsync(Request(actor.Session, path + suffix));
                Assert.Equal(HttpStatusCode.OK, success.StatusCode);
                Assert.Equal("text/csv", success.Content.Headers.ContentType?.MediaType);
                Assert.Equal("utf-8", success.Content.Headers.ContentType?.CharSet);
                Assert.Equal("attachment", success.Content.Headers.ContentDisposition?.DispositionType);
                Assert.Contains("uzllm-activity.csv", success.Content.Headers.ContentDisposition?.ToString());
                Assert.Equal("no-store", success.Headers.CacheControl?.ToString());
                Assert.Equal("nosniff", success.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Equal("3", success.Headers.GetValues("X-Usage-Row-Count").Single());
                Assert.Equal("AuthoritativeRequest", success.Headers.GetValues("X-Usage-Source").Single());
                Assert.Equal(Start.AddHours(-1), DateTimeOffset.Parse(success.Headers.GetValues("X-Usage-From").Single(), CultureInfo.InvariantCulture));
                var body = await success.Content.ReadAsByteArrayAsync();
                Assert.All(ParseCsv(body), row => Assert.Equal("'=1+2\";,bad\r\nToshkent", row["projectName"]));
                Assert.DoesNotContain(seed.ForeignRequestId.ToString("D"), Encoding.UTF8.GetString(body));
                Assert.DoesNotContain("trace-safe", Encoding.UTF8.GetString(body));
                Assert.DoesNotContain("SecretFingerprint", Encoding.UTF8.GetString(body));
            }
            using (var foreign = await client.SendAsync(Request(owner.Session, path + $"&projectId={seed.ForeignProjectId}")))
                Assert.Empty(ParseCsv(await foreign.Content.ReadAsByteArrayAsync()));
            foreach (var suffix in new[] { "&kind=summary", "&rowLimit=0", "&rowLimit=5001", "&rowLimit=invalid", "&limit=1", "&cursor=abc", "&modelId=invalid", "&status=invalid" })
            {
                using var invalid = await client.SendAsync(Request(owner.Session, path + suffix));
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.Null(invalid.Content.Headers.ContentDisposition);
            }
            using (var oversized = await client.SendAsync(Request(owner.Session, path + "&rowLimit=1")))
            {
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
                Assert.Equal("application/problem+json", oversized.Content.Headers.ContentType?.MediaType);
                Assert.Null(oversized.Content.Headers.ContentDisposition);
                Assert.DoesNotContain(FirstRequestId.ToString("D"), await oversized.Content.ReadAsStringAsync());
            }
            await db.Set<ProjectGrantEntity>().Where(value => value.AccountId == developer.Id).ExecuteDeleteAsync();
            using var revoked = await client.SendAsync(Request(developer.Session, path + $"&projectId={seed.ProjectId}"));
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);

            async Task<(Guid Id, BrowserSessionTokens Session)> Actor(string? role)
            {
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
                var email = $"csv-{role ?? "outsider"}@example.test";
                var registration = await identity.RegisterAsync(email, "correct horse battery staple");
                Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
                var session = (await identity.AuthenticateAsync(email, "correct horse battery staple"))!;
                if (role is not null)
                    db.Set<OrganizationMemberEntity>().Add(new() { OrganizationId = seed.OrganizationId, AccountId = registration.AccountId, Role = role, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
                return (registration.AccountId, session);
            }
        }
        finally { await app.StopAsync(); }

        static HttpRequestMessage Request(BrowserSessionTokens session, string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url.Replace("+00:00", "Z", StringComparison.Ordinal));
            request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}");
            return request;
        }
    }

    private static List<Dictionary<string, string>> ParseCsv(byte[] content)
    {
        using var parser = new TextFieldParser(new StringReader(Encoding.UTF8.GetString(content).TrimStart('\uFEFF')))
            { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()!;
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields()!;
            Assert.Equal(headers.Length, values.Length);
            rows.Add(headers.Zip(values).ToDictionary(value => value.First, value => value.Second));
        }
        return rows;
    }
}
