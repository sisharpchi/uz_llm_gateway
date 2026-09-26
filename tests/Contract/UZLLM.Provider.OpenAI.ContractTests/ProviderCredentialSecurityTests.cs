using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using UZLLM.Modules.Providers.Infrastructure;

namespace UZLLM.Provider.OpenAI.ContractTests;

public sealed class ProviderCredentialSecurityTests
{
    [Fact]
    public void Secret_is_encrypted_bound_to_identity_and_survives_key_rotation()
    {
        var firstKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var nextKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var credentialId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var first = CreateProtector("v1", firstKey, nextKey);
        var secret = "sk-platform-secret-test";

        var protectedSecret = first.Protect(credentialId, providerId, secret);

        Assert.Equal("v1", protectedSecret.KeyVersion);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(protectedSecret.EncryptedSecret));
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(protectedSecret.WrappedDataKey));
        Assert.Equal(secret, first.Unprotect(credentialId, providerId, protectedSecret));
        Assert.Equal(secret, CreateProtector("v2", firstKey, nextKey)
            .Unprotect(credentialId, providerId, protectedSecret));
        Assert.ThrowsAny<CryptographicException>(() => first.Unprotect(Guid.NewGuid(), providerId, protectedSecret));
        Assert.ThrowsAny<CryptographicException>(() => first.Unprotect(credentialId, Guid.NewGuid(), protectedSecret));
        protectedSecret.EncryptedSecret[protectedSecret.EncryptedSecret.Length - 1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => first.Unprotect(credentialId, providerId, protectedSecret));
    }

    [Fact]
    public void Secret_encryption_requires_a_valid_active_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecrets:ActiveKeyVersion"] = "v1",
            ["ProviderSecrets:Keys:v1"] = "short"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ProviderEnvelopeSecretProtector(config));
    }

    [Fact]
    public void Byok_secret_is_tenant_provider_bound_and_key_version_survives_rotation()
    {
        var firstKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var nextKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tenantId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var oldProtector = CreateProtector("v1", firstKey, nextKey);
        var ciphertext = oldProtector.ProtectForOrganization(tenantId, credentialId,
            providerId, "sk-tenant-secret-012345");

        Assert.Equal("v1", ciphertext.KeyVersion);
        Assert.DoesNotContain("sk-tenant-secret-012345",
            Encoding.UTF8.GetString(ciphertext.EncryptedSecret));
        Assert.Equal("sk-tenant-secret-012345", CreateProtector("v2", firstKey, nextKey)
            .UnprotectForOrganization(tenantId, credentialId, providerId, ciphertext));
        var rotated = CreateProtector("v2", firstKey, nextKey).ProtectForOrganization(
            tenantId, Guid.NewGuid(), providerId, "sk-new-key-version-987654");
        Assert.Equal("v2", rotated.KeyVersion);
        Assert.ThrowsAny<CryptographicException>(() => oldProtector.UnprotectForOrganization(
            Guid.NewGuid(), credentialId, providerId, ciphertext));
        Assert.ThrowsAny<CryptographicException>(() => oldProtector.UnprotectForOrganization(
            tenantId, credentialId, Guid.NewGuid(), ciphertext));
        Assert.ThrowsAny<CryptographicException>(() => oldProtector.UnprotectForOrganization(
            tenantId, Guid.NewGuid(), providerId, ciphertext));
        Assert.ThrowsAny<CryptographicException>(() => oldProtector.Unprotect(
            credentialId, providerId, ciphertext));
        var wrongKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(() => CreateProtector("v1", wrongKey, nextKey)
            .UnprotectForOrganization(tenantId, credentialId, providerId, ciphertext));
        ciphertext.WrappedDataKey[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => oldProtector.UnprotectForOrganization(
            tenantId, credentialId, providerId, ciphertext));
    }

    private static ProviderEnvelopeSecretProtector CreateProtector(string active, string first, string next)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProviderSecrets:ActiveKeyVersion"] = active,
            ["ProviderSecrets:Keys:v1"] = first,
            ["ProviderSecrets:Keys:v2"] = next
        }).Build();
        return new ProviderEnvelopeSecretProtector(config);
    }
}
