using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Modules.Catalog.Application;

public sealed class ProviderDataPolicyService(IProviderDataPolicyStore store, TimeProvider clock)
    : IProviderDataPolicyService
{
    public Task<CatalogProvider?> FindProviderAsync(string code, CancellationToken cancellationToken = default) =>
        store.FindProviderAsync(Text(code, 1, 200).ToLowerInvariant(), cancellationToken);

    public Task<ProviderDataPolicy?> FindCurrentAsync(Guid providerId, CancellationToken cancellationToken = default) =>
        store.FindCurrentAsync(RequiredId(providerId), cancellationToken);

    public Task<IReadOnlyList<ProviderDataPolicy>> ListAsync(Guid providerId, int limit,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(RequiredId(providerId), limit is >= 1 and <= 100 ? limit
            : throw new ArgumentOutOfRangeException(nameof(limit)), cancellationToken);

    public Task<ProviderDataPolicy?> FindVersionAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.FindVersionAsync(RequiredId(id), cancellationToken);

    public async Task<ProviderDataPolicy> PublishAsync(Guid providerId, Guid actorAccountId,
        ProviderDataPolicyInput input, CancellationToken cancellationToken = default)
    {
        RequiredId(providerId);
        RequiredId(actorAccountId);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Retention is not ("Unknown" or "MayRetain" or "NoRetentionReported")
            || input.Training is not ("Unknown" or "MayTrain" or "NoTrainingReported")
            || input.Residency is not ("Unknown" or "ProviderReported"))
            throw new ArgumentException("Unknown provider policy classification.");
        if (input.Regions is null || input.Regions.Count > 16)
            throw new ArgumentException("At most 16 reported regions are allowed.");
        var regions = input.Regions.Select(region => Text(region, 1, 80))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (input.Residency == "Unknown" && regions.Length != 0
            || input.Residency == "ProviderReported" && regions.Length == 0)
            throw new ArgumentException("Unknown residency has no regions; reported residency needs regions.");
        var source = Text(input.SourceUrl, 1, 2048);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0
            || source.Any(char.IsWhiteSpace) || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns
            || !uri.Host.Contains('.') || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A public HTTPS source URL without credentials or query is required.");
        var now = clock.GetUtcNow();
        if (input.SourceObservedAt.Offset != TimeSpan.Zero || input.SourceObservedAt < DateTimeOffset.UnixEpoch.AddTicks(10)
            || input.SourceObservedAt > now)
            throw new ArgumentException("Source observation must be a past or current UTC instant.");
        // PostgreSQL timestamps have microsecond precision; replay/order must compare the same instants as storage.
        var observed = new DateTimeOffset(input.SourceObservedAt.Ticks - input.SourceObservedAt.Ticks % 10, TimeSpan.Zero);
        var published = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var policy = new ProviderDataPolicy(Guid.CreateVersion7(), providerId, input.Retention,
            input.Training, input.Residency, regions, Text(input.Scope, 8, 500),
            Text(input.Conditions, 8, 2000), source, observed, published, actorAccountId);
        await store.AppendAsync(policy, cancellationToken);
        return policy;
    }

    private static Guid RequiredId(Guid id) => id != Guid.Empty ? id
        : throw new ArgumentException("A nonempty identifier is required.");

    private static string Text(string? value, int minimum, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= minimum && value.Trim().Length <= maximum
            && !value.Any(char.IsControl) ? value.Trim()
            : throw new ArgumentException($"Text must contain {minimum}–{maximum} characters without control characters.");
}
