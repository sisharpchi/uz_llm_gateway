namespace UZLLM.Modules.Identity.Contracts;

public sealed record IdentityRegistration(Guid AccountId, string VerificationToken);

public sealed record BrowserSessionTokens(string SessionToken, string CsrfToken, DateTimeOffset ExpiresAt);

public sealed record AuthenticatedIdentity(
    Guid AccountId,
    string Email,
    bool IsEmailVerified,
    bool IsOperator);

public sealed record OperatorMfaEnrollment(string SharedSecret);

public interface IIdentityService
{
    Task<IdentityRegistration> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyEmailAsync(string verificationToken, CancellationToken cancellationToken = default);

    Task<BrowserSessionTokens?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<AuthenticatedIdentity?> AuthenticateSessionAsync(string sessionToken, CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(string sessionToken, CancellationToken cancellationToken = default);

    Task<string?> BeginPasswordRecoveryAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ResetPasswordAsync(string recoveryToken, string newPassword, CancellationToken cancellationToken = default);

    Task GrantOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<OperatorMfaEnrollment> EnrollOperatorMfaAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<bool> VerifyOperatorMfaAsync(string sessionToken, string code, CancellationToken cancellationToken = default);

    Task<bool> HasRecentOperatorReauthenticationAsync(string sessionToken, CancellationToken cancellationToken = default);
}

public interface ICsrfTokenValidator
{
    Task<bool> ValidateAsync(
        string? sessionToken,
        string? csrfCookieToken,
        string? csrfHeaderToken,
        CancellationToken cancellationToken = default);
}

public enum IdentityAccountStatus
{
    Active,
    Suspended
}

public enum IdentityChallengeKind
{
    EmailVerification,
    PasswordRecovery
}

public sealed record IdentityAccount(
    Guid Id,
    string Email,
    string PasswordHash,
    IdentityAccountStatus Status,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsEmailVerified => EmailVerifiedAt is not null;
}

public sealed record IdentitySession(
    Guid Id,
    Guid AccountId,
    byte[] SecretHash,
    byte[] CsrfHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? MfaReauthenticatedAt);

public sealed record IdentityOperatorAccess(
    Guid AccountId,
    bool IsActive,
    string? ProtectedTotpSecret,
    DateTimeOffset? MfaEnabledAt);

public interface IIdentityStore
{
    Task<bool> TryCreateAccountAsync(
        IdentityAccount account,
        byte[] verificationTokenHash,
        DateTimeOffset verificationExpiresAt,
        CancellationToken cancellationToken = default);

    Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<IdentityAccount?> FindAccountByIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<IdentitySession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task CreateSessionAsync(IdentitySession session, CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    Task CreateChallengeAsync(
        Guid accountId,
        IdentityChallengeKind kind,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryVerifyEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> TryResetPasswordAsync(
        byte[] tokenHash,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IdentityOperatorAccess?> FindOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task GrantOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task SetOperatorMfaAsync(
        Guid accountId,
        string protectedTotpSecret,
        DateTimeOffset enabledAt,
        CancellationToken cancellationToken = default);

    Task RecordMfaReauthenticationAsync(Guid sessionId, DateTimeOffset reauthenticatedAt, CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

public interface IIdentitySecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

public interface ITotpAuthenticator
{
    string CreateSharedSecret();

    string CreateCode(string sharedSecret, DateTimeOffset timestamp);

    bool VerifyCode(string sharedSecret, string code, DateTimeOffset timestamp);
}
