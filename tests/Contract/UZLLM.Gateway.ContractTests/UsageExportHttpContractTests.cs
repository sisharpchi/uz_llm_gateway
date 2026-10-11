using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed class UsageExportHttpContractTests
{
    [Fact]
    public async Task Csv_deadline_cancels_pending_io_and_returns_problem_without_partial_download()
    {
        var export = new PendingExport();
        await using var app = await StartAsync(export);
        using var client = Client(app);
        using var response = await client.GetAsync(Path);
        await export.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Contains("Export deadline exceeded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Csv_client_disconnect_cancels_pending_io_without_a_download()
    {
        var export = new PendingExport();
        await using var app = await StartAsync(export);
        using var client = Client(app);
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync(Path, cancellation.Token);
        await export.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await export.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(export.Canceled.Task.IsCompletedSuccessfully);
    }

    private const string Path = "/management/v1/organizations/11111111-1111-4111-8111-111111111111/usage/export";

    private static async Task<WebApplication> StartAsync(PendingExport export)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IUsageReadService>(new UsageReadService(new UsageCsvContractTests.ExportStore(), TimeProvider.System));
        builder.Services.AddSingleton<IUsageExportService>(export);
        builder.Services.AddSingleton<IOrganizationAuthorizationService, AllowedReader>();
        var app = builder.Build();
        app.UseUzllmManagementNoStore();
        app.Use((context, next) =>
        {
            // Real session/role/grant enforcement is covered by PostgreSQL integration tests.
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString())], "fixture"));
            return next(context);
        });
        app.MapUzllmUsageReadEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
        Timeout = TimeSpan.FromSeconds(45)
    };

    private sealed class PendingExport : IUsageExportService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<UsageCsvExport> ExportAsync(Guid organizationId, UsageReadQuery query,
            string? kind = null, int? rowLimit = null, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            throw new InvalidOperationException("Pending fixture must only terminate through cancellation.");
        }
    }

    private sealed class AllowedReader : IOrganizationAuthorizationService
    {
        public Task EnsureOwnerAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>?> GetReadableProjectIdsAsync(Guid accountId, Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsurePermissionAsync(Guid accountId, Guid organizationId, OrganizationPermission permission,
            Guid? projectId = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OrganizationPermission.ReadUsage, permission);
            return Task.CompletedTask;
        }
    }
}
