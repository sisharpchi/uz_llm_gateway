using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed class TelegramAlertOptions(IConfiguration configuration)
{
    public string? BotUsername { get; } = configuration["Telegram:BotUsername"]?.Trim().TrimStart('@');
    public string? BotToken { get; } = configuration["Telegram:BotToken"];
    public string? WebhookSecret { get; } = configuration["Telegram:WebhookSecret"];
    public string? ActiveKeyVersion { get; } = configuration["Telegram:ActiveChatKeyVersion"];
    public IReadOnlyDictionary<string, byte[]> ChatKeys { get; } = configuration.GetSection("Telegram:ChatKeys")
        .GetChildren().ToDictionary(child => child.Key, child => ParseKey(child.Value), StringComparer.Ordinal);

    public void RequireLinkConfiguration()
    {
        if (string.IsNullOrWhiteSpace(BotUsername) || string.IsNullOrWhiteSpace(WebhookSecret)
            || string.IsNullOrWhiteSpace(ActiveKeyVersion) || !ChatKeys.ContainsKey(ActiveKeyVersion))
            throw new InvalidOperationException("Telegram bot username, webhook secret and active chat key are required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(BotUsername, "^[A-Za-z0-9_]{5,32}$"))
            throw new InvalidOperationException("Telegram bot username is invalid.");
        if (WebhookSecret.Length is < 16 or > 256 || WebhookSecret.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new InvalidOperationException("Telegram webhook secret must be a 16-256 character URL-safe token.");
    }

    public void RequireDeliveryConfiguration()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("Telegram bot token is required for alert delivery.");
    }

    public bool ValidWebhookSecret(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(WebhookSecret) || supplied is null) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] ParseKey(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length == 32) return bytes;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("Telegram chat keys must be 32-byte base64 values.");
    }
}

public sealed class TelegramChatProtector(TelegramAlertOptions options)
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public (byte[] Ciphertext, string Version) Protect(Guid organizationId, Guid destinationId, long chatId)
    {
        options.RequireLinkConfiguration();
        var version = options.ActiveKeyVersion!;
        var key = options.ChatKeys[version];
        var plain = Encoding.UTF8.GetBytes(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var encrypted = new byte[NonceLength + plain.Length + TagLength];
        RandomNumberGenerator.Fill(encrypted.AsSpan(0, NonceLength));
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(encrypted.AsSpan(0, NonceLength), plain,
            encrypted.AsSpan(NonceLength, plain.Length), encrypted.AsSpan(NonceLength + plain.Length, TagLength),
            AssociatedData(organizationId, destinationId, version));
        CryptographicOperations.ZeroMemory(plain);
        return (encrypted, version);
    }

    public long Unprotect(Guid organizationId, Guid destinationId, byte[] ciphertext, string version)
    {
        if (!options.ChatKeys.TryGetValue(version, out var key))
            throw new InvalidOperationException("Telegram chat key version is unavailable.");
        if (ciphertext.Length < NonceLength + TagLength + 1) throw new CryptographicException("Invalid encrypted chat ID.");
        var length = ciphertext.Length - NonceLength - TagLength;
        var plain = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(ciphertext.AsSpan(0, NonceLength), ciphertext.AsSpan(NonceLength, length),
                ciphertext.AsSpan(NonceLength + length, TagLength), plain,
                AssociatedData(organizationId, destinationId, version));
            return long.Parse(Encoding.UTF8.GetString(plain), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] AssociatedData(Guid organizationId, Guid destinationId, string version) =>
        Encoding.UTF8.GetBytes($"uzllm:telegram-chat:{organizationId:N}:{destinationId:N}:{version}");
}
