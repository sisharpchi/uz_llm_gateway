using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Application;

public sealed class PlatformCredentialService(IProviderCredentialStore store,
    IProviderSecretProtector protector, TimeProvider clock) : IPlatformCredentialService
{
    public async Task<ProviderCredential> CreateAsync(Guid providerId, string secret,
        CancellationToken cancellationToken = default)
    {
        if (providerId == Guid.Empty || string.IsNullOrWhiteSpace(secret) || secret.Length > 4096)
            throw new ArgumentException("A provider and bounded secret are required.");
        var credential = new ProviderCredential(Guid.CreateVersion7(), providerId,
            ProviderCredentialStatus.Active, clock.GetUtcNow());
        var protectedSecret = protector.Protect(credential.Id, providerId, secret);
        await store.CreatePlatformAsync(new StoredProviderCredential(credential, protectedSecret),
            cancellationToken);
        return credential;
    }

    public Task<bool> SetStatusAsync(Guid credentialId, ProviderCredentialStatus status,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || !Enum.IsDefined(status))
            throw new ArgumentException("A credential and valid status are required.");
        return store.SetStatusAsync(credentialId, status, cancellationToken);
    }
}

public sealed class ProviderCredentialResolver(IProviderCredentialStore store,
    IProviderSecretProtector protector) : IProviderCredentialResolver
{
    public async Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || providerId == Guid.Empty)
            throw new ArgumentException("Credential and provider IDs are required.");
        var stored = await store.FindActivePlatformAsync(credentialId, providerId, cancellationToken);
        return stored is null ? null : protector.Unprotect(credentialId, providerId, stored.ProtectedSecret);
    }
}
