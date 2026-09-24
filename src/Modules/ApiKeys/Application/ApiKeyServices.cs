using System.Text.Json;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.ApiKeys.Application;

public sealed class ApiKeyService(IApiKeyStore store, IProjectAccessService projectAccess, IApiKeySecretGenerator secretGenerator, IApiKeySecretFingerprint secretFingerprint, IAuditTrail auditTrail, ITransactionCoordinator transactionCoordinator, TimeProvider timeProvider) : IApiKeyService
{
    public async Task<IssuedGatewayApiKey> CreateAsync(Guid accountId, Guid projectId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(accountId, nameof(accountId));
        ValidateIdentifier(projectId, nameof(projectId));
        var project = await projectAccess.GetOwnedAsync(accountId, projectId, cancellationToken) ?? throw new KeyNotFoundException("The project does not exist.");
        if (project.Status is not ProjectStatus.Active)
        {
            throw new InvalidOperationException("Gateway keys cannot be created for an archived project.");
        }

        var normalizedName = NormalizeName(name);
        var now = timeProvider.GetUtcNow();
        if (expiresAt is not null && expiresAt <= now)
        {
            throw new ArgumentException("Key expiration must be in the future.", nameof(expiresAt));
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var secret = secretGenerator.Create();
            var apiKey = new GatewayApiKey(Guid.CreateVersion7(), projectId, normalizedName, secret.Prefix, GatewayApiKeyStatus.Active, expiresAt, accountId, now);
            await using var transaction = await transactionCoordinator.BeginAsync(cancellationToken);
            if (!await store.TryCreateAsync(new StoredGatewayApiKey(apiKey, secretFingerprint.Create(secret.Value), true), cancellationToken))
            {
                continue;
            }

            await auditTrail.RecordAsync(new AuditEventInput(project.OrganizationId, accountId, "api_key.created", "api_key", apiKey.Id, null, JsonSerializer.Serialize(new { projectId, keyPrefix = apiKey.Prefix })), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IssuedGatewayApiKey(apiKey, secret.Value);
        }

        throw new InvalidOperationException("Could not generate a unique gateway key prefix.");
    }

    public async Task<IReadOnlyList<GatewayApiKey>> ListAsync(Guid accountId, Guid projectId, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(accountId, nameof(accountId));
        ValidateIdentifier(projectId, nameof(projectId));
        _ = await projectAccess.GetOwnedAsync(accountId, projectId, cancellationToken) ?? throw new KeyNotFoundException("The project does not exist.");
        return await store.ListForProjectAsync(projectId, cancellationToken);
    }

    public async Task<bool> SetStatusAsync(Guid accountId, Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(accountId, nameof(accountId));
        ValidateIdentifier(apiKeyId, nameof(apiKeyId));
        var apiKey = await store.FindByIdAsync(apiKeyId, cancellationToken);
        if (apiKey is null)
        {
            return false;
        }

        var project = await projectAccess.GetOwnedAsync(accountId, apiKey.ProjectId, cancellationToken) ?? throw new KeyNotFoundException("The project does not exist.");
        if (apiKey.Status == status)
        {
            return true;
        }

        await using var transaction = await transactionCoordinator.BeginAsync(cancellationToken);
        if (!await store.TrySetStatusAsync(apiKeyId, status, cancellationToken))
        {
            return false;
        }

        await auditTrail.RecordAsync(new AuditEventInput(project.OrganizationId, accountId, "api_key.status_changed", "api_key", apiKeyId, null, JsonSerializer.Serialize(new { status = status.ToString() })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void ValidateIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
        {
            throw new ArgumentException("API key name must be between 2 and 120 characters.", nameof(name));
        }

        return normalized;
    }
}

public sealed class ApiKeyAuthenticator(IApiKeyStore store, IApiKeySecretGenerator secretGenerator, IApiKeySecretFingerprint secretFingerprint, TimeProvider timeProvider) : IApiKeyAuthenticator
{
    public async Task<GatewayApiKeyAuthentication?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey) || !secretGenerator.TryParse(presentedKey, out var secret))
        {
            return null;
        }

        var candidate = await store.FindAuthenticationCandidateAsync(secret.Prefix, cancellationToken);
        if (candidate is null || !candidate.IsProjectActive || candidate.ApiKey.Status is not GatewayApiKeyStatus.Active || candidate.ApiKey.ExpiresAt <= timeProvider.GetUtcNow() || !secretFingerprint.Matches(secret.Value, candidate.SecretFingerprint))
        {
            return null;
        }

        return new GatewayApiKeyAuthentication(candidate.ApiKey.Id, candidate.ApiKey.ProjectId);
    }
}
