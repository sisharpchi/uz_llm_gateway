using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Gateway.Api.Inference;

public sealed record GatewayTenantScope(Guid OrganizationId, Guid ProjectId);
public sealed record GatewayByokCredential(Guid Id, Guid ProviderId);

public interface IGatewayReadStore
{
    Task<GatewayTenantScope?> FindTenantAsync(Guid projectId, CancellationToken cancellationToken);
    Task<FeePolicyVersion?> FindFeePolicyAsync(string policyCode, DateTimeOffset at,
        CancellationToken cancellationToken);
    Task<Guid?> FindPlatformCredentialAsync(Guid providerId, CancellationToken cancellationToken);
    Task<GatewayByokCredential?> FindByokCredentialAsync(Guid organizationId,
        Guid projectId, Guid credentialId, string canonicalModelCode,
        CancellationToken cancellationToken) => Task.FromResult<GatewayByokCredential?>(null);
}

public sealed class PostgreSqlGatewayReadStore(FoundationDbContext db) : IGatewayReadStore
{
    public Task<GatewayTenantScope?> FindTenantAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.Set<ProjectEntity>().AsNoTracking()
            .Where(project => project.Id == projectId && project.Status == "Active"
                && project.Organization.Status == "Active")
            .Select(project => new GatewayTenantScope(project.OrganizationId, project.Id))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<FeePolicyVersion?> FindFeePolicyAsync(string policyCode, DateTimeOffset at,
        CancellationToken cancellationToken) =>
        (await db.Set<BillingFeePolicyVersionEntity>().AsNoTracking()
            .Where(value => value.PolicyCode == policyCode && value.EffectiveFrom <= at
                && (value.EffectiveTo == null || value.EffectiveTo > at))
            .SingleOrDefaultAsync(cancellationToken)) is { } fee
            ? new FeePolicyVersion(fee.Id, fee.PolicyCode, fee.MarkupBasisPoints,
                new UsdMicroAmount(fee.FixedFeeMicroUsd), fee.EffectiveFrom,
                fee.EffectiveTo, fee.CreatedAt) : null;

    public Task<Guid?> FindPlatformCredentialAsync(Guid providerId, CancellationToken cancellationToken) =>
        db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.ProviderId == providerId && value.CredentialType == "Platform"
                && value.Status == "Active")
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => (Guid?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GatewayByokCredential?> FindByokCredentialAsync(Guid organizationId,
        Guid projectId, Guid credentialId, string canonicalModelCode,
        CancellationToken cancellationToken)
    {
        var value = await db.Set<ProviderCredentialProjectGrantEntity>().AsNoTracking()
            .Where(grant => grant.OrganizationId == organizationId && grant.ProjectId == projectId
                && grant.CredentialId == credentialId)
            .Join(db.Set<ProviderCredentialEntity>(), grant => new { grant.OrganizationId,
                    grant.CredentialId },
                credential => new { OrganizationId = credential.OrganizationId!.Value,
                    CredentialId = credential.Id }, (_, credential) => credential)
            .Where(credential => credential.CredentialType == "BYOK"
                && credential.Status == "Active" && credential.DeletedAt == null)
            .Select(credential => new { credential.Id, credential.ProviderId,
                credential.AllowedModelsJson }).SingleOrDefaultAsync(cancellationToken);
        if (value is null) return null;
        if (value.AllowedModelsJson is { } json
            && JsonSerializer.Deserialize<string[]>(json)?.Contains(canonicalModelCode,
                StringComparer.Ordinal) != true) return null;
        return new GatewayByokCredential(value.Id, value.ProviderId);
    }
}
