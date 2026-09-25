using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Management.Api.Budgets;

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapUzllmBudgetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/management/v1/projects/{projectId:guid}/budgets");
        group.MapGet("", async Task<Results<Ok<IReadOnlyList<BudgetPolicyResponse>>, UnauthorizedHttpResult,
            StatusCodeHttpResult, NotFound>> (Guid projectId, HttpContext context,
                IBudgetManagementService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId)) return TypedResults.Unauthorized();
            try
            {
                var budgets = await service.ListAsync(accountId, projectId, cancellationToken);
                return TypedResults.Ok<IReadOnlyList<BudgetPolicyResponse>>(budgets.Select(ToResponse).ToArray());
            }
            catch (TenantAccessDeniedException) { return TypedResults.StatusCode(StatusCodes.Status403Forbidden); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        }).WithName("ListProjectBudgets").WithSummary("List project and API-key budget policies with current UTC windows");

        group.MapPut("/{period}", async Task<Results<Ok<BudgetPolicyResponse>, UnauthorizedHttpResult,
            StatusCodeHttpResult, NotFound, ValidationProblem, Conflict>> (Guid projectId, string period,
                SetBudgetRequest request, HttpContext context, IBudgetManagementService service,
                CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId)) return TypedResults.Unauthorized();
            if (!Enum.TryParse<BudgetPeriod>(period, true, out var parsed)
                || !Enum.IsDefined(parsed) || long.TryParse(period, out _)
                || request.LimitMicroUsd < 0 || request.ApiKeyId == Guid.Empty)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["budget"] = ["A known period, non-negative limit, and valid optional API key are required."] });
            try
            {
                var result = await service.SetAsync(accountId, projectId, request.ApiKeyId,
                    parsed, new UsdMicroAmount(request.LimitMicroUsd), cancellationToken);
                return result is null ? TypedResults.Conflict() : TypedResults.Ok(ToResponse(result));
            }
            catch (TenantAccessDeniedException) { return TypedResults.StatusCode(StatusCodes.Status403Forbidden); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        }).RequireManagementCsrf().WithName("SetProjectBudget")
            .WithSummary("Set an all-time or UTC recurring hard budget for a project or one of its API keys");
        return endpoints;
    }

    private static BudgetPolicyResponse ToResponse(BudgetPolicy value) => new(value.Id, value.ProjectId,
        value.ApiKeyId, value.Period.ToString(), value.Window.Start, value.Window.End,
        value.Limit.Value, value.Captured.Value, value.Reserved.Value);

    private static bool TryGetAccountId(ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);
}

/// <summary>Hard budget to apply to the project or an API key within it.</summary>
public sealed record SetBudgetRequest(long LimitMicroUsd, Guid? ApiKeyId);

/// <summary>Current UTC budget window and its captured and reserved spend.</summary>
public sealed record BudgetPolicyResponse(Guid Id, Guid ProjectId, Guid? ApiKeyId, string Period,
    DateTimeOffset WindowStart, DateTimeOffset? WindowEnd, long LimitMicroUsd,
    long CapturedMicroUsd, long ReservedMicroUsd);
