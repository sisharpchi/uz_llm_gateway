namespace UZLLM.Modules.ApiKeys.Contracts;

public enum GatewayApiKeyStatus
{
    Active,
    Disabled
}

public sealed record GatewayApiKey(Guid Id, Guid ProjectId, string Name, string Prefix, GatewayApiKeyStatus Status, DateTimeOffset? ExpiresAt, Guid CreatedByAccountId, DateTimeOffset CreatedAt, int Generation = 1, ApiKeyPermissions? Permissions = null);

/// <summary>Null allowlists are unrestricted; empty allowlists deny all. Deny rules take precedence.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ApiKeyPermissions(IReadOnlyList<string>? AllowedModels = null,
    IReadOnlyList<string>? DeniedModels = null, IReadOnlyList<string>? AllowedProviders = null,
    IReadOnlyList<string>? DeniedProviders = null);

public sealed record IssuedGatewayApiKey(GatewayApiKey ApiKey, string Secret);

public sealed record GatewayApiKeyAuthentication(Guid ApiKeyId, Guid ProjectId, ApiKeyPermissions? Permissions = null);

public sealed record ApiKeySecret(string Value, string Prefix);

public sealed record StoredGatewayApiKey(GatewayApiKey ApiKey, byte[] SecretFingerprint, bool IsProjectActive,
    int FingerprintKeyVersion = 1);

public enum ApiKeyRotationStoreResult { Rotated, Conflict, PrefixCollision }

public interface IApiKeyStore
{
    Task<bool> TryCreateAsync(StoredGatewayApiKey apiKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default);

    Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default);

    Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default);

    Task<bool> TrySetPermissionsAsync(Guid apiKeyId, ApiKeyPermissions permissions, CancellationToken cancellationToken = default);

    Task<ApiKeyRotationStoreResult> TryRotateAsync(Guid apiKeyId, int expectedGeneration,
        string newPrefix, byte[] newFingerprint, int fingerprintKeyVersion, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public interface IApiKeySecretGenerator
{
    ApiKeySecret Create();

    bool TryParse(string value, out ApiKeySecret secret);
}

public interface IApiKeySecretFingerprint
{
    int ActiveKeyVersion { get; }

    byte[] Create(string secret);

    bool Matches(string secret, byte[] expectedFingerprint, int keyVersion);
}

public interface IApiKeyService
{
    Task<IssuedGatewayApiKey> CreateAsync(Guid accountId, Guid projectId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayApiKey>> ListAsync(Guid accountId, Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> SetStatusAsync(Guid accountId, Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default);

    Task<bool> SetPermissionsAsync(Guid accountId, Guid apiKeyId, ApiKeyPermissions permissions, CancellationToken cancellationToken = default);

    Task<IssuedGatewayApiKey?> RotateAsync(Guid accountId, Guid apiKeyId, CancellationToken cancellationToken = default);
}

public interface IApiKeyAuthenticator
{
    Task<GatewayApiKeyAuthentication?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default);
}
