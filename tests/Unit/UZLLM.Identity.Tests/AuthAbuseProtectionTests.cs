using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Identity.Tests;

public sealed class AuthAbuseProtectionTests
{
    [Theory]
    [InlineData("/management/v1/auth/login")]
    [InlineData("/MANAGEMENT/V1/AUTH/LOGIN/")]
    [InlineData("/management/v1/auth/register")]
    [InlineData("/management/v1/auth/recover")]
    [InlineData("/management/v1/auth/reset-password")]
    [InlineData("/management/v1/team/invitations/accept")]
    [InlineData("/management/v1/organizations/11111111-1111-1111-1111-111111111111/team/invitations")]
    [InlineData("/MANAGEMENT/V1/ORGANIZATIONS/11111111-1111-1111-1111-111111111111/TEAM/INVITATIONS")]
    [InlineData("/management/v1/organizations/11111111-1111-1111-1111-111111111111/team/invitations/")]
    public async Task Protected_auth_and_invitation_routes_reject_oversized_chunked_bodies(string path)
    {
        var limiter = new RecordingLimiter();
        var (handler, services, calls) = Build(limiter, 32);
        await using var _ = services;
        var context = Context(services, path, "{\"email\":\"" + new string('x', 40) + "\"}", null);

        await handler(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal(0, calls());
        Assert.Empty(limiter.Attempts);
    }

    [Fact]
    public async Task Exact_body_limit_is_replayed_to_handler_and_spoofed_forwarding_is_ignored()
    {
        var limiter = new RecordingLimiter();
        var (handler, services, calls) = Build(limiter, 32);
        await using var _ = services;
        var body = "{\"email\":\"a@example.uz\"}" + new string(' ', 8);
        Assert.Equal(32, Encoding.UTF8.GetByteCount(body));
        var context = Context(services, "/management/v1/auth/login", body, 32);
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.9";

        await handler(context);

        Assert.Equal(1, calls());
        Assert.Single(limiter.Attempts);
        Assert.Equal(body, context.Items["body"]);
        Assert.DoesNotContain("a@example.uz", limiter.Attempts[0].Subject!, StringComparison.Ordinal);
        var second = Context(services, "/management/v1/auth/login", "{\"EMAIL\":\" A@Example.UZ \"}", null);
        second.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        second.Request.Headers["X-Forwarded-For"] = "203.0.113.20";
        await handler(second);
        Assert.Equal(limiter.Attempts[0].Ip, limiter.Attempts[1].Ip);
        Assert.Equal(limiter.Attempts[0].Subject, limiter.Attempts[1].Subject);
    }

    [Fact]
    public async Task Redis_unavailable_fails_closed_and_rate_limit_has_retry_after()
    {
        var limiter = new RecordingLimiter { Decision = new(AuthAttemptOutcome.Unavailable, TimeSpan.Zero) };
        var (handler, services, calls) = Build(limiter, 32);
        await using var _ = services;
        var unavailable = Context(services, "/management/v1/auth/recover", "{\"email\":\"a@example.uz\"}", null);
        await handler(unavailable);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.Response.StatusCode);
        Assert.Equal("no-store", unavailable.Response.Headers.CacheControl);
        Assert.Equal(0, calls());

        limiter.Decision = new(AuthAttemptOutcome.RateLimited, TimeSpan.FromSeconds(2.1));
        var limited = Context(services, "/management/v1/auth/recover", "{\"email\":\"a@example.uz\"}", null);
        await handler(limited);
        Assert.Equal(StatusCodes.Status429TooManyRequests, limited.Response.StatusCode);
        Assert.Equal("3", limited.Response.Headers.RetryAfter);
        Assert.Equal(0, calls());
    }

    [Fact]
    public async Task Unprotected_route_does_not_consume_auth_attempts()
    {
        var limiter = new RecordingLimiter();
        var (handler, services, calls) = Build(limiter, 32);
        await using var _ = services;
        await handler(Context(services, "/management/v1/organizations", new string('x', 100), null));
        Assert.Equal(1, calls());
        Assert.Empty(limiter.Attempts);
    }

    private static (RequestDelegate Handler, ServiceProvider Services, Func<int> Calls) Build(
        RecordingLimiter limiter, int maxBodyBytes)
    {
        var services = new ServiceCollection()
            .AddSingleton(new AuthAbusePolicy(3, 2, TimeSpan.FromMinutes(5), maxBodyBytes))
            .AddSingleton<IAuthAttemptLimiter>(limiter).BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var calls = 0;
        builder.UseUzllmAuthAbuseProtection();
        builder.Run(async context =>
        {
            calls++;
            using var reader = new StreamReader(context.Request.Body);
            context.Items["body"] = await reader.ReadToEndAsync();
        });
        return (builder.Build(), services, () => calls);
    }

    private static DefaultHttpContext Context(ServiceProvider services, string path, string body, long? contentLength)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = contentLength;
        return context;
    }

    private sealed class RecordingLimiter : IAuthAttemptLimiter
    {
        public AuthAttemptDecision Decision { get; set; } = new(AuthAttemptOutcome.Admitted, TimeSpan.Zero);
        public List<(string Operation, string Ip, string? Subject)> Attempts { get; } = [];
        public Task<AuthAttemptDecision> TryAcquireAsync(string operation, string ipFingerprint,
            string? subjectFingerprint, AuthAbusePolicy policy, CancellationToken cancellationToken = default)
        {
            Attempts.Add((operation, ipFingerprint, subjectFingerprint));
            return Task.FromResult(Decision);
        }
    }
}
