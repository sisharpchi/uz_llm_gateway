using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Providers.Infrastructure;

/// <summary>Operator-invoked, bounded re-encryption; one credential and audit event commit together.</summary>
public sealed class ProviderCredentialKeyRotation(
    FoundationDbContext db, ProviderEnvelopeSecretProtector protector,
    IAuditTrail audit)
{
    public async Task<int> RotateBatchAsync(Guid actorId, int limit,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("An operator account ID is required.");
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!await db.Set<IdentityOperatorAccessEntity>().AsNoTracking().AnyAsync(value =>
            value.AccountId == actorId && value.IsActive && value.MfaEnabledAt != null
            && value.Account.Status == "Active", cancellationToken))
            throw new UnauthorizedAccessException("An active MFA-enrolled operator is required.");

        var ids = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(row => row.DeletedAt == null && row.KeyVersion != protector.ActiveKeyVersion)
            .OrderBy(row => row.Id).Select(row => row.Id).Take(limit)
            .ToArrayAsync(cancellationToken);
        var rotated = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await db.Set<ProviderCredentialEntity>()
                .FromSqlInterpolated($"SELECT * FROM gateway.provider_credential WHERE id = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);
            var row = rows.SingleOrDefault();
            if (row is not null && row.DeletedAt == null && row.KeyVersion != protector.ActiveKeyVersion)
            {
                var priorVersion = row.KeyVersion;
                var previous = new ProtectedProviderSecret(row.EncryptedSecret, row.WrappedDataKey,
                    row.KeyVersion);
                var updated = row.CredentialType == "BYOK" && row.OrganizationId is { } tenantId
                    ? protector.ProtectForOrganization(tenantId, row.Id, row.ProviderId,
                        protector.UnprotectForOrganization(tenantId, row.Id, row.ProviderId, previous))
                    : row.CredentialType == "Platform" && row.OrganizationId is null
                        ? protector.Protect(row.Id, row.ProviderId,
                            protector.Unprotect(row.Id, row.ProviderId, previous))
                        : throw new InvalidOperationException("Invalid provider credential scope.");
                row.EncryptedSecret = updated.EncryptedSecret;
                row.WrappedDataKey = updated.WrappedDataKey;
                row.KeyVersion = updated.KeyVersion;
                await db.SaveChangesAsync(cancellationToken);
                await audit.RecordAsync(new AuditEventInput(row.OrganizationId, actorId,
                    "provider_credential.key_rotated", "provider_credential", row.Id, null,
                    JsonSerializer.Serialize(new { fromVersion = priorVersion,
                        toVersion = updated.KeyVersion, row.CredentialType })), cancellationToken);
                rotated++;
            }
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        return rotated;
    }
}
