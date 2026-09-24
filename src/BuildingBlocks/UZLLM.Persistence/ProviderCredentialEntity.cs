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
    public DateTimeOffset CreatedAt { get; set; }
}
