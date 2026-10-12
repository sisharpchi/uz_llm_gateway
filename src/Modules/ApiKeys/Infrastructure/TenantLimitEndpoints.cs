using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public static class TenantLimitEndpoints
{
    public static IEndpointRouteBuilder MapUzllmTenantLimitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/management/v1/projects/{projectId:guid}/limits",
            (Guid projectId, HttpContext context, ITenantLimitService service, CancellationToken ct) =>
                ExecuteAsync(context, service, projectId, false, null, ct)).WithName("ReadProjectLimits");
        endpoints.MapPut("/management/v1/projects/{projectId:guid}/limits",
            (Guid projectId, TenantLimits request, HttpContext context, ITenantLimitService service, CancellationToken ct) =>
                ExecuteAsync(context, service, projectId, false, request, ct)).RequireManagementCsrf().WithName("SetProjectLimits");
        endpoints.MapGet("/management/v1/api-keys/{apiKeyId:guid}/limits",
            (Guid apiKeyId, HttpContext context, ITenantLimitService service, CancellationToken ct) =>
                ExecuteAsync(context, service, apiKeyId, true, null, ct)).WithName("ReadApiKeyLimits");
        endpoints.MapPut("/management/v1/api-keys/{apiKeyId:guid}/limits",
            (Guid apiKeyId, TenantLimits request, HttpContext context, ITenantLimitService service, CancellationToken ct) =>
                ExecuteAsync(context, service, apiKeyId, true, request, ct)).RequireManagementCsrf().WithName("SetApiKeyLimits");
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, ITenantLimitService service,
        Guid id, bool apiKey, TenantLimits? request, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return Results.Unauthorized();
        try { return Results.Ok(request is null ? await service.ReadAsync(accountId, id, apiKey, ct)
            : await service.ReplaceAsync(accountId, id, apiKey, request, ct)); }
        catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["limits"] = [exception.Message] }); }
    }
}
