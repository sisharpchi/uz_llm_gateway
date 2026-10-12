using System.Text.Json.Serialization;

namespace UZLLM.Modules.ApiKeys.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantLimits(int? RequestsPerMinute = null, int? Concurrency = null);

public sealed record TenantLimitView(TenantLimits Configured, TenantLimits Effective, TenantLimits Ceiling);
public sealed record TenantLimitSnapshot(TenantLimits Project, TenantLimits ApiKey);

public interface ITenantLimitStore
{
    Task<TenantLimitSnapshot?> FindAsync(LimitScope scope, CancellationToken cancellationToken = default);
    Task<TenantLimits?> ReadAsync(Guid projectId, Guid? apiKeyId, CancellationToken cancellationToken = default);
    Task<bool> ReplaceAsync(Guid projectId, Guid? apiKeyId, TenantLimits limits, CancellationToken cancellationToken = default);
}

public interface ITenantLimitService
{
    Task<TenantLimitView> ReadAsync(Guid accountId, Guid resourceId, bool apiKey,
        CancellationToken cancellationToken = default);
    Task<TenantLimitView> ReplaceAsync(Guid accountId, Guid resourceId, bool apiKey, TenantLimits limits,
        CancellationToken cancellationToken = default);
}
