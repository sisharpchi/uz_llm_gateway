using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Modules.Projects.Infrastructure;

public static class ProjectSettingsEndpoints
{
    public static IEndpointRouteBuilder MapUzllmProjectSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        const string route = "/management/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/settings";
        endpoints.MapGet(route, (Guid organizationId, Guid projectId, HttpContext context,
            IProjectSettingsService service, CancellationToken ct) =>
            ExecuteAsync(context, service, organizationId, projectId, null, ct)).WithName("ReadProjectSettings");
        endpoints.MapPut(route, (Guid organizationId, Guid projectId, ProjectSettings request, HttpContext context,
            IProjectSettingsService service, CancellationToken ct) =>
            ExecuteAsync(context, service, organizationId, projectId, request, ct))
            .RequireManagementCsrf().WithName("SetProjectSettings");
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, IProjectSettingsService service,
        Guid organizationId, Guid projectId, ProjectSettings? settings, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return Results.Unauthorized();
        try { return Results.Ok(settings is null ? await service.ReadAsync(accountId, organizationId, projectId, ct)
            : await service.ReplaceAsync(accountId, organizationId, projectId, settings, ct)); }
        catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["settings"] = [exception.Message] }); }
    }
}
