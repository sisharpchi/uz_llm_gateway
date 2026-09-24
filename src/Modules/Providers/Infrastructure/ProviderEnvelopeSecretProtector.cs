using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Infrastructure;

public sealed class ProviderEnvelopeSecretProtector : IProviderSecretProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly string activeVersion;
    private readonly IReadOnlyDictionary<string, byte[]> keys;

    public ProviderEnvelopeSecretProtector(IConfiguration configuration)
    {
        activeVersion = configuration["ProviderSecrets:ActiveKeyVersion"]
            ?? throw new InvalidOperationException("ProviderSecrets:ActiveKeyVersion is required.");
        keys = configuration.GetSection("ProviderSecrets:Keys").GetChildren()
            .ToDictionary(child => child.Key, child => ParseKey(child.Value), StringComparer.Ordinal);
        if (!keys.ContainsKey(activeVersion))
            throw new InvalidOperationException("The active provider-secret key version is unavailable.");
    }

    public ProtectedProviderSecret Protect(Guid credentialId, Guid providerId, string secret)
    {
        ValidateIds(credentialId, providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            var aad = AssociatedData(credentialId, providerId, activeVersion);
            var encrypted = Encrypt(dataKey, plaintext, aad);
            var wrappedKey = Encrypt(keys[activeVersion], dataKey, aad);
            return new ProtectedProviderSecret(encrypted, wrappedKey, activeVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(Guid credentialId, Guid providerId, ProtectedProviderSecret protectedSecret)
    {
        ValidateIds(credentialId, providerId);
        ArgumentNullException.ThrowIfNull(protectedSecret);
        if (!keys.TryGetValue(protectedSecret.KeyVersion, out var key))
            throw new InvalidOperationException("The provider-secret key version is unavailable.");
        var aad = AssociatedData(credentialId, providerId, protectedSecret.KeyVersion);
        var dataKey = Decrypt(key, protectedSecret.WrappedDataKey, aad);
        try
        {
            if (dataKey.Length != 32) throw new CryptographicException("Invalid wrapped provider data key.");
            var plaintext = Decrypt(dataKey, protectedSecret.EncryptedSecret, aad);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { CryptographicOperations.ZeroMemory(dataKey); }
    }

    private static byte[] Encrypt(byte[] key, byte[] plaintext, byte[] aad)
    {
        var result = new byte[NonceLength + plaintext.Length + TagLength];
        RandomNumberGenerator.Fill(result.AsSpan(0, NonceLength));
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(result.AsSpan(0, NonceLength), plaintext,
            result.AsSpan(NonceLength, plaintext.Length), result.AsSpan(NonceLength + plaintext.Length, TagLength), aad);
        return result;
    }

    private static byte[] Decrypt(byte[] key, byte[] value, byte[] aad)
    {
        if (value.Length < NonceLength + TagLength)
            throw new CryptographicException("Invalid provider ciphertext.");
        var size = value.Length - NonceLength - TagLength;
        var result = new byte[size];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(value.AsSpan(0, NonceLength), value.AsSpan(NonceLength, size),
                value.AsSpan(NonceLength + size, TagLength), result, aad);
            return result;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(result);
            throw;
        }
    }

    private static byte[] AssociatedData(Guid credentialId, Guid providerId, string version) =>
        Encoding.UTF8.GetBytes($"uzllm:provider-secret:{credentialId:N}:{providerId:N}:{version}");

    private static void ValidateIds(Guid credentialId, Guid providerId)
    {
        if (credentialId == Guid.Empty || providerId == Guid.Empty)
            throw new ArgumentException("Credential and provider IDs are required.");
    }

    private static byte[] ParseKey(string? value)
    {
        try
        {
            var key = Convert.FromBase64String(value ?? string.Empty);
            if (key.Length == 32) return key;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("Provider-secret keys must be 32-byte base64 values.");
    }
}
