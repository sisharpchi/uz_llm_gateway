using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Management.Api;

public static class PayloadRetentionEndpoints
{
    public static IEndpointRouteBuilder MapUzllmPayloadRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
            "/management/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/payload-retention");

        group.MapGet("", async (Guid organizationId, Guid projectId, HttpContext context,
            FoundationDbContext db, IOrganizationAuthorizationService authorization,
            IPayloadRetentionService retention, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var accountId)) return (IResult)Results.Unauthorized();
            try { await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            if (!await ProjectExistsAsync(db, organizationId, projectId, cancellationToken))
                return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await retention.GetPolicyAsync(organizationId, projectId, cancellationToken));
        }).WithName("GetPayloadRetentionPolicy");

        group.MapPut("", async (Guid organizationId, Guid projectId, SetPayloadRetentionRequest request,
            HttpContext context, IOrganizationAuthorizationService authorization,
            IPayloadRetentionService retention, PayloadEnvelopeProtector protector,
            IAuditTrail audit, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var accountId)) return (IResult)Results.Unauthorized();
            try { await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            if (request.RetentionMinutes is < 60 or > 10080)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["retentionMinutes"] = ["Must be between 60 and 10080 minutes."] });
            if (request.Enabled)
            {
                try { protector.ValidateActiveKey(); }
                catch (InvalidOperationException)
                {
                    return Results.Problem("Payload encryption is not configured.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }
            var policy = await retention.SetPolicyAsync(organizationId, projectId, accountId,
                request.Enabled, request.RetentionMinutes, cancellationToken);
            if (policy is null) return Results.NotFound();
            await audit.RecordAsync(new(organizationId, accountId, "payload_retention.updated",
                "project", projectId, context.Connection.RemoteIpAddress?.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new { request.Enabled, request.RetentionMinutes })),
                cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(policy);
        }).RequireManagementCsrf().WithName("SetPayloadRetentionPolicy");

        group.MapGet("/requests/{requestId:guid}", async (Guid organizationId, Guid projectId,
            Guid requestId, HttpContext context, IOrganizationAuthorizationService authorization,
            IPayloadRetentionService retention, IAuditTrail audit, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var accountId)) return (IResult)Results.Unauthorized();
            try { await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            context.Response.Headers.CacheControl = "no-store";
            var payload = await retention.GetPayloadAsync(organizationId, projectId, requestId,
                cancellationToken);
            if (payload is null) return Results.NotFound();
            await audit.RecordAsync(new(organizationId, accountId, "payload.read", "usage.request",
                requestId, context.Connection.RemoteIpAddress?.ToString(), "{}"), cancellationToken);
            return Results.Ok(payload);
        }).WithName("GetRetainedRequestPayload");
        return endpoints;
    }

    private static bool TryAccount(HttpContext context, out Guid accountId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private static Task<bool> ProjectExistsAsync(FoundationDbContext db, Guid organizationId,
        Guid projectId, CancellationToken cancellationToken) => db.Set<ProjectEntity>().AsNoTracking()
        .AnyAsync(value => value.OrganizationId == organizationId && value.Id == projectId,
            cancellationToken);
}

public sealed record SetPayloadRetentionRequest(bool Enabled, int RetentionMinutes);
