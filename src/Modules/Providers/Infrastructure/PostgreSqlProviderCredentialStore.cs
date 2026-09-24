using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Providers.Infrastructure;

public sealed class PostgreSqlProviderCredentialStore(FoundationDbContext db) : IProviderCredentialStore
{
    public async Task CreatePlatformAsync(StoredProviderCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        db.Set<ProviderCredentialEntity>().Add(new ProviderCredentialEntity
        {
            Id = credential.Credential.Id,
            ProviderId = credential.Credential.ProviderId,
            CredentialType = "Platform",
            Status = credential.Credential.Status.ToString(),
            EncryptedSecret = credential.ProtectedSecret.EncryptedSecret,
            WrappedDataKey = credential.ProtectedSecret.WrappedDataKey,
            KeyVersion = credential.ProtectedSecret.KeyVersion,
            CreatedAt = credential.Credential.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoredProviderCredential?> FindActivePlatformAsync(Guid credentialId, Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Set<ProviderCredentialEntity>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.Id == credentialId && value.ProviderId == providerId
            && value.CredentialType == "Platform" && value.Status == "Active",
            cancellationToken);
        return row is null ? null : new StoredProviderCredential(
            new ProviderCredential(row.Id, row.ProviderId, ProviderCredentialStatus.Active, row.CreatedAt),
            new ProtectedProviderSecret(row.EncryptedSecret, row.WrappedDataKey, row.KeyVersion));
    }

    public async Task<bool> SetStatusAsync(Guid credentialId, ProviderCredentialStatus status,
        CancellationToken cancellationToken = default) =>
        await db.Set<ProviderCredentialEntity>()
            .Where(value => value.Id == credentialId && value.CredentialType == "Platform")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, status.ToString()),
                cancellationToken) == 1;
}
