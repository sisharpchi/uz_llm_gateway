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
        CreatedAt = apiKey.ApiKey.CreatedAt
    };

    private static GatewayApiKey ToContract(GatewayApiKeyEntity apiKey) => new(apiKey.Id, apiKey.ProjectId, apiKey.Name, apiKey.Prefix, Enum.Parse<GatewayApiKeyStatus>(apiKey.Status, false), apiKey.ExpiresAt, apiKey.CreatedByAccountId, apiKey.CreatedAt);
}
