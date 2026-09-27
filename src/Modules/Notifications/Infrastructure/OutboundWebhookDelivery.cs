using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed record WebhookDestinationCreated(Guid Id, string EndpointUrl, string SigningSecret);
public sealed class WebhookDestinationAlreadyExistsException : Exception;

public sealed class OutboundWebhookService(FoundationDbContext db, IOrganizationAuthorizationService authorization,
    WebhookEndpointPolicy endpoints, OutboundWebhookSecretProtector secrets, IAuditTrail audit, TimeProvider clock)
{
    public async Task<WebhookDestinationCreated> CreateAsync(Guid accountId, Guid organizationId, string endpointUrl,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageBilling, cancellationToken: cancellationToken);
        var uri = WebhookEndpointPolicy.Parse(endpointUrl);
        _ = await endpoints.ResolvePublicAsync(uri.IdnHost, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await db.Set<OrganizationEntity>()
            .FromSqlInterpolated($"SELECT * FROM org.organization WHERE id = {organizationId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var existing = await db.NotificationDestinations.SingleOrDefaultAsync(value =>
            value.OrganizationId == organizationId && value.Type == "Webhook", cancellationToken);
        if (existing is { Status: "Active" }) throw new WebhookDestinationAlreadyExistsException();
        var destination = existing ?? new NotificationDestinationEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, Type = "Webhook",
            CreatedAt = clock.GetUtcNow()
        };
        var signingSecret = RandomNumberGenerator.GetBytes(32);
        try
        {
            var encrypted = secrets.Protect(organizationId, destination.Id, signingSecret);
            destination.EndpointUrl = uri.AbsoluteUri;
            destination.EncryptedWebhookSecret = encrypted.Ciphertext;
            destination.WebhookKeyVersion = encrypted.Version;
            destination.Status = "Active";
            destination.VerifiedAt = clock.GetUtcNow();
            if (existing is null) db.NotificationDestinations.Add(destination);
            await db.SaveChangesAsync(cancellationToken);
            _ = await audit.RecordAsync(new AuditEventInput(organizationId, accountId,
                "notification.webhook.created", "notification_destination", destination.Id, null, "{}"), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WebhookDestinationCreated(destination.Id, destination.EndpointUrl,
                Convert.ToHexStringLower(signingSecret));
        }
        finally { CryptographicOperations.ZeroMemory(signingSecret); }
    }
}

public interface IOutboundWebhookSender
{
    Task SendAsync(Uri endpoint, byte[] secret, Guid eventId, byte[] body,
        CancellationToken cancellationToken);
}

public sealed class OutboundWebhookPermanentFailureException : Exception;

public sealed class HttpOutboundWebhookSender(HttpClient client, WebhookEndpointPolicy endpoints, TimeProvider clock)
    : IOutboundWebhookSender
{
    public async Task SendAsync(Uri endpoint, byte[] secret, Guid eventId, byte[] body,
        CancellationToken cancellationToken)
    {
        _ = WebhookEndpointPolicy.Parse(endpoint.AbsoluteUri);
        _ = await endpoints.ResolvePublicAsync(endpoint.IdnHost, cancellationToken);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("X-UZLLM-Event-Id", eventId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-UZLLM-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-UZLLM-Signature",
            OutboundWebhookSignature.Sign(secret, eventId, timestamp, body));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new HttpRequestException("Outbound webhook transient response.", null, response.StatusCode);
        throw new OutboundWebhookPermanentFailureException();
    }
}

public sealed class OutboundWebhookAlertHandler(FoundationDbContext db, IConsumerInboxStore inbox,
    IOutboundWebhookSender sender, OutboundWebhookSecretProtector secrets, TimeProvider clock) : IOutboxHandler
{
    public string EventType => "customer.alert.webhook";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (await inbox.HasProcessedAsync("customer-webhook", message.Id, cancellationToken)) return;
        var payload = JsonSerializer.Deserialize<WebhookAlertPayload>(message.Payload)
            ?? throw new InvalidOperationException("Missing alert event ID.");
        var alertEvent = await db.CustomerAlertEvents.SingleAsync(value => value.Id == payload.EventId,
            cancellationToken);
        if (alertEvent.Status != "Pending")
        {
            _ = await inbox.TryRecordProcessedAsync("customer-webhook", message.Id, cancellationToken);
            return;
        }
        var rule = await db.CustomerAlertRules.SingleAsync(value => value.Id == alertEvent.RuleId,
            cancellationToken);
        var destination = await db.NotificationDestinations.SingleAsync(value => value.Id == rule.DestinationId &&
            value.OrganizationId == rule.OrganizationId && value.Type == "Webhook", cancellationToken);
        if (!rule.Enabled || destination.Status != "Active") alertEvent.Status = "Suppressed";
        else
        {
            // Only non-sensitive, immutable alert evidence enters the signed body.
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = alertEvent.Id, type = "customer.alert.triggered", version = 1,
                organizationId = rule.OrganizationId, projectId = rule.ProjectId, ruleId = rule.Id,
                alertType = rule.Type, threshold = rule.Threshold, observedValue = alertEvent.ObservedValue,
                triggeredAt = alertEvent.TriggeredAt
            });
            var secret = secrets.Unprotect(destination.OrganizationId, destination.Id,
                destination.EncryptedWebhookSecret!, destination.WebhookKeyVersion!);
            try
            {
                await sender.SendAsync(WebhookEndpointPolicy.Parse(destination.EndpointUrl!), secret,
                    alertEvent.Id, body, cancellationToken);
                alertEvent.Status = "Delivered";
                alertEvent.DeliveredAt = clock.GetUtcNow();
            }
            catch (OutboundWebhookPermanentFailureException)
            {
                destination.Status = "Disabled";
                alertEvent.Status = "Suppressed";
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        _ = await inbox.TryRecordProcessedAsync("customer-webhook", message.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record WebhookAlertPayload(Guid EventId);
}

public static class OutboundWebhookServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmOutboundWebhooks(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(new OutboundWebhookOptions(configuration));
        services.AddSingleton<OutboundWebhookSecretProtector>();
        services.AddSingleton<IWebhookAddressResolver, SystemWebhookAddressResolver>();
        services.AddSingleton<WebhookEndpointPolicy>();
        services.AddScoped<OutboundWebhookService>();
        services.AddHttpClient<IOutboundWebhookSender, HttpOutboundWebhookSender>(client =>
            client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var endpoints = provider.GetRequiredService<WebhookEndpointPolicy>();
                return new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                    ConnectCallback = async (context, cancellationToken) =>
                    {
                        if (context.DnsEndPoint.Port != 443)
                            throw new InvalidOperationException("Outbound webhook port is not allowed.");
                        var address = await endpoints.ResolvePublicAsync(context.DnsEndPoint.Host, cancellationToken);
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(address, 443, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch { socket.Dispose(); throw; }
                    }
                };
            })
            .RemoveAllLoggers();
        services.AddScoped<IOutboxHandler, OutboundWebhookAlertHandler>();
        return services;
    }
}
