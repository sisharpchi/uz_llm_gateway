using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Domain;

namespace UZLLM.ApiKeys.Tests;

public sealed class TenantLimitTests
{
    private static readonly LimitPolicy Ceiling = new(60, 600, 8, 64, TimeSpan.FromMinutes(15));

    [Theory]
    [InlineData(null, null, 60, 8)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(60, 8, 60, 8)]
    [InlineData(12, null, 12, 8)]
    [InlineData(null, 3, 60, 3)]
    public void Nullable_limits_inherit_but_never_disable_the_operator_ceiling(int? rpm, int? concurrency,
        int effectiveRpm, int effectiveConcurrency)
    {
        var value = new TenantLimits(rpm, concurrency);
        TenantLimitPolicy.Validate(value, new(60, 8));
        var result = TenantLimitPolicy.Apply(new(new(), value), Ceiling);
        Assert.Equal(effectiveRpm, result.ApiKeyRequestsPerMinute);
        Assert.Equal(effectiveConcurrency, result.ApiKeyConcurrency);
        Assert.Equal(Ceiling.ProjectRequestsPerMinute, result.ProjectRequestsPerMinute);
        Assert.Equal(Ceiling.LeaseDuration, result.LeaseDuration);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(61, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 9)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Invalid_or_unbounded_limits_are_rejected(int rpm, int concurrency) =>
        Assert.Throws<ArgumentException>(() => TenantLimitPolicy.Validate(new(rpm, concurrency), new(60, 8)));

    [Fact]
    public void Configuration_above_absolute_bounds_is_rejected_and_lower_gateway_caps_always_win()
    {
        var max = new TenantLimits(TenantLimitPolicy.MaximumRpm, TenantLimitPolicy.MaximumConcurrency);
        TenantLimitPolicy.Validate(max, max);
        Assert.Throws<ArgumentException>(() => TenantLimitPolicy.Validate(max with { RequestsPerMinute = 1_000_001 }, max));
        Assert.Throws<ArgumentException>(() => TenantLimitPolicy.Validate(max with { Concurrency = 10_001 }, max));
        var result = TenantLimitPolicy.Apply(new(new(700, 70), new(70, 10)), Ceiling);
        Assert.Equal(Ceiling, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Missing_or_unavailable_policy_fails_closed_but_release_does_not_read_Postgres(bool unavailable, bool wrapped)
    {
        var store = new Store { Unavailable = unavailable, Wrapped = wrapped };
        var inner = new Limiter();
        var limiter = new TenantAdmissionLimiter(store, inner);
        var scope = new LimitScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var result = await limiter.TryAcquireAsync(scope, Ceiling, Guid.NewGuid());
        Assert.Equal(LimitOutcome.DependencyUnavailable, result.Outcome);
        Assert.Null(result.Lease);
        Assert.Equal(0, inner.Acquires);
        Assert.Equal(LimitReleaseOutcome.Released, await limiter.ReleaseAsync(new(scope, Guid.NewGuid())));
        Assert.Equal(1, store.Reads);
        Assert.Equal(1, inner.Releases);
    }

    [Fact]
    public async Task Fresh_policy_is_applied_each_admission_and_cancellation_is_not_swallowed()
    {
        var store = new Store { Snapshot = new(new(40, 4), new(20, 2)) };
        var inner = new Limiter();
        var limiter = new TenantAdmissionLimiter(store, inner);
        var scope = new LimitScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await limiter.TryAcquireAsync(scope, Ceiling, Guid.NewGuid());
        Assert.Equal(Ceiling with { ProjectRequestsPerMinute = 40, ProjectConcurrency = 4,
            ApiKeyRequestsPerMinute = 20, ApiKeyConcurrency = 2 }, inner.Policy);
        store.Snapshot = new(new(), new());
        await limiter.TryAcquireAsync(scope, Ceiling, Guid.NewGuid());
        Assert.Equal(Ceiling, inner.Policy);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.TryAcquireAsync(scope, Ceiling, Guid.NewGuid(), cancellation.Token));
        Assert.Equal(2, inner.Acquires);
    }

    [Fact]
    public async Task Unrelated_policy_errors_are_not_disguised_as_database_outages()
    {
        var inner = new Limiter();
        var limiter = new TenantAdmissionLimiter(new Store { ProgrammingFailure = true }, inner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => limiter.TryAcquireAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Ceiling, Guid.NewGuid()));
        Assert.Equal(0, inner.Acquires);
    }

    private sealed class Store : ITenantLimitStore
    {
        public TenantLimitSnapshot? Snapshot { get; set; }
        public bool Unavailable { get; init; }
        public bool Wrapped { get; init; }
        public bool ProgrammingFailure { get; init; }
        public int Reads { get; private set; }
        public Task<TenantLimitSnapshot?> FindAsync(LimitScope scope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (ProgrammingFailure) throw new InvalidOperationException("Synthetic programming defect");
            if (Wrapped) throw new InvalidOperationException("Synthetic EF transient wrapper", new TimeoutException());
            return Unavailable ? throw new TimeoutException() : Task.FromResult(Snapshot);
        }
        public Task<TenantLimits?> ReadAsync(Guid projectId, Guid? apiKeyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReplaceAsync(Guid projectId, Guid? apiKeyId, TenantLimits limits, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Limiter : IDistributedAdmissionLimiter
    {
        public int Acquires { get; private set; }
        public int Releases { get; private set; }
        public LimitPolicy? Policy { get; private set; }
        public Task<LimitDecision> TryAcquireAsync(LimitScope scope, LimitPolicy policy, Guid requestId, CancellationToken cancellationToken = default)
        {
            Acquires++; Policy = policy;
            return Task.FromResult(new LimitDecision(LimitOutcome.Admitted, new(scope, requestId), null));
        }
        public Task<LimitReleaseOutcome> ReleaseAsync(LimitLease lease, CancellationToken cancellationToken = default)
        { Releases++; return Task.FromResult(LimitReleaseOutcome.Released); }
    }
}
