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
            var authorization = context.HttpContext.RequestServices.GetRequiredService<IOrganizationAuthorizationService>();
            try
            {
                await authorization.EnsureOwnerAsync(accountId, organizationId, context.HttpContext.RequestAborted);
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

        usage.MapGet("/requests/{requestId:guid}", async Task<Results<Ok<UsageRequestDetail>, NotFound>> (
                Guid organizationId, Guid requestId, IUsageReadService service, CancellationToken cancellationToken) =>
            (await service.GetDetailAsync(organizationId, requestId, cancellationToken)) is { } detail
                ? TypedResults.Ok(detail) : TypedResults.NotFound())
            .WithName("GetUsageRequestDetail").WithSummary("Read safe request, attempt, evidence, and settlement metadata.");

        usage.MapGet("/summary", async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetSummaryAsync(organizationId, parameters.ToQuery(), cancellationToken)))
            .WithName("GetUsageSummary").WithSummary("Read request, token, error, and charged-cost totals.");

        usage.MapGet("/timeseries", async (Guid organizationId, [AsParameters] UsageReadQueryParameters parameters,
                IUsageReadService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetTimeSeriesAsync(organizationId, parameters.ToQuery(), cancellationToken)))
            .WithName("GetUsageTimeSeries").WithSummary("Read UTC daily usage rollups.");

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
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ApiKeyId { get; init; }
    public Guid? ModelId { get; init; }
    public Guid? ProviderId { get; init; }
    public string? Status { get; init; }
    public bool? IsStream { get; init; }
    public Guid? RequestId { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }

    public UsageReadQuery ToQuery() => new(From, To, ProjectId, ApiKeyId, ModelId,
        ProviderId, Status, IsStream, RequestId);
}
