namespace UZLLM.Modules.Catalog.Contracts;

/// <summary>Conditional provider-reported metadata, not a ZDR or residency guarantee.</summary>
public sealed record ProviderDataPolicy(
    Guid Id, Guid ProviderId, string Retention, string Training, string Residency,
    IReadOnlyList<string> Regions, string Scope, string Conditions, string SourceUrl,
    DateTimeOffset SourceObservedAt, DateTimeOffset PublishedAt, Guid PublishedByAccountId);

/// <summary>One dated observation; publishing a new version never rewrites old evidence.</summary>
public sealed record ProviderDataPolicyInput(string Retention, string Training, string Residency,
    IReadOnlyList<string> Regions, string Scope, string Conditions, string SourceUrl,
    DateTimeOffset SourceObservedAt);

public interface IProviderDataPolicyStore
{
    Task<CatalogProvider?> FindProviderAsync(string code, CancellationToken cancellationToken);
    Task<ProviderDataPolicy?> FindCurrentAsync(Guid providerId, CancellationToken cancellationToken);
    Task<ProviderDataPolicy?> FindVersionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderDataPolicy>> ListAsync(Guid providerId, int limit, CancellationToken cancellationToken);
    Task AppendAsync(ProviderDataPolicy policy, CancellationToken cancellationToken);
}

public interface IProviderDataPolicyService
{
    Task<CatalogProvider?> FindProviderAsync(string code, CancellationToken cancellationToken = default);
    Task<ProviderDataPolicy?> FindCurrentAsync(Guid providerId, CancellationToken cancellationToken = default);
    Task<ProviderDataPolicy?> FindVersionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderDataPolicy>> ListAsync(Guid providerId, int limit, CancellationToken cancellationToken = default);
    Task<ProviderDataPolicy> PublishAsync(Guid providerId, Guid actorAccountId, ProviderDataPolicyInput input,
        CancellationToken cancellationToken = default);
}
