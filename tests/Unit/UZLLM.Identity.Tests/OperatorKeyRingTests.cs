using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Identity.Tests;

public sealed class OperatorKeyRingTests
{
    [Fact]
    public void Certificate_rotation_and_rollback_keep_old_operator_and_email_payloads_readable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uzllm-keyring-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string password = "test-only-certificate-password";
        var firstPath = Path.Combine(directory, "first.pfx");
        var secondPath = Path.Combine(directory, "second.pfx");
        try
        {
            CreateCertificate(firstPath, password);
            CreateCertificate(secondPath, password);
            string oldCiphertext;
            string oldEmail;
            using (var first = CreateKeyRing(directory, firstPath, password))
            {
                var provider = first.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
                oldCiphertext = new DataProtectionIdentitySecretProtector(provider).Protect("old-mfa-secret");
                oldEmail = new IdentityEmailPayloadCodec(provider).Protect(new IdentityEmailNotification(
                    "old@example.uz", "old-token", IdentityEmailKind.Verification,
                    DateTimeOffset.UtcNow.AddHours(1)));
            }

            string newCiphertext;
            using (var second = CreateKeyRing(directory, secondPath, password, firstPath))
            {
                var provider = second.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
                Assert.Equal("old-mfa-secret", new DataProtectionIdentitySecretProtector(provider).Unprotect(oldCiphertext));
                Assert.Equal("old-token", new IdentityEmailPayloadCodec(provider).Unprotect(oldEmail).Token);
                second.GetRequiredService<IKeyManager>().CreateNewKey(
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
            }

            using (var newOnly = CreateKeyRing(directory, secondPath, password))
            {
                var protector = new DataProtectionIdentitySecretProtector(
                    newOnly.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>());
                newCiphertext = protector.Protect("new-mfa-secret");
                Assert.Equal("new-mfa-secret", protector.Unprotect(newCiphertext));
                Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(oldCiphertext));
            }

            using var rolledBack = CreateKeyRing(directory, firstPath, password, secondPath);
            var rollbackProvider = rolledBack.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
            Assert.Equal("old-mfa-secret", new DataProtectionIdentitySecretProtector(rollbackProvider).Unprotect(oldCiphertext));
            Assert.Equal("new-mfa-secret", new DataProtectionIdentitySecretProtector(rollbackProvider).Unprotect(newCiphertext));
            Assert.Equal("old-token", new IdentityEmailPayloadCodec(rollbackProvider).Unprotect(oldEmail).Token);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Certificate_rotation_rejects_missing_previous_private_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uzllm-keyring-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string password = "test-only-certificate-password";
        var active = Path.Combine(directory, "active.pfx");
        try
        {
            CreateCertificate(active, password);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = directory,
                ["DataProtection:CertificatePath"] = active,
                ["DataProtection:CertificatePassword"] = password,
                ["DataProtection:PreviousCertificates:0:Path"] = Path.Combine(directory, "missing.pfx"),
                ["DataProtection:PreviousCertificates:0:Password"] = password
            }).Build();
            Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
                .AddUzllmOperatorKeyRing(config, true));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ServiceProvider CreateKeyRing(string directory, string active, string password,
        string? previous = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = directory,
            ["DataProtection:CertificatePath"] = active,
            ["DataProtection:CertificatePassword"] = password
        };
        if (previous is not null)
        {
            values["DataProtection:PreviousCertificates:0:Path"] = previous;
            values["DataProtection:PreviousCertificates:0:Password"] = password;
        }
        return new ServiceCollection().AddUzllmOperatorKeyRing(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), true)
            .BuildServiceProvider();
    }

    private static void CreateCertificate(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=UZLLM Test Data Protection", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
    }
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
