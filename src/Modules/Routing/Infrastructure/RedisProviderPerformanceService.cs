using System.Text.Json;
using StackExchange.Redis;
using UZLLM.Modules.Routing.Contracts;

namespace UZLLM.Modules.Routing.Infrastructure;

public sealed record ProviderPerformanceOptions(TimeSpan Window, int MinimumSamples,
    int MaximumSamples, decimal Hysteresis,
    UZLLM.Modules.Routing.Domain.PerformanceRouteWeights Weights)
{
    public static ProviderPerformanceOptions Default { get; } = new(
        TimeSpan.FromMinutes(5), 10, 256, 0.10m,
        UZLLM.Modules.Routing.Domain.PerformanceRouteWeights.Default);

    public void Validate()
    {
        if (Window <= TimeSpan.Zero || Window > TimeSpan.FromHours(1)
            || MinimumSamples < 2 || MaximumSamples < MinimumSamples
            || MaximumSamples > 1024 || Hysteresis is < 0 or > 1)
            throw new ArgumentException("Invalid provider-performance options.");
        Weights.Validate();
    }
}

public sealed class RedisProviderPerformanceService(IConnectionMultiplexer connection,
    TimeProvider clock, ProviderPerformanceOptions options) : IProviderPerformanceService
{
    private const string RecordScript = """
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', '(' .. ARGV[3])
        local count = redis.call('ZCARD', KEYS[1])
        local maximum = tonumber(ARGV[4])
        if count > maximum then
          redis.call('ZREMRANGEBYRANK', KEYS[1], 0, count - maximum - 1)
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[5])
        return 1
        """;

    public async Task RecordAsync(ProviderPerformanceObservation observation,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (observation.AttemptId == Guid.Empty || observation.ProviderModelId == Guid.Empty
            || observation.DurationMs < 1 || observation.TimeToFirstTokenMs is < 0
            || observation.OutputTokens is < 0)
            throw new ArgumentException("Invalid provider-performance observation.");
        var now = clock.GetUtcNow();
        decimal? throughput = observation.Succeeded && observation.OutputTokens is > 0
            ? decimal.Round((decimal)observation.OutputTokens.Value * 1000m
                / observation.DurationMs, 4)
            : null;
        var payload = JsonSerializer.Serialize(new StoredObservation(observation.AttemptId,
            now, observation.Succeeded, observation.TimeToFirstTokenMs, throughput));
        await connection.GetDatabase().ScriptEvaluateAsync(RecordScript,
            [Key(observation.ProviderModelId, observation.IsStream)],
            [now.ToUnixTimeMilliseconds(), payload,
                now.Subtract(options.Window).ToUnixTimeMilliseconds(), options.MaximumSamples,
                (long)(options.Window * 2).TotalMilliseconds]);
    }

    public async Task<ProviderPerformanceWindow?> ReadAsync(Guid providerModelId, bool isStream,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (providerModelId == Guid.Empty) throw new ArgumentException("Provider model is required.");
        var now = clock.GetUtcNow();
        RedisValue[] values;
        try
        {
            values = await connection.GetDatabase().SortedSetRangeByScoreAsync(Key(providerModelId, isStream),
                now.Subtract(options.Window).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds(),
                order: Order.Descending, take: options.MaximumSamples);
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        { return null; }
        var samples = values.Select(value =>
        {
            try { return JsonSerializer.Deserialize<StoredObservation>((string)value!); }
            catch (JsonException) { return null; }
        }).Where(value => value is not null && value.ObservedAt >= now - options.Window
            && value.ObservedAt <= now).DistinctBy(value => value!.AttemptId)
            .Cast<StoredObservation>().ToArray();
        if (samples.Length == 0) return null;
        var successful = samples.Where(value => value.Succeeded
            && value.TimeToFirstTokenMs is > 0 && value.TokensPerSecond is > 0).ToArray();
        return new ProviderPerformanceWindow(samples.Length, successful.Length,
            Median(successful.Select(value => (decimal)value.TimeToFirstTokenMs!.Value)),
            Median(successful.Select(value => value.TokensPerSecond!.Value)),
            (decimal)samples.Count(value => !value.Succeeded) / samples.Length,
            samples.Max(value => value.ObservedAt));
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private static RedisKey Key(Guid providerModelId, bool isStream) =>
        $"uzllm:routing:performance:{providerModelId:N}:{(isStream ? "stream" : "nonstream")}";

    private sealed record StoredObservation(Guid AttemptId, DateTimeOffset ObservedAt,
        bool Succeeded, long? TimeToFirstTokenMs, decimal? TokensPerSecond);
}
