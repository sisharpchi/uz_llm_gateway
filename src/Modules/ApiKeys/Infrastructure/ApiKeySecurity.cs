using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public sealed class GatewayApiKeySecretGenerator : IApiKeySecretGenerator
{
    private const int PublicPrefixByteLength = 6;
    private const int SecretByteLength = 32;

    public ApiKeySecret Create()
    {
        var publicPrefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(PublicPrefixByteLength)).ToLowerInvariant();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretByteLength)).ToLowerInvariant();
        return new ApiKeySecret($"uzllm_live_{publicPrefix}_{secret}", publicPrefix);
    }

    public bool TryParse(string value, out ApiKeySecret secret)
    {
        secret = default!;
        var parts = value.Trim().Split('_', StringSplitOptions.None);
        if (parts.Length != 4 || !string.Equals(parts[0], "uzllm", StringComparison.Ordinal) || !string.Equals(parts[1], "live", StringComparison.Ordinal) || !IsLowerHex(parts[2], PublicPrefixByteLength * 2) || !IsLowerHex(parts[3], SecretByteLength * 2))
        {
            return false;
        }

        secret = new ApiKeySecret(value.Trim(), parts[2]);
        return true;
    }

    private static bool IsLowerHex(string value, int expectedLength) => value.Length == expectedLength && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
}

public sealed class HmacApiKeySecretFingerprint : IApiKeySecretFingerprint
{
    private readonly byte[] key;

    public HmacApiKeySecretFingerprint(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("The API-key HMAC key must be at least 32 bytes.", nameof(key));
        }

        this.key = [.. key];
    }

    public byte[] Create(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(secret));
    }

    public bool Matches(string secret, byte[] expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        var actualFingerprint = Create(secret);
        return actualFingerprint.Length == expectedFingerprint.Length && CryptographicOperations.FixedTimeEquals(actualFingerprint, expectedFingerprint);
    }

    public static HmacApiKeySecretFingerprint FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredKey = configuration["ApiKeys:FingerprintKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException("ApiKeys:FingerprintKey must be configured with a Base64-encoded key.");
        }

        try
        {
            return new HmacApiKeySecretFingerprint(Convert.FromBase64String(configuredKey));
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("ApiKeys:FingerprintKey must be Base64 encoded.", exception);
        }
    }
}
