using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public enum PlatformFeature { ManagedTraffic, TopUps }

public sealed record PlatformControl(PlatformFeature Feature, bool Enabled, DateTimeOffset UpdatedAt);

public interface IPlatformControlStore
{
    Task<bool> IsEnabledAsync(PlatformFeature feature, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformControl>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> SetEnabledAsync(PlatformFeature feature, bool enabled, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed class PostgreSqlPlatformControlStore(FoundationDbContext dbContext) : IPlatformControlStore
{
    public async Task<bool> IsEnabledAsync(PlatformFeature feature, CancellationToken cancellationToken = default)
    {
        var name = feature.ToString();
        return await dbContext.Set<PlatformControlEntity>().AsNoTracking()
            .Where(value => value.Feature == name).Select(value => (bool?)value.Enabled)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Platform control is missing.");
    }

    public async Task<IReadOnlyList<PlatformControl>> ListAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.Set<PlatformControlEntity>().AsNoTracking().OrderBy(value => value.Feature)
            .ToListAsync(cancellationToken))
        .Select(value => new PlatformControl(Enum.Parse<PlatformFeature>(value.Feature), value.Enabled, value.UpdatedAt))
        .ToArray();

    public async Task<bool> SetEnabledAsync(PlatformFeature feature, bool enabled, DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<PlatformControlEntity>().Where(value => value.Feature == feature.ToString())
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Enabled, enabled)
                .SetProperty(value => value.UpdatedAt, now), cancellationToken) == 1;
}

public sealed class PlatformControlEntity
{
    public string Feature { get; set; } = null!;
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
