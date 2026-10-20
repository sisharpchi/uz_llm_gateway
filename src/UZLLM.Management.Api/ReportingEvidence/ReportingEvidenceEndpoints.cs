using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Management.Api.ReportingEvidence;

public static class ReportingEvidenceEndpoints
{
    public static IServiceCollection AddUzllmReportingEvidenceApi(this IServiceCollection services) =>
        services.AddScoped<IReportingEvidenceApiService, ReportingEvidenceApiService>();
    public static IEndpointRouteBuilder MapUzllmReportingEvidenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/management/v1/admin/organizations/{organizationId:guid}/reporting-evidence");
        group.AddEndpointFilter(async (context, next) =>
        {
            await context.HttpContext.RequestServices.GetRequiredService<IReportingEvidenceApiService>()
                .EnsureOperatorAsync(Session(context.HttpContext), context.HttpContext.RequestAborted);
            return await next(context);
        });
        group.MapPost("/fiscal", async (Guid organizationId, RegisterFiscalEvidenceRequest request, HttpContext http,
            IReportingEvidenceApiService service, CancellationToken ct) =>
        { var value = await service.FiscalAsync(Session(http), organizationId, request, ct); return TypedResults.Created(Location(organizationId, "fiscal", value.Id), value); })
            .RequireManagementCsrf().WithName("RegisterFiscalEvidence").WithSummary("Record external fiscal provenance only; no issuance, download, refund or tax inference.");
        group.MapPost("/contracts", async (Guid organizationId, RegisterContractReferenceRequest request, HttpContext http,
            IReportingEvidenceApiService service, CancellationToken ct) =>
        { var value = await service.ContractAsync(Session(http), organizationId, request, ct); return TypedResults.Created(Location(organizationId, "contracts", value.Id), value); })
            .RequireManagementCsrf().WithName("RegisterContractReference").WithSummary("Append approved-profile contract evidence or revocation; never grant SLA eligibility.");
        group.MapPost("/links", async (Guid organizationId, LinkContractReportRequest request, HttpContext http,
            IReportingEvidenceApiService service, CancellationToken ct) =>
        { var value = await service.LinkAsync(Session(http), organizationId, request, ct); return TypedResults.Created(Location(organizationId, "links", value.Id), value); })
            .RequireManagementCsrf().WithName("LinkContractReport").WithSummary("Pin immutable statement/SLI versions to one contract scope as EvidenceOnly.");
        group.MapGet("/{kind}", async (Guid organizationId, string kind, int? limit, string? cursor, HttpContext http,
            IReportingEvidenceApiService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(Session(http), organizationId, kind, limit, cursor, ct)))
            .WithName("ListReportingEvidence").WithSummary("List operator-only fiscal/contracts/links within one organization.");
        group.MapGet("/{kind}/{id:guid}", async Task<IResult> (Guid organizationId, string kind, Guid id, HttpContext http,
            IReportingEvidenceApiService service, CancellationToken ct) => await service.FindAsync(Session(http), organizationId, kind, id, ct) is { } value
                ? TypedResults.Ok(value) : TypedResults.NotFound())
            .WithName("GetReportingEvidence").WithSummary("Read immutable external evidence provenance; never fetch an external document.");
        return endpoints;
    }
    private static string Session(HttpContext http) => http.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty;
    private static string Location(Guid org, string kind, Guid id) => $"/management/v1/admin/organizations/{org}/reporting-evidence/{kind}/{id}";
}
