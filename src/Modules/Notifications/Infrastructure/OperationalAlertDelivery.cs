using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed record OperationalAlertRecipientOptions(string Email)
{
    public static OperationalAlertRecipientOptions FromConfiguration(IConfiguration configuration)
    {
        var address = configuration["Operations:AlertEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Operations:AlertEmail is required for Worker alert delivery.");
        try
        {
            var parsed = new MailAddress(address);
            if (!string.Equals(parsed.Address, address, StringComparison.Ordinal) || !string.IsNullOrEmpty(parsed.DisplayName))
                throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Operations:AlertEmail must be a single mailbox address.", exception);
        }
        return new OperationalAlertRecipientOptions(address);
    }
}

public interface IOperationalAlertSender
{
    Task SendAsync(Guid eventId, OperationalAlertDelivery alert, CancellationToken cancellationToken = default);
}

/// <summary>STARTTLS operator notification with a stable Message-ID; alert details never leave the database.</summary>
public sealed class SmtpOperationalAlertSender(IdentitySmtpOptions smtp, OperationalAlertRecipientOptions recipient)
    : IOperationalAlertSender
{
    public async Task SendAsync(Guid eventId, OperationalAlertDelivery alert,
        CancellationToken cancellationToken = default)
    {
        using var message = CreateMessage(eventId, alert, smtp, recipient);
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = smtp.Username is null ? null : new NetworkCredential(smtp.Username, smtp.Password)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await client.SendMailAsync(message, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Operational alert SMTP send timed out."); }
    }

    public static MailMessage CreateMessage(Guid eventId, OperationalAlertDelivery alert,
        IdentitySmtpOptions smtp, OperationalAlertRecipientOptions recipient)
    {
        if (!Enum.TryParse<OperationalAlertKind>(alert.Kind, out var kind) || !Enum.IsDefined(kind))
            throw new InvalidOperationException("Unknown operational alert kind.");
        var from = new MailAddress(smtp.FromAddress);
        var message = new MailMessage(from, new MailAddress(recipient.Email))
        {
            Subject = $"UZLLM critical alert: {kind}",
            Body = $"Operational alert {alert.Id:N}\nKind: {kind}\nOccurred: {alert.OccurredAt:O}\nReview in the MFA-protected operator console. Do not reply with credentials or customer data.",
            IsBodyHtml = false
        };
        message.Headers.Add("Message-ID", $"<{eventId:N}@{from.Host}>");
        return message;
    }
}

public sealed class OperationalAlertOutboxHandler(IOperationalAlertDeliveryStore alerts,
    IOperationalAlertSender sender, IConsumerInboxStore inbox, ITransactionCoordinator transactions,
    TimeProvider clock) : IOutboxHandler
{
    private const string Consumer = "operator-alert-email";
    public string EventType => "ops.alert.raised";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventType != EventType) throw new ArgumentException("Unexpected operational alert event.");
        if (await inbox.HasProcessedAsync(Consumer, message.Id, cancellationToken)) return;
        using var document = JsonDocument.Parse(message.Payload);
        var alertId = document.RootElement.GetProperty("Id").GetGuid();
        var alert = await alerts.FindAsync(alertId, cancellationToken)
            ?? throw new InvalidOperationException("Operational alert was not found.");
        if (alert.NotificationEventId is Guid expectedEventId && expectedEventId != message.Id)
            throw new InvalidOperationException("Operational alert event ID does not match.");

        // No database transaction spans SMTP. A remote accept immediately before a crash can
        // still cause a resend; the stable Message-ID helps downstream deduplication.
        if (alert.NotifiedAt is null) await sender.SendAsync(message.Id, alert, cancellationToken);

        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (alert.NotifiedAt is null)
            _ = await alerts.MarkNotifiedAsync(alertId, clock.GetUtcNow(), cancellationToken);
        _ = await inbox.TryRecordProcessedAsync(Consumer, message.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public static class OperationalAlertDeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmOperationalAlertDelivery(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(OperationalAlertRecipientOptions.FromConfiguration(configuration));
        services.AddSingleton<IOperationalAlertSender, SmtpOperationalAlertSender>();
        services.AddScoped<IOutboxHandler, OperationalAlertOutboxHandler>();
        return services;
    }
}
