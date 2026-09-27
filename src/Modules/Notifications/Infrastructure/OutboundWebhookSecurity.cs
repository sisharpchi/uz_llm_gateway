using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed class OutboundWebhookOptions(IConfiguration configuration)
{
    public string? ActiveKeyVersion { get; } = configuration["OutboundWebhooks:ActiveKeyVersion"];
    public IReadOnlyDictionary<string, byte[]> Keys { get; } = configuration.GetSection("OutboundWebhooks:Keys")
        .GetChildren().ToDictionary(child => child.Key, child => ParseKey(child.Value), StringComparer.Ordinal);

    public (string Version, byte[] Key) ActiveKey()
    {
        if (ActiveKeyVersion is null || !Keys.TryGetValue(ActiveKeyVersion, out var key))
            throw new InvalidOperationException("Outbound webhook encryption key is not configured.");
        return (ActiveKeyVersion, key);
    }

    private static byte[] ParseKey(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length == 32) return bytes;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("Outbound webhook keys must be 32-byte base64 values.");
    }
}

public sealed class OutboundWebhookSecretProtector(OutboundWebhookOptions options)
{
    public (byte[] Ciphertext, string Version) Protect(Guid organizationId, Guid destinationId, byte[] secret)
    {
        if (secret.Length != 32) throw new ArgumentException("Webhook signing secret must be 32 bytes.");
        var (version, key) = options.ActiveKey();
        var ciphertext = new byte[12 + secret.Length + 16];
        RandomNumberGenerator.Fill(ciphertext.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(ciphertext.AsSpan(0, 12), secret, ciphertext.AsSpan(12, secret.Length),
            ciphertext.AsSpan(12 + secret.Length, 16), Aad(organizationId, destinationId, version));
        return (ciphertext, version);
    }

    public byte[] Unprotect(Guid organizationId, Guid destinationId, byte[] ciphertext, string version)
    {
        if (!options.Keys.TryGetValue(version, out var key))
            throw new InvalidOperationException("Outbound webhook key version is unavailable.");
        if (ciphertext.Length != 60) throw new CryptographicException("Invalid webhook signing secret.");
        var secret = new byte[32];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(12, 32), ciphertext.AsSpan(44, 16),
            secret, Aad(organizationId, destinationId, version));
        return secret;
    }

    private static byte[] Aad(Guid organizationId, Guid destinationId, string version) =>
        Encoding.UTF8.GetBytes($"uzllm:outbound-webhook:{organizationId:N}:{destinationId:N}:{version}");
}

public interface IWebhookAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemWebhookAddressResolver : IWebhookAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class WebhookEndpointPolicy(IWebhookAddressResolver resolver)
{
    public static Uri Parse(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (endpoint.Length is < 12 or > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.HostNameType != UriHostNameType.Dns ||
            uri.IdnHost.Length > 253 || uri.Host.Contains('_') || !uri.IsWellFormedOriginalString())
            throw new ArgumentException("Webhook endpoint must be a public HTTPS DNS URL on port 443 without credentials, query or fragment.");
        return uri;
    }

    public async Task<IPAddress> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await resolver.ResolveAsync(host, cancellationToken);
        // Reject mixed public/private DNS answers instead of selecting a seemingly safe answer.
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
            throw new ArgumentException("Webhook endpoint must resolve only to public addresses.");
        return addresses[0];
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
            return a is >= 1 and <= 223 && a != 10 && a != 127 &&
                !(a == 100 && b is >= 64 and <= 127) && !(a == 169 && b == 254) &&
                !(a == 172 && b is >= 16 and <= 31) && !(a == 192 && (b == 168 ||
                    (b == 88 && c == 99) ||
                    (b == 0 && c == 0) || (b == 0 && c == 2))) &&
                !(a == 198 && (b is 18 or 19 || b == 51 && c == 100)) &&
                !(a == 203 && b == 0 && c == 113);
        }
        // Public global-unicast only; exclude special-use 2001::/23, 6to4 and documentation.
        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xE0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 &&
                (bytes[2] <= 0x01 || bytes[2] == 0x0d && bytes[3] == 0xb8));
    }
}

public static class OutboundWebhookSignature
{
    public static string Sign(byte[] secret, Guid eventId, long timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp}.{eventId:D}.");
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return "v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, input));
    }

    public static bool Verify(byte[] secret, Guid eventId, long timestamp, ReadOnlySpan<byte> body,
        string signature, DateTimeOffset now)
    {
        var current = now.ToUnixTimeSeconds();
        if (eventId == Guid.Empty || timestamp < current - 300 || timestamp > current + 300 ||
            !signature.StartsWith("v1=", StringComparison.Ordinal) || signature.Length != 67) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Sign(secret, eventId, timestamp, body)[3..]),
                Convert.FromHexString(signature[3..]));
        }
        catch (FormatException) { return false; }
    }
}
