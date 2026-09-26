using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Domain;
using UZLLM.Modules.Routing.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(RedisLimitCollection))]
public sealed class ProviderPerformanceIntegrationTests(RedisLimitFixture fixture)
{
    [Fact]
    public async Task Two_gateway_nodes_share_bounded_window_and_stale_samples_expire_from_routing()
    {
        var clock = new ShiftableClock(DateTimeOffset.UtcNow);
        var options = new ProviderPerformanceOptions(TimeSpan.FromMinutes(5), 2, 4,
            0.10m, PerformanceRouteWeights.Default);
        var first = new RedisProviderPerformanceService(fixture.First, clock, options);
        var second = new RedisProviderPerformanceService(fixture.Second, clock, options);
        var mappingId = Guid.CreateVersion7();
        for (var index = 0; index < 5; index++)
        {
            await first.RecordAsync(new ProviderPerformanceObservation(Guid.CreateVersion7(),
                mappingId, false, true, 100 + index, 10, 1000));
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        await first.RecordAsync(new ProviderPerformanceObservation(Guid.CreateVersion7(),
            mappingId, false, false, null, null, 1000));
        await first.RecordAsync(new ProviderPerformanceObservation(Guid.CreateVersion7(),
            mappingId, true, true, 5, 20, 1000));

        var window = await second.ReadAsync(mappingId, false);
        Assert.NotNull(window);
        Assert.Equal(4, window.Attempts);
        Assert.Equal(3, window.SuccessfulSamples);
        Assert.Equal(103m, window.MedianTimeToFirstTokenMs);
        Assert.Equal(10m, window.MedianOutputTokensPerSecond);
        Assert.Equal(0.25m, window.ErrorRate);
        Assert.Equal(1, (await second.ReadAsync(mappingId, true))!.Attempts);

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await second.ReadAsync(mappingId, false));
    }

    private sealed class ShiftableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
