using System.Security.Claims;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Payments.Contracts;

namespace UZLLM.Modules.Payments.Infrastructure;

public static class PaymentEndpointExtensions
{
    public static IEndpointRouteBuilder MapUzllmPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var billing = endpoints.MapGroup("/management/v1/organizations/{organizationId:guid}/billing");
        billing.MapPost("/quotes", async (Guid organizationId, QuoteRequest request, HttpContext context,
            IPaymentService payments, CancellationToken token) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            if (!Enum.TryParse<PaymentProvider>(request.Provider, true, out var provider)
                || !Enum.IsDefined(provider)
                || !long.TryParse(request.AmountTiyin, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var amountTiyin) || amountTiyin <= 0)
                return Results.BadRequest(new { error = "invalid_provider_or_amount" });
            try { return Results.Ok(Quote(await payments.CreateQuoteAsync(accountId, organizationId,
                provider, new UzsTiyinAmount(amountTiyin), token))); }
            catch (TenantAccessDeniedException) { return Results.Forbid(); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_amount" }); }
            catch (InvalidOperationException) { return Results.Problem("Payment quote is unavailable.", statusCode: 503); }
        }).RequireManagementCsrf();

        billing.MapPost("/topups", async (Guid organizationId, TopUpRequest request, HttpContext context,
            IPaymentService payments, CancellationToken token) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key)) return Results.BadRequest(new { error = "idempotency_key_required" });
            try
            {
                var result = await payments.CreateIntentAsync(accountId, organizationId, request.QuoteId, key, token);
                var body = new { intent = Intent(result.Intent), result.Duplicate, result.CheckoutUrl };
                return result.Duplicate ? Results.Ok(body) : Results.Created(
                    $"/management/v1/organizations/{organizationId}/billing/topups/{result.Intent.Id}", body);
            }
            catch (TenantAccessDeniedException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_quote_or_key" }); }
            catch (InvalidOperationException) { return Results.Conflict(new { error = "quote_or_merchant_unavailable" }); }
        }).RequireManagementCsrf();

        billing.MapGet("/topups", async (Guid organizationId, HttpContext context,
            IPaymentService payments, CancellationToken token) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return Results.Ok((await payments.ListIntentsAsync(accountId, organizationId, token)).Select(Intent)); }
            catch (TenantAccessDeniedException) { return Results.Forbid(); }
        });

        billing.MapGet("/topups/{intentId:guid}", async (Guid organizationId, Guid intentId,
            HttpContext context, IPaymentService payments, CancellationToken token) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try { return await payments.GetIntentAsync(accountId, organizationId, intentId, token) is { } intent
                ? Results.Ok(Intent(intent)) : Results.NotFound(); }
            catch (TenantAccessDeniedException) { return Results.Forbid(); }
        });

        billing.MapGet("/wallet", async (Guid organizationId, HttpContext context,
            IOrganizationAuthorizationService authorization, IWalletLedgerService wallet, CancellationToken token) =>
        {
            if (!AccountId(context, out var accountId)) return Results.Unauthorized();
            try
            {
                await authorization.EnsureOwnerAsync(accountId, organizationId, token);
                return await wallet.GetWalletAsync(organizationId, token) is { } value
                    ? Results.Ok(new { value.OrganizationId,
                        postedBalanceMicroUsd = value.PostedBalance.Value.ToString(CultureInfo.InvariantCulture),
                        reservedBalanceMicroUsd = value.ReservedBalance.Value.ToString(CultureInfo.InvariantCulture),
                        availableBalanceMicroUsd = value.AvailableBalance.Value.ToString(CultureInfo.InvariantCulture),
                        value.Version }) : Results.NotFound();
            }
            catch (TenantAccessDeniedException) { return Results.Forbid(); }
        });

        endpoints.MapPost("/payments/payme/callback", async (HttpContext context,
            PaymeMerchantApi payme, CancellationToken token) =>
        {
            var body = await ReadBoundedBodyAsync(context, token);
            if (body is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var response = await payme.HandleAsync(context.Request.Headers.Authorization, body, token);
            return Results.Json(response);
        });

        endpoints.MapPost("/payments/click/callback", async (HttpContext context,
            ClickShopApi click, CancellationToken token) =>
        {
            var body = await ReadBoundedBodyAsync(context, token);
            if (body is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!context.Request.HasFormContentType)
                return Results.Json(new ClickShopResponse(null, null, null, null, -8, "Error in request from click"));
            var parsed = QueryHelpers.ParseQuery(body);
            if (parsed.Values.Any(value => value.Count != 1))
                return Results.Json(new ClickShopResponse(null, null, null, null, -8, "Error in request from click"));
            var fields = parsed.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.Ordinal);
            if (!int.TryParse(fields.GetValueOrDefault("action"), out var action)) action = -1;
            return Results.Json(await click.HandleAsync(fields, body, action, token));
        });
        return endpoints;
    }

    private static bool AccountId(HttpContext context, out Guid accountId) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private static async Task<string?> ReadBoundedBodyAsync(HttpContext context, CancellationToken token)
    {
        const int limit = 32_768;
        if (context.Request.ContentLength > limit) return null;
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, token)) > 0)
        {
            if (stream.Length + count > limit) return null;
            await stream.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static object Quote(PaymentQuote value) => new
    {
        value.Id, value.OrganizationId, provider = value.Provider.ToString(),
        amountTiyin = value.Amount.Value.ToString(CultureInfo.InvariantCulture),
        feeTiyin = value.Fee.Value.ToString(CultureInfo.InvariantCulture),
        creditMicroUsd = value.Credit.Value.ToString(CultureInfo.InvariantCulture),
        value.FxSnapshotId,
        uzsTiyinPerUsd = value.UzsTiyinPerUsd.ToString(CultureInfo.InvariantCulture),
        value.CreatedAt, value.ExpiresAt
    };

    private static object Intent(PaymentIntent value) => new
    {
        value.Id, value.OrganizationId, provider = value.Provider.ToString(),
        value.QuoteId, status = value.Status.ToString(),
        amountTiyin = value.Amount.Value.ToString(CultureInfo.InvariantCulture),
        feeTiyin = value.Fee.Value.ToString(CultureInfo.InvariantCulture),
        creditMicroUsd = value.Credit.Value.ToString(CultureInfo.InvariantCulture),
        value.FxSnapshotId,
        uzsTiyinPerUsd = value.UzsTiyinPerUsd.ToString(CultureInfo.InvariantCulture),
        value.ExternalTransactionId, value.CreatedAt, value.BoundAt, value.PaidAt, value.CanceledAt
    };

    private sealed record QuoteRequest(string Provider, string AmountTiyin);
    private sealed record TopUpRequest(Guid QuoteId);
}
