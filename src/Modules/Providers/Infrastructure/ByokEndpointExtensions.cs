using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

public static class ByokEndpointExtensions
{
    public static IEndpointRouteBuilder MapUzllmByokEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var keys = endpoints.MapGroup(
            "/management/v1/organizations/{organizationId:guid}/provider-keys");
        keys.MapGet("", async (Guid organizationId, HttpContext context,
            IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            try { return Results.Ok((await service.ListAsync(actor, organizationId, ct)).Select(ToResponse)); }
            catch (TenantAccessDeniedException) { return Denied(); }
        }).WithName("ListByokCredentials").WithSummary("List masked organization provider keys.");

        keys.MapGet("/{id:guid}", async (Guid organizationId, Guid id, HttpContext context,
            IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            try
            {
                var item = await service.FindAsync(actor, organizationId, id, ct);
                return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
            }
            catch (TenantAccessDeniedException) { return Denied(); }
        }).WithName("GetByokCredential").WithSummary("Get masked key metadata and project grants.");

        keys.MapPost("", async (Guid organizationId, CreateByokCredentialRequest request,
            HttpContext context, IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            try
            {
                var item = await service.CreateAsync(actor, organizationId, request.ProviderId,
                    request.Name, request.Secret, ct);
                return Results.Created($"/management/v1/organizations/{organizationId}/provider-keys/{item.Id}",
                    ToResponse(item));
            }
            catch (TenantAccessDeniedException) { return Denied(); }
            catch (ArgumentException error) { return Invalid(error.Message); }
        }).RequireManagementCsrf().WithName("CreateByokCredential")
            .WithSummary("Store an encrypted organization provider key; plaintext is never returned.");

        keys.MapPatch("/{id:guid}", async (Guid organizationId, Guid id,
            UpdateByokCredentialRequest request, HttpContext context,
            IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            try
            {
                var item = await service.UpdateAsync(actor, organizationId, id,
                    request.Name, request.Secret, ct);
                return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
            }
            catch (TenantAccessDeniedException) { return Denied(); }
            catch (ArgumentException error) { return Invalid(error.Message); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        }).RequireManagementCsrf().WithName("UpdateByokCredential")
            .WithSummary("Rename or rotate a provider key without returning its plaintext.");

        keys.MapPost("/{id:guid}/test", async (Guid organizationId, Guid id,
            HttpContext context, IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            try
            {
                var result = await service.TestAsync(actor, organizationId, id, ct);
                return result is null ? Results.NotFound() : Results.Ok(new ByokTestResponse(result.Value.ToString()));
            }
            catch (TenantAccessDeniedException) { return Denied(); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        }).RequireManagementCsrf().WithName("TestByokCredential")
            .WithSummary("Test a key against a fixed provider-owned endpoint without a DB transaction.");

        keys.MapPut("/{id:guid}/restrictions", async (Guid organizationId, Guid id,
            SetByokRestrictionsRequest request, HttpContext context,
            IByokCredentialService service, CancellationToken ct) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            if (request.SpendLimitMicroUsd is not null
                && !long.TryParse(request.SpendLimitMicroUsd,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                return Invalid("spendLimitMicroUsd must be a non-negative integer string.");
            try
            {
                var cap = request.SpendLimitMicroUsd is null ? (long?)null
                    : long.Parse(request.SpendLimitMicroUsd, System.Globalization.CultureInfo.InvariantCulture);
                var item = await service.SetRestrictionsAsync(actor, organizationId,
                    id, request.AllowedModels, cap, ct);
                return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
            }
            catch (TenantAccessDeniedException) { return Denied(); }
            catch (ArgumentException error) { return Invalid(error.Message); }
        }).RequireManagementCsrf().WithName("SetByokRestrictions")
            .WithSummary("Replace model allowlist and lifetime external provider spend cap.");

        keys.MapPost("/{id:guid}/disable", async (Guid organizationId, Guid id,
            HttpContext context, IByokCredentialService service, CancellationToken ct) =>
            await MutateAsync(organizationId, id, context, service,
                (actor, org, credential, token) => service.DisableAsync(actor, org, credential, token), ct))
            .RequireManagementCsrf().WithName("DisableByokCredential")
            .WithSummary("Prevent future use of a provider key.");

        keys.MapDelete("/{id:guid}", async (Guid organizationId, Guid id,
            HttpContext context, IByokCredentialService service, CancellationToken ct) =>
            await MutateAsync(organizationId, id, context, service,
                (actor, org, credential, token) => service.DeleteAsync(actor, org, credential, token), ct))
            .RequireManagementCsrf().WithName("DeleteByokCredential")
            .WithSummary("Soft-delete a provider key while preserving audit history.");

        keys.MapPut("/{id:guid}/projects/{projectId:guid}", async (Guid organizationId,
            Guid id, Guid projectId, HttpContext context, IByokCredentialService service,
            CancellationToken ct) => await GrantAsync(organizationId, id, projectId,
                true, context, service, ct)).RequireManagementCsrf()
            .WithName("GrantByokProject").WithSummary("Allow one project to use a provider key.");

        keys.MapDelete("/{id:guid}/projects/{projectId:guid}", async (Guid organizationId,
            Guid id, Guid projectId, HttpContext context, IByokCredentialService service,
            CancellationToken ct) => await GrantAsync(organizationId, id, projectId,
                false, context, service, ct)).RequireManagementCsrf()
            .WithName("RevokeByokProject").WithSummary("Remove a project provider-key grant.");
        return endpoints;
    }

    private static async Task<IResult> MutateAsync(Guid organizationId, Guid credentialId,
        HttpContext context, IByokCredentialService service,
        Func<Guid, Guid, Guid, CancellationToken, Task<bool>> action, CancellationToken ct)
    {
        if (!TryActor(context, out var actor)) return Results.Unauthorized();
        try { return await action(actor, organizationId, credentialId, ct)
            ? Results.NoContent() : Results.NotFound(); }
        catch (TenantAccessDeniedException) { return Denied(); }
    }

    private static async Task<IResult> GrantAsync(Guid organizationId, Guid credentialId,
        Guid projectId, bool enabled, HttpContext context,
        IByokCredentialService service, CancellationToken ct)
    {
        if (!TryActor(context, out var actor)) return Results.Unauthorized();
        try { return await service.SetProjectGrantAsync(actor, organizationId, credentialId,
            projectId, enabled, ct) ? Results.NoContent() : Results.NotFound(); }
        catch (TenantAccessDeniedException) { return Denied(); }
        catch (ArgumentException error) { return Invalid(error.Message); }
    }

    private static bool TryActor(HttpContext context, out Guid actor) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out actor);

    private static IResult Invalid(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["request"] = [message] });

    private static IResult Denied() => Results.StatusCode(StatusCodes.Status403Forbidden);

    private static ByokCredentialResponse ToResponse(ByokCredential item) => new(item.Id,
        item.OrganizationId, item.ProviderId, item.ProviderCode, item.Name, item.MaskedKey,
        item.Status.ToString(), item.CreatedAt, item.UpdatedAt, item.LastTestedAt,
        item.LastTestStatus, item.ProjectIds, item.AllowedModels,
        item.SpendLimitMicroUsd?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        item.ExternalSpentMicroUsd.ToString(System.Globalization.CultureInfo.InvariantCulture),
        item.ExternalReservedMicroUsd.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>Accepts one provider-owned secret for an organization.</summary>
public sealed record CreateByokCredentialRequest(Guid ProviderId, string Name, string Secret);

/// <summary>Optionally renames or rotates an existing organization provider key.</summary>
public sealed record UpdateByokCredentialRequest(string? Name, string? Secret);

/// <summary>Replaces the optional canonical-model allowlist and external spend cap.</summary>
public sealed record SetByokRestrictionsRequest(IReadOnlyList<string>? AllowedModels,
    string? SpendLimitMicroUsd);

/// <summary>Safe provider credential verification outcome without upstream response details.</summary>
public sealed record ByokTestResponse(string Status);

/// <summary>Masked provider key metadata; the secret and ciphertext are never returned.</summary>
public sealed record ByokCredentialResponse(Guid Id, Guid OrganizationId, Guid ProviderId,
    string ProviderCode, string Name, string MaskedKey, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastTestedAt,
    string? LastTestStatus, IReadOnlyList<Guid> ProjectIds,
    IReadOnlyList<string>? AllowedModels, string? SpendLimitMicroUsd,
    string ExternalSpentMicroUsd, string ExternalReservedMicroUsd);
