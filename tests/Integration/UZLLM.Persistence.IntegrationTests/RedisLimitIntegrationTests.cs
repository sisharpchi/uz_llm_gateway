using StackExchange.Redis;
using Testcontainers.Redis;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

[CollectionDefinition(nameof(RedisLimitCollection))]
public sealed class RedisLimitCollection : ICollectionFixture<RedisLimitFixture>;

[Collection(nameof(RedisLimitCollection))]
public sealed class RedisLimitIntegrationTests(RedisLimitFixture fixture)
{
    private static readonly LimitRecoveryOptions Recovery = new(TimeSpan.FromMilliseconds(1200));
    private static readonly LimitPolicy Policy = new(20, 30, 20, 30, TimeSpan.FromMilliseconds(900));

    [Fact]
    public async Task Two_nodes_share_exact_key_and_project_rpm_boundaries()
    {
        var source = new MutableEpochSource(Guid.NewGuid().ToString("N"));
        var first = new RedisAdmissionLimiter(fixture.First, source, Recovery);
        var second = new RedisAdmissionLimiter(fixture.Second, source, Recovery);
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var scopeA = new LimitScope(organizationId, projectId, Guid.CreateVersion7());
        var scopeB = new LimitScope(organizationId, projectId, Guid.CreateVersion7());
        await WarmAsync(first, scopeA, Policy);
        var policy = new LimitPolicy(2, 3, 10, 10, TimeSpan.FromMilliseconds(900));

        var a = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            (index % 2 == 0 ? first : second).TryAcquireAsync(scopeA, policy, Guid.CreateVersion7())));
        Assert.Equal(2, a.Count(result => result.Outcome == LimitOutcome.Admitted));
        Assert.Equal(6, a.Count(result => result.Outcome == LimitOutcome.RateLimited));
        Assert.All(a.Where(result => result.Outcome == LimitOutcome.RateLimited),
            result => Assert.True(result.RetryAfter > TimeSpan.Zero));

        Assert.Equal(LimitOutcome.Admitted,
            (await second.TryAcquireAsync(scopeB, policy, Guid.CreateVersion7())).Outcome);
        Assert.Equal(LimitOutcome.RateLimited,
            (await first.TryAcquireAsync(scopeB, policy, Guid.CreateVersion7())).Outcome);
    }

    [Fact]
    public async Task Concurrent_leases_are_atomic_releasable_and_duplicate_safe()
    {
        var source = new MutableEpochSource(Guid.NewGuid().ToString("N"));
        var first = new RedisAdmissionLimiter(fixture.First, source, Recovery);
        var second = new RedisAdmissionLimiter(fixture.Second, source, Recovery);
        var organizationId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var keyA = new LimitScope(organizationId, projectId, Guid.CreateVersion7());
        var keyB = new LimitScope(organizationId, projectId, Guid.CreateVersion7());
        var keyC = new LimitScope(organizationId, projectId, Guid.CreateVersion7());
        await WarmAsync(first, keyA, Policy);
        var policy = new LimitPolicy(10, 10, 1, 2, TimeSpan.FromMilliseconds(900));

        var firstAdmit = await first.TryAcquireAsync(keyA, policy, Guid.CreateVersion7());
        Assert.Equal(LimitOutcome.Admitted, firstAdmit.Outcome);
        Assert.Equal(LimitOutcome.Admitted,
            (await second.TryAcquireAsync(keyA, policy, firstAdmit.Lease!.RequestId)).Outcome);
        Assert.Equal(LimitOutcome.ConcurrencyLimited,
            (await second.TryAcquireAsync(keyA, policy, Guid.CreateVersion7())).Outcome);
        Assert.Equal(LimitOutcome.Admitted,
            (await second.TryAcquireAsync(keyB, policy, Guid.CreateVersion7())).Outcome);
        Assert.Equal(LimitOutcome.ConcurrencyLimited,
            (await first.TryAcquireAsync(keyC, policy, Guid.CreateVersion7())).Outcome);

        Assert.Equal(LimitReleaseOutcome.Released, await second.ReleaseAsync(firstAdmit.Lease));
        Assert.Equal(LimitReleaseOutcome.Missing, await first.ReleaseAsync(firstAdmit.Lease));
        Assert.Equal(LimitOutcome.Duplicate,
            (await first.TryAcquireAsync(keyA, policy, firstAdmit.Lease.RequestId)).Outcome);
        Assert.Equal(LimitOutcome.Admitted,
            (await first.TryAcquireAsync(keyC, policy, Guid.CreateVersion7())).Outcome);
    }

    [Fact]
    public async Task Fifty_parallel_requests_across_two_nodes_cannot_exceed_three_shared_leases()
    {
        var source = new MutableEpochSource(Guid.NewGuid().ToString("N"));
        var first = new RedisAdmissionLimiter(fixture.First, source, Recovery);
        var second = new RedisAdmissionLimiter(fixture.Second, source, Recovery);
        var scope = NewScope();
        await WarmAsync(first, scope, Policy);
        var policy = new LimitPolicy(100, 100, 3, 3, TimeSpan.FromMilliseconds(900));

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(index =>
            (index % 2 == 0 ? first : second).TryAcquireAsync(scope, policy, Guid.CreateVersion7())));

        Assert.Equal(3, results.Count(value => value.Outcome == LimitOutcome.Admitted));
        Assert.Equal(47, results.Count(value => value.Outcome == LimitOutcome.ConcurrencyLimited));
        Assert.All(results.Where(value => value.Outcome == LimitOutcome.Admitted),
            value => Assert.NotNull(value.Lease));
    }

    [Fact]
    public async Task Expired_leases_are_evicted_without_releasing_newer_leases()
    {
        var source = new MutableEpochSource(Guid.NewGuid().ToString("N"));
        var limiter = new RedisAdmissionLimiter(fixture.First, source, Recovery);
        var scope = NewScope();
        await WarmAsync(limiter, scope, Policy);
        var shortPolicy = new LimitPolicy(10, 10, 1, 1, TimeSpan.FromMilliseconds(300));
        var admitted = await limiter.TryAcquireAsync(scope, shortPolicy, Guid.CreateVersion7());
        Assert.Equal(LimitOutcome.Admitted, admitted.Outcome);
        await Task.Delay(380);
        Assert.Equal(LimitOutcome.Admitted,
            (await limiter.TryAcquireAsync(scope, shortPolicy, Guid.CreateVersion7())).Outcome);
        Assert.Equal(LimitReleaseOutcome.Missing, await limiter.ReleaseAsync(admitted.Lease!));
    }

    [Fact]
    public async Task Redis_epoch_change_closes_new_admission_for_full_lease_horizon()
    {
        var source = new MutableEpochSource(Guid.NewGuid().ToString("N"));
        var limiter = new RedisAdmissionLimiter(fixture.First, source, Recovery);
        var scope = NewScope();
        await WarmAsync(limiter, scope, Policy);
        Assert.Equal(LimitOutcome.Admitted,
            (await limiter.TryAcquireAsync(scope, Policy, Guid.CreateVersion7())).Outcome);

        source.Epoch = Guid.NewGuid().ToString("N");
        var closed = await limiter.TryAcquireAsync(scope, Policy, Guid.CreateVersion7());
        Assert.Equal(LimitOutcome.RecoveryWindow, closed.Outcome);
        Assert.True(closed.RetryAfter >= TimeSpan.FromMilliseconds(1000));
        await Task.Delay(1300);
        Assert.Equal(LimitOutcome.Admitted,
            (await limiter.TryAcquireAsync(scope, Policy, Guid.CreateVersion7())).Outcome);
    }

    [Fact]
    public async Task Provider_quota_cooldown_cannot_be_shortened_by_a_later_429()
    {
        var limiter = new RedisAdmissionLimiter(fixture.First,
            new MutableEpochSource(Guid.NewGuid().ToString("N")), Recovery);
        var credentialId = Guid.CreateVersion7();
        Assert.Equal(ProviderQuotaOutcome.Available, (await limiter.CheckAsync(credentialId)).Outcome);
        Assert.True(await limiter.MarkExhaustedAsync(credentialId, TimeSpan.FromSeconds(2)));
        Assert.True(await limiter.MarkExhaustedAsync(credentialId, TimeSpan.FromMilliseconds(500)));
        var blocked = await limiter.CheckAsync(credentialId);
        Assert.Equal(ProviderQuotaOutcome.Exhausted, blocked.Outcome);
        Assert.True(blocked.RetryAfter > TimeSpan.FromSeconds(1));
        await Task.Delay(2100);
        Assert.Equal(ProviderQuotaOutcome.Available, (await limiter.CheckAsync(credentialId)).Outcome);

        var extendedId = Guid.CreateVersion7();
        Assert.True(await limiter.MarkExhaustedAsync(extendedId, TimeSpan.FromSeconds(1)));
        Assert.True(await limiter.MarkExhaustedAsync(extendedId, TimeSpan.FromSeconds(3)));
        Assert.True((await limiter.CheckAsync(extendedId)).RetryAfter > TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Lost_Redis_connection_denies_admission_and_quota_checks()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.AdminOptions);
        var limiter = new RedisAdmissionLimiter(connection,
            new RedisServerEpochSource(connection), Recovery);
        var epoch = await new RedisServerEpochSource(connection).GetCurrentEpochAsync();
        Assert.False(string.IsNullOrWhiteSpace(epoch));
        connection.Dispose();

        Assert.Equal(LimitOutcome.DependencyUnavailable,
            (await limiter.TryAcquireAsync(NewScope(), Policy, Guid.CreateVersion7())).Outcome);
        Assert.Equal(ProviderQuotaOutcome.DependencyUnavailable,
            (await limiter.CheckAsync(Guid.CreateVersion7())).Outcome);
        Assert.Equal(LimitReleaseOutcome.DependencyUnavailable,
            await limiter.ReleaseAsync(new LimitLease(NewScope(), Guid.CreateVersion7())));
        Assert.False(await limiter.MarkExhaustedAsync(Guid.CreateVersion7(), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Policy_cannot_grant_a_lease_longer_than_recovery_wait()
    {
        var limiter = new RedisAdmissionLimiter(fixture.First,
            new MutableEpochSource(Guid.NewGuid().ToString("N")), Recovery);
        var tooLong = Policy with { LeaseDuration = TimeSpan.FromMilliseconds(1201) };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            limiter.TryAcquireAsync(NewScope(), tooLong, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Invalid_scope_policy_and_quota_cooldown_fail_before_Redis_mutation()
    {
        var limiter = new RedisAdmissionLimiter(fixture.First,
            new MutableEpochSource(Guid.NewGuid().ToString("N")), Recovery);
        var scope = NewScope();
        await Assert.ThrowsAsync<ArgumentException>(() => limiter.TryAcquireAsync(
            scope with { OrganizationId = Guid.Empty }, Policy, Guid.CreateVersion7()));
        await Assert.ThrowsAsync<ArgumentException>(() => limiter.TryAcquireAsync(
            scope, Policy with { ApiKeyRequestsPerMinute = 0 }, Guid.CreateVersion7()));
        await Assert.ThrowsAsync<ArgumentException>(() => limiter.MarkExhaustedAsync(
            Guid.CreateVersion7(), TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(() => limiter.MarkExhaustedAsync(
            Guid.CreateVersion7(), TimeSpan.FromHours(25)));
    }

    private static LimitScope NewScope() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private static async Task WarmAsync(RedisAdmissionLimiter limiter, LimitScope scope, LimitPolicy policy)
    {
        var first = await limiter.TryAcquireAsync(scope, policy, Guid.CreateVersion7());
        Assert.Equal(LimitOutcome.RecoveryWindow, first.Outcome);
        await Task.Delay(1300);
    }

    private sealed class MutableEpochSource(string epoch) : IRedisEpochSource
    {
        public string Epoch { get; set; } = epoch;
        public Task<string> GetCurrentEpochAsync(CancellationToken cancellationToken = default) => Task.FromResult(Epoch);
    }
}

public sealed class RedisLimitFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:8-alpine").Build();
    public IConnectionMultiplexer First { get; private set; } = null!;
    public IConnectionMultiplexer Second { get; private set; } = null!;
    public string ConnectionString => container.GetConnectionString();
    public ConfigurationOptions AdminOptions
    {
        get
        {
            var options = ConfigurationOptions.Parse(ConnectionString);
            options.AllowAdmin = true;
            return options;
        }
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        First = await ConnectionMultiplexer.ConnectAsync(AdminOptions);
        Second = await ConnectionMultiplexer.ConnectAsync(AdminOptions);
    }

    public async Task DisposeAsync()
    {
        await First.DisposeAsync();
        await Second.DisposeAsync();
        await container.DisposeAsync();
    }
}
