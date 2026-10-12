using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Modules.ApiKeys.Domain;

public sealed record TenantLimitCeilings(TenantLimits Project, TenantLimits ApiKey);

public static class TenantLimitPolicy
{
    public const int MaximumRpm = 1_000_000;
    public const int MaximumConcurrency = 10_000;

    public static void Validate(TenantLimits value, TenantLimits ceiling)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.RequestsPerMinute is <= 0 or > MaximumRpm
            || value.Concurrency is <= 0 or > MaximumConcurrency
            || value.RequestsPerMinute > ceiling.RequestsPerMinute
            || value.Concurrency > ceiling.Concurrency)
            throw new ArgumentException("Limits must be positive integers within the operator ceiling; null inherits it.");
    }

    public static TenantLimits Effective(TenantLimits value, TenantLimits ceiling) => new(
        Math.Min(value.RequestsPerMinute ?? ceiling.RequestsPerMinute!.Value, ceiling.RequestsPerMinute!.Value),
        Math.Min(value.Concurrency ?? ceiling.Concurrency!.Value, ceiling.Concurrency!.Value));

    public static LimitPolicy Apply(TenantLimitSnapshot snapshot, LimitPolicy ceiling)
    {
        var project = Effective(snapshot.Project, new(ceiling.ProjectRequestsPerMinute, ceiling.ProjectConcurrency));
        var key = Effective(snapshot.ApiKey, new(ceiling.ApiKeyRequestsPerMinute, ceiling.ApiKeyConcurrency));
        return ceiling with { ProjectRequestsPerMinute = project.RequestsPerMinute!.Value,
            ProjectConcurrency = project.Concurrency!.Value, ApiKeyRequestsPerMinute = key.RequestsPerMinute!.Value,
            ApiKeyConcurrency = key.Concurrency!.Value };
    }
}
