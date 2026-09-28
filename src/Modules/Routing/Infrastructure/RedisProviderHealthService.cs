using StackExchange.Redis;
using UZLLM.Modules.Routing.Contracts;

namespace UZLLM.Modules.Routing.Infrastructure;

public sealed record ProviderHealthOptions(int FailureThreshold, TimeSpan FailureWindow,
    TimeSpan OpenDuration, TimeSpan? ProbeLease = null)
{
    public static ProviderHealthOptions Default { get; } =
        new(3, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
}

public sealed class RedisProviderHealthService(IConnectionMultiplexer connection,
    ProviderHealthOptions options) : IProviderHealthService
{
    // The short open key blocks traffic. After it expires, a recovery marker
    // permits exactly one cross-node probe until success or probe-lease expiry.
    private const string CheckScript = """
        for i = 1, 2 do
          local base = (i - 1) * 3
          if redis.call('EXISTS', KEYS[base + 1]) == 1 then return 0 end
          if redis.call('EXISTS', KEYS[base + 2]) == 1 and
             redis.call('EXISTS', KEYS[base + 3]) == 1 then return 0 end
        end
        return 1
        """;

    private const string BeginScript = """
        for i = 1, 2 do
          local base = (i - 1) * 3
          if redis.call('EXISTS', KEYS[base + 1]) == 1 then return 0 end
          if redis.call('EXISTS', KEYS[base + 2]) == 1 and
             redis.call('EXISTS', KEYS[base + 3]) == 1 then return 0 end
        end
        for i = 1, 2 do
          local base = (i - 1) * 3
          if redis.call('EXISTS', KEYS[base + 2]) == 1 then
            redis.call('PSETEX', KEYS[base + 3], ARGV[1], ARGV[2])
          end
        end
        return 1
        """;

    private const string CompleteScript = """
        local function owns(marker, probe)
          if redis.call('EXISTS', marker) == 0 then return true end
          return redis.call('GET', probe) == ARGV[1]
        end
        local function release(probe)
          if redis.call('GET', probe) == ARGV[1] then redis.call('DEL', probe) end
        end
        local function success(base)
          redis.call('DEL', KEYS[base + 1], KEYS[base + 2], KEYS[base + 3])
          release(KEYS[base + 4])
        end
        local function failure(base, immediate)
          local count = redis.call('INCR', KEYS[base + 1])
          if count == 1 then redis.call('PEXPIRE', KEYS[base + 1], ARGV[2]) end
          if immediate or redis.call('EXISTS', KEYS[base + 3]) == 1 or
             count >= tonumber(ARGV[3]) then
            redis.call('PSETEX', KEYS[base + 2], ARGV[4], 'open')
            redis.call('PSETEX', KEYS[base + 3], ARGV[6], 'recovering')
          end
          release(KEYS[base + 4])
        end
        local globalOwns = owns(KEYS[3], KEYS[4])
        local credentialOwns = owns(KEYS[7], KEYS[8])
        if ARGV[5] == 'Success' then
          if globalOwns then success(0) end
          if credentialOwns then success(4) end
        elseif ARGV[5] == 'EndpointTransientFailure' then
          if globalOwns then failure(0, false) end
          release(KEYS[8])
        elseif ARGV[5] == 'CredentialRejected' or ARGV[5] == 'CredentialThrottled' then
          release(KEYS[4])
          if credentialOwns then failure(4, ARGV[5] == 'CredentialRejected') end
        else
          release(KEYS[4])
          release(KEYS[8])
        end
        return 1
        """;

    public async Task<ProviderHealthState> CheckAsync(ProviderHealthTarget target,
        CancellationToken cancellationToken = default)
    {
        Validate(target, cancellationToken);
        try
        {
            var result = await connection.GetDatabase().ScriptEvaluateAsync(CheckScript, CheckKeys(target));
            return (long)result == 1 ? ProviderHealthState.Healthy : ProviderHealthState.Open;
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        { return ProviderHealthState.DependencyUnavailable; }
    }

    public async Task<ProviderHealthAttempt> BeginAttemptAsync(ProviderHealthTarget target,
        CancellationToken cancellationToken = default)
    {
        Validate(target, cancellationToken);
        var token = Guid.CreateVersion7();
        try
        {
            var result = await connection.GetDatabase().ScriptEvaluateAsync(BeginScript, CheckKeys(target),
                [(long)(options.ProbeLease ?? TimeSpan.FromMinutes(15)).TotalMilliseconds,
                    token.ToString("N")]);
            return (long)result == 1
                ? new ProviderHealthAttempt(ProviderHealthState.Healthy, new ProviderHealthPermit(target, token))
                : new ProviderHealthAttempt(ProviderHealthState.Open, null);
        }
        catch (Exception ex) when (ex is RedisException or InvalidOperationException)
        { return new ProviderHealthAttempt(ProviderHealthState.DependencyUnavailable, null); }
    }

    public async Task CompleteAttemptAsync(ProviderHealthPermit permit, ProviderHealthOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permit);
        Validate(permit.Target, cancellationToken);
        await connection.GetDatabase().ScriptEvaluateAsync(CompleteScript, CompletionKeys(permit.Target),
            [permit.Token.ToString("N"), (long)options.FailureWindow.TotalMilliseconds,
                options.FailureThreshold, (long)options.OpenDuration.TotalMilliseconds,
                outcome.ToString(), (long)TimeSpan.FromDays(1).TotalMilliseconds]);
    }

    private static RedisKey[] CheckKeys(ProviderHealthTarget target) =>
    [
        GlobalKey(target, "open"), GlobalKey(target, "recovering"), GlobalKey(target, "probe"),
        CredentialKey(target, "open"), CredentialKey(target, "recovering"), CredentialKey(target, "probe")
    ];

    private static RedisKey[] CompletionKeys(ProviderHealthTarget target) =>
    [
        GlobalKey(target, "failure"), GlobalKey(target, "open"),
        GlobalKey(target, "recovering"), GlobalKey(target, "probe"),
        CredentialKey(target, "failure"), CredentialKey(target, "open"),
        CredentialKey(target, "recovering"), CredentialKey(target, "probe")
    ];

    private static RedisKey GlobalKey(ProviderHealthTarget target, string kind) =>
        $"uzllm:routing:health:v2:{{{target.ProviderModelId:N}}}:global:{kind}";

    private static RedisKey CredentialKey(ProviderHealthTarget target, string kind) =>
        $"uzllm:routing:health:v2:{{{target.ProviderModelId:N}}}:" +
        $"{(target.OrganizationId is null ? "managed" : "byok")}:" +
        $"{target.OrganizationId?.ToString("N") ?? "platform"}:{target.CredentialId:N}:{kind}";

    private static void Validate(ProviderHealthTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ProviderModelId == Guid.Empty || target.CredentialId == Guid.Empty
            || target.OrganizationId == Guid.Empty)
            throw new ArgumentException("A provider mapping and credential are required.");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
