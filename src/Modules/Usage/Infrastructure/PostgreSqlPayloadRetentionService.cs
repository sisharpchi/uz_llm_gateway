using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed class PostgreSqlPayloadRetentionService(
    FoundationDbContext db, PayloadEnvelopeProtector protector, TimeProvider clock)
    : IPayloadRetentionService
{
    private static readonly PayloadRetentionPolicy Disabled = new(false, 1440);

    public async Task<PayloadRetentionPolicy> GetPolicyAsync(Guid organizationId, Guid projectId,
        CancellationToken cancellationToken)
    {
        var policy = await db.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.ProjectId == projectId, cancellationToken);
        return policy is null ? Disabled : new(policy.Enabled, policy.RetentionMinutes);
    }

    public async Task<PayloadRetentionPolicy?> SetPolicyAsync(Guid organizationId, Guid projectId,
        Guid accountId, bool enabled, int retentionMinutes, CancellationToken cancellationToken)
    {
        if (retentionMinutes is < 60 or > 10080)
            throw new ArgumentOutOfRangeException(nameof(retentionMinutes),
                "Retention must be between one hour and seven days.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var projectExists = await db.Set<ProjectEntity>().FromSqlInterpolated(
            $"SELECT * FROM gateway.project WHERE organization_id = {organizationId} AND id = {projectId} FOR UPDATE")
            .AsNoTracking().AnyAsync(cancellationToken);
        if (!projectExists) return null;
        var policy = await LockPolicyAsync(organizationId, projectId, cancellationToken);
        var now = clock.GetUtcNow();
        if (policy is null)
        {
            db.Set<UsagePayloadRetentionPolicyEntity>().Add(new UsagePayloadRetentionPolicyEntity
            {
                OrganizationId = organizationId, ProjectId = projectId, Enabled = enabled,
                RetentionMinutes = retentionMinutes, UpdatedByAccountId = accountId, UpdatedAt = now
            });
        }
        else
        {
            await db.Set<UsagePayloadRetentionPolicyEntity>()
                .Where(value => value.OrganizationId == organizationId && value.ProjectId == projectId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Enabled, enabled)
                    .SetProperty(value => value.RetentionMinutes, retentionMinutes)
                    .SetProperty(value => value.UpdatedByAccountId, accountId)
                    .SetProperty(value => value.UpdatedAt, now), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (!enabled)
            await db.Set<UsagePayloadEntity>()
                .Where(value => value.OrganizationId == organizationId && value.ProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
        else if (policy is { Enabled: true } && retentionMinutes < policy.RetentionMinutes)
        {
            // Shortening retention must also shorten the already-stored records.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE usage.payload SET expires_at = LEAST(expires_at, created_at +
                    ({retentionMinutes} * interval '1 minute'))
                WHERE organization_id = {organizationId} AND project_id = {projectId}
                """, cancellationToken);
            await db.Set<UsagePayloadEntity>().Where(value => value.OrganizationId == organizationId
                && value.ProjectId == projectId && value.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(enabled, retentionMinutes);
    }

    public async Task<bool> CaptureRequestAsync(Guid organizationId, Guid projectId,
        Guid requestId, byte[] payload, CancellationToken cancellationToken)
    {
        if (!(await GetPolicyAsync(organizationId, projectId, cancellationToken)).Enabled) return false;
        // Encryption is deliberately before the transaction; no plaintext is persisted.
        var protectedPayload = protector.Protect(organizationId, projectId, requestId, "request", payload);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var policy = await LockPolicyAsync(organizationId, projectId, cancellationToken);
        if (policy is not { Enabled: true }) return false;
        var now = clock.GetUtcNow();
        db.Set<UsagePayloadEntity>().Add(new UsagePayloadEntity
        {
            RequestId = requestId, OrganizationId = organizationId, ProjectId = projectId,
            EncryptedRequestPayload = protectedPayload.Ciphertext,
            WrappedRequestKey = protectedPayload.WrappedKey,
            RequestKeyVersion = protectedPayload.KeyVersion,
            CreatedAt = now, ExpiresAt = now.AddMinutes(policy.RetentionMinutes)
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task CaptureResponseAsync(Guid organizationId, Guid projectId, Guid requestId,
        byte[] payload, CancellationToken cancellationToken)
    {
        var protectedPayload = protector.Protect(organizationId, projectId, requestId, "response", payload);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var policy = await LockPolicyAsync(organizationId, projectId, cancellationToken);
        if (policy is not { Enabled: true }) return;
        var now = clock.GetUtcNow();
        await db.Set<UsagePayloadEntity>().Where(value => value.RequestId == requestId
            && value.OrganizationId == organizationId && value.ProjectId == projectId
            && value.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.EncryptedResponsePayload, protectedPayload.Ciphertext)
                .SetProperty(value => value.WrappedResponseKey, protectedPayload.WrappedKey)
                .SetProperty(value => value.ResponseKeyVersion, protectedPayload.KeyVersion), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RetainedPayload?> GetPayloadAsync(Guid organizationId, Guid projectId,
        Guid requestId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var row = await db.Set<UsagePayloadEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.ProjectId == projectId
                && value.RequestId == requestId && value.ExpiresAt > now)
            .Join(db.Set<UsagePayloadRetentionPolicyEntity>().Where(value => value.Enabled),
                payload => new { payload.OrganizationId, payload.ProjectId },
                policy => new { policy.OrganizationId, policy.ProjectId }, (payload, _) => payload)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var requestBytes = protector.Unprotect(organizationId, projectId, requestId, "request",
            new(row.EncryptedRequestPayload, row.WrappedRequestKey, row.RequestKeyVersion));
        try
        {
            string? response = null;
            if (row.EncryptedResponsePayload is not null && row.WrappedResponseKey is not null
                && row.ResponseKeyVersion is not null)
            {
                var responseBytes = protector.Unprotect(organizationId, projectId, requestId,
                    "response", new(row.EncryptedResponsePayload, row.WrappedResponseKey,
                        row.ResponseKeyVersion));
                try { response = Encoding.UTF8.GetString(responseBytes); }
                finally { CryptographicOperations.ZeroMemory(responseBytes); }
            }
            return new(requestId, projectId, Encoding.UTF8.GetString(requestBytes), response, row.ExpiresAt);
        }
        finally { CryptographicOperations.ZeroMemory(requestBytes); }
    }

    public async Task<int> DeleteExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var now = clock.GetUtcNow();
        var ids = await db.Set<UsagePayloadEntity>().AsNoTracking()
            .Where(value => value.ExpiresAt <= now).OrderBy(value => value.ExpiresAt)
            .Select(value => value.RequestId).Take(batchSize).ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return 0;
        return await db.Set<UsagePayloadEntity>()
            .Where(value => ids.Contains(value.RequestId) && value.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task<UsagePayloadRetentionPolicyEntity?> LockPolicyAsync(Guid organizationId,
        Guid projectId, CancellationToken cancellationToken) =>
        db.Set<UsagePayloadRetentionPolicyEntity>().FromSqlInterpolated($"""
            SELECT * FROM usage.payload_retention_policy
            WHERE organization_id = {organizationId} AND project_id = {projectId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
}
