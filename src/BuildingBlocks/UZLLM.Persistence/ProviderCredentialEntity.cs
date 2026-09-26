namespace UZLLM.Persistence;

public sealed class ProviderCredentialEntity
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string CredentialType { get; set; } = "Platform";
    public string Status { get; set; } = "Active";
    public byte[] EncryptedSecret { get; set; } = [];
    public byte[] WrappedDataKey { get; set; } = [];
    public string KeyVersion { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? MaskedKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public string? LastTestStatus { get; set; }
}

public sealed class ProviderCredentialProjectGrantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid CredentialId { get; set; }
    public Guid ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
