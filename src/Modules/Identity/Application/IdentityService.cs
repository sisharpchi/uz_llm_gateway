using System.Security.Cryptography;
using System.Text;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Modules.Identity.Application;

public sealed class IdentityService(
    IIdentityStore store,
    IPasswordHasher passwordHasher,
    IIdentitySecretProtector secretProtector,
    ITotpAuthenticator totpAuthenticator,
    TimeProvider timeProvider) : IIdentityService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RecoveryLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OperatorReauthenticationLifetime = TimeSpan.FromMinutes(15);

    public async Task<IdentityRegistration> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailAddress.Normalize(email);
        var now = timeProvider.GetUtcNow();
        var verificationToken = OpaqueToken.Create();
        var account = new IdentityAccount(
            Guid.CreateVersion7(),
            normalizedEmail,
            passwordHasher.Hash(password),
            IdentityAccountStatus.Active,
            null,
            now,
            now);

        var created = await store.TryCreateAccountAsync(
            account,
            TokenHash.Create(verificationToken),
            now.Add(VerificationLifetime),
            cancellationToken);
        if (!created)
        {
            throw new InvalidOperationException("An account already exists for this email address.");
        }

        return new IdentityRegistration(account.Id, verificationToken);
    }

    public Task<bool> VerifyEmailAsync(string verificationToken, CancellationToken cancellationToken = default) =>
        store.TryVerifyEmailAsync(TokenHash.Create(verificationToken), timeProvider.GetUtcNow(), cancellationToken);

    public async Task<BrowserSessionTokens?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var account = await store.FindAccountByEmailAsync(EmailAddress.Normalize(email), cancellationToken);
        if (account is null
            || account.Status != IdentityAccountStatus.Active
            || !account.IsEmailVerified
            || !passwordHasher.Verify(password, account.PasswordHash))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var sessionSecret = OpaqueToken.Create();
        var csrfToken = OpaqueToken.Create();
        var session = new IdentitySession(
            Guid.CreateVersion7(),
            account.Id,
            TokenHash.Create(sessionSecret),
            TokenHash.Create(csrfToken),
            now,
            now.Add(SessionLifetime),
            null,
            null);
        await store.CreateSessionAsync(session, cancellationToken);
        return new BrowserSessionTokens(SessionToken.Create(session.Id, sessionSecret), csrfToken, session.ExpiresAt);
    }

    public async Task<AuthenticatedIdentity?> AuthenticateSessionAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var account = await store.FindAccountByIdAsync(session.AccountId, cancellationToken);
        if (account is null || account.Status != IdentityAccountStatus.Active)
        {
            return null;
        }

        var operatorAccess = await store.FindOperatorAccessAsync(account.Id, cancellationToken);
        return new AuthenticatedIdentity(account.Id, account.Email, account.IsEmailVerified, operatorAccess is { IsActive: true });
    }

    public async Task RevokeSessionAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        if (session is not null)
        {
            await store.RevokeSessionAsync(session.Id, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    public async Task<string?> BeginPasswordRecoveryAsync(string email, CancellationToken cancellationToken = default)
    {
        var account = await store.FindAccountByEmailAsync(EmailAddress.Normalize(email), cancellationToken);
        if (account is null || account.Status != IdentityAccountStatus.Active)
        {
            return null;
        }

        var token = OpaqueToken.Create();
        var now = timeProvider.GetUtcNow();
        await store.CreateChallengeAsync(
            account.Id,
            IdentityChallengeKind.PasswordRecovery,
            TokenHash.Create(token),
            now,
            now.Add(RecoveryLifetime),
            cancellationToken);
        return token;
    }

    public Task<bool> ResetPasswordAsync(string recoveryToken, string newPassword, CancellationToken cancellationToken = default)
    {
        var passwordHash = passwordHasher.Hash(newPassword);
        return store.TryResetPasswordAsync(
            TokenHash.Create(recoveryToken),
            passwordHash,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task GrantOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        store.GrantOperatorAccessAsync(accountId, cancellationToken);

    public async Task<OperatorMfaEnrollment> EnrollOperatorMfaAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var access = await store.FindOperatorAccessAsync(accountId, cancellationToken);
        if (access is not { IsActive: true })
        {
            throw new InvalidOperationException("Only an active operator may enroll MFA.");
        }

        var sharedSecret = totpAuthenticator.CreateSharedSecret();
        await store.SetOperatorMfaAsync(accountId, secretProtector.Protect(sharedSecret), timeProvider.GetUtcNow(), cancellationToken);
        return new OperatorMfaEnrollment(sharedSecret);
    }

    public async Task<bool> VerifyOperatorMfaAsync(string sessionToken, string code, CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        if (session is null)
        {
            return false;
        }

        var access = await store.FindOperatorAccessAsync(session.AccountId, cancellationToken);
        if (access is not { IsActive: true, ProtectedTotpSecret: not null, MfaEnabledAt: not null })
        {
            return false;
        }

        string sharedSecret;
        try
        {
            sharedSecret = secretProtector.Unprotect(access.ProtectedTotpSecret);
        }
        catch (CryptographicException)
        {
            return false;
        }

        if (!totpAuthenticator.VerifyCode(sharedSecret, code, timeProvider.GetUtcNow()))
        {
            return false;
        }

        await store.RecordMfaReauthenticationAsync(session.Id, timeProvider.GetUtcNow(), cancellationToken);
        return true;
    }

    public async Task<bool> HasRecentOperatorReauthenticationAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        if (session?.MfaReauthenticatedAt is not { } reauthenticatedAt
            || reauthenticatedAt < timeProvider.GetUtcNow().Subtract(OperatorReauthenticationLifetime))
        {
            return false;
        }

        return (await store.FindOperatorAccessAsync(session.AccountId, cancellationToken)) is { IsActive: true, MfaEnabledAt: not null };
    }

    private async Task<IdentitySession?> GetValidSessionAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (!SessionToken.TryParse(sessionToken, out var sessionId, out var secret))
        {
            return null;
        }

        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        if (session is null
            || session.RevokedAt is not null
            || session.ExpiresAt <= timeProvider.GetUtcNow()
            || !TokenHash.Matches(secret, session.SecretHash))
        {
            return null;
        }

        return session;
    }
}

public sealed class CsrfTokenValidator(IIdentityStore store, TimeProvider timeProvider) : ICsrfTokenValidator
{
    public async Task<bool> ValidateAsync(
        string? sessionToken,
        string? csrfCookieToken,
        string? csrfHeaderToken,
        CancellationToken cancellationToken = default)
    {
        if (!SessionToken.TryParse(sessionToken, out var sessionId, out var sessionSecret)
            || string.IsNullOrWhiteSpace(csrfCookieToken)
            || string.IsNullOrWhiteSpace(csrfHeaderToken)
            || !TokenHash.Matches(csrfCookieToken, TokenHash.Create(csrfHeaderToken)))
        {
            return false;
        }

        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        return session is not null
            && session.RevokedAt is null
            && session.ExpiresAt > timeProvider.GetUtcNow()
            && TokenHash.Matches(sessionSecret, session.SecretHash)
            && TokenHash.Matches(csrfHeaderToken, session.CsrfHash);
    }
}

internal static class EmailAddress
{
    public static string Normalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 320 || !normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Email address is invalid.", nameof(email));
        }

        return normalized;
    }
}

internal static class OpaqueToken
{
    public static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

internal static class TokenHash
{
    public static byte[] Create(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public static bool Matches(string token, byte[] hash) =>
        CryptographicOperations.FixedTimeEquals(Create(token), hash);
}

internal static class SessionToken
{
    public static string Create(Guid sessionId, string secret) => $"{sessionId:N}.{secret}";

    public static bool TryParse(string? token, out Guid sessionId, out string secret)
    {
        sessionId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var segments = token.Split('.', StringSplitOptions.None);
        if (segments.Length != 2
            || !Guid.TryParseExact(segments[0], "N", out sessionId)
            || segments[1].Length < 32)
        {
            return false;
        }

        secret = segments[1];
        return true;
    }
}
