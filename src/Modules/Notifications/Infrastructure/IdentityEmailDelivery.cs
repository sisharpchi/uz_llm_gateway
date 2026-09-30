using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public interface IIdentityEmailSender
{
    Task SendAsync(Guid messageId, IdentityEmailNotification notification,
        CancellationToken cancellationToken = default);
}

public sealed record IdentitySmtpOptions(string Host, int Port, string FromAddress,
    string? Username, string? Password, Uri? DashboardBaseUrl = null)
{
    public static IdentitySmtpOptions FromConfiguration(IConfiguration configuration)
    {
        var host = configuration["Email:SmtpHost"]?.Trim();
        var from = configuration["Email:FromAddress"]?.Trim();
        var port = configuration.GetValue<int?>("Email:SmtpPort") ?? 587;
        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];
        var dashboardUrl = configuration["Email:DashboardBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)
            || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Email SMTP host, sender, port, and paired credentials are required.");
        _ = new MailAddress(from);
        Uri? dashboardBaseUrl = null;
        if (!string.IsNullOrWhiteSpace(dashboardUrl))
        {
            if (!Uri.TryCreate(dashboardUrl, UriKind.Absolute, out dashboardBaseUrl)
                || (dashboardBaseUrl.Scheme != Uri.UriSchemeHttps
                    && !(dashboardBaseUrl.Scheme == Uri.UriSchemeHttp && dashboardBaseUrl.IsLoopback))
                || !string.IsNullOrEmpty(dashboardBaseUrl.UserInfo)
                || !string.IsNullOrEmpty(dashboardBaseUrl.Query)
                || !string.IsNullOrEmpty(dashboardBaseUrl.Fragment))
                throw new InvalidOperationException("Email dashboard base URL must be HTTPS (or local HTTP) without credentials, query or fragment.");
        }
        return new IdentitySmtpOptions(host, port, from, username, password, dashboardBaseUrl);
    }
}

/// <summary>STARTTLS-only SMTP transport; no token, address or SMTP exception is logged here.</summary>
public sealed class SmtpIdentityEmailSender(IdentitySmtpOptions options) : IIdentityEmailSender
{
    public async Task SendAsync(Guid messageId, IdentityEmailNotification notification,
        CancellationToken cancellationToken = default)
    {
        var subject = notification.Kind switch
        {
            IdentityEmailKind.Verification => "Verify your UZLLM account",
            IdentityEmailKind.PasswordRecovery => "Reset your UZLLM password",
            IdentityEmailKind.TeamInvitation => "Your UZLLM team invitation",
            _ => throw new ArgumentOutOfRangeException(nameof(notification))
        };
        var from = new MailAddress(options.FromAddress);
        using var message = new MailMessage(from,
            new MailAddress(notification.Email))
        {
            Subject = subject,
            Body = BuildBody(options, notification),
            IsBodyHtml = false
        };
        message.Headers.Add("Message-ID", $"<{messageId:N}@{from.Host}>");
        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = options.Username is null ? null
                : new NetworkCredential(options.Username, options.Password)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await client.SendMailAsync(message, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Identity email SMTP send timed out."); }
    }

    public static string BuildBody(IdentitySmtpOptions options, IdentityEmailNotification notification)
    {
        var instruction = notification.Kind switch
        {
            IdentityEmailKind.Verification => "Enter this token on the UZLLM email verification page:",
            IdentityEmailKind.PasswordRecovery => "Use this token in the UZLLM password recovery flow:",
            IdentityEmailKind.TeamInvitation => "Sign in with this email and accept your team invitation using this token:",
            _ => throw new ArgumentOutOfRangeException(nameof(notification))
        };
        var route = notification.Kind switch
        {
            IdentityEmailKind.Verification => "verify-email",
            IdentityEmailKind.PasswordRecovery => "reset-password",
            _ => null
        };
        // The token is a fragment, not a query: it is not sent in HTTP requests or Referer headers.
        var link = route is null || options.DashboardBaseUrl is null ? string.Empty
            : $"\n\nOr open this link and confirm the action:\n{options.DashboardBaseUrl.ToString().TrimEnd('/')}/{route}#token={Uri.EscapeDataString(notification.Token)}";
        return $"{instruction}\n\n{notification.Token}{link}\n\nExpires at {notification.ExpiresAt:O} UTC. If you did not request this, ignore this email.";
    }
}

public sealed class IdentityEmailOutboxHandler(
    string eventType,
    IdentityEmailPayloadCodec codec,
    IIdentityEmailSender sender,
    IConsumerInboxStore inbox,
    TimeProvider clock) : IOutboxHandler
{
    private const string Consumer = "identity-email";
    public string EventType { get; } = eventType;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventType != EventType) throw new ArgumentException("Unexpected identity email event.");
        if (await inbox.HasProcessedAsync(Consumer, message.Id, cancellationToken)) return;
        var notification = codec.Unprotect(message.Payload);
        var expected = notification.Kind switch
        {
            IdentityEmailKind.Verification => IdentityEmailEventTypes.Verification,
            IdentityEmailKind.PasswordRecovery => IdentityEmailEventTypes.PasswordRecovery,
            IdentityEmailKind.TeamInvitation => IdentityEmailEventTypes.TeamInvitation,
            _ => throw new InvalidOperationException("Unknown email event kind.")
        };
        if (expected != EventType) throw new InvalidOperationException("Identity email event kind does not match.");
        if (clock.GetUtcNow() < notification.ExpiresAt)
            await sender.SendAsync(message.Id, notification, cancellationToken);
        _ = await inbox.TryRecordProcessedAsync(Consumer, message.Id, cancellationToken);
    }
}

public static class IdentityEmailServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmIdentityEmailDelivery(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(IdentitySmtpOptions.FromConfiguration(configuration));
        services.AddSingleton<IIdentityEmailSender, SmtpIdentityEmailSender>();
        services.AddSingleton<IdentityEmailPayloadCodec>();
        services.AddScoped<IOutboxHandler>(provider => new IdentityEmailOutboxHandler(
            IdentityEmailEventTypes.Verification,
            provider.GetRequiredService<IdentityEmailPayloadCodec>(),
            provider.GetRequiredService<IIdentityEmailSender>(),
            provider.GetRequiredService<IConsumerInboxStore>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IOutboxHandler>(provider => new IdentityEmailOutboxHandler(
            IdentityEmailEventTypes.PasswordRecovery,
            provider.GetRequiredService<IdentityEmailPayloadCodec>(),
            provider.GetRequiredService<IIdentityEmailSender>(),
            provider.GetRequiredService<IConsumerInboxStore>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IOutboxHandler>(provider => new IdentityEmailOutboxHandler(
            IdentityEmailEventTypes.TeamInvitation,
            provider.GetRequiredService<IdentityEmailPayloadCodec>(),
            provider.GetRequiredService<IIdentityEmailSender>(),
            provider.GetRequiredService<IConsumerInboxStore>(),
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
