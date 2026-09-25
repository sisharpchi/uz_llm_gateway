namespace UZLLM.Persistence;

public sealed class GatewayApiKeyEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = null!;
    public string Prefix { get; set; } = null!;
    public byte[] SecretFingerprint { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Generation { get; set; } = 1;
    public ProjectEntity Project { get; set; } = null!;
    public IdentityAccountEntity CreatedByAccount { get; set; } = null!;
}

public sealed class GatewayApiKeyGenerationEntity
{
    public Guid ApiKeyId { get; set; }
    public int Generation { get; set; }
    public string Prefix { get; set; } = null!;
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
