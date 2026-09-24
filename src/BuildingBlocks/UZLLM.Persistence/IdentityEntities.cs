namespace UZLLM.Persistence;

public sealed class IdentityAccountEntity
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<IdentitySessionEntity> Sessions { get; } = [];

    public List<IdentityChallengeEntity> Challenges { get; } = [];

    public IdentityOperatorAccessEntity? OperatorAccess { get; set; }
}

public sealed class IdentitySessionEntity
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public byte[] SecretHash { get; set; } = null!;

    public byte[] CsrfHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? MfaReauthenticatedAt { get; set; }

    public IdentityAccountEntity Account { get; set; } = null!;
}

public sealed class IdentityChallengeEntity
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Kind { get; set; } = null!;

    public byte[] TokenHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public IdentityAccountEntity Account { get; set; } = null!;
}

public sealed class IdentityOperatorAccessEntity
{
    public Guid AccountId { get; set; }

    public bool IsActive { get; set; }

    public string? ProtectedTotpSecret { get; set; }

    public DateTimeOffset? MfaEnabledAt { get; set; }

    public IdentityAccountEntity Account { get; set; } = null!;
}
