using StackExchange.Redis;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(RedisLimitCollection))]
public sealed class AuthAbuseRedisIntegrationTests(RedisLimitFixture fixture)
{
    [Fact]
    public async Task Concurrent_nodes_enforce_subject_and_IP_attempt_budgets_atomically()
    {
        var policy = new AuthAbusePolicy(3, 2, TimeSpan.FromMinutes(1), 8192,
            $"test:auth:{Guid.NewGuid():N}");
        var first = new RedisAuthAttemptLimiter(fixture.First);
        var second = new RedisAuthAttemptLimiter(fixture.Second);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            (index % 2 == 0 ? first : second).TryAcquireAsync("login", "ip-one", "account-one", policy)));

        Assert.Equal(2, attempts.Count(value => value.Outcome == AuthAttemptOutcome.Admitted));
        Assert.Equal(10, attempts.Count(value => value.Outcome == AuthAttemptOutcome.RateLimited));
        Assert.All(attempts.Where(value => value.Outcome == AuthAttemptOutcome.RateLimited),
            value => Assert.True(value.RetryAfter > TimeSpan.Zero));
        Assert.Equal(AuthAttemptOutcome.RateLimited,
            (await second.TryAcquireAsync("login", "ip-two", "account-one", policy)).Outcome);
        Assert.Equal(AuthAttemptOutcome.Admitted,
            (await first.TryAcquireAsync("login", "ip-one", "account-two", policy)).Outcome);
        Assert.Equal(AuthAttemptOutcome.RateLimited,
            (await second.TryAcquireAsync("login", "ip-one", "account-three", policy)).Outcome);
        Assert.Equal(AuthAttemptOutcome.Admitted,
            (await first.TryAcquireAsync("recover", "ip-one", "account-one", policy)).Outcome);
    }

    [Fact]
    public async Task Redis_connection_loss_denies_auth_attempts_instead_of_bypassing_limits()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.AdminOptions);
        var limiter = new RedisAuthAttemptLimiter(connection);
        await connection.DisposeAsync();
        var policy = new AuthAbusePolicy(3, 2, TimeSpan.FromMinutes(1), 8192,
            $"test:auth:{Guid.NewGuid():N}");

        var result = await limiter.TryAcquireAsync("recover", "ip", "account", policy);

        Assert.Equal(AuthAttemptOutcome.Unavailable, result.Outcome);
    }
}
