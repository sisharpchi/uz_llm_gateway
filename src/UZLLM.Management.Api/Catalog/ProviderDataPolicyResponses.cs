using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Management.Api.Catalog;

/// <summary>Provider-reported conditional metadata, never a verified ZDR or routing guarantee. Null provenance means unknown.</summary>
public sealed record ProviderDataPolicyResponse(Guid ProviderId, Guid? VersionId, string Retention,
    string Training, string Residency, IReadOnlyList<string> Regions, string? Scope,
    string? Conditions, string? SourceUrl, DateTimeOffset? SourceObservedAt, DateTimeOffset? PublishedAt)
{
    public static ProviderDataPolicyResponse From(Guid providerId, ProviderDataPolicy? value) =>
        value is null ? new(providerId, null, "Unknown", "Unknown", "Unknown", [], null, null, null, null, null)
            : new(providerId, value.Id, value.Retention, value.Training, value.Residency,
                value.Regions, value.Scope, value.Conditions, value.SourceUrl, value.SourceObservedAt, value.PublishedAt);
}
