using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public interface ITelegramMessageSender
{
    Task SendAsync(long chatId, string message, CancellationToken cancellationToken = default);
}

public sealed class TelegramBotMessageSender(HttpClient client, TelegramAlertOptions options) : ITelegramMessageSender
{
    public async Task SendAsync(long chatId, string message, CancellationToken cancellationToken = default)
    {
        options.RequireDeliveryConfiguration();
        // The Bot API requires the token in the URL. HttpClient tracing excludes this host.
        using var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{options.BotToken}/sendMessage",
            new { chat_id = chatId, text = message, disable_notification = false }, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            throw new TelegramDestinationUnavailableException();
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Telegram did not acknowledge alert delivery.");
    }
}

public sealed class TelegramDestinationUnavailableException : Exception;

public sealed class TelegramAlertOutboxHandler(FoundationDbContext db, IConsumerInboxStore inbox,
    ITelegramMessageSender sender, TelegramChatProtector chats, TimeProvider clock) : IOutboxHandler
{
    public string EventType => "customer.alert.telegram";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (await inbox.HasProcessedAsync("customer-telegram", message.Id, cancellationToken)) return;
        var payload = JsonSerializer.Deserialize<TelegramAlertPayload>(message.Payload)
            ?? throw new InvalidOperationException("Missing alert event ID.");
        var alertEvent = await db.CustomerAlertEvents.SingleOrDefaultAsync(value => value.Id == payload.EventId,
            cancellationToken) ?? throw new InvalidOperationException("Alert event not found.");
        if (alertEvent.Status != "Pending")
        {
            _ = await inbox.TryRecordProcessedAsync("customer-telegram", message.Id, cancellationToken);
            return;
        }
        var rule = await db.CustomerAlertRules.SingleAsync(value => value.Id == alertEvent.RuleId, cancellationToken);
        var destination = await db.NotificationDestinations.SingleAsync(value => value.Id == rule.DestinationId &&
            value.OrganizationId == rule.OrganizationId, cancellationToken);
        if (!rule.Enabled || destination.Status != "Verified")
        {
            alertEvent.Status = "Suppressed";
        }
        else
        {
            var chatId = chats.Unprotect(destination.OrganizationId, destination.Id,
                destination.EncryptedChatId, destination.KeyVersion);
            try
            {
                await sender.SendAsync(chatId, FormatMessage(rule, alertEvent), cancellationToken);
                alertEvent.Status = "Delivered";
                alertEvent.DeliveredAt = clock.GetUtcNow();
            }
            catch (TelegramDestinationUnavailableException)
            {
                destination.Status = "Disabled";
                alertEvent.Status = "Suppressed";
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        _ = await inbox.TryRecordProcessedAsync("customer-telegram", message.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string FormatMessage(CustomerAlertRuleEntity rule, CustomerAlertEventEntity alertEvent) => rule.Type switch
    {
        "LowBalance" => $"UZLLM: wallet available balance is {alertEvent.ObservedValue} micro-USD, at or below the {rule.Threshold} micro-USD alert threshold.",
        "BudgetWarning" => $"UZLLM: project budget use reached {alertEvent.ObservedValue / 100m:F2}% (alert threshold {rule.Threshold / 100m:F2}%).",
        "ErrorSpike" => $"UZLLM: recent request error rate reached {alertEvent.ObservedValue / 100m:F2}% (alert threshold {rule.Threshold / 100m:F2}%).",
        _ => throw new InvalidOperationException("Unknown customer alert type.")
    };

    private sealed record TelegramAlertPayload(Guid EventId);
}

public static class CustomerAlertServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmCustomerAlerts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new TelegramAlertOptions(configuration));
        services.AddSingleton<TelegramChatProtector>();
        services.AddScoped<CustomerAlertService>();
        services.AddScoped<CustomerAlertEvaluator>();
        services.AddHttpClient<ITelegramMessageSender, TelegramBotMessageSender>(client =>
            client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IOutboxHandler, TelegramAlertOutboxHandler>();
        return services;
    }
}
