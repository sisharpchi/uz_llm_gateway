using System.Security.Claims;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Management.Api.Catalog;

public static class CustomerCatalogEndpoints
{
    public static IEndpointRouteBuilder MapUzllmCustomerCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/management/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/catalog/providers/{providerCode}/data-policy",
            async (Guid organizationId, Guid projectId, string providerCode, HttpContext context,
                ICustomerCatalogService service, CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
                    return Results.Unauthorized();
                try
                {
                    var policy = await service.GetProviderDataPolicyAsync(accountId, organizationId,
                        projectId, providerCode, cancellationToken);
                    return policy is null ? Results.NotFound() : Results.Ok(policy);
                }
                catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
                catch (ArgumentException) { return Results.Problem("Invalid provider code.", statusCode: 400); }
            }).WithName("GetCustomerProviderDataPolicy")
            .WithSummary("Read dated conditional upstream policy; unknown is not ZDR or a residency guarantee");
        endpoints.MapGet("/management/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/catalog/models",
            async (Guid organizationId, Guid projectId, HttpContext context,
                ICustomerCatalogService service, CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
                    return Results.Unauthorized();
                try
                {
                    var catalog = await service.ListAsync(accountId, organizationId, projectId,
                        cancellationToken);
                    return catalog is null ? Results.NotFound() : Results.Ok(catalog);
                }
                catch (TenantAccessDeniedException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                catch (InvalidOperationException)
                {
                    return Results.Problem("Published managed pricing is unavailable.", statusCode: 503);
                }
            })
            .WithName("ListCustomerCatalogModels")
            .WithSummary("List effective managed chat model prices and capabilities for an authorized project")
            .Produces<CustomerCatalogResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }
}
