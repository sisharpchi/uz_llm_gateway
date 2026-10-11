using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed partial class PostgreSqlUsageReadStore
{
    public async Task<UsageSummary> ReadSummaryAsync(UsageReadFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var snapshot = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var (asOf, aggregation) = await FreshnessAsync(filter, cancellationToken);
        var totals = await AnalyticsFacts(filter).GroupBy(_ => 1).Select(group => new
        {
            Requests = group.Sum(value => value.RequestCount),
            Completed = group.Sum(value => value.CompletedCount),
            Errors = group.Sum(value => value.ErrorCount),
            Pending = group.Sum(value => value.PendingCount),
            Input = group.Sum(value => value.InputTokens), Output = group.Sum(value => value.OutputTokens),
            Charged = group.Sum(value => value.ChargedMicroUsd), Refunded = group.Sum(value => value.RefundedMicroUsd)
        }).SingleOrDefaultAsync(cancellationToken);
        var models = await BreakdownItemsAsync(filter, UsageBreakdownDimension.Model, cancellationToken);
        var providers = await BreakdownItemsAsync(filter, UsageBreakdownDimension.Provider, cancellationToken);
        return new UsageSummary(filter.From, filter.To, asOf, totals?.Requests ?? 0,
            totals?.Completed ?? 0, totals?.Errors ?? 0, totals?.Pending ?? 0,
            totals?.Input ?? 0, totals?.Output ?? 0, Amount(totals?.Charged ?? 0), 0,
            models.Take(5).ToArray(), providers.Take(5).ToArray(), Amount(totals?.Refunded ?? 0),
            Amount((totals?.Charged ?? 0) - (totals?.Refunded ?? 0)), aggregation);
    }

    public async Task<UsageTimeSeries> ReadTimeSeriesAsync(UsageReadFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var snapshot = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var (asOf, aggregation) = await FreshnessAsync(filter, cancellationToken);
        var rows = await AnalyticsFacts(filter).GroupBy(value => value.Timestamp.Date)
            .Select(group => new
            {
                Day = group.Key, Requests = group.Sum(value => value.RequestCount),
                Errors = group.Sum(value => value.ErrorCount), Input = group.Sum(value => value.InputTokens),
                Output = group.Sum(value => value.OutputTokens), Charged = group.Sum(value => value.ChargedMicroUsd),
                Refunded = group.Sum(value => value.RefundedMicroUsd)
            }).OrderBy(value => value.Day).ToListAsync(cancellationToken);
        return new UsageTimeSeries(filter.From, filter.To, asOf, rows.Select(value => new UsageDailyBucket(
            DateOnly.FromDateTime(value.Day), value.Requests, value.Errors, value.Input, value.Output,
            Amount(value.Charged), Amount(value.Refunded), Amount(value.Charged - value.Refunded))).ToArray(), aggregation);
    }

    public async Task<UsageBreakdown> ReadBreakdownAsync(UsageReadFilter filter,
        UsageBreakdownDimension dimension, CancellationToken cancellationToken = default)
    {
        await using var snapshot = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var (asOf, aggregation) = await FreshnessAsync(filter, cancellationToken);
        return new UsageBreakdown(dimension.ToString(), filter.From, filter.To, asOf,
            await BreakdownItemsAsync(filter, dimension, cancellationToken), aggregation);
    }

    private async Task<IReadOnlyList<UsageBreakdownItem>> BreakdownItemsAsync(UsageReadFilter filter,
        UsageBreakdownDimension dimension, CancellationToken cancellationToken)
    {
        var rows = await AnalyticsFacts(filter).GroupBy(value => dimension == UsageBreakdownDimension.Model
                ? value.ModelId : dimension == UsageBreakdownDimension.Provider ? value.ProviderId
                : dimension == UsageBreakdownDimension.ApiKey ? value.ApiKeyId : value.ProjectId)
            .Select(group => new
            {
                Id = group.Key, Requests = group.Sum(value => value.RequestCount),
                Errors = group.Sum(value => value.ErrorCount), Input = group.Sum(value => value.InputTokens),
                Output = group.Sum(value => value.OutputTokens), Charged = group.Sum(value => value.ChargedMicroUsd),
                Refunded = group.Sum(value => value.RefundedMicroUsd)
            }).OrderByDescending(value => value.Requests).ThenBy(value => value.Id).Take(100)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(value => value.Id).ToArray();
        var names = dimension switch
        {
            UsageBreakdownDimension.Model => await dbContext.Set<CatalogModelEntity>().AsNoTracking()
                .Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.CanonicalCode, cancellationToken),
            UsageBreakdownDimension.Provider => await dbContext.Set<CatalogProviderEntity>().AsNoTracking()
                .Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.Code, cancellationToken),
            UsageBreakdownDimension.ApiKey => await dbContext.Set<GatewayApiKeyEntity>().AsNoTracking()
                .Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken),
            UsageBreakdownDimension.Project => await dbContext.Set<ProjectEntity>().AsNoTracking()
                .Where(value => value.OrganizationId == filter.OrganizationId && ids.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        return rows.Select(value => new UsageBreakdownItem(value.Id == Guid.Empty ? "unrouted" : value.Id.ToString("D"),
            names.GetValueOrDefault(value.Id, "Unrouted"), value.Requests, value.Errors, value.Input, value.Output,
            Amount(value.Charged), Amount(value.Refunded), Amount(value.Charged - value.Refunded))).ToArray();
    }

    private IQueryable<AnalyticsFact> AnalyticsFacts(UsageReadFilter filter)
    {
        var (fullFrom, fullTo) = FullHours(filter);
        var query = dbContext.Database.SqlQuery<AnalyticsFact>($"""
            SELECT h.hour AS "Timestamp", NULL::uuid AS "RequestId", h.project_id AS "ProjectId",
                h.api_key_id AS "ApiKeyId", h.model_id AS "ModelId", h.provider_id AS "ProviderId",
                h.execution_state AS "ExecutionState", h.is_stream AS "IsStream",
                h.request_count AS "RequestCount", h.completed_count AS "CompletedCount",
                h.error_count AS "ErrorCount", h.pending_count AS "PendingCount",
                h.input_tokens AS "InputTokens", h.output_tokens AS "OutputTokens",
                h.charged_micro_usd AS "ChargedMicroUsd", h.refunded_micro_usd AS "RefundedMicroUsd"
            FROM usage.hourly_rollup h
            WHERE h.organization_id = {filter.OrganizationId} AND h.hour >= {fullFrom} AND h.hour < {fullTo}
            UNION ALL
            SELECT s.started_at, s.request_id, s.project_id, s.api_key_id, s.model_id, s.provider_id,
                s.execution_state, s.is_stream, 1::bigint, s.completed_count, s.error_count, s.pending_count,
                s.input_tokens, s.output_tokens, s.charged_micro_usd, s.refunded_micro_usd
            FROM usage.rollup_source s
            WHERE s.organization_id = {filter.OrganizationId} AND s.started_at >= {filter.From}
                AND s.started_at < {filter.To} AND (s.started_at < {fullFrom} OR s.started_at >= {fullTo})
            """);
        if (filter.ProjectId is { } project) query = query.Where(value => value.ProjectId == project);
        if (filter.ApiKeyId is { } key) query = query.Where(value => value.ApiKeyId == key);
        if (filter.ModelId is { } model) query = query.Where(value => value.ModelId == model);
        if (filter.ProviderId is { } provider) query = query.Where(value => value.ProviderId == provider);
        if (filter.Status is { } state) query = query.Where(value => value.ExecutionState == state);
        if (filter.IsStream is { } stream) query = query.Where(value => value.IsStream == stream);
        if (filter.RequestId is { } request) query = query.Where(value => value.RequestId == request);
        return query;
    }

    private async Task<(DateTimeOffset DataAsOf, UsageAggregationInfo Info)> FreshnessAsync(
        UsageReadFilter filter, CancellationToken cancellationToken)
    {
        var (from, to) = FullHours(filter);
        // Include invalidations that move OUT of a mutable filter as well as into it.
        // Project/key/model are immutable request scope; status/provider/stream may change.
        var eligible = FilteredRequests(filter with { Status = null, ProviderId = null, IsStream = null });
        var pending = dbContext.Set<UsageRollupQueueEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == filter.OrganizationId && value.Hour >= from && value.Hour < to)
            .Where(value => eligible.Any(request => request.Id == value.RequestId));
        var count = await pending.Select(value => value.Hour).Distinct().LongCountAsync(cancellationToken);
        var oldest = await pending.MinAsync(value => (DateTimeOffset?)value.EnqueuedAt, cancellationToken);
        var buckets = dbContext.Set<UsageRollupBucketEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == filter.OrganizationId && value.Hour >= from && value.Hour < to);
        var missing = await pending.AnyAsync(value => !dbContext.Set<UsageRollupBucketEntity>()
            .Any(bucket => bucket.OrganizationId == value.OrganizationId && bucket.Hour == value.Hour), cancellationToken);
        var asOf = missing ? DateTimeOffset.UnixEpoch
            : await buckets.MinAsync(value => (DateTimeOffset?)value.DataAsOf, cancellationToken)
                ?? await dbContext.Database.SqlQueryRaw<DateTimeOffset>("SELECT statement_timestamp() AS \"Value\"")
                    .SingleAsync(cancellationToken);
        return (asOf, new UsageAggregationInfo(count, oldest,
            filter.RequestId is null ? "HourlyRollupsWithLiveEdges" : "AuthoritativeRequest"));
    }

    private static (DateTimeOffset From, DateTimeOffset To) FullHours(UsageReadFilter filter)
    {
        if (filter.RequestId is not null) return (filter.To, filter.To);
        var from = filter.From.ToUniversalTime();
        var to = filter.To.ToUniversalTime();
        var floor = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, 0, 0, TimeSpan.Zero);
        return (from == floor ? floor : floor.AddHours(1),
            new DateTimeOffset(to.Year, to.Month, to.Day, to.Hour, 0, 0, TimeSpan.Zero));
    }

    private static string Amount(long amount) => amount.ToString(CultureInfo.InvariantCulture);

    private sealed class AnalyticsFact
    {
        public DateTimeOffset Timestamp { get; set; }
        public Guid? RequestId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ApiKeyId { get; set; }
        public Guid ModelId { get; set; }
        public Guid ProviderId { get; set; }
        public string ExecutionState { get; set; } = null!;
        public bool IsStream { get; set; }
        public long RequestCount { get; set; }
        public long CompletedCount { get; set; }
        public long ErrorCount { get; set; }
        public long PendingCount { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ChargedMicroUsd { get; set; }
        public long RefundedMicroUsd { get; set; }
    }
}
