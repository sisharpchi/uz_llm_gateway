using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Identity.Infrastructure;

public static class IdentityEmailEventTypes
{
    public const string Verification = "identity.email.verification";
    public const string PasswordRecovery = "identity.email.recovery";
}

/// <summary>Protects recipient and one-time token before they enter the durable outbox.</summary>
public sealed class IdentityEmailPayloadCodec(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("UZLLM.Identity.Email.v1");

    public string Protect(IdentityEmailNotification notification) =>
        JsonSerializer.Serialize(new ProtectedPayload(protector.Protect(
            JsonSerializer.Serialize(notification))));

    public IdentityEmailNotification Unprotect(string payload)
    {
        var envelope = JsonSerializer.Deserialize<ProtectedPayload>(payload)
            ?? throw new InvalidOperationException("Invalid protected email payload.");
        return JsonSerializer.Deserialize<IdentityEmailNotification>(protector.Unprotect(envelope.Ciphertext))
            ?? throw new InvalidOperationException("Invalid identity email notification.");
    }

    private sealed record ProtectedPayload(string Ciphertext);
}

public sealed class ProtectedIdentityNotificationQueue(IOutboxStore outbox,
    IdentityEmailPayloadCodec codec) : IIdentityNotificationQueue
{
    public async Task QueueAsync(IdentityEmailNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var eventType = notification.Kind switch
        {
            IdentityEmailKind.Verification => IdentityEmailEventTypes.Verification,
            IdentityEmailKind.PasswordRecovery => IdentityEmailEventTypes.PasswordRecovery,
            _ => throw new ArgumentOutOfRangeException(nameof(notification))
        };
        await outbox.EnqueueAsync(eventType, codec.Protect(notification),
            cancellationToken: cancellationToken);
    }
}
