using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Gateway.Api.Inference;

public sealed record GatewayTenantScope(Guid OrganizationId, Guid ProjectId);

public interface IGatewayReadStore
{
    Task<GatewayTenantScope?> FindTenantAsync(Guid projectId, CancellationToken cancellationToken);
    Task<FeePolicyVersion?> FindFeePolicyAsync(string policyCode, DateTimeOffset at,
        CancellationToken cancellationToken);
    Task<Guid?> FindPlatformCredentialAsync(Guid providerId, CancellationToken cancellationToken);
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
}
