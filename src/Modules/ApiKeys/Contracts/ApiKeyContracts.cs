namespace UZLLM.Modules.ApiKeys.Contracts;

public enum GatewayApiKeyStatus
{
    Active,
    Disabled
}

public sealed record GatewayApiKey(Guid Id, Guid ProjectId, string Name, string Prefix, GatewayApiKeyStatus Status, DateTimeOffset? ExpiresAt, Guid CreatedByAccountId, DateTimeOffset CreatedAt);

public sealed record IssuedGatewayApiKey(GatewayApiKey ApiKey, string Secret);

public sealed record GatewayApiKeyAuthentication(Guid ApiKeyId, Guid ProjectId);

public sealed record ApiKeySecret(string Value, string Prefix);

public sealed record StoredGatewayApiKey(GatewayApiKey ApiKey, byte[] SecretFingerprint, bool IsProjectActive);

public interface IApiKeyStore
{
    Task<bool> TryCreateAsync(StoredGatewayApiKey apiKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default);

    Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default);

    Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default);
}

public interface IApiKeySecretGenerator
{
    ApiKeySecret Create();

    bool TryParse(string value, out ApiKeySecret secret);
}

public interface IApiKeySecretFingerprint
{
    byte[] Create(string secret);

    bool Matches(string secret, byte[] expectedFingerprint);
}

public interface IApiKeyService
{
    Task<IssuedGatewayApiKey> CreateAsync(Guid accountId, Guid projectId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayApiKey>> ListAsync(Guid accountId, Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> SetStatusAsync(Guid accountId, Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default);
}

public interface IApiKeyAuthenticator
{
    Task<GatewayApiKeyAuthentication?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default);
}
