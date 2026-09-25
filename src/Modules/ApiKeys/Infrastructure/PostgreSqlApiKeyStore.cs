using Microsoft.EntityFrameworkCore;
using Npgsql;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public sealed class PostgreSqlApiKeyStore(FoundationDbContext dbContext) : IApiKeyStore
{
    public async Task<bool> TryCreateAsync(StoredGatewayApiKey apiKey, CancellationToken cancellationToken = default)
    {
        dbContext.Set<GatewayApiKeyEntity>().Add(ToEntity(apiKey));
        dbContext.Set<GatewayApiKeyGenerationEntity>().Add(new GatewayApiKeyGenerationEntity
        {
            ApiKeyId = apiKey.ApiKey.Id, Generation = 1, Prefix = apiKey.ApiKey.Prefix,
            ActivatedAt = apiKey.ApiKey.CreatedAt
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Set<GatewayApiKeyEntity>().AsNoTracking()
            .Where(apiKey => apiKey.ProjectId == projectId)
            .OrderByDescending(apiKey => apiKey.CreatedAt)
            .Select(apiKey => ToContract(apiKey))
            .ToListAsync(cancellationToken);

    public async Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<GatewayApiKeyEntity>().AsNoTracking().SingleOrDefaultAsync(apiKey => apiKey.Id == apiKeyId, cancellationToken)) is { } apiKey
            ? ToContract(apiKey)
            : null;

    public async Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default) =>
        await dbContext.Set<GatewayApiKeyEntity>().AsNoTracking()
            .Where(apiKey => apiKey.Prefix == prefix)
            .Select(apiKey => new StoredGatewayApiKey(ToContract(apiKey), apiKey.SecretFingerprint, apiKey.Project.Status == "Active"))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default) =>
        await dbContext.Set<GatewayApiKeyEntity>()
            .Where(apiKey => apiKey.Id == apiKeyId && apiKey.Status != status.ToString())
            .ExecuteUpdateAsync(setters => setters.SetProperty(apiKey => apiKey.Status, status.ToString()), cancellationToken) == 1;

    public async Task<ApiKeyRotationStoreResult> TryRotateAsync(Guid apiKeyId, int expectedGeneration,
        string newPrefix, byte[] newFingerprint, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Set<GatewayApiKeyGenerationEntity>().AsNoTracking()
            .AnyAsync(value => value.Prefix == newPrefix, cancellationToken))
            return ApiKeyRotationStoreResult.PrefixCollision;
        try
        {
            var updated = await dbContext.Set<GatewayApiKeyEntity>()
                .Where(value => value.Id == apiKeyId && value.Generation == expectedGeneration
                    && value.Status == nameof(GatewayApiKeyStatus.Active)
                    && (value.ExpiresAt == null || value.ExpiresAt > now)
                    && value.Project.Status == "Active")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Generation, expectedGeneration + 1)
                    .SetProperty(value => value.Prefix, newPrefix)
                    .SetProperty(value => value.SecretFingerprint, newFingerprint), cancellationToken);
            if (updated != 1) return ApiKeyRotationStoreResult.Conflict;
            var revoked = await dbContext.Set<GatewayApiKeyGenerationEntity>()
                .Where(value => value.ApiKeyId == apiKeyId && value.Generation == expectedGeneration
                    && value.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RevokedAt, now), cancellationToken);
            if (revoked != 1) throw new InvalidOperationException("API key generation history is inconsistent.");
            dbContext.Set<GatewayApiKeyGenerationEntity>().Add(new GatewayApiKeyGenerationEntity
            {
                ApiKeyId = apiKeyId, Generation = expectedGeneration + 1, Prefix = newPrefix,
                ActivatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApiKeyRotationStoreResult.Rotated;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return ApiKeyRotationStoreResult.PrefixCollision;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            dbContext.ChangeTracker.Clear();
            return ApiKeyRotationStoreResult.PrefixCollision;
        }
    }

    private static GatewayApiKeyEntity ToEntity(StoredGatewayApiKey apiKey) => new()
    {
        Id = apiKey.ApiKey.Id,
        ProjectId = apiKey.ApiKey.ProjectId,
        Name = apiKey.ApiKey.Name,
        Prefix = apiKey.ApiKey.Prefix,
        SecretFingerprint = apiKey.SecretFingerprint,
        Status = apiKey.ApiKey.Status.ToString(),
        ExpiresAt = apiKey.ApiKey.ExpiresAt,
        CreatedByAccountId = apiKey.ApiKey.CreatedByAccountId,
        CreatedAt = apiKey.ApiKey.CreatedAt,
        Generation = apiKey.ApiKey.Generation
    };

    private static GatewayApiKey ToContract(GatewayApiKeyEntity apiKey) => new(apiKey.Id, apiKey.ProjectId, apiKey.Name, apiKey.Prefix, Enum.Parse<GatewayApiKeyStatus>(apiKey.Status, false), apiKey.ExpiresAt, apiKey.CreatedByAccountId, apiKey.CreatedAt, apiKey.Generation);
}
