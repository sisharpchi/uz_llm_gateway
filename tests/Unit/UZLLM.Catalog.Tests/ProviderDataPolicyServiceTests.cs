using UZLLM.Modules.Catalog.Application;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Catalog.Tests;

public sealed class ProviderDataPolicyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static ProviderDataPolicyInput Input => new("Unknown", "NoTrainingReported", "ProviderReported",
        [" US ", "US", "EU"], " Public API only ", " Depends on account and endpoint settings ",
        "https://example.com/policy", Now.AddDays(-1));

    [Fact]
    public async Task PublishAsync_preserves_conditional_unknown_and_dated_provenance()
    {
        var store = new PolicyStore();
        var service = new ProviderDataPolicyService(store, new FixedCatalogTimeProvider(Now));
        var provider = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var result = await service.PublishAsync(provider, actor, Input);
        Assert.Equal("Unknown", result.Retention);
        Assert.Equal("NoTrainingReported", result.Training);
        Assert.Equal(new[] { "EU", "US" }, result.Regions);
        Assert.Equal("Public API only", result.Scope);
        Assert.Equal("Depends on account and endpoint settings", result.Conditions);
        Assert.Equal(Input.SourceObservedAt, result.SourceObservedAt);
        Assert.Equal(Now, result.PublishedAt);
        Assert.Equal(actor, result.PublishedByAccountId);
        Assert.Equal(provider, result.ProviderId);
        Assert.Equal(result.Id, Assert.Single(store.Values).Id);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        yield return [Input with { Retention = "ZDR" }];
        yield return [Input with { Training = "0" }];
        yield return [Input with { Residency = "Guaranteed" }];
        yield return [Input with { Regions = null! }];
        yield return [Input with { Regions = Enumerable.Range(0, 17).Select(index => $"region-{index}").ToArray() }];
        yield return [Input with { Regions = [new string('r', 81)] }];
        yield return [Input with { Regions = [" "] }];
        yield return [Input with { Regions = [] }];
        yield return [Input with { Residency = "Unknown" }];
        yield return [Input with { Scope = "short" }];
        yield return [Input with { Conditions = new string('x', 2001) }];
        yield return [Input with { Conditions = "contains\ncontrol" }];
        yield return [Input with { SourceUrl = "http://example.com/policy" }];
        yield return [Input with { SourceUrl = "https://127.0.0.1/policy" }];
        yield return [Input with { SourceUrl = "https://localhost/policy" }];
        yield return [Input with { SourceUrl = "https://user:secret@example.com/policy" }];
        yield return [Input with { SourceUrl = "https://example.com/policy?token=secret" }];
        yield return [Input with { SourceObservedAt = Now.AddTicks(1) }];
        yield return [Input with { SourceObservedAt = Now.ToOffset(TimeSpan.FromHours(5)) }];
        yield return [Input with { SourceObservedAt = DateTimeOffset.UnixEpoch }];
        yield return [Input with { SourceObservedAt = DateTimeOffset.UnixEpoch.AddTicks(1) }];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task PublishAsync_rejects_invalid_claims_provenance_or_bounds_before_store(ProviderDataPolicyInput input)
    {
        var store = new PolicyStore();
        var service = new ProviderDataPolicyService(store, new FixedCatalogTimeProvider(Now));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.PublishAsync(Guid.NewGuid(), Guid.NewGuid(), input));
        Assert.Empty(store.Values);
    }

    [Fact]
    public async Task PublishAsync_accepts_exact_limits_and_current_utc_observation()
    {
        var store = new PolicyStore();
        var service = new ProviderDataPolicyService(store, new FixedCatalogTimeProvider(Now));
        var result = await service.PublishAsync(Guid.NewGuid(), Guid.NewGuid(), Input with
        {
            Retention = "NoRetentionReported", Training = "MayTrain", Regions = Enumerable.Range(0, 16)
                .Select(index => index.ToString("D2") + new string('r', 78)).ToArray(),
            Scope = new string('s', 500), Conditions = new string('c', 2000), SourceObservedAt = Now
        });
        Assert.Equal(16, result.Regions.Count);
        Assert.Equal(500, result.Scope.Length);
        Assert.Equal(Now, result.SourceObservedAt);
    }

    [Fact]
    public async Task FindCurrentAsync_has_no_invented_default_or_policy()
    {
        var service = new ProviderDataPolicyService(new PolicyStore(), new FixedCatalogTimeProvider(Now));
        Assert.Null(await service.FindCurrentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task PublishAsync_normalizes_observation_to_database_precision_for_replay_order()
    {
        var service = new ProviderDataPolicyService(new PolicyStore(), new FixedCatalogTimeProvider(Now.AddTicks(7)));
        var result = await service.PublishAsync(Guid.NewGuid(), Guid.NewGuid(), Input with { SourceObservedAt = Now.AddTicks(-1) });
        Assert.Equal(Now.AddTicks(-10), result.SourceObservedAt);
        Assert.Equal(Now, result.PublishedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListAsync_rejects_unbounded_history(int limit)
    {
        var service = new ProviderDataPolicyService(new PolicyStore(), new FixedCatalogTimeProvider(Now));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(Guid.NewGuid(), limit));
    }

    [Fact]
    public async Task PublishAsync_requires_nonempty_provider_and_actor()
    {
        var store = new PolicyStore();
        var service = new ProviderDataPolicyService(store, new FixedCatalogTimeProvider(Now));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishAsync(Guid.Empty, Guid.NewGuid(), Input));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishAsync(Guid.NewGuid(), Guid.Empty, Input));
        Assert.Empty(store.Values);
    }

    private sealed class PolicyStore : IProviderDataPolicyStore
    {
        public List<ProviderDataPolicy> Values { get; } = [];
        public Task AppendAsync(ProviderDataPolicy policy, CancellationToken cancellationToken)
        { Values.Add(policy); return Task.CompletedTask; }
        public Task<CatalogProvider?> FindProviderAsync(string code, CancellationToken cancellationToken) => Task.FromResult<CatalogProvider?>(null);
        public Task<ProviderDataPolicy?> FindVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Values.SingleOrDefault(value => value.Id == id));
        public Task<ProviderDataPolicy?> FindCurrentAsync(Guid providerId, CancellationToken cancellationToken) =>
            Task.FromResult(Values.Where(value => value.ProviderId == providerId).OrderByDescending(value => value.SourceObservedAt).FirstOrDefault());
        public Task<IReadOnlyList<ProviderDataPolicy>> ListAsync(Guid providerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderDataPolicy>>(Values.Where(value => value.ProviderId == providerId).Take(limit).ToArray());
    }
}
