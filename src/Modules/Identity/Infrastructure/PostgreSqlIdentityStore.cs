using Microsoft.EntityFrameworkCore;
using Npgsql;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Identity.Infrastructure;

public sealed class PostgreSqlIdentityStore(FoundationDbContext dbContext) : IIdentityStore
{
    public async Task<bool> TryCreateAccountAsync(
        IdentityAccount account,
        byte[] verificationTokenHash,
        DateTimeOffset verificationExpiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = account.Id,
            Email = account.Email,
            PasswordHash = account.PasswordHash,
            Status = account.Status.ToString(),
            EmailVerifiedAt = account.EmailVerifiedAt,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        });
        dbContext.Set<IdentityChallengeEntity>().Add(new IdentityChallengeEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            Kind = IdentityChallengeKind.EmailVerification.ToString(),
            TokenHash = verificationTokenHash,
            CreatedAt = account.CreatedAt,
            ExpiresAt = verificationExpiresAt
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<IdentityAccountEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Email == normalizedEmail, cancellationToken)) is { } account
            ? ToContract(account)
            : null;

    public async Task<IdentityAccount?> FindAccountByIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<IdentityAccountEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken)) is { } account
            ? ToContract(account)
            : null;

    public async Task<IdentitySession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<IdentitySessionEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken)) is { } session
            ? ToContract(session)
            : null;

    public async Task CreateSessionAsync(IdentitySession session, CancellationToken cancellationToken = default)
    {
        dbContext.Set<IdentitySessionEntity>().Add(new IdentitySessionEntity
        {
            Id = session.Id,
            AccountId = session.AccountId,
            SecretHash = session.SecretHash,
            CsrfHash = session.CsrfHash,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
            RevokedAt = session.RevokedAt,
            MfaReauthenticatedAt = session.MfaReauthenticatedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        _ = await dbContext.Set<IdentitySessionEntity>()
            .Where(session => session.Id == sessionId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, revokedAt), cancellationToken);
    }

    public async Task CreateChallengeAsync(
        Guid accountId,
        IdentityChallengeKind kind,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Set<IdentityChallengeEntity>()
            .Where(challenge => challenge.AccountId == accountId
                && challenge.Kind == kind.ToString()
                && challenge.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.ConsumedAt, createdAt), cancellationToken);
        dbContext.Set<IdentityChallengeEntity>().Add(new IdentityChallengeEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Kind = kind.ToString(),
            TokenHash = tokenHash,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> TryVerifyEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var challenge = await dbContext.Set<IdentityChallengeEntity>()
            .AsNoTracking()
            .Where(candidate => candidate.Kind == IdentityChallengeKind.EmailVerification.ToString()
                && candidate.TokenHash == tokenHash
                && candidate.ConsumedAt == null
                && candidate.ExpiresAt > now)
            .Select(candidate => new { candidate.Id, candidate.AccountId })
            .SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
        {
            return false;
        }

        var consumed = await dbContext.Set<IdentityChallengeEntity>()
            .Where(candidate => candidate.Id == challenge.Id && candidate.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAt, now), cancellationToken);
        if (consumed != 1)
        {
            return false;
        }

        await dbContext.Set<IdentityAccountEntity>()
            .Where(account => account.Id == challenge.AccountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(account => account.EmailVerifiedAt, now)
                .SetProperty(account => account.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryResetPasswordAsync(
        byte[] tokenHash,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var challenge = await dbContext.Set<IdentityChallengeEntity>()
            .AsNoTracking()
            .Where(candidate => candidate.Kind == IdentityChallengeKind.PasswordRecovery.ToString()
                && candidate.TokenHash == tokenHash
                && candidate.ConsumedAt == null
                && candidate.ExpiresAt > now)
            .Select(candidate => new { candidate.Id, candidate.AccountId })
            .SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
        {
            return false;
        }

        var consumed = await dbContext.Set<IdentityChallengeEntity>()
            .Where(candidate => candidate.Id == challenge.Id && candidate.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAt, now), cancellationToken);
        if (consumed != 1)
        {
            return false;
        }

        await dbContext.Set<IdentityAccountEntity>()
            .Where(account => account.Id == challenge.AccountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(account => account.PasswordHash, passwordHash)
                .SetProperty(account => account.UpdatedAt, now), cancellationToken);
        await dbContext.Set<IdentitySessionEntity>()
            .Where(session => session.AccountId == challenge.AccountId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IdentityOperatorAccess?> FindOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<IdentityOperatorAccessEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(access => access.AccountId == accountId, cancellationToken)) is { } access
            ? new IdentityOperatorAccess(access.AccountId, access.IsActive, access.ProtectedTotpSecret, access.MfaEnabledAt)
            : null;

    public async Task GrantOperatorAccessAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Set<IdentityAccountEntity>()
            .AnyAsync(account => account.Id == accountId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("The account does not exist.");
        }

        var access = await dbContext.Set<IdentityOperatorAccessEntity>()
            .SingleOrDefaultAsync(candidate => candidate.AccountId == accountId, cancellationToken);
        if (access is null)
        {
            dbContext.Set<IdentityOperatorAccessEntity>().Add(new IdentityOperatorAccessEntity
            {
                AccountId = accountId,
                IsActive = true
            });
        }
        else
        {
            access.IsActive = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetOperatorMfaAsync(
        Guid accountId,
        string protectedTotpSecret,
        DateTimeOffset enabledAt,
        CancellationToken cancellationToken = default)
    {
        _ = await dbContext.Set<IdentityOperatorAccessEntity>()
            .Where(access => access.AccountId == accountId && access.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(access => access.ProtectedTotpSecret, protectedTotpSecret)
                .SetProperty(access => access.MfaEnabledAt, enabledAt), cancellationToken);
    }

    public async Task RecordMfaReauthenticationAsync(Guid sessionId, DateTimeOffset reauthenticatedAt, CancellationToken cancellationToken = default)
    {
        _ = await dbContext.Set<IdentitySessionEntity>()
            .Where(session => session.Id == sessionId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.MfaReauthenticatedAt, reauthenticatedAt), cancellationToken);
    }

    private static IdentityAccount ToContract(IdentityAccountEntity account) => new(
        account.Id,
        account.Email,
        account.PasswordHash,
        Enum.Parse<IdentityAccountStatus>(account.Status, ignoreCase: false),
        account.EmailVerifiedAt,
        account.CreatedAt,
        account.UpdatedAt);

    private static IdentitySession ToContract(IdentitySessionEntity session) => new(
        session.Id,
        session.AccountId,
        session.SecretHash,
        session.CsrfHash,
        session.CreatedAt,
        session.ExpiresAt,
        session.RevokedAt,
        session.MfaReauthenticatedAt);
}
