using System.Security.Cryptography;
using System.Text;
using UZLLM.Modules.Identity.Application;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Identity.Tests;

internal sealed class IdentityFixture
{
    public IdentityFixture()
    {
        Service = new IdentityService(Store, new Pbkdf2PasswordHasher(), new TestSecretProtector(), Totp, Clock);
        Csrf = new CsrfTokenValidator(Store, Clock);
    }

    public InMemoryIdentityStore Store { get; } = new();

    public AdjustableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));

    public TotpAuthenticator Totp { get; } = new();

    public IIdentityService Service { get; }

    public ICsrfTokenValidator Csrf { get; }

    public async Task<IdentityRegistration> RegisterAndVerifyAsync()
    {
        var registration = await Service.RegisterAsync("person@example.uz", "correct horse battery staple");
        Assert.True(await Service.VerifyEmailAsync(registration.VerificationToken));
        return registration;
    }
}

internal sealed class AdjustableTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset current = initial;

    public override DateTimeOffset GetUtcNow() => current;

    public void Advance(TimeSpan duration) => current = current.Add(duration);
}

internal sealed class TestSecretProtector : IIdentitySecretProtector
{
    public string Protect(string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
}

internal sealed class InMemoryIdentityStore : IIdentityStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, IdentityAccount> accounts = [];
    private readonly Dictionary<Guid, IdentitySession> sessions = [];
    private readonly Dictionary<Guid, Challenge> challenges = [];
    private readonly Dictionary<Guid, IdentityOperatorAccess> operatorAccesses = [];

    public Task<bool> TryCreateAccountAsync(
        IdentityAccount account,
        byte[] verificationTokenHash,
        DateTimeOffset verificationExpiresAt,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (accounts.Values.Any(existing => string.Equals(existing.Email, account.Email, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            accounts.Add(account.Id, account);
            challenges.Add(Guid.CreateVersion7(), new Challenge(account.Id, IdentityChallengeKind.EmailVerification, verificationTokenHash, account.CreatedAt, verificationExpiresAt, null));
            return Task.FromResult(true);
        }
    }

    public Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.Values.SingleOrDefault(account => account.Email == normalizedEmail));
        }
    }

    public Task<IdentityAccount?> FindAccountByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.GetValueOrDefault(accountId));
        }
    }

    public Task<IdentityAccount?> GetAccountAsync(Guid accountId) => FindAccountByIdAsync(accountId);

    public Task<IdentitySession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(sessions.GetValueOrDefault(sessionId));
        }
    }

    public Task CreateSessionAsync(IdentitySession session, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            sessions.Add(session.Id, session);
            return Task.CompletedTask;
        }
    }

    public Task RevokeSessionAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (sessions.GetValueOrDefault(sessionId) is { RevokedAt: null } session)
            {
                sessions[sessionId] = session with { RevokedAt = revokedAt };
            }

            return Task.CompletedTask;
        }
    }

    public Task CreateChallengeAsync(
        Guid accountId,
        IdentityChallengeKind kind,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            foreach (var challengeId in challenges.Where(pair => pair.Value.AccountId == accountId && pair.Value.Kind == kind && pair.Value.ConsumedAt is null).Select(pair => pair.Key).ToArray())
            {
                challenges[challengeId] = challenges[challengeId] with { ConsumedAt = createdAt };
            }

            challenges.Add(Guid.CreateVersion7(), new Challenge(accountId, kind, tokenHash, createdAt, expiresAt, null));
            return Task.CompletedTask;
        }
    }

    public Task<bool> TryVerifyEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var match = FindChallenge(tokenHash, IdentityChallengeKind.EmailVerification, now);
            if (match is null)
            {
                return Task.FromResult(false);
            }

            Consume(match.Value.Id, now);
            var account = accounts[match.Value.Challenge.AccountId];
            accounts[account.Id] = account with { EmailVerifiedAt = now, UpdatedAt = now };
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryResetPasswordAsync(byte[] tokenHash, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var match = FindChallenge(tokenHash, IdentityChallengeKind.PasswordRecovery, now);
            if (match is null)
            {
                return Task.FromResult(false);
            }

            Consume(match.Value.Id, now);
            var account = accounts[match.Value.Challenge.AccountId];
            accounts[account.Id] = account with { PasswordHash = passwordHash, UpdatedAt = now };
            foreach (var sessionId in sessions.Where(pair => pair.Value.AccountId == account.Id && pair.Value.RevokedAt is null).Select(pair => pair.Key).ToArray())
            {
                sessions[sessionId] = sessions[sessionId] with { RevokedAt = now };
            }

            return Task.FromResult(true);
        }
    }

    public Task<IdentityOperatorAccess?> FindOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(operatorAccesses.GetValueOrDefault(accountId));
        }
    }

    public Task GrantOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!accounts.ContainsKey(accountId))
            {
                throw new InvalidOperationException("The account does not exist.");
            }

            operatorAccesses[accountId] = operatorAccesses.GetValueOrDefault(accountId) is { } existing
                ? existing with { IsActive = true }
                : new IdentityOperatorAccess(accountId, true, null, null);
            return Task.CompletedTask;
        }
    }

    public Task SetOperatorMfaAsync(Guid accountId, string protectedTotpSecret, DateTimeOffset enabledAt, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var access = operatorAccesses.GetValueOrDefault(accountId) ?? throw new InvalidOperationException("The operator does not exist.");
            operatorAccesses[accountId] = access with { ProtectedTotpSecret = protectedTotpSecret, MfaEnabledAt = enabledAt };
            return Task.CompletedTask;
        }
    }

    public Task RecordMfaReauthenticationAsync(Guid sessionId, DateTimeOffset reauthenticatedAt, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var session = sessions.GetValueOrDefault(sessionId) ?? throw new InvalidOperationException("The session does not exist.");
            sessions[sessionId] = session with { MfaReauthenticatedAt = reauthenticatedAt };
            return Task.CompletedTask;
        }
    }

    private (Guid Id, Challenge Challenge)? FindChallenge(byte[] tokenHash, IdentityChallengeKind kind, DateTimeOffset now) =>
        challenges.Where(pair => pair.Value.Kind == kind
                && pair.Value.ConsumedAt is null
                && pair.Value.ExpiresAt > now
                && CryptographicOperations.FixedTimeEquals(pair.Value.TokenHash, tokenHash))
            .Select(pair => ((Guid, Challenge)?)(pair.Key, pair.Value))
            .SingleOrDefault();

    private void Consume(Guid challengeId, DateTimeOffset now) =>
        challenges[challengeId] = challenges[challengeId] with { ConsumedAt = now };

    private sealed record Challenge(
        Guid AccountId,
        IdentityChallengeKind Kind,
        byte[] TokenHash,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? ConsumedAt);
}
