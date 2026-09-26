using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Modules.Organizations.Infrastructure;

public static class TeamEndpointExtensions
{
    public static IEndpointRouteBuilder MapUzllmTeamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var team = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/team");
        team.MapGet("/members", async (Guid organizationId, HttpContext context, ITeamService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
            try
            {
                return Results.Ok((await service.ListMembersAsync(actorId, organizationId, cancellationToken))
                    .Select(ToResponse));
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).WithName("ListTeamMembers").WithSummary("List organization members and explicit project grants.");

        team.MapGet("/invitations", async (Guid organizationId, HttpContext context,
            ITeamService service, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
            try
            {
                return Results.Ok((await service.ListInvitationsAsync(actorId, organizationId,
                    cancellationToken)).Select(ToResponse));
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).WithName("ListTeamInvitations").WithSummary("List safe invitation metadata; tokens are never returned.");

        team.MapPost("/invitations", async (Guid organizationId, InviteTeamMemberRequest request,
            HttpContext context, ITeamService service, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
            if (!ParseRole(request.Role, out var role)) return BadRole();
            try
            {
                var invitation = await service.InviteAsync(actorId, organizationId,
                    request.Email, role, cancellationToken);
                return Results.Created($"/management/v1/organizations/{organizationId}/team/invitations/{invitation.Id}",
                    ToResponse(invitation));
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (ArgumentException exception) { return BadRequest(exception.Message); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        }).RequireManagementCsrf().WithName("InviteTeamMember")
            .WithSummary("Send a time-limited, one-use invitation to an email address.");

        endpoints.MapPost("/management/v1/team/invitations/accept", async (
            AcceptTeamInvitationRequest request, HttpContext context, ITeamService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var accountId)) return Results.Unauthorized();
            var member = await service.AcceptAsync(accountId, request.Token, cancellationToken);
            return member is null ? Results.NotFound() : Results.Ok(ToResponse(member));
        }).RequireManagementCsrf().WithName("AcceptTeamInvitation")
            .WithSummary("Accept an invitation as the verified account matching its email.");

        team.MapPatch("/members/{accountId:guid}/role", async (Guid organizationId,
            Guid accountId, ChangeTeamRoleRequest request, HttpContext context,
            ITeamService service, CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
            if (!ParseRole(request.Role, out var role)) return BadRole();
            try
            {
                return await service.ChangeRoleAsync(actorId, organizationId, accountId,
                    role, cancellationToken) ? Results.NoContent() : Results.NotFound();
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        }).RequireManagementCsrf().WithName("ChangeTeamRole")
            .WithSummary("Change a member role without weakening the last-owner rule.");

        team.MapDelete("/members/{accountId:guid}", async (Guid organizationId,
            Guid accountId, HttpContext context, ITeamService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
            try
            {
                return await service.RevokeAsync(actorId, organizationId, accountId,
                    cancellationToken) ? Results.NoContent() : Results.NotFound();
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        }).RequireManagementCsrf().WithName("RevokeTeamMember")
            .WithSummary("Revoke membership and all project grants immediately.");

        team.MapPut("/members/{accountId:guid}/projects/{projectId:guid}", async (
            Guid organizationId, Guid accountId, Guid projectId, HttpContext context,
            ITeamService service, CancellationToken cancellationToken) =>
            await SetGrantAsync(organizationId, accountId, projectId, true, context, service,
                cancellationToken)).RequireManagementCsrf().WithName("GrantTeamProject")
            .WithSummary("Grant a project-scoped member access to an active project.");

        team.MapDelete("/members/{accountId:guid}/projects/{projectId:guid}", async (
            Guid organizationId, Guid accountId, Guid projectId, HttpContext context,
            ITeamService service, CancellationToken cancellationToken) =>
            await SetGrantAsync(organizationId, accountId, projectId, false, context, service,
                cancellationToken)).RequireManagementCsrf().WithName("RevokeTeamProject")
            .WithSummary("Remove an explicit project grant immediately.");
        return endpoints;
    }

    private static async Task<IResult> SetGrantAsync(Guid organizationId, Guid accountId,
        Guid projectId, bool enabled, HttpContext context, ITeamService service,
        CancellationToken cancellationToken)
    {
        if (!TryAccount(context, out var actorId)) return Results.Unauthorized();
        try
        {
            return await service.SetProjectGrantAsync(actorId, organizationId, accountId,
                projectId, enabled, cancellationToken) ? Results.NoContent() : Results.NotFound();
        }
        catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (InvalidOperationException) { return Results.Conflict(); }
    }

    private static TeamMemberResponse ToResponse(TeamMember member) => new(member.AccountId,
        member.Email, member.Role.ToString(), member.Status.ToString(), member.CreatedAt,
        member.ProjectIds);

    private static TeamInvitationResponse ToResponse(TeamInvitation invitation) => new(invitation.Id,
        invitation.Email, invitation.Role.ToString(), invitation.ExpiresAt,
        invitation.AcceptedAt, invitation.RevokedAt);

    private static bool TryAccount(HttpContext context, out Guid accountId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private static bool ParseRole(string? value, out OrganizationMemberRole role) =>
        Enum.TryParse(value, ignoreCase: false, out role) && Enum.IsDefined(role)
        && Enum.GetName(role) == value;

    private static IResult BadRole() => BadRequest("A valid role name is required.");

    private static IResult BadRequest(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["request"] = [message] });
}

/// <summary>Invite an email address into the organization with a non-owner role.</summary>
public sealed record InviteTeamMemberRequest(string Email, string Role);

/// <summary>Accept an invitation using the emailed one-time token.</summary>
public sealed record AcceptTeamInvitationRequest(string Token);

/// <summary>Change an active member's organization role.</summary>
public sealed record ChangeTeamRoleRequest(string Role);

/// <summary>Safe organization member view including explicit project grants.</summary>
public sealed record TeamMemberResponse(Guid AccountId, string Email, string Role,
    string Status, DateTimeOffset CreatedAt, IReadOnlyList<Guid> ProjectIds);

/// <summary>Safe invitation view without the token or its hash.</summary>
public sealed record TeamInvitationResponse(Guid Id, string Email, string Role,
    DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);
