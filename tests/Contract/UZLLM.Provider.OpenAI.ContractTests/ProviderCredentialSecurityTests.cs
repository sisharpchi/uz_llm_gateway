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
