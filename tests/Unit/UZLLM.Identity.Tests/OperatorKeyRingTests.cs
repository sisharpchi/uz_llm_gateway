using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Identity.Tests;

public sealed class OperatorKeyRingTests
{
    [Fact]
    public void Production_rejects_an_ephemeral_operator_key_ring()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddUzllmOperatorKeyRing(configuration, requireSharedRing: true));
    }

    [Fact]
    public void Separate_management_nodes_can_unprotect_the_same_operator_secret()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uzllm-keyring-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "test.pfx");
        const string password = "test-only-certificate-password";
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=UZLLM Test Data Protection", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(2));
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pkcs12, password));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = directory,
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = password
            }).Build();

            string protectedSecret;
            string protectedEmail;
            using (var first = new ServiceCollection().AddUzllmOperatorKeyRing(configuration, true)
                .BuildServiceProvider())
            {
                var protector = new DataProtectionIdentitySecretProtector(
                    first.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
                protectedSecret = protector.Protect("operator-totp-secret");
                protectedEmail = new IdentityEmailPayloadCodec(first.GetRequiredService<
                    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()).Protect(
                    new IdentityEmailNotification("person@example.uz", "one-time-test-token",
                        IdentityEmailKind.Verification, DateTimeOffset.UtcNow.AddHours(1)));
            }
            using (var second = new ServiceCollection().AddUzllmOperatorKeyRing(configuration, true)
                .BuildServiceProvider())
            {
                var protector = new DataProtectionIdentitySecretProtector(
                    second.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
                Assert.Equal("operator-totp-secret", protector.Unprotect(protectedSecret));
                var email = new IdentityEmailPayloadCodec(second.GetRequiredService<
                    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()).Unprotect(protectedEmail);
                Assert.Equal("one-time-test-token", email.Token);
            }
            Assert.Contains("encryptedSecret", File.ReadAllText(Directory.GetFiles(directory, "key-*.xml").Single()));
        }
        finally
        {
            // The generated path is a test-owned temporary directory, never a user directory.
            Directory.Delete(directory, recursive: true);
        }
    }
}
