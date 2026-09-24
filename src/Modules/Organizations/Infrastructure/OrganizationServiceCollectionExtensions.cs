using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Modules.Organizations.Infrastructure;

public static class OrganizationServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmOrganizations(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationStore, PostgreSqlOrganizationStore>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationAuthorizationService, OrganizationAuthorizationService>();
        return services;
    }

    public static IEndpointRouteBuilder MapUzllmOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var organizations = endpoints.MapGroup("/management/v1/organizations");
        organizations.MapGet("", async (HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            return TryGetAccountId(context.User, out var accountId)
                ? Results.Ok(await service.ListForAccountAsync(accountId, cancellationToken))
                : Results.Unauthorized();
        });

        organizations.MapPost("", async (CreateOrganizationRequest request, HttpContext context, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var organization = await service.CreateAsync(accountId, request.Name, cancellationToken);
                return Results.Created($"/management/v1/organizations/{organization.Id}", organization);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [exception.Message] });
            }
        }).RequireManagementCsrf();

        return endpoints;
    }

    private static bool TryGetAccountId(ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private sealed record CreateOrganizationRequest(string Name);
}
