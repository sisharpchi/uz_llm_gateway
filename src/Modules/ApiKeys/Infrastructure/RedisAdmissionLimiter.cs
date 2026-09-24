using StackExchange.Redis;
using UZLLM.Modules.ApiKeys.Contracts;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public sealed record LimitRecoveryOptions(TimeSpan MaxLeaseHorizon);

public interface IRedisEpochSource
{
    Task<string> GetCurrentEpochAsync(CancellationToken cancellationToken = default);
}

public sealed class RedisServerEpochSource(IConnectionMultiplexer connection) : IRedisEpochSource
{
    public async Task<string> GetCurrentEpochAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var servers = connection.GetEndPoints().Select(endpoint => connection.GetServer(endpoint)).ToArray();
        var primary = servers.FirstOrDefault(server => server.IsConnected && !server.IsReplica)
            ?? throw new InvalidOperationException("No connected Redis primary is available.");
        var info = await primary.InfoAsync("server");
        cancellationToken.ThrowIfCancellationRequested();
        return info.SelectMany(group => group).FirstOrDefault(value => value.Key == "run_id").Value
            is { Length: > 0 } epoch ? epoch
            : throw new InvalidOperationException("Redis did not report its current run ID.");
    }
}

public sealed class RedisAdmissionLimiter(
    IConnectionMultiplexer connection, IRedisEpochSource epochSource,
    LimitRecoveryOptions recoveryOptions) : IDistributedAdmissionLimiter, IProviderQuotaProtection
{
    private const string RecoveryKey = "uzllm:{admission}:epoch";
    private const string AcquireScript = """
        local time = redis.call('TIME')
        local now = time[1] * 1000 + math.floor(time[2] / 1000)
        local marker = redis.call('GET', KEYS[1])
        local oldEpoch, readyAt = nil, nil
        if marker then oldEpoch, readyAt = string.match(marker, '^([^|]+)|(%d+)$') end
        if oldEpoch ~= ARGV[1] then
            readyAt = now + tonumber(ARGV[2])
            redis.call('SET', KEYS[1], ARGV[1] .. '|' .. readyAt)
        else
            readyAt = tonumber(readyAt)
        end
        if now < readyAt then return {5, readyAt - now} end

        local member = ARGV[3]
        redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', now - 60000)
        redis.call('ZREMRANGEBYSCORE', KEYS[3], '-inf', now - 60000)
        redis.call('ZREMRANGEBYSCORE', KEYS[4], '-inf', now)
        redis.call('ZREMRANGEBYSCORE', KEYS[5], '-inf', now)
        local keyLease = redis.call('ZSCORE', KEYS[4], member)
        local projectLease = redis.call('ZSCORE', KEYS[5], member)
        if keyLease and projectLease then return {1, 0} end
        if redis.call('ZSCORE', KEYS[2], member) or redis.call('ZSCORE', KEYS[3], member) then
            return {2, 0}
        end

        if redis.call('ZCARD', KEYS[2]) >= tonumber(ARGV[4])
            or redis.call('ZCARD', KEYS[3]) >= tonumber(ARGV[5]) then
            local keyOldest = redis.call('ZRANGE', KEYS[2], 0, 0, 'WITHSCORES')
            local projectOldest = redis.call('ZRANGE', KEYS[3], 0, 0, 'WITHSCORES')
            local retry = 0
            if #keyOldest > 0 and redis.call('ZCARD', KEYS[2]) >= tonumber(ARGV[4]) then
                retry = math.max(retry, tonumber(keyOldest[2]) + 60000 - now)
            end
            if #projectOldest > 0 and redis.call('ZCARD', KEYS[3]) >= tonumber(ARGV[5]) then
                retry = math.max(retry, tonumber(projectOldest[2]) + 60000 - now)
            end
            return {3, math.max(1, retry)}
        end
        if redis.call('ZCARD', KEYS[4]) >= tonumber(ARGV[6])
            or redis.call('ZCARD', KEYS[5]) >= tonumber(ARGV[7]) then
            local keyOldest = redis.call('ZRANGE', KEYS[4], 0, 0, 'WITHSCORES')
            local projectOldest = redis.call('ZRANGE', KEYS[5], 0, 0, 'WITHSCORES')
            local retry = 0
            if #keyOldest > 0 and redis.call('ZCARD', KEYS[4]) >= tonumber(ARGV[6]) then
                retry = tonumber(keyOldest[2]) - now
            end
            if #projectOldest > 0 and redis.call('ZCARD', KEYS[5]) >= tonumber(ARGV[7]) then
                retry = math.max(retry, tonumber(projectOldest[2]) - now)
            end
            return {4, math.max(1, retry)}
        end
        redis.call('ZADD', KEYS[2], now, member)
        redis.call('ZADD', KEYS[3], now, member)
        redis.call('PEXPIRE', KEYS[2], 61000)
        redis.call('PEXPIRE', KEYS[3], 61000)
        redis.call('ZADD', KEYS[4], now + tonumber(ARGV[8]), member)
        redis.call('ZADD', KEYS[5], now + tonumber(ARGV[8]), member)
        redis.call('PEXPIRE', KEYS[4], tonumber(ARGV[2]) + 1000)
        redis.call('PEXPIRE', KEYS[5], tonumber(ARGV[2]) + 1000)
        return {1, 0}
        """;

    private const string ReleaseScript = """
        local keyRemoved = redis.call('ZREM', KEYS[1], ARGV[1])
        local projectRemoved = redis.call('ZREM', KEYS[2], ARGV[1])
        if keyRemoved > 0 or projectRemoved > 0 then return 1 end
        return 0
        """;

    private const string QuotaMarkScript = """
        local remaining = redis.call('PTTL', KEYS[1])
        if remaining < tonumber(ARGV[1]) then
            redis.call('SET', KEYS[1], 'exhausted', 'PX', ARGV[1])
        end
        return 1
        """;

    public async Task<LimitDecision> TryAcquireAsync(LimitScope scope, LimitPolicy policy, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        Validate(scope, policy, requestId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var epoch = await epochSource.GetCurrentEpochAsync(cancellationToken);
            var raw = (RedisResult[]?)await connection.GetDatabase().ScriptEvaluateAsync(AcquireScript,
                Keys(scope),
                [epoch, Milliseconds(recoveryOptions.MaxLeaseHorizon), requestId.ToString("N"),
                    policy.ApiKeyRequestsPerMinute, policy.ProjectRequestsPerMinute,
                    policy.ApiKeyConcurrency, policy.ProjectConcurrency, Milliseconds(policy.LeaseDuration)]);
            if (raw is null || raw.Length != 2)
                return new LimitDecision(LimitOutcome.DependencyUnavailable, null, null);
            var outcome = (int)(long)raw[0] switch
            {
                1 => LimitOutcome.Admitted,
                2 => LimitOutcome.Duplicate,
                3 => LimitOutcome.RateLimited,
                4 => LimitOutcome.ConcurrencyLimited,
                5 => LimitOutcome.RecoveryWindow,
                _ => LimitOutcome.DependencyUnavailable
            };
            return new LimitDecision(outcome,
                outcome == LimitOutcome.Admitted ? new LimitLease(scope, requestId) : null,
                outcome is LimitOutcome.RateLimited or LimitOutcome.ConcurrencyLimited or LimitOutcome.RecoveryWindow
                    ? TimeSpan.FromMilliseconds((long)raw[1]) : null);
        }
        catch (RedisException) { return new LimitDecision(LimitOutcome.DependencyUnavailable, null, null); }
        catch (InvalidOperationException) { return new LimitDecision(LimitOutcome.DependencyUnavailable, null, null); }
        catch (ArgumentException) { return new LimitDecision(LimitOutcome.DependencyUnavailable, null, null); }
    }

    public async Task<LimitReleaseOutcome> ReleaseAsync(LimitLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidateScope(lease.Scope);
        if (lease.RequestId == Guid.Empty) throw new ArgumentException("A request ID is required.", nameof(lease));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var keys = Keys(lease.Scope);
            var removed = (long)await connection.GetDatabase().ScriptEvaluateAsync(ReleaseScript,
                [keys[3], keys[4]], [lease.RequestId.ToString("N")]);
            return removed == 1 ? LimitReleaseOutcome.Released : LimitReleaseOutcome.Missing;
        }
        catch (RedisException) { return LimitReleaseOutcome.DependencyUnavailable; }
        catch (InvalidOperationException) { return LimitReleaseOutcome.DependencyUnavailable; }
        catch (ArgumentException) { return LimitReleaseOutcome.DependencyUnavailable; }
    }

    public async Task<ProviderQuotaDecision> CheckAsync(Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("A credential ID is required.", nameof(credentialId));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ttl = (long)await connection.GetDatabase().ExecuteAsync("PTTL", QuotaKey(credentialId));
            return ttl == -2
                ? new ProviderQuotaDecision(ProviderQuotaOutcome.Available, null)
                : new ProviderQuotaDecision(ProviderQuotaOutcome.Exhausted,
                    ttl >= 0 ? TimeSpan.FromMilliseconds(ttl) : null);
        }
        catch (RedisException) { return new ProviderQuotaDecision(ProviderQuotaOutcome.DependencyUnavailable, null); }
        catch (InvalidOperationException) { return new ProviderQuotaDecision(ProviderQuotaOutcome.DependencyUnavailable, null); }
        catch (ArgumentException) { return new ProviderQuotaDecision(ProviderQuotaOutcome.DependencyUnavailable, null); }
    }

    public async Task<bool> MarkExhaustedAsync(Guid credentialId, TimeSpan retryAfter,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || retryAfter <= TimeSpan.Zero || retryAfter > TimeSpan.FromHours(24))
            throw new ArgumentException("A credential and bounded cooldown are required.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await connection.GetDatabase().ScriptEvaluateAsync(QuotaMarkScript,
                [QuotaKey(credentialId)], [Milliseconds(retryAfter)]);
            return true;
        }
        catch (RedisException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private void Validate(LimitScope scope, LimitPolicy policy, Guid requestId)
    {
        ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(policy);
        if (requestId == Guid.Empty || policy.ApiKeyRequestsPerMinute <= 0
            || policy.ProjectRequestsPerMinute <= 0 || policy.ApiKeyConcurrency <= 0
            || policy.ProjectConcurrency <= 0 || policy.LeaseDuration <= TimeSpan.Zero
            || policy.LeaseDuration < TimeSpan.FromMilliseconds(1)
            || recoveryOptions.MaxLeaseHorizon < TimeSpan.FromMilliseconds(1)
            || policy.LeaseDuration > recoveryOptions.MaxLeaseHorizon)
            throw new ArgumentException("Valid rates, concurrency, request ID, and a lease within the recovery horizon are required.");
    }

    private static void ValidateScope(LimitScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.OrganizationId == Guid.Empty || scope.ProjectId == Guid.Empty || scope.ApiKeyId == Guid.Empty)
            throw new ArgumentException("Organization, project, and API key IDs are required.", nameof(scope));
    }

    private static RedisKey[] Keys(LimitScope scope)
    {
        var project = $"{scope.OrganizationId:N}:{scope.ProjectId:N}";
        var key = $"{project}:{scope.ApiKeyId:N}";
        return [RecoveryKey, $"uzllm:{{admission}}:rpm:key:{key}",
            $"uzllm:{{admission}}:rpm:project:{project}",
            $"uzllm:{{admission}}:active:key:{key}",
            $"uzllm:{{admission}}:active:project:{project}"];
    }

    private static RedisKey QuotaKey(Guid credentialId) =>
        $"uzllm:{{admission}}:provider-quota:{credentialId:N}";

    private static long Milliseconds(TimeSpan duration) => checked((long)duration.TotalMilliseconds);
}
