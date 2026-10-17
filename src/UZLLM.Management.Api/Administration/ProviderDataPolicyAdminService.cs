using System.Text.Json;
using UZLLM.Management.Api.Catalog;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Administration;

/// <summary>Publish one conditional provider-policy observation with an audited reason.</summary>
public sealed record PublishProviderDataPolicyRequest(string Retention, string Training, string Residency,
    string[] Regions, string Scope, string Conditions, string SourceUrl, DateTimeOffset SourceObservedAt, string Reason);

/// <summary>Immutable operator publication metadata; customer reads omit the operator identity.</summary>
public sealed record AdminProviderDataPolicyResponse(ProviderDataPolicyResponse Policy, Guid PublishedByAccountId);

public interface IProviderDataPolicyAdminService
{
    Task<AdminProviderDataPolicyResponse> PublishAsync(Guid actorId, Guid providerId,
        PublishProviderDataPolicyRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminProviderDataPolicyResponse>> ListAsync(Guid providerId, int limit, CancellationToken cancellationToken);
    Task<AdminProviderDataPolicyResponse?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ProviderDataPolicyAdminService(IProviderDataPolicyService policies, IAuditTrail audit,
    ITransactionCoordinator transactions) : IProviderDataPolicyAdminService
{
    public async Task<AdminProviderDataPolicyResponse> PublishAsync(Guid actorId, Guid providerId,
        PublishProviderDataPolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rawReason = request.Reason ?? string.Empty;
        var reason = rawReason.Trim();
        if (reason.Length is < 8 or > 500 || rawReason.Any(char.IsControl))
            throw new ArgumentException("An 8–500 character publication reason is required.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var policy = await policies.PublishAsync(providerId, actorId, new(request.Retention,
            request.Training, request.Residency, request.Regions, request.Scope, request.Conditions,
            request.SourceUrl, request.SourceObservedAt), cancellationToken);
        await audit.RecordAsync(new AuditEventInput(null, actorId, "provider-data-policy.published",
            "provider_data_policy", policy.Id, null, JsonSerializer.Serialize(new
            { providerId, reason, sourceObservedAt = policy.SourceObservedAt })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Response(policy);
    }

    public async Task<IReadOnlyList<AdminProviderDataPolicyResponse>> ListAsync(Guid providerId,
        int limit, CancellationToken cancellationToken) =>
        (await policies.ListAsync(providerId, limit, cancellationToken)).Select(Response).ToArray();

    public async Task<AdminProviderDataPolicyResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await policies.FindVersionAsync(id, cancellationToken) is { } policy ? Response(policy) : null;

    private static AdminProviderDataPolicyResponse Response(ProviderDataPolicy value) =>
        new(ProviderDataPolicyResponse.From(value.ProviderId, value), value.PublishedByAccountId);
}
