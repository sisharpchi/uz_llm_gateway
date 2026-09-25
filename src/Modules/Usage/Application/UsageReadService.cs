using System.Globalization;
using System.Text;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Modules.Usage.Application;

public sealed class UsageReadService(IUsageReadStore store, TimeProvider timeProvider) : IUsageReadService
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(90);
    private static readonly HashSet<string> ValidStatuses = Enum.GetNames<ExecutionState>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<UsageActivityPage> GetActivityAsync(Guid organizationId, UsageReadQuery query,
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var filter = Normalize(organizationId, query);
        var pageSize = limit ?? 50;
        if (pageSize is < 1 or > 100)
            throw new ArgumentException("Limit must be between 1 and 100.", nameof(limit));
        var position = DecodeCursor(cursor);
        var rows = await store.ListActivityAsync(filter, position, pageSize + 1, cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToArray() : rows;
        var next = hasMore ? EncodeCursor(items[^1].StartedAt, items[^1].RequestId) : null;
        return new UsageActivityPage(items, next, timeProvider.GetUtcNow());
    }

    public Task<UsageRequestDetail?> GetDetailAsync(Guid organizationId, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || requestId == Guid.Empty)
            throw new ArgumentException("Organization and request IDs are required.");
        return store.FindDetailAsync(organizationId, requestId, cancellationToken);
    }

    public async Task<UsageSummary> GetSummaryAsync(Guid organizationId, UsageReadQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = Normalize(organizationId, query);
        var totals = await store.GetTotalsAsync(filter, cancellationToken);
        var topModels = await store.ListBreakdownAsync(filter, UsageBreakdownDimension.Model, cancellationToken);
        var topProviders = await store.ListBreakdownAsync(filter, UsageBreakdownDimension.Provider, cancellationToken);
        var denominator = totals.Completed;
        var errorRate = denominator == 0 ? 0m : decimal.Round(100m * totals.Errors / denominator, 2);
        return new UsageSummary(filter.From, filter.To, timeProvider.GetUtcNow(),
            totals.Requests, totals.Completed, totals.Errors, totals.Pending,
            totals.InputTokens, totals.OutputTokens, totals.ChargedMicroUsd.ToString(CultureInfo.InvariantCulture),
            errorRate, topModels.Take(5).ToArray(), topProviders.Take(5).ToArray());
    }

    public async Task<UsageTimeSeries> GetTimeSeriesAsync(Guid organizationId, UsageReadQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = Normalize(organizationId, query);
        return new UsageTimeSeries(filter.From, filter.To, timeProvider.GetUtcNow(),
            await store.ListDailyAsync(filter, cancellationToken));
    }

    public async Task<UsageBreakdown> GetBreakdownAsync(Guid organizationId, UsageReadQuery query,
        UsageBreakdownDimension dimension, CancellationToken cancellationToken = default)
    {
        var filter = Normalize(organizationId, query);
        if (!Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
        return new UsageBreakdown(dimension.ToString(), filter.From, filter.To,
            timeProvider.GetUtcNow(), await store.ListBreakdownAsync(filter, dimension, cancellationToken));
    }

    private UsageReadFilter Normalize(Guid organizationId, UsageReadQuery query)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization ID is required.");
        ArgumentNullException.ThrowIfNull(query);
        var now = timeProvider.GetUtcNow();
        var to = (query.To ?? now).ToUniversalTime();
        var from = (query.From ?? to.Subtract(DefaultWindow)).ToUniversalTime();
        if (from >= to || to - from > MaximumWindow)
            throw new ArgumentException("Usage range must be greater than zero and at most 90 days.");
        if (query.ProjectId == Guid.Empty || query.ApiKeyId == Guid.Empty || query.ModelId == Guid.Empty
            || query.ProviderId == Guid.Empty || query.RequestId == Guid.Empty)
            throw new ArgumentException("Filter IDs must be non-empty GUIDs.");
        string? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var requested = query.Status.Trim();
            if (!ValidStatuses.Contains(requested)) throw new ArgumentException("Unknown execution status.");
            status = Enum.Parse<ExecutionState>(requested, true).ToString();
        }
        return new UsageReadFilter(organizationId, from, to, query.ProjectId, query.ApiKeyId,
            query.ModelId, query.ProviderId, status, query.IsStream, query.RequestId);
    }

    private static string EncodeCursor(DateTimeOffset startedAt, Guid requestId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{startedAt.UtcTicks}:{requestId:N}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static UsageActivityCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        if (cursor.Length > 128 || cursor.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new ArgumentException("Invalid activity cursor.", nameof(cursor));
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            var parts = decoded.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out var id) || id == Guid.Empty)
                throw new FormatException();
            return new UsageActivityCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("Invalid activity cursor.", nameof(cursor), exception);
        }
    }
}
