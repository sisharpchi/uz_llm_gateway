using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Application;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Modules.Projects.Infrastructure;

public static class ProjectServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmProjects(this IServiceCollection services)
    {
        services.AddScoped<IProjectStore, PostgreSqlProjectStore>();
        services.AddScoped<IProjectService, ProjectService>();
        return services;
    }

    public static IEndpointRouteBuilder MapUzllmProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var projects = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/projects");
        projects.MapGet("", async (Guid organizationId, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, organizationId, accountId => service.ListAsync(accountId, organizationId, cancellationToken)));

        projects.MapPost("", async (Guid organizationId, CreateProjectRequest request, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var project = await service.CreateAsync(accountId, organizationId, request.Name, cancellationToken);
                return Results.Created($"/management/v1/organizations/{organizationId}/projects/{project.Id}", project);
            }
            catch (TenantAccessDeniedException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [exception.Message] });
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict();
            }
        }).RequireManagementCsrf();

        projects.MapGet("/{projectId:guid}", async (Guid organizationId, Guid projectId, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId))
            {
                return Results.Unauthorized();
            }

            try
            {
                return await service.FindAsync(accountId, organizationId, projectId, cancellationToken) is { } project
                    ? Results.Ok(project)
                    : Results.NotFound();
            }
            catch (TenantAccessDeniedException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        });

        projects.MapPost("/{projectId:guid}/archive", async (Guid organizationId, Guid projectId, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId))
            {
                return Results.Unauthorized();
            }

            try
            {
                return await service.ArchiveAsync(accountId, organizationId, projectId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (TenantAccessDeniedException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }).RequireManagementCsrf();

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        Guid organizationId,
        Func<Guid, Task<IReadOnlyList<Project>>> action)
    {
        if (!TryGetAccountId(context.User, out var accountId))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await action(accountId));
        }
        catch (TenantAccessDeniedException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    private static bool TryGetAccountId(ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private sealed record CreateProjectRequest(string Name);
}
