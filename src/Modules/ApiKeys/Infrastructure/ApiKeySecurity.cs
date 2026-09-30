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
    private readonly IReadOnlyDictionary<int, byte[]> keys;

    public HmacApiKeySecretFingerprint(byte[] key) : this(1, new Dictionary<int, byte[]> { [1] = key })
    {
    }

    public HmacApiKeySecretFingerprint(int activeKeyVersion, IReadOnlyDictionary<int, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (activeKeyVersion < 1 || !keys.ContainsKey(activeKeyVersion))
        {
            throw new ArgumentException("An active API-key HMAC version must be present in the key ring.", nameof(activeKeyVersion));
        }

        var copied = new Dictionary<int, byte[]>(keys.Count);
        foreach (var (version, key) in keys)
        {
            if (version < 1 || key is null || key.Length < 32)
                throw new ArgumentException("Every API-key HMAC version must be positive and at least 32 bytes.", nameof(keys));
            copied.Add(version, [.. key]);
        }
        this.keys = copied;
        ActiveKeyVersion = activeKeyVersion;
    }

    public int ActiveKeyVersion { get; }

    public byte[] Create(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return HMACSHA256.HashData(keys[ActiveKeyVersion], System.Text.Encoding.UTF8.GetBytes(secret));
    }

    public bool Matches(string secret, byte[] expectedFingerprint, int keyVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        if (!keys.TryGetValue(keyVersion, out var key)) return false;
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var actualFingerprint = HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(secret));
        return actualFingerprint.Length == expectedFingerprint.Length && CryptographicOperations.FixedTimeEquals(actualFingerprint, expectedFingerprint);
    }

    public static HmacApiKeySecretFingerprint FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredVersion = configuration["ApiKeys:ActiveKeyVersion"];
        if (!int.TryParse(configuredVersion ?? "1", out var activeVersion) || activeVersion < 1)
        {
            throw new InvalidOperationException("ApiKeys:ActiveKeyVersion must be a positive integer.");
        }
        var keys = new Dictionary<int, byte[]>();
        var legacy = configuration["ApiKeys:FingerprintKey"];
        if (!string.IsNullOrWhiteSpace(legacy)) keys.Add(1, DecodeKey(legacy, "ApiKeys:FingerprintKey"));
        foreach (var child in configuration.GetSection("ApiKeys:Keys").GetChildren())
        {
            if (!int.TryParse(child.Key, out var version) || version < 1)
                throw new InvalidOperationException("ApiKeys:Keys contains an invalid version.");
            var configuredKey = DecodeKey(child.Value, $"ApiKeys:Keys:{child.Key}");
            if (keys.TryGetValue(version, out var existing))
            {
                if (!CryptographicOperations.FixedTimeEquals(existing, configuredKey))
                    throw new InvalidOperationException("Legacy and versioned API-key HMAC configuration disagree for version 1.");
            }
            else keys.Add(version, configuredKey);
        }
        if (!keys.ContainsKey(activeVersion))
            throw new InvalidOperationException("The active API-key HMAC version is not configured.");
        return new HmacApiKeySecretFingerprint(activeVersion, keys);
    }

    private static byte[] DecodeKey(string? configuredKey, string path)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException($"{path} must be a Base64-encoded key of at least 32 bytes.");
        try
        {
            var decoded = Convert.FromBase64String(configuredKey);
            if (decoded.Length < 32)
                throw new InvalidOperationException($"{path} must contain at least 32 bytes.");
            return decoded;
        }
        catch (FormatException exception) { throw new InvalidOperationException($"{path} must be Base64 encoded.", exception); }
    }
}
