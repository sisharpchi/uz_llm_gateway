using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UZLLM.Modules.Identity.Infrastructure;

/// <summary>Shares and encrypts the operator MFA key ring across Management nodes.</summary>
public static class OperatorKeyRingConfiguration
{
    public static IServiceCollection AddUzllmOperatorKeyRing(this IServiceCollection services,
        IConfiguration configuration, bool requireSharedRing)
    {
        var directory = configuration["DataProtection:KeyRingPath"];
        if (string.IsNullOrWhiteSpace(directory))
        {
            if (requireSharedRing)
                throw new InvalidOperationException("Production Management requires DataProtection:KeyRingPath.");
            services.AddDataProtection().SetApplicationName("UZLLM.Management");
            return services;
        }

        var certificatePath = configuration["DataProtection:CertificatePath"];
        var password = configuration["DataProtection:CertificatePassword"];
        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(password)
            || !Directory.Exists(directory) || !File.Exists(certificatePath))
            throw new InvalidOperationException("A writable shared key ring and its encryption certificate are required.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException("The Data Protection certificate requires a private key.");
        var previous = configuration.GetSection("DataProtection:PreviousCertificates").GetChildren()
            .Select(entry =>
            {
                var path = entry["Path"];
                var priorPassword = entry["Password"];
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(priorPassword)
                    || !File.Exists(path))
                    throw new InvalidOperationException("Every previous Data Protection certificate must be readable.");
                var prior = X509CertificateLoader.LoadPkcs12FromFile(path, priorPassword);
                if (!prior.HasPrivateKey)
                    throw new InvalidOperationException("A previous Data Protection certificate requires its private key.");
                return prior;
            }).ToArray();
        services.AddDataProtection()
            .SetApplicationName("UZLLM.Management")
            .PersistKeysToFileSystem(new DirectoryInfo(directory))
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate([certificate, .. previous]);
        return services;
    }
}
