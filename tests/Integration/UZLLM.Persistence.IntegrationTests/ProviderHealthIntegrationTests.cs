using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(RedisLimitCollection))]
public sealed class ProviderHealthIntegrationTests(RedisLimitFixture fixture)
{
    [Fact]
    public async Task Credential_401_and_429_do_not_poison_managed_or_other_tenant()
    {
        var options = new ProviderHealthOptions(3, TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));
        var first = new RedisProviderHealthService(fixture.First, options);
        var second = new RedisProviderHealthService(fixture.Second, options);
        var mapping = Guid.CreateVersion7();
        var tenantA = new ProviderHealthTarget(mapping, Guid.CreateVersion7(), Guid.CreateVersion7());
        var tenantAOtherKey = new ProviderHealthTarget(mapping, Guid.CreateVersion7(),
            tenantA.OrganizationId);
        var tenantB = new ProviderHealthTarget(mapping, Guid.CreateVersion7(), Guid.CreateVersion7());
        var managed = new ProviderHealthTarget(mapping, Guid.CreateVersion7());

        await CompleteAsync(first, tenantA, ProviderHealthOutcome.CredentialRejected);
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(tenantA));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(tenantB));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(tenantAOtherKey));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(managed));

        for (var index = 0; index < 3; index++)
            await CompleteAsync(first, tenantB, ProviderHealthOutcome.CredentialThrottled);
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(tenantB));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(managed));

        await Task.Delay(600);
        var credentialProbe = await second.BeginAttemptAsync(tenantA);
        Assert.Equal(ProviderHealthState.Healthy, credentialProbe.State);
        Assert.Equal(ProviderHealthState.Open, (await first.BeginAttemptAsync(tenantA)).State);
        Assert.Equal(ProviderHealthState.Healthy, await first.CheckAsync(managed));
        await second.CompleteAttemptAsync(credentialProbe.Permit!, ProviderHealthOutcome.Success);
        Assert.Equal(ProviderHealthState.Healthy, await first.CheckAsync(tenantA));
    }

    [Fact]
    public async Task Expired_probe_result_cannot_close_a_newer_half_open_probe()
    {
        var options = new ProviderHealthOptions(1, TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
        var first = new RedisProviderHealthService(fixture.First, options);
        var second = new RedisProviderHealthService(fixture.Second, options);
        var target = new ProviderHealthTarget(Guid.CreateVersion7(), Guid.CreateVersion7());
        await CompleteAsync(first, target, ProviderHealthOutcome.EndpointTransientFailure);
        await Task.Delay(500);

        var stale = await first.BeginAttemptAsync(target);
        Assert.Equal(ProviderHealthState.Healthy, stale.State);
        await Task.Delay(500);
        var current = await second.BeginAttemptAsync(target);
        Assert.Equal(ProviderHealthState.Healthy, current.State);
        await first.CompleteAttemptAsync(stale.Permit!, ProviderHealthOutcome.Success);
        Assert.Equal(ProviderHealthState.Open, await first.CheckAsync(target));
        await second.CompleteAttemptAsync(current.Permit!, ProviderHealthOutcome.Success);
        Assert.Equal(ProviderHealthState.Healthy, await first.CheckAsync(target));
    }

    [Fact]
    public async Task Global_outage_blocks_all_credentials_then_admits_one_cross_node_probe()
    {
        var options = new ProviderHealthOptions(3, TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(2));
        var first = new RedisProviderHealthService(fixture.First, options);
        var second = new RedisProviderHealthService(fixture.Second, options);
        var mapping = Guid.CreateVersion7();
        var byok = new ProviderHealthTarget(mapping, Guid.CreateVersion7(), Guid.CreateVersion7());
        var managed = new ProviderHealthTarget(mapping, Guid.CreateVersion7());
        var otherMapping = new ProviderHealthTarget(Guid.CreateVersion7(), managed.CredentialId);

        for (var index = 0; index < 3; index++)
            await CompleteAsync(first, byok, ProviderHealthOutcome.EndpointTransientFailure);
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(byok));
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(managed));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(otherMapping));

        await Task.Delay(500);
        var attempts = await Task.WhenAll(first.BeginAttemptAsync(byok), second.BeginAttemptAsync(managed));
        var admitted = Assert.Single(attempts, value => value.State == ProviderHealthState.Healthy);
        Assert.Single(attempts, value => value.State == ProviderHealthState.Open);
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(managed));

        await first.CompleteAttemptAsync(admitted.Permit!, ProviderHealthOutcome.Neutral);
        var retry = await second.BeginAttemptAsync(managed);
        Assert.Equal(ProviderHealthState.Healthy, retry.State);
        await second.CompleteAttemptAsync(retry.Permit!, ProviderHealthOutcome.EndpointTransientFailure);
        Assert.Equal(ProviderHealthState.Open, await first.CheckAsync(byok));

        await Task.Delay(500);
        var recovery = await first.BeginAttemptAsync(byok);
        Assert.Equal(ProviderHealthState.Healthy, recovery.State);
        await first.CompleteAttemptAsync(recovery.Permit!, ProviderHealthOutcome.Success);
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(managed));
    }

    private static async Task CompleteAsync(IProviderHealthService health, ProviderHealthTarget target,
        ProviderHealthOutcome outcome)
    {
        var attempt = await health.BeginAttemptAsync(target);
        Assert.Equal(ProviderHealthState.Healthy, attempt.State);
        await health.CompleteAttemptAsync(attempt.Permit!, outcome);
    }
}
