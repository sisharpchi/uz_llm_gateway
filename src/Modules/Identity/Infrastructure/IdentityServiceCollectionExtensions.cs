using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Application;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Modules.Identity.Infrastructure;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmIdentity(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddScoped<IIdentityStore, PostgreSqlIdentityStore>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ICsrfTokenValidator, CsrfTokenValidator>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IIdentitySecretProtector, DataProtectionIdentitySecretProtector>();
        services.AddSingleton<ITotpAuthenticator, TotpAuthenticator>();
        return services;
    }

    public static IApplicationBuilder UseUzllmManagementSession(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var sessionToken = context.Request.Cookies[IdentityCookieNames.Session];
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                var identityService = context.RequestServices.GetRequiredService<IIdentityService>();
                var identity = await identityService.AuthenticateSessionAsync(sessionToken, context.RequestAborted);
                if (identity is not null)
                {
                    var claims = new List<System.Security.Claims.Claim>
                    {
                        new(System.Security.Claims.ClaimTypes.NameIdentifier, identity.AccountId.ToString("N")),
                        new(System.Security.Claims.ClaimTypes.Email, identity.Email),
                        new("uzllm:email_verified", identity.IsEmailVerified.ToString())
                    };
                    if (identity.IsOperator)
                    {
                        claims.Add(new System.Security.Claims.Claim("uzllm:operator", "true"));
                    }

                    context.User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(claims, "uzllm-session"));
                }
            }

            await next(context);
        });
    }

    public static IEndpointRouteBuilder MapUzllmIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/management/v1/auth");
        auth.MapPost("/register", async (RegisterRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            try
            {
                _ = await identityService.RegisterAsync(request.Email, request.Password, cancellationToken);
                return Results.Accepted();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] });
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict();
            }
        });

        auth.MapPost("/verify-email", async (TokenRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
            await identityService.VerifyEmailAsync(request.Token, cancellationToken) ? Results.NoContent() : Results.BadRequest());

        auth.MapPost("/login", async (LoginRequest request, HttpResponse response, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            BrowserSessionTokens? session;
            try
            {
                session = await identityService.AuthenticateAsync(request.Email, request.Password, cancellationToken);
            }
            catch (ArgumentException)
            {
                session = null;
            }

            if (session is null)
            {
                return Results.Unauthorized();
            }

            AppendSessionCookies(response, session);
            return Results.NoContent();
        });

        auth.MapPost("/recover", async (RecoveryRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            _ = await identityService.BeginPasswordRecoveryAsync(request.Email, cancellationToken);
            return Results.Accepted();
        });

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            try
            {
                return await identityService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken)
                    ? Results.NoContent()
                    : Results.BadRequest();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [exception.Message] });
            }
        });

        auth.MapPost("/logout", async (HttpRequest request, HttpResponse response, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            await identityService.RevokeSessionAsync(request.Cookies[IdentityCookieNames.Session] ?? string.Empty, cancellationToken);
            DeleteSessionCookies(response);
            return Results.NoContent();
        }).RequireIdentityCsrf();

        auth.MapGet("/session", async (HttpRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
        {
            var identity = await identityService.AuthenticateSessionAsync(request.Cookies[IdentityCookieNames.Session] ?? string.Empty, cancellationToken);
            return identity is null
                ? Results.Unauthorized()
                : Results.Ok(new SessionResponse(identity.AccountId, identity.Email, identity.IsEmailVerified, identity.IsOperator));
        });

        auth.MapPost("/operator/mfa/verify", async (TotpRequest request, HttpRequest httpRequest, IIdentityService identityService, CancellationToken cancellationToken) =>
            await identityService.VerifyOperatorMfaAsync(httpRequest.Cookies[IdentityCookieNames.Session] ?? string.Empty, request.Code, cancellationToken)
                ? Results.NoContent()
                : Results.Unauthorized())
            .RequireIdentityCsrf();

        return endpoints;
    }

    private static void AppendSessionCookies(HttpResponse response, BrowserSessionTokens session)
    {
        response.Cookies.Append(IdentityCookieNames.Session, session.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = session.ExpiresAt
        });
        response.Cookies.Append(IdentityCookieNames.Csrf, session.CsrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = session.ExpiresAt
        });
    }

    private static void DeleteSessionCookies(HttpResponse response)
    {
        response.Cookies.Delete(IdentityCookieNames.Session, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });
        response.Cookies.Delete(IdentityCookieNames.Csrf, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });
    }

    private sealed record RegisterRequest(string Email, string Password);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record TokenRequest(string Token);

    private sealed record RecoveryRequest(string Email);

    private sealed record ResetPasswordRequest(string Token, string NewPassword);

    private sealed record TotpRequest(string Code);

    private sealed record SessionResponse(Guid AccountId, string Email, bool EmailVerified, bool IsOperator);
}

public static class IdentityRouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder RequireIdentityCsrf(this RouteHandlerBuilder builder) => builder.RequireManagementCsrf();

    public static RouteHandlerBuilder RequireManagementCsrf(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var request = context.HttpContext.Request;
            var csrf = context.HttpContext.RequestServices.GetRequiredService<ICsrfTokenValidator>();
            var valid = await csrf.ValidateAsync(
                request.Cookies[IdentityCookieNames.Session],
                request.Cookies[IdentityCookieNames.Csrf],
                request.Headers[IdentityCookieNames.CsrfHeader].ToString(),
                context.HttpContext.RequestAborted);
            return valid ? await next(context) : Results.StatusCode(StatusCodes.Status403Forbidden);
        });
}

public static class IdentityCookieNames
{
    public const string Session = "__Host-uzllm-session";
    public const string Csrf = "__Host-uzllm-csrf";
    public const string CsrfHeader = "X-CSRF-Token";
}
