using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Catalog.Infrastructure;

public sealed class PostgreSqlProviderDataPolicyStore(FoundationDbContext db) : IProviderDataPolicyStore
{
    public async Task<CatalogProvider?> FindProviderAsync(string code, CancellationToken cancellationToken)
    {
        var value = await db.Set<CatalogProviderEntity>().AsNoTracking()
            .SingleOrDefaultAsync(provider => provider.Code == code && provider.Status == "Active", cancellationToken);
        return value is null ? null : new CatalogProvider(value.Id, value.Code, value.Name,
            CatalogStatus.Active, value.CreatedAt);
    }

    public async Task<ProviderDataPolicy?> FindCurrentAsync(Guid providerId, CancellationToken cancellationToken) =>
        (await ListAsync(providerId, 1, cancellationToken)).SingleOrDefault();

    public async Task<ProviderDataPolicy?> FindVersionAsync(Guid id, CancellationToken cancellationToken)
    {
        var value = await db.Set<CatalogProviderDataPolicyEntity>().AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.Id == id, cancellationToken);
        return value is null ? null : ToContract(value);
    }

    public async Task<IReadOnlyList<ProviderDataPolicy>> ListAsync(Guid providerId, int limit,
        CancellationToken cancellationToken) =>
        (await db.Set<CatalogProviderDataPolicyEntity>().AsNoTracking()
            .Where(value => value.ProviderId == providerId).OrderByDescending(value => value.SourceObservedAt)
            .Take(limit).ToListAsync(cancellationToken)).Select(ToContract).ToArray();

    public async Task AppendAsync(ProviderDataPolicy policy, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Policy publication requires an audited transaction.");
        // Serialize publications for this provider before checking the observation order.
        if (await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.provider SET code = code WHERE id = {policy.ProviderId};", cancellationToken) != 1)
            throw new KeyNotFoundException("Provider not found.");
        var latest = await FindCurrentAsync(policy.ProviderId, cancellationToken);
        if (latest is not null && policy.SourceObservedAt <= latest.SourceObservedAt)
            throw new InvalidOperationException("Observation time must advance; publish a new correction instead of rewriting history.");
        db.Set<CatalogProviderDataPolicyEntity>().Add(new()
        {
            Id = policy.Id, ProviderId = policy.ProviderId, Retention = policy.Retention,
            Training = policy.Training, Residency = policy.Residency, Regions = policy.Regions.ToArray(),
            Scope = policy.Scope, Conditions = policy.Conditions, SourceUrl = policy.SourceUrl,
            SourceObservedAt = policy.SourceObservedAt, PublishedAt = policy.PublishedAt,
            PublishedByAccountId = policy.PublishedByAccountId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static ProviderDataPolicy ToContract(CatalogProviderDataPolicyEntity value) => new(
        value.Id, value.ProviderId, value.Retention, value.Training, value.Residency, value.Regions,
        value.Scope, value.Conditions, value.SourceUrl, value.SourceObservedAt, value.PublishedAt,
        value.PublishedByAccountId);
}
