using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace UZLLM.Modules.Identity.Infrastructure;

public sealed record AuthAbusePolicy(int IpAttempts, int SubjectAttempts, TimeSpan Window,
    int MaxBodyBytes, string KeyPrefix = "uzllm:auth")
{
    public static AuthAbusePolicy Default { get; } = new(600, 10, TimeSpan.FromMinutes(5), 8192);
}

public enum AuthAttemptOutcome { Admitted, RateLimited, Unavailable }

public readonly record struct AuthAttemptDecision(AuthAttemptOutcome Outcome, TimeSpan RetryAfter);

public interface IAuthAttemptLimiter
{
    Task<AuthAttemptDecision> TryAcquireAsync(string operation, string ipFingerprint,
        string? subjectFingerprint, AuthAbusePolicy policy, CancellationToken cancellationToken = default);
}

public sealed class RedisAuthAttemptLimiter(IConnectionMultiplexer connection) : IAuthAttemptLimiter
{
    private const string Script = """
        local ip_count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if ip_count >= tonumber(ARGV[1]) then return {1, redis.call('PTTL', KEYS[1])} end
        if #KEYS > 1 then
            local subject_count = tonumber(redis.call('GET', KEYS[2]) or '0')
            if subject_count >= tonumber(ARGV[2]) then return {1, redis.call('PTTL', KEYS[2])} end
        end
        if redis.call('INCR', KEYS[1]) == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[3]) end
        if #KEYS > 1 and redis.call('INCR', KEYS[2]) == 1 then
            redis.call('PEXPIRE', KEYS[2], ARGV[3])
        end
        return {0, 0}
        """;

    public async Task<AuthAttemptDecision> TryAcquireAsync(string operation, string ipFingerprint,
        string? subjectFingerprint, AuthAbusePolicy policy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keys = subjectFingerprint is null
            ? new RedisKey[] { $"{policy.KeyPrefix}:{operation}:ip:{ipFingerprint}" }
            : new RedisKey[] { $"{policy.KeyPrefix}:{operation}:ip:{ipFingerprint}",
                $"{policy.KeyPrefix}:{operation}:subject:{subjectFingerprint}" };
        try
        {
            var result = (RedisResult[]?)await connection.GetDatabase().ScriptEvaluateAsync(Script,
                keys, [(RedisValue)policy.IpAttempts, policy.SubjectAttempts,
                    (long)policy.Window.TotalMilliseconds]);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not { Length: 2 }) return new(AuthAttemptOutcome.Unavailable, TimeSpan.Zero);
            return (long)result[0] == 0
                ? new(AuthAttemptOutcome.Admitted, TimeSpan.Zero)
                : new(AuthAttemptOutcome.RateLimited, TimeSpan.FromMilliseconds(Math.Max(1000, (long)result[1])));
        }
        catch (Exception exception) when (exception is RedisException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            return new(AuthAttemptOutcome.Unavailable, TimeSpan.Zero);
        }
    }
}

public static class AuthAbuseProtection
{
    public static IApplicationBuilder UseUzllmAuthAbuseProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Method != HttpMethods.Post || !TryOperation(context.Request.Path, out var operation))
            {
                await next(context);
                return;
            }

            var policy = context.RequestServices.GetRequiredService<AuthAbusePolicy>();
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.ContentLength > policy.MaxBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            byte[] body;
            try { body = await ReadBoundedBodyAsync(context.Request, policy.MaxBodyBytes, context.RequestAborted); }
            catch (BodyTooLargeException)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            var subject = ReadSubject(body, operation);
            var ip = context.Connection.RemoteIpAddress ?? IPAddress.None;
            var ipFingerprint = Fingerprint(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString());
            var subjectFingerprint = subject is null ? null : Fingerprint(subject);
            var limiter = context.RequestServices.GetRequiredService<IAuthAttemptLimiter>();
            var decision = await limiter.TryAcquireAsync(operation, ipFingerprint, subjectFingerprint,
                policy, context.RequestAborted);
            if (decision.Outcome == AuthAttemptOutcome.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            if (decision.Outcome == AuthAttemptOutcome.RateLimited)
            {
                context.Response.Headers.RetryAfter = Math.Ceiling(decision.RetryAfter.TotalSeconds).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }

            await using var replay = new MemoryStream(body, writable: false);
            context.Request.Body = replay;
            await next(context);
        });

    private static bool TryOperation(PathString path, out string operation)
    {
        var value = (path.Value ?? string.Empty).TrimEnd('/');
        const string authPrefix = "/management/v1/auth/";
        if (value.StartsWith(authPrefix, StringComparison.OrdinalIgnoreCase))
        {
            operation = value[authPrefix.Length..].ToLowerInvariant();
            return operation is "register" or "login" or "recover" or "reset-password" or "verify-email";
        }
        if (value.Equals("/management/v1/team/invitations/accept", StringComparison.OrdinalIgnoreCase))
        {
            operation = "invite-accept";
            return true;
        }
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 6
            && segments[0].Equals("management", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("organizations", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[3], out _)
            && segments[4].Equals("team", StringComparison.OrdinalIgnoreCase)
            && segments[5].Equals("invitations", StringComparison.OrdinalIgnoreCase))
        {
            operation = "invite-create";
            return true;
        }
        operation = string.Empty;
        return false;
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) return destination.ToArray();
                if (destination.Length + read > limit) throw new BodyTooLargeException();
                destination.Write(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static string? ReadSubject(byte[] body, string operation)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var field = operation is "reset-password" or "verify-email" or "invite-accept" ? "token" : "email";
            string? value = null;
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals(field, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                    value = property.Value.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
        catch (JsonException) { return null; }
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class BodyTooLargeException : Exception;
}
