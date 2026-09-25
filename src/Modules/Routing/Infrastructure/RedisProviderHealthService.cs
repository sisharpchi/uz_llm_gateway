using StackExchange.Redis;
using UZLLM.Modules.Routing.Contracts;

namespace UZLLM.Modules.Routing.Infrastructure;

public sealed record ProviderHealthOptions(int FailureThreshold, TimeSpan FailureWindow,
    TimeSpan OpenDuration)
{
    public static ProviderHealthOptions Default { get; } =
        new(3, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30));
}

public sealed class RedisProviderHealthService(IConnectionMultiplexer connection,
    ProviderHealthOptions options) : IProviderHealthService
{
    private const string FailureScript = """
        local failures = redis.call('INCR', KEYS[1])
        if failures == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        if failures >= tonumber(ARGV[2]) then
          redis.call('PSETEX', KEYS[2], ARGV[3], 'open')
        end
        return failures
        """;

    public async Task<ProviderHealthState> CheckAsync(Guid providerModelId,
        CancellationToken cancellationToken = default)
    {
        Validate(providerModelId, cancellationToken);
        try
        {
            return await connection.GetDatabase().KeyExistsAsync(OpenKey(providerModelId))
                ? ProviderHealthState.Open : ProviderHealthState.Healthy;
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        { return ProviderHealthState.DependencyUnavailable; }
    }

    public async Task RecordSuccessAsync(Guid providerModelId,
        CancellationToken cancellationToken = default)
    {
        Validate(providerModelId, cancellationToken);
        await connection.GetDatabase().KeyDeleteAsync(FailureKey(providerModelId));
    }

    public async Task RecordTransientFailureAsync(Guid providerModelId,
        CancellationToken cancellationToken = default)
    {
        Validate(providerModelId, cancellationToken);
        await connection.GetDatabase().ScriptEvaluateAsync(FailureScript,
            [FailureKey(providerModelId), OpenKey(providerModelId)],
            [(long)options.FailureWindow.TotalMilliseconds, options.FailureThreshold,
                (long)options.OpenDuration.TotalMilliseconds]);
    }

    private static RedisKey FailureKey(Guid providerModelId) =>
        $"uzllm:routing:health:failure:{providerModelId:N}";
    private static RedisKey OpenKey(Guid providerModelId) =>
        $"uzllm:routing:health:open:{providerModelId:N}";

    private static void Validate(Guid providerModelId, CancellationToken cancellationToken)
    {
        if (providerModelId == Guid.Empty) throw new ArgumentException("A provider model is required.");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
