using System.Text.Json;
using System.Security.Cryptography;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Providers.Application;

public sealed class ByokCredentialService(
    IByokCredentialStore store,
    IProviderSecretProtector protector,
    IByokCredentialVerifier verifier,
    IOrganizationAuthorizationService authorization,
    IAuditTrail audit,
    ITransactionCoordinator transactions,
    TimeProvider clock) : IByokCredentialService
{
    public async Task<IReadOnlyList<ByokCredential>> ListAsync(Guid actorId,
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        return await store.ListAsync(organizationId, cancellationToken);
    }

    public async Task<ByokCredential?> FindAsync(Guid actorId, Guid organizationId,
        Guid credentialId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        return (await store.FindAsync(organizationId, credentialId, cancellationToken))?.Credential;
    }

    public async Task<ByokCredential> CreateAsync(Guid actorId, Guid organizationId,
        Guid providerId, string name, string secret, CancellationToken cancellationToken = default)
    {
        var cleanName = NormalizeName(name);
        ValidateSecret(secret);
        if (providerId == Guid.Empty) throw new ArgumentException("Provider ID is required.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        var providerCode = await store.GetActiveProviderCodeAsync(providerId, cancellationToken);
        if (!IsSupported(providerCode))
            throw new ArgumentException("BYOK currently supports active OpenAI and Anthropic providers.");
        var now = clock.GetUtcNow();
        var credential = new ByokCredential(Guid.CreateVersion7(), organizationId, providerId,
            providerCode!, cleanName, Mask(secret), ProviderCredentialStatus.Active,
            now, now, null, null, []);
        var protectedSecret = protector.ProtectForOrganization(organizationId,
            credential.Id, providerId, secret);
        await store.CreateAsync(new StoredByokCredential(credential, protectedSecret), cancellationToken);
        await RecordAsync(organizationId, actorId, "byok.created", credential.Id,
            new { providerId }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return credential;
    }

    public async Task<ByokCredential?> UpdateAsync(Guid actorId, Guid organizationId,
        Guid credentialId, string? name, string? secret,
        CancellationToken cancellationToken = default)
    {
        if (name is null && secret is null)
            throw new ArgumentException("Name or secret is required.");
        var cleanName = name is null ? null : NormalizeName(name);
        if (secret is not null) ValidateSecret(secret);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        var existing = await store.FindAsync(organizationId, credentialId, cancellationToken);
        if (existing is null) return null;
        if (existing.Credential.Status != ProviderCredentialStatus.Active)
            throw new InvalidOperationException("Disabled credentials cannot be edited.");
        var protectedSecret = secret is null ? null : protector.ProtectForOrganization(
            organizationId, credentialId, existing.Credential.ProviderId, secret);
        await store.UpdateAsync(organizationId, credentialId, cleanName,
            secret is null ? null : Mask(secret), protectedSecret,
            clock.GetUtcNow(), cancellationToken);
        await RecordAsync(organizationId, actorId, "byok.updated", credentialId,
            new { secretRotated = secret is not null }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await store.FindAsync(organizationId, credentialId, cancellationToken))?.Credential;
    }

    public Task<bool> DisableAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default) =>
        SetStatusAsync(actorId, organizationId, credentialId, false, cancellationToken);

    public Task<bool> DeleteAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default) =>
        SetStatusAsync(actorId, organizationId, credentialId, true, cancellationToken);

    public async Task<bool> SetProjectGrantAsync(Guid actorId, Guid organizationId,
        Guid credentialId, Guid projectId, bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || credentialId == Guid.Empty)
            throw new ArgumentException("Credential and project IDs are required.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        if (!await store.SetProjectGrantAsync(organizationId, credentialId,
            projectId, enabled, clock.GetUtcNow(), cancellationToken)) return false;
        await RecordAsync(organizationId, actorId,
            enabled ? "byok.project.granted" : "byok.project.revoked", credentialId,
            new { projectId }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ByokTestStatus?> TestAsync(Guid actorId, Guid organizationId,
        Guid credentialId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
        var existing = await store.FindAsync(organizationId, credentialId, cancellationToken);
        if (existing is not { Credential.Status: ProviderCredentialStatus.Active }) return null;
        var secret = protector.UnprotectForOrganization(organizationId, credentialId,
            existing.Credential.ProviderId, existing.ProtectedSecret);
        // Upstream network I/O must not hold a database transaction.
        var result = await verifier.VerifyAsync(existing.Credential.ProviderCode, secret,
            cancellationToken);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        var current = await store.FindAsync(organizationId, credentialId, cancellationToken);
        if (current is not { Credential.Status: ProviderCredentialStatus.Active }
            || current.Credential.UpdatedAt != existing.Credential.UpdatedAt
            || !CryptographicOperations.FixedTimeEquals(current.ProtectedSecret.EncryptedSecret,
                existing.ProtectedSecret.EncryptedSecret))
            throw new InvalidOperationException("Credential changed during verification; retry the test.");
        await store.SetTestResultAsync(organizationId, credentialId, result,
            clock.GetUtcNow(), cancellationToken);
        await RecordAsync(organizationId, actorId, "byok.tested", credentialId,
            new { result = result.ToString() }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ByokCredential?> SetRestrictionsAsync(Guid actorId, Guid organizationId,
        Guid credentialId, IReadOnlyList<string>? allowedModels, long? spendLimitMicroUsd,
        CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty || spendLimitMicroUsd is < 0)
            throw new ArgumentException("A credential and non-negative spend cap are required.");
        if (allowedModels is { Count: 0 or > 100 }
            || allowedModels?.Any(code => string.IsNullOrWhiteSpace(code) || code.Length > 200
                || code != code.Trim()) == true
            || allowedModels?.Distinct(StringComparer.Ordinal).Count() != allowedModels?.Count)
            throw new ArgumentException("Allowed models must be unique canonical model codes (1-100).");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        var updated = await store.SetRestrictionsAsync(organizationId, credentialId,
            allowedModels, spendLimitMicroUsd, clock.GetUtcNow(), cancellationToken);
        if (!updated) return null;
        await RecordAsync(organizationId, actorId, "byok.restrictions.updated", credentialId,
            new { allowedModels, spendLimitMicroUsd }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await store.FindAsync(organizationId, credentialId, cancellationToken))?.Credential;
    }

    private async Task<bool> SetStatusAsync(Guid actorId, Guid organizationId,
        Guid credentialId, bool deleted, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await LockAndAuthorizeAsync(actorId, organizationId, cancellationToken);
        if (!await store.SetStatusAsync(organizationId, credentialId,
            ProviderCredentialStatus.Disabled, deleted, clock.GetUtcNow(), cancellationToken)) return false;
        await RecordAsync(organizationId, actorId, deleted ? "byok.deleted" : "byok.disabled",
            credentialId, new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private Task EnsureManagerAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken) => authorization.EnsurePermissionAsync(actorId,
            organizationId, OrganizationPermission.ManageProjects, cancellationToken: cancellationToken);

    private async Task LockAndAuthorizeAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || !await store.LockOrganizationAsync(organizationId,
            cancellationToken)) throw new TenantAccessDeniedException();
        await EnsureManagerAsync(actorId, organizationId, cancellationToken);
    }

    private async Task RecordAsync(Guid organizationId, Guid actorId, string action,
        Guid credentialId, object metadata, CancellationToken cancellationToken) =>
        _ = await audit.RecordAsync(new AuditEventInput(organizationId, actorId, action,
            "provider_credential", credentialId, null, JsonSerializer.Serialize(metadata)),
            cancellationToken);

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 120)
            throw new ArgumentException("Credential name must be 2-120 characters.");
        return normalized;
    }

    private static void ValidateSecret(string secret)
    {
        if (secret is null || secret.Length is < 12 or > 4096
            || secret.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("Provider secret must be 12-4096 printable ASCII characters.");
    }

    private static string Mask(string secret) => $"••••{secret[^4..]}";

    private static bool IsSupported(string? providerCode) =>
        providerCode is "openai" or "anthropic";
}
