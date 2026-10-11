using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Management.Api;

public static class UsageReadEndpoints
{
    public static IEndpointRouteBuilder MapUzllmUsageReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var usage = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/usage");
        usage.AddEndpointFilter(async (context, next) =>
        {
            if (!Guid.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
                return TypedResults.Unauthorized();
            var organizationId = Guid.Parse(context.HttpContext.Request.RouteValues["organizationId"]!.ToString()!);
            if (context.HttpContext.Request.RouteValues.ContainsKey("requestId"))
                return await next(context);
            var authorization = context.HttpContext.RequestServices.GetRequiredService<IOrganizationAuthorizationService>();
            try
            {
                Guid? projectId = Guid.TryParse(context.HttpContext.Request.Query["projectId"], out var parsed)
                    ? parsed : null;
                await authorization.EnsurePermissionAsync(accountId, organizationId,
                    OrganizationPermission.ReadUsage, projectId, context.HttpContext.RequestAborted);
                return await next(context);
            }
            catch (TenantAccessDeniedException) { return TypedResults.StatusCode(StatusCodes.Status403Forbidden); }
            catch (ArgumentException exception)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    { ["query"] = [exception.Message] });
            }
        });

        usage.MapGet("/activity", async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetActivityAsync(organizationId, parameters.ToQuery(),
                parameters.Limit, parameters.Cursor, cancellationToken)))
            .WithName("GetUsageActivity").WithSummary("List organization inference requests using a stable cursor.");

        usage.MapGet("/requests/{requestId:guid}", async (
                Guid organizationId, Guid requestId, HttpContext context,
                IUsageReadService service, IOrganizationAuthorizationService authorization,
                CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
                return (IResult)TypedResults.Unauthorized();
            try
            {
                var allowedProjectIds = await authorization.GetReadableProjectIdsAsync(accountId,
                    organizationId, cancellationToken);
                var detail = await service.GetDetailAsync(organizationId, requestId, cancellationToken);
                if (detail is null || allowedProjectIds is not null
                    && !allowedProjectIds.Contains(detail.Request.ProjectId))
                    return TypedResults.NotFound();
                return TypedResults.Ok(detail);
            }
            catch (TenantAccessDeniedException)
            {
                return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
            }
        })
            .WithName("GetUsageRequestDetail").WithSummary("Read safe request, attempt, evidence, and settlement metadata.");

        usage.MapGet("/summary", async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetSummaryAsync(organizationId, parameters.ToQuery(), cancellationToken)))
            .WithName("GetUsageSummary").WithSummary("Read request, token, error, and charged-cost totals.");

        usage.MapGet("/timeseries", async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetTimeSeriesAsync(organizationId, parameters.ToQuery(), cancellationToken)))
            .WithName("GetUsageTimeSeries").WithSummary("Read UTC daily usage rollups.");

        usage.MapGet("/export", async Task<IResult> (Guid organizationId,
            [AsParameters] UsageReadQueryParameters parameters, string? kind, int? rowLimit,
            HttpContext context, IUsageExportService service, CancellationToken cancellationToken) =>
        {
            if (parameters.Limit is not null || parameters.Cursor is not null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    { ["query"] = ["Export is complete or rejected; use rowLimit, not limit/cursor."] });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var csv = await service.ExportAsync(organizationId, parameters.ToQuery(), kind, rowLimit, deadline.Token);
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Usage-From"] = csv.From.ToString("O");
                context.Response.Headers["X-Usage-To"] = csv.To.ToString("O");
                context.Response.Headers["X-Usage-Data-As-Of"] = csv.DataAsOf.ToString("O");
                context.Response.Headers["X-Usage-Source"] = csv.Source;
                context.Response.Headers["X-Usage-Pending-Buckets"] = csv.PendingBucketCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Usage-Row-Count"] = csv.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return TypedResults.File(csv.Content, "text/csv; charset=utf-8", csv.FileName);
            }
            catch (UsageExportLimitException)
            {
                return TypedResults.Problem(statusCode: 413, title: "Export too large",
                    detail: "Narrow the date range or filters. No partial export was produced.");
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return TypedResults.Problem(statusCode: 504, title: "Export deadline exceeded",
                    detail: "Narrow the date range or filters and retry.");
            }
        }).WithName("ExportUsageCsv").WithSummary("Download bounded activity or daily usage CSV.")
            .WithDescription("Same tenant/project permissions and UTC filters as usage reads. Default 1000, maximum 5000 rows and 4 MiB; no partial export. Unknown values are blank; monetary units are integer micro-USD.");

        MapBreakdown(usage, "/by-model", UsageBreakdownDimension.Model);
        MapBreakdown(usage, "/by-provider", UsageBreakdownDimension.Provider);
        MapBreakdown(usage, "/by-api-key", UsageBreakdownDimension.ApiKey);
        MapBreakdown(usage, "/by-project", UsageBreakdownDimension.Project);
        return endpoints;
    }

    private static void MapBreakdown(RouteGroupBuilder usage, string route, UsageBreakdownDimension dimension) =>
        usage.MapGet(route, async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetBreakdownAsync(organizationId, parameters.ToQuery(), dimension, cancellationToken)))
            .WithName($"GetUsageBy{dimension}").WithSummary($"Read usage grouped by {dimension}.");
}

/// <summary>Bounded organization-scoped usage filters. Dates are UTC, from inclusive and to exclusive.</summary>
public sealed record UsageReadQueryParameters
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "from")] public DateTimeOffset? From { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "to")] public DateTimeOffset? To { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "projectId")] public Guid? ProjectId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "apiKeyId")] public Guid? ApiKeyId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "modelId")] public Guid? ModelId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "providerId")] public Guid? ProviderId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "status")] public string? Status { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "isStream")] public bool? IsStream { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "requestId")] public Guid? RequestId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "limit")] public int? Limit { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "cursor")] public string? Cursor { get; init; }

    public UsageReadQuery ToQuery() => new(From, To, ProjectId, ApiKeyId, ModelId,
        ProviderId, Status, IsStream, RequestId);
}
