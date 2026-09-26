using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed record ProtectedPayload(byte[] Ciphertext, byte[] WrappedKey, string KeyVersion);

public sealed class PayloadEnvelopeProtector(IConfiguration configuration)
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public void ValidateActiveKey() => _ = Key(ActiveVersion());

    public ProtectedPayload Protect(Guid organizationId, Guid projectId, Guid requestId,
        string purpose, ReadOnlySpan<byte> plaintext)
    {
        var version = ActiveVersion();
        var masterKey = Key(version);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var aad = Aad(organizationId, projectId, requestId, purpose, version);
            return new ProtectedPayload(Encrypt(dataKey, plaintext, aad),
                Encrypt(masterKey, dataKey, aad), version);
        }
        finally { CryptographicOperations.ZeroMemory(dataKey); }
    }

    public byte[] Unprotect(Guid organizationId, Guid projectId, Guid requestId,
        string purpose, ProtectedPayload payload)
    {
        var aad = Aad(organizationId, projectId, requestId, purpose, payload.KeyVersion);
        var dataKey = Decrypt(Key(payload.KeyVersion), payload.WrappedKey, aad);
        try
        {
            if (dataKey.Length != 32) throw new CryptographicException("Invalid payload data key.");
            return Decrypt(dataKey, payload.Ciphertext, aad);
        }
        finally { CryptographicOperations.ZeroMemory(dataKey); }
    }

    private string ActiveVersion() =>
        configuration["PayloadSecrets:ActiveKeyVersion"] is { Length: > 0 } version
            ? version : throw new InvalidOperationException("Payload encryption key version is unavailable.");

    private byte[] Key(string version)
    {
        var configured = configuration[$"PayloadSecrets:Keys:{version}"];
        if (configured is null) throw new InvalidOperationException("Payload encryption key is unavailable.");
        byte[] key;
        try { key = Convert.FromBase64String(configured); }
        catch (FormatException) { throw new InvalidOperationException("Payload encryption key is invalid."); }
        if (key.Length != 32) throw new InvalidOperationException("Payload encryption key must be 32 bytes.");
        return key;
    }

    private static byte[] Aad(Guid organizationId, Guid projectId, Guid requestId,
        string purpose, string version) => Encoding.UTF8.GetBytes(
        $"uzllm:payload:{organizationId:N}:{projectId:N}:{requestId:N}:{purpose}:{version}");

    private static byte[] Encrypt(byte[] key, ReadOnlySpan<byte> plaintext, byte[] aad)
    {
        var value = new byte[NonceLength + plaintext.Length + TagLength];
        RandomNumberGenerator.Fill(value.AsSpan(0, NonceLength));
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(value.AsSpan(0, NonceLength), plaintext,
            value.AsSpan(NonceLength, plaintext.Length), value.AsSpan(NonceLength + plaintext.Length, TagLength), aad);
        return value;
    }

    private static byte[] Decrypt(byte[] key, byte[] value, byte[] aad)
    {
        if (value.Length < NonceLength + TagLength) throw new CryptographicException("Invalid payload ciphertext.");
        var size = value.Length - NonceLength - TagLength;
        var plaintext = new byte[size];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(value.AsSpan(0, NonceLength), value.AsSpan(NonceLength, size),
                value.AsSpan(NonceLength + size, TagLength), plaintext, aad);
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }
}
