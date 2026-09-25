using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(RedisLimitCollection))]
public sealed class ProviderHealthIntegrationTests(RedisLimitFixture fixture)
{
    [Fact]
    public async Task Two_gateway_nodes_share_bounded_open_circuit_and_recover()
    {
        var options = new ProviderHealthOptions(3, TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(400));
        var first = new RedisProviderHealthService(fixture.First, options);
        var second = new RedisProviderHealthService(fixture.Second, options);
        var failing = Guid.CreateVersion7();
        var healthy = Guid.CreateVersion7();

        await first.RecordTransientFailureAsync(failing);
        await second.RecordTransientFailureAsync(failing);
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(failing));
        await first.RecordTransientFailureAsync(failing);
        Assert.Equal(ProviderHealthState.Open, await second.CheckAsync(failing));
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(healthy));

        await Task.Delay(500);
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(failing));
        await second.RecordSuccessAsync(failing);
        await first.RecordTransientFailureAsync(failing);
        Assert.Equal(ProviderHealthState.Healthy, await second.CheckAsync(failing));
    }
}
