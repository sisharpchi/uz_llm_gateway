using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Providers.Infrastructure;

public sealed class PostgreSqlByokCredentialStore(FoundationDbContext db) : IByokCredentialStore
{
    public async Task<bool> LockOrganizationAsync(Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await db.Set<OrganizationEntity>().FromSqlInterpolated(
            $"SELECT * FROM org.organization WHERE id = {organizationId} FOR UPDATE")
            .AsNoTracking().AnyAsync(cancellationToken);

    public Task<string?> GetActiveProviderCodeAsync(Guid providerId,
        CancellationToken cancellationToken = default) =>
        db.Set<CatalogProviderEntity>().AsNoTracking()
            .Where(value => value.Id == providerId && value.Status == "Active")
            .Select(value => value.Code).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ByokCredential>> ListAsync(Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.CredentialType == "BYOK"
                && value.DeletedAt == null)
            .Join(db.Set<CatalogProviderEntity>(), credential => credential.ProviderId,
                provider => provider.Id, (credential, provider) => new { credential, provider.Code })
            .OrderByDescending(value => value.credential.CreatedAt).ToListAsync(cancellationToken);
        var grants = await db.Set<ProviderCredentialProjectGrantEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId)
            .Select(value => new { value.CredentialId, value.ProjectId }).ToListAsync(cancellationToken);
        var lookup = grants.ToLookup(value => value.CredentialId, value => value.ProjectId);
        return rows.Select(value => ToCredential(value.credential, value.Code,
            lookup[value.credential.Id].ToArray())).ToArray();
    }

    public async Task<StoredByokCredential?> FindAsync(Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.Id == credentialId
                && value.CredentialType == "BYOK" && value.DeletedAt == null)
            .Join(db.Set<CatalogProviderEntity>(), credential => credential.ProviderId,
                provider => provider.Id, (credential, provider) => new { credential, provider.Code })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var grants = await db.Set<ProviderCredentialProjectGrantEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.CredentialId == credentialId)
            .Select(value => value.ProjectId).ToArrayAsync(cancellationToken);
        return new StoredByokCredential(ToCredential(row.credential, row.Code, grants),
            new ProtectedProviderSecret(row.credential.EncryptedSecret,
                row.credential.WrappedDataKey, row.credential.KeyVersion));
    }

    public async Task CreateAsync(StoredByokCredential credential,
        CancellationToken cancellationToken = default)
    {
        var item = credential.Credential;
        db.Set<ProviderCredentialEntity>().Add(new ProviderCredentialEntity
        {
            Id = item.Id, OrganizationId = item.OrganizationId, ProviderId = item.ProviderId,
            CredentialType = "BYOK", Status = item.Status.ToString(), Name = item.Name,
            MaskedKey = item.MaskedKey, EncryptedSecret = credential.ProtectedSecret.EncryptedSecret,
            WrappedDataKey = credential.ProtectedSecret.WrappedDataKey,
            KeyVersion = credential.ProtectedSecret.KeyVersion,
            CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Guid organizationId, Guid credentialId, string? name,
        string? maskedKey, ProtectedProviderSecret? protectedSecret, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Set<ProviderCredentialEntity>().SingleOrDefaultAsync(value =>
            value.OrganizationId == organizationId && value.Id == credentialId
            && value.CredentialType == "BYOK" && value.DeletedAt == null,
            cancellationToken);
        if (row is null) return false;
        // ExecuteUpdate in the test/status paths bypasses the change tracker.
        // Refresh before clearing a prior test result on secret rotation.
        await db.Entry(row).ReloadAsync(cancellationToken);
        if (name is not null) row.Name = name;
        if (maskedKey is not null) row.MaskedKey = maskedKey;
        if (protectedSecret is not null)
        {
            row.EncryptedSecret = protectedSecret.EncryptedSecret;
            row.WrappedDataKey = protectedSecret.WrappedDataKey;
            row.KeyVersion = protectedSecret.KeyVersion;
            row.LastTestedAt = null;
            row.LastTestStatus = null;
        }
        row.UpdatedAt = updatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetStatusAsync(Guid organizationId, Guid credentialId,
        ProviderCredentialStatus status, bool deleted, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var rows = db.Set<ProviderCredentialEntity>()
            .Where(value => value.OrganizationId == organizationId && value.Id == credentialId
                && value.CredentialType == "BYOK" && value.DeletedAt == null);
        var count = deleted
            ? await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, status.ToString())
                .SetProperty(value => value.UpdatedAt, updatedAt)
                .SetProperty(value => value.DeletedAt, updatedAt)
                .SetProperty(value => value.EncryptedSecret, RandomNumberGenerator.GetBytes(32))
                .SetProperty(value => value.WrappedDataKey, RandomNumberGenerator.GetBytes(60))
                .SetProperty(value => value.KeyVersion, "deleted")
                .SetProperty(value => value.MaskedKey, "••••deleted"), cancellationToken)
            : await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, status.ToString())
                .SetProperty(value => value.UpdatedAt, updatedAt), cancellationToken);
        return count == 1;
    }

    public async Task<bool> SetProjectGrantAsync(Guid organizationId, Guid credentialId,
        Guid projectId, bool enabled, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var credential = await db.Set<ProviderCredentialEntity>().AsNoTracking().AnyAsync(value =>
            value.OrganizationId == organizationId && value.Id == credentialId
            && value.CredentialType == "BYOK" && (!enabled || value.Status == "Active")
            && value.DeletedAt == null, cancellationToken);
        var project = await db.Set<ProjectEntity>().AsNoTracking().AnyAsync(value =>
            value.OrganizationId == organizationId && value.Id == projectId
            && (!enabled || value.Status == "Active"), cancellationToken);
        if (!credential || !project) return false;
        var grants = db.Set<ProviderCredentialProjectGrantEntity>();
        if (enabled)
        {
            if (await grants.AnyAsync(value => value.OrganizationId == organizationId
                && value.CredentialId == credentialId && value.ProjectId == projectId,
                cancellationToken)) return true;
            grants.Add(new ProviderCredentialProjectGrantEntity
            {
                OrganizationId = organizationId, CredentialId = credentialId,
                ProjectId = projectId, CreatedAt = createdAt
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var existing = await grants.SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.CredentialId == credentialId && value.ProjectId == projectId,
                cancellationToken);
            if (existing is not null)
            {
                grants.Remove(existing);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return true;
    }

    public async Task<bool> SetTestResultAsync(Guid organizationId, Guid credentialId,
        ByokTestStatus result, DateTimeOffset testedAt,
        CancellationToken cancellationToken = default) =>
        await db.Set<ProviderCredentialEntity>().Where(value =>
            value.OrganizationId == organizationId && value.Id == credentialId
            && value.CredentialType == "BYOK" && value.Status == "Active"
            && value.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LastTestStatus, result.ToString())
                .SetProperty(value => value.LastTestedAt, testedAt), cancellationToken) == 1;

    public async Task<ProtectedProviderSecret?> FindGrantedSecretAsync(Guid organizationId,
        Guid projectId, Guid credentialId, Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Set<ProviderCredentialProjectGrantEntity>().AsNoTracking()
            .Where(grant => grant.OrganizationId == organizationId && grant.ProjectId == projectId
                && grant.CredentialId == credentialId)
            .Join(db.Set<ProjectEntity>(), grant => new { grant.OrganizationId, grant.ProjectId },
                project => new { project.OrganizationId, ProjectId = project.Id },
                (grant, project) => new { grant, project })
            .Join(db.Set<ProviderCredentialEntity>(), value => new { value.grant.OrganizationId,
                    value.grant.CredentialId },
                credential => new { OrganizationId = credential.OrganizationId!.Value,
                    CredentialId = credential.Id },
                (value, credential) => new { value.project, credential })
            .Where(value => value.project.Status == "Active"
                && value.credential.CredentialType == "BYOK"
                && value.credential.Status == "Active" && value.credential.DeletedAt == null
                && value.credential.ProviderId == providerId)
            .Select(value => new { value.credential.EncryptedSecret,
                value.credential.WrappedDataKey, value.credential.KeyVersion })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new ProtectedProviderSecret(row.EncryptedSecret,
            row.WrappedDataKey, row.KeyVersion);
    }

    private static ByokCredential ToCredential(ProviderCredentialEntity row, string providerCode,
        IReadOnlyList<Guid> grants) => new(row.Id, row.OrganizationId!.Value, row.ProviderId,
            providerCode, row.Name!, row.MaskedKey!, Enum.Parse<ProviderCredentialStatus>(row.Status),
            row.CreatedAt, row.UpdatedAt ?? row.CreatedAt, row.LastTestedAt,
            row.LastTestStatus, grants);
}
