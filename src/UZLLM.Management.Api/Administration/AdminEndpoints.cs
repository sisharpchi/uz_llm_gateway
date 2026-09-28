using System.Security.Claims;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Administration;

public static class AdminEndpoints
{
    public static IServiceCollection AddUzllmAdministration(this IServiceCollection services) => services
        .AddScoped<IAdminReadStore, PostgreSqlAdminReadStore>()
        .AddScoped<IAdminService, AdminService>();

    public static IEndpointRouteBuilder MapUzllmAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var access = endpoints.MapGroup("/management/v1/admin");
        access.MapGet("/access", async (HttpContext context, IIdentityService identity, CancellationToken ct) =>
        {
            if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            if (!IsOperator(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(new { recentMfa = await identity.HasRecentOperatorReauthenticationAsync(
                context.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty, ct) });
        });

        var admin = access.MapGroup(string.Empty).AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            if (!IsOperator(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var identity = http.RequestServices.GetRequiredService<IIdentityService>();
            if (!await identity.HasRecentOperatorReauthenticationAsync(
                http.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty, http.RequestAborted))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            try { return await next(context); }
            catch (ArgumentException exception) { return Results.Problem(exception.Message, statusCode: 400); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: 409); }
        });

        admin.MapGet("/accounts", (string query, IAdminService service, CancellationToken ct) => service.SearchAccountsAsync(query, ct));
        admin.MapGet("/organizations", (string query, IAdminService service, CancellationToken ct) => service.SearchOrganizationsAsync(query, ct));
        admin.MapGet("/providers", (IAdminService service, CancellationToken ct) => service.ListProvidersAsync(ct));
        admin.MapGet("/mappings/{id:guid}/prices", (Guid id, IAdminService service, CancellationToken ct) => service.ListPricesAsync(id, ct));
        admin.MapGet("/organizations/{id:guid}/ledger", (Guid id, int? limit, IAdminService service, CancellationToken ct) =>
            service.ListLedgerAsync(id, limit ?? 50, ct));
        admin.MapGet("/payments", (Guid? organizationId, int? limit, IAdminService service, CancellationToken ct) =>
            service.ListPaymentsAsync(organizationId, limit ?? 50, ct));
        admin.MapGet("/audit", (int? limit, IAdminService service, CancellationToken ct) => service.ListAuditAsync(limit ?? 50, ct));
        admin.MapGet("/controls", (IAdminService service, CancellationToken ct) => service.ListControlsAsync(ct));
        admin.MapGet("/work/dead-letters", (int? limit, IOperationalWorkMonitor monitor, CancellationToken ct) =>
            monitor.ListDeadLettersAsync(limit ?? 50, ct));

        admin.MapPost("/operators/{accountId:guid}/mfa/reset", async (Guid accountId,
            ResetOperatorMfaRequest request, HttpContext http, IIdentityService identity, CancellationToken ct) =>
        {
            try
            {
                return await identity.ResetOperatorMfaAsync(
                    http.Request.Cookies[IdentityCookieNames.Session] ?? string.Empty,
                    accountId, request.Reason, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).RequireManagementCsrf();

        admin.MapPost("/providers", async (CreateAdminProviderRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            Results.Created($"/management/v1/admin/providers", new { id = await service.CreateProviderAsync(Actor(http), request, ct) })).RequireManagementCsrf();
        admin.MapPost("/models", async (CreateAdminModelRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            Results.Created($"/management/v1/admin/providers", new { id = await service.CreateModelAsync(Actor(http), request, ct) })).RequireManagementCsrf();
        admin.MapPost("/mappings", async (CreateAdminMappingRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            Results.Created($"/management/v1/admin/providers", new { id = await service.CreateMappingAsync(Actor(http), request, ct) })).RequireManagementCsrf();
        admin.MapPost("/credentials", async (CreateAdminCredentialRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            Results.Created($"/management/v1/admin/providers", new { id = await service.CreateCredentialAsync(Actor(http), request, ct) })).RequireManagementCsrf();
        admin.MapPost("/prices", async (CreateAdminPriceRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            Results.Created($"/management/v1/admin/mappings/{request.ProviderModelId}/prices", new { id = await service.SchedulePriceAsync(Actor(http), request, ct) })).RequireManagementCsrf();

        admin.MapPatch("/providers/{id:guid}", (Guid id, SetAdminStatusRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            ResultFor(service.SetProviderEnabledAsync(Actor(http), id, request, ct))).RequireManagementCsrf();
        admin.MapPatch("/models/{id:guid}", (Guid id, SetAdminStatusRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            ResultFor(service.SetModelEnabledAsync(Actor(http), id, request, ct))).RequireManagementCsrf();
        admin.MapPatch("/mappings/{id:guid}", (Guid id, SetAdminStatusRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            ResultFor(service.SetMappingEnabledAsync(Actor(http), id, request, ct))).RequireManagementCsrf();
        admin.MapPatch("/credentials/{id:guid}", (Guid id, SetAdminStatusRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            ResultFor(service.SetCredentialEnabledAsync(Actor(http), id, request, ct))).RequireManagementCsrf();
        admin.MapPatch("/controls/{feature}", (string feature, SetAdminStatusRequest request, HttpContext http, IAdminService service, CancellationToken ct) =>
            ResultFor(service.SetPlatformEnabledAsync(Actor(http), feature, request, ct))).RequireManagementCsrf();
        return endpoints;
    }

    private static async Task<IResult> ResultFor(Task<bool> operation) =>
        await operation ? Results.NoContent() : Results.NotFound();
    private static bool IsOperator(HttpContext context) => context.User.FindFirst("uzllm:operator")?.Value == "true";
    private static Guid Actor(HttpContext context) => Guid.Parse(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private sealed record ResetOperatorMfaRequest(string Reason);
}
