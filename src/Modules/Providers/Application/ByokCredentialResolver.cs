using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Modules.Providers.Application;

/// <summary>Execution-only key lookup; never exposed by Management HTTP endpoints.</summary>
public sealed class ByokCredentialResolver(IByokCredentialStore store,
    IProviderSecretProtector protector) : IByokCredentialResolver
{
    public async Task<string?> ResolveGrantedSecretAsync(Guid organizationId, Guid projectId,
        Guid credentialId, Guid providerId, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || projectId == Guid.Empty || credentialId == Guid.Empty
            || providerId == Guid.Empty) return null;
        var protectedSecret = await store.FindGrantedSecretAsync(organizationId, projectId,
            credentialId, providerId, cancellationToken);
        return protectedSecret is null ? null : protector.UnprotectForOrganization(
            organizationId, credentialId, providerId, protectedSecret);
    }
}
