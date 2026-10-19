using System.Security.Claims;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Management.Api.Statements;

public static class StatementEndpoints
{
    public static IServiceCollection AddUzllmStatementApi(this IServiceCollection services) =>
        services.AddScoped<IStatementApiService, StatementApiService>();

    public static IEndpointRouteBuilder MapUzllmStatementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/statements");
        customer.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var service = http.RequestServices.GetRequiredService<IStatementApiService>();
            await service.EnsureCustomerAsync(Session(http), Org(http), http.RequestAborted);
            return await next(context);
        });
        customer.MapGet("", async (Guid organizationId, DateTimeOffset? from, DateTimeOffset? to, int? limit, string? cursor,
            HttpContext http, IStatementApiService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(Session(http), organizationId, from, to, limit, cursor, ct)))
            .WithName("ListStatements").WithSummary("List non-fiscal immutable statement versions by posting month/revision.");
        customer.MapGet("/{id:guid}", async Task<IResult> (Guid organizationId, Guid id, HttpContext http,
            IStatementApiService service, CancellationToken ct) => await service.FindAsync(Session(http), organizationId, id, ct) is { } value
                ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("GetStatement").WithSummary("Read allowlisted metadata for one immutable statement version.");
        customer.MapGet("/{id:guid}/lines", async Task<IResult> (Guid organizationId, Guid id, int? limit, string? cursor,
            HttpContext http, IStatementApiService service, CancellationToken ct) =>
                await service.LinesAsync(Session(http), organizationId, id, limit, cursor, ct) is { } value
                    ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("ListStatementLines").WithSummary("Page fixed posting ordinals within the selected version.");
        customer.MapGet("/{id:guid}/export", async Task<IResult> (Guid organizationId, Guid id, string? format,
            HttpContext http, IStatementApiService service, CancellationToken ct) =>
        {
            var value = await service.ExportAsync(Session(http), organizationId, id, format, ct);
            if (value is null) return TypedResults.NotFound();
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["X-UZLLM-Report-Sha256"] = value.Sha256;
            return TypedResults.File(value.Content, "text/csv; charset=utf-8", $"account-statement-{id:N}.csv", enableRangeProcessing: false);
        }).WithName("ExportStatementCsv").WithSummary("Download original version-pinned CSV bytes; verify length and SHA-256.");

        var admin = endpoints.MapGroup("/management/v1/admin/organizations/{organizationId:guid}/reports");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out _)) return TypedResults.Unauthorized();
            if (!http.User.HasClaim("uzllm:operator", "true")) return TypedResults.StatusCode(403);
            await http.RequestServices.GetRequiredService<IStatementApiService>().EnsureOperatorAsync(Session(http), http.RequestAborted);
            return await next(context);
        });
        admin.MapPost("/statements", async (Guid organizationId, CreateStatementRequest request, HttpContext http,
            IStatementApiService service, CancellationToken ct) =>
        {
            var value = await service.CreateAsync(Session(http), organizationId, request, ct);
            return TypedResults.Accepted(JobLocation(organizationId, value.Id), value);
        }).RequireManagementCsrf().WithName("CreateStatement").WithSummary("Queue one non-fiscal UTC-month snapshot; replay retains operation identity.");
        admin.MapPost("/statements/{id:guid}/revisions", async Task<IResult> (Guid organizationId, Guid id,
            ReviseStatementRequest request, HttpContext http, IStatementApiService service, CancellationToken ct) =>
        {
            var value = await service.ReviseAsync(Session(http), organizationId, id, request, ct);
            return value is null ? TypedResults.NotFound() : TypedResults.Accepted(JobLocation(organizationId, value.Id), value);
        }).RequireManagementCsrf().WithName("ReviseStatement").WithSummary("Queue an explicit predecessor revision; never rewrite published bytes.");
        admin.MapGet("/jobs/{id:guid}", async Task<IResult> (Guid organizationId, Guid id, HttpContext http,
            IStatementApiService service, CancellationToken ct) => await service.JobAsync(Session(http), organizationId, id, ct) is { } value
                ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("GetStatementJob").WithSummary("Read tenant-scoped operation and transport status, including terminal dead letters.");
        return endpoints;
    }

    private static string Session(HttpContext http) => http.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty;
    private static Guid Org(HttpContext http) => Guid.Parse(http.Request.RouteValues["organizationId"]!.ToString()!);
    private static string JobLocation(Guid org, Guid id) => $"/management/v1/admin/organizations/{org}/reports/jobs/{id}";
}
