using System.Security.Claims;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Management.Api.SliReports;

public static class SliReportEndpoints
{
    public static IServiceCollection AddUzllmSliReportApi(this IServiceCollection services) => services.AddScoped<ISliReportApiService, SliReportApiService>();
    public static IEndpointRouteBuilder MapUzllmSliReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/management/v1/admin/sli-reports");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out _)) return TypedResults.Unauthorized();
            if (!http.User.HasClaim("uzllm:operator", "true")) return TypedResults.StatusCode(403);
            await http.RequestServices.GetRequiredService<ISliReportApiService>().EnsureOperatorAsync(Session(http), http.RequestAborted);
            return await next(context);
        });
        group.MapGet("", async (string scopeId, int? limit, string? cursor, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(Session(http), scopeId, limit, cursor, ct)))
            .WithName("ListSliReports").WithSummary("List immutable operator-only SLI versions for a registered deployment scope.");
        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
            await service.FindAsync(Session(http), id, ct) is { } value ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("GetSliReport").WithSummary("Read exact ratios, Unknown/NoData and coverage; not a contractual SLA.");
        group.MapGet("/{id:guid}/sources", async Task<IResult> (Guid id, int? limit, string? cursor, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
            await service.SourcesAsync(Session(http), id, limit, cursor, ct) is { } value ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("ListSliReportSources").WithSummary("Read immutable manifest membership, loss and coverage facts for one version.");
        group.MapGet("/{id:guid}/executions", async Task<IResult> (Guid id, int? limit, string? cursor, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
            await service.ExecutionsAsync(Session(http), id, limit, cursor, ct) is { } value ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("ListSliReportExecutions").WithSummary("Page frozen logical execution classifications, never provider content.");
        group.MapPost("", async (CreateSliReportRequest request, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
        {
            var value = await service.CreateAsync(Session(http), request, ct);
            return TypedResults.Accepted(Location(value.Operation.Id), value);
        }).RequireManagementCsrf().WithName("CreateSliReport").WithSummary("Queue one closed-month operational report; no maintenance or financial override.");
        group.MapPost("/{id:guid}/revisions", async Task<IResult> (Guid id, ReviseSliReportRequest request, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
        {
            var value = await service.ReviseAsync(Session(http), id, request, ct);
            return value is null ? TypedResults.NotFound() : TypedResults.Accepted(Location(value.Operation.Id), value);
        }).RequireManagementCsrf().WithName("ReviseSliReport").WithSummary("Queue an explicit predecessor/generation revision; keep original bytes.");
        group.MapGet("/jobs/{id:guid}", async Task<IResult> (Guid id, HttpContext http, ISliReportApiService service, CancellationToken ct) =>
            await service.JobAsync(Session(http), id, ct) is { } value ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("GetSliReportJob").WithSummary("Read stable operation and leased-job failure/dead-letter state.");
        return endpoints;
    }
    private static string Session(HttpContext http) => http.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty;
    private static string Location(Guid id) => $"/management/v1/admin/sli-reports/jobs/{id}";
}
