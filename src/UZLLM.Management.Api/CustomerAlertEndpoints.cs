using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Management.Api;

public static class CustomerAlertEndpoints
{
    public static IEndpointRouteBuilder MapUzllmCustomerAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/alerts");
        group.MapGet("/rules", async (Guid organizationId, HttpContext context, CustomerAlertService service,
            CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return Results.Ok(await service.ListRulesAsync(accountId, organizationId, cancellationToken)); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).WithName("ListCustomerAlertRules");
        group.MapPost("/rules", async (Guid organizationId, CreateAlertRuleRequest request, HttpContext context,
            CustomerAlertService service, CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try
            {
                var result = await service.CreateRuleAsync(accountId, organizationId, request.ProjectId,
                    request.BudgetPolicyId, request.DestinationId, request.Type, request.Threshold, cancellationToken);
                return Results.Created($"/management/v1/organizations/{organizationId}/alerts/rules/{result.Id}", result);
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (ArgumentException) { return Results.BadRequest(new { Error = "Invalid alert type, scope or threshold." }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireManagementCsrf().WithName("CreateCustomerAlertRule");
        group.MapPatch("/rules/{ruleId:guid}", async (Guid organizationId, Guid ruleId,
            SetAlertRuleEnabledRequest request, HttpContext context, CustomerAlertService service,
            CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return await service.SetRuleEnabledAsync(accountId, organizationId, ruleId, request.Enabled,
                cancellationToken) ? Results.NoContent() : Results.NotFound(); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).RequireManagementCsrf().WithName("SetCustomerAlertRuleEnabled");
        group.MapGet("/destinations", async (Guid organizationId, HttpContext context, CustomerAlertService service,
            CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return Results.Ok(await service.ListDestinationsAsync(accountId, organizationId, cancellationToken)); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).WithName("ListCustomerAlertDestinations");
        group.MapPost("/telegram/link", async (Guid organizationId, HttpContext context,
            CustomerAlertService service, CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try
            {
                var link = await service.BeginTelegramLinkAsync(accountId, organizationId, cancellationToken);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(link);
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (InvalidOperationException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        }).RequireManagementCsrf().WithName("BeginTelegramAlertLink");
        group.MapDelete("/destinations/{destinationId:guid}", async (Guid organizationId, Guid destinationId,
            HttpContext context, CustomerAlertService service, CancellationToken cancellationToken) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return await service.DisableDestinationAsync(accountId, organizationId, destinationId,
                cancellationToken) ? Results.NoContent() : Results.NotFound(); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).RequireManagementCsrf().WithName("DisableCustomerAlertDestination");

        endpoints.MapPost("/integrations/telegram/webhook", async (HttpContext context,
            TelegramAlertOptions options, CustomerAlertService service, CancellationToken cancellationToken) =>
        {
            if (!options.ValidWebhookSecret(context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"]))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = 16384;
            if (context.Request.ContentLength is > 16384) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            try
            {
                using var payload = await JsonDocument.ParseAsync(context.Request.Body,
                    new JsonDocumentOptions { MaxDepth = 10 }, cancellationToken);
                var root = payload.RootElement;
                if (root.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("chat", out var chat) &&
                    chat.TryGetProperty("type", out var chatType) && chatType.GetString() == "private" &&
                    chat.TryGetProperty("id", out var chatId) && chatId.TryGetInt64(out var id) && id > 0 &&
                    message.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    var value = text.GetString() ?? "";
                    if (value.StartsWith("/start ", StringComparison.Ordinal) && value.Length == 50)
                        _ = await service.CompleteTelegramLinkAsync(value[7..], id, cancellationToken);
                }
            }
            catch (JsonException) { /* malformed updates are ignored, not retried forever */ }
            return Results.Ok();
        }).WithName("TelegramAlertWebhook");
        return endpoints;
    }

    private static bool AccountId(HttpContext context, out Guid accountId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);
}

public sealed record CreateAlertRuleRequest(string Type, long Threshold, Guid DestinationId,
    Guid? ProjectId, Guid? BudgetPolicyId);
public sealed record SetAlertRuleEnabledRequest(bool Enabled);
