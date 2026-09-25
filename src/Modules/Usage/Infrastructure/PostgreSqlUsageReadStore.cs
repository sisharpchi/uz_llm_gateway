using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

/// <summary>
/// Read-only cross-module projection. The settlement row, not a recalculated price,
/// is authoritative for the customer's final charge.
/// </summary>
public sealed class PostgreSqlUsageReadStore(FoundationDbContext dbContext) : IUsageReadStore
{
    public async Task<IReadOnlyList<UsageActivityItem>> ListActivityAsync(UsageReadFilter filter,
        UsageActivityCursor? cursor, int take, CancellationToken cancellationToken = default)
    {
        var facts = Facts(FilteredRequests(filter));
        if (cursor is not null)
            facts = facts.Where(value => value.StartedAt < cursor.StartedAt
                || (value.StartedAt == cursor.StartedAt && value.Id.CompareTo(cursor.RequestId) < 0));
        var rows = await facts.OrderByDescending(value => value.StartedAt)
            .ThenByDescending(value => value.Id).Take(take).ToListAsync(cancellationToken);
        return rows.Select(ToActivity).ToArray();
    }

    public async Task<UsageRequestDetail?> FindDetailAsync(Guid organizationId, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var fact = await Facts(dbContext.Set<UsageRequestEntity>().AsNoTracking()
                .Where(value => value.OrganizationId == organizationId && value.Id == requestId))
            .SingleOrDefaultAsync(cancellationToken);
        if (fact is null) return null;

        var attempts = await dbContext.Set<UsageAttemptEntity>().AsNoTracking()
            .Where(value => value.RequestId == requestId)
            .OrderBy(value => value.Number)
            .Select(value => new UsageAttemptDetail(value.Id, value.Number, value.ProviderModelId,
                value.ProviderModel.Provider.Code, value.StartedAt, value.CompletedAt,
                value.ExecutionState, value.ProviderRequestId, value.ErrorCategory))
            .ToListAsync(cancellationToken);
        var evidence = await dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
            .Where(value => value.RequestId == requestId)
            .OrderBy(value => value.CapturedAt).ThenBy(value => value.Id)
            .Select(value => new UsageEvidenceDetail(value.Id, value.AttemptId,
                value.State, value.Source, value.InputTokens, value.OutputTokens,
                value.CachedInputTokens, value.ReasoningTokens, value.CapturedAt, value.ReconcileAfter))
            .ToListAsync(cancellationToken);

        return new UsageRequestDetail(ToActivity(fact), fact.TraceId, fact.RouteStrategy,
            fact.HasSettlement ? Money(fact.ProviderCostMicroUsd) : null,
            fact.HasSettlement ? Money(fact.ChargedMicroUsd) : null,
            fact.HasSettlement ? Money(fact.PlatformExposureMicroUsd) : null, fact.UnresolvedUsage,
            attempts, evidence);
    }

    public async Task<(long Requests, long Completed, long Errors, long Pending,
        long InputTokens, long OutputTokens, long ChargedMicroUsd)> GetTotalsAsync(
        UsageReadFilter filter, CancellationToken cancellationToken = default)
    {
        var totals = await Facts(FilteredRequests(filter)).GroupBy(_ => 1)
            .Select(group => new
            {
                Requests = group.LongCount(),
                Completed = group.LongCount(value => value.CompletedAt != null),
                Errors = group.LongCount(value => value.HttpStatus >= 400
                    || value.ExecutionState == "Failed" || value.ExecutionState == "Canceled"
                    || value.ExecutionState == "OutcomeUnknown"
                    || value.ExecutionState == "RejectedBeforeExecution"),
                Pending = group.LongCount(value => value.FinancialState != "Settled"
                    && value.FinancialState != "Released"),
                InputTokens = group.Sum(value => value.InputTokens ?? 0),
                OutputTokens = group.Sum(value => value.OutputTokens ?? 0),
                ChargedMicroUsd = group.Sum(value => value.ChargedMicroUsd ?? 0)
            }).SingleOrDefaultAsync(cancellationToken);
        return totals is null ? (0, 0, 0, 0, 0, 0, 0)
            : (totals.Requests, totals.Completed, totals.Errors, totals.Pending,
                totals.InputTokens, totals.OutputTokens, totals.ChargedMicroUsd);
    }

    public async Task<IReadOnlyList<UsageDailyBucket>> ListDailyAsync(UsageReadFilter filter,
        CancellationToken cancellationToken = default)
    {
        var rows = await Facts(FilteredRequests(filter))
            .GroupBy(value => value.StartedAt.Date)
            .Select(group => new
            {
                Day = group.Key,
                Requests = group.LongCount(),
                Errors = group.LongCount(value => value.HttpStatus >= 400
                    || value.ExecutionState == "Failed" || value.ExecutionState == "Canceled"
                    || value.ExecutionState == "OutcomeUnknown"
                    || value.ExecutionState == "RejectedBeforeExecution"),
                InputTokens = group.Sum(value => value.InputTokens ?? 0),
                OutputTokens = group.Sum(value => value.OutputTokens ?? 0),
                ChargedMicroUsd = group.Sum(value => value.ChargedMicroUsd ?? 0)
            }).OrderBy(value => value.Day).ToListAsync(cancellationToken);
        return rows.Select(value => new UsageDailyBucket(DateOnly.FromDateTime(value.Day),
            value.Requests, value.Errors, value.InputTokens, value.OutputTokens,
            value.ChargedMicroUsd.ToString(CultureInfo.InvariantCulture))).ToArray();
    }

    public async Task<IReadOnlyList<UsageBreakdownItem>> ListBreakdownAsync(UsageReadFilter filter,
        UsageBreakdownDimension dimension, CancellationToken cancellationToken = default)
    {
        var requests = FilteredRequests(filter);
        IQueryable<BreakdownSource> source = dimension switch
        {
            UsageBreakdownDimension.Model => requests.Select(value => new BreakdownSource
            { RequestId = value.Id, Id = value.CanonicalModelId, Name = value.CanonicalModel.CanonicalCode,
                HttpStatus = value.HttpStatus, ExecutionState = value.ExecutionState }),
            UsageBreakdownDimension.Provider => requests.Select(value => new BreakdownSource
            { RequestId = value.Id,
                Id = value.Attempts.OrderByDescending(attempt => attempt.Number)
                    .Select(attempt => (Guid?)attempt.ProviderModel.ProviderId).FirstOrDefault(),
                Name = value.Attempts.OrderByDescending(attempt => attempt.Number)
                    .Select(attempt => attempt.ProviderModel.Provider.Code).FirstOrDefault() ?? "Unrouted",
                HttpStatus = value.HttpStatus, ExecutionState = value.ExecutionState }),
            UsageBreakdownDimension.ApiKey => requests.Select(value => new BreakdownSource
            { RequestId = value.Id, Id = value.ApiKeyId, Name = value.ApiKey.Name,
                HttpStatus = value.HttpStatus, ExecutionState = value.ExecutionState }),
            UsageBreakdownDimension.Project => requests.Select(value => new BreakdownSource
            { RequestId = value.Id, Id = value.ProjectId, Name = value.Project.Name,
                HttpStatus = value.HttpStatus, ExecutionState = value.ExecutionState }),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        var counts = await source.GroupBy(value => new { value.Id, value.Name })
            .Select(group => new
            {
                group.Key.Id, group.Key.Name,
                RequestCount = group.LongCount(),
                ErrorCount = group.LongCount(value => value.HttpStatus >= 400
                    || value.ExecutionState == "Failed" || value.ExecutionState == "Canceled"
                    || value.ExecutionState == "OutcomeUnknown"
                    || value.ExecutionState == "RejectedBeforeExecution")
            }).OrderByDescending(value => value.RequestCount).ThenBy(value => value.Name)
            .Take(100).ToListAsync(cancellationToken);
        var tokens = await source.Join(dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
                .Where(value => value.State == "Verified"), value => value.RequestId,
                value => value.RequestId, (request, evidence) => new { request.Id, request.Name, evidence.InputTokens, evidence.OutputTokens })
            .GroupBy(value => new { value.Id, value.Name })
            .Select(group => new { group.Key.Id, group.Key.Name,
                InputTokens = group.Sum(value => value.InputTokens),
                OutputTokens = group.Sum(value => value.OutputTokens) })
            .ToListAsync(cancellationToken);
        var charges = await source.Join(dbContext.Set<BillingSettlementEntity>().AsNoTracking(),
                value => value.RequestId, value => value.RequestId,
                (request, settlement) => new { request.Id, request.Name, settlement.ChargedMicroUsd })
            .GroupBy(value => new { value.Id, value.Name })
            .Select(group => new { group.Key.Id, group.Key.Name,
                ChargedMicroUsd = group.Sum(value => value.ChargedMicroUsd) })
            .ToListAsync(cancellationToken);
        return counts.Select(value =>
        {
            var token = tokens.FirstOrDefault(item => item.Id == value.Id && item.Name == value.Name);
            var charge = charges.FirstOrDefault(item => item.Id == value.Id && item.Name == value.Name);
            return new UsageBreakdownItem(value.Id?.ToString("D") ?? "unrouted", value.Name,
                value.RequestCount, value.ErrorCount, token?.InputTokens ?? 0,
                token?.OutputTokens ?? 0, (charge?.ChargedMicroUsd ?? 0).ToString(CultureInfo.InvariantCulture));
        }).ToArray();
    }

    private IQueryable<UsageRequestEntity> FilteredRequests(UsageReadFilter filter)
    {
        var query = dbContext.Set<UsageRequestEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == filter.OrganizationId
                && value.StartedAt >= filter.From && value.StartedAt < filter.To);
        if (filter.ProjectId is { } projectId) query = query.Where(value => value.ProjectId == projectId);
        if (filter.ApiKeyId is { } apiKeyId) query = query.Where(value => value.ApiKeyId == apiKeyId);
        if (filter.ModelId is { } modelId) query = query.Where(value => value.CanonicalModelId == modelId);
        if (filter.ProviderId is { } providerId)
            query = query.Where(value => value.Attempts.OrderByDescending(attempt => attempt.Number)
                .Select(attempt => (Guid?)attempt.ProviderModel.ProviderId).FirstOrDefault() == providerId);
        if (filter.Status is { } status) query = query.Where(value => value.ExecutionState == status);
        if (filter.IsStream is { } stream) query = query.Where(value => value.IsStream == stream);
        if (filter.RequestId is { } requestId) query = query.Where(value => value.Id == requestId);
        return query;
    }

    private IQueryable<UsageFact> Facts(IQueryable<UsageRequestEntity> requests) =>
        requests.Select(value => new UsageFact
        {
            Id = value.Id,
            ProjectId = value.ProjectId,
            ProjectName = value.Project.Name,
            ApiKeyId = value.ApiKeyId,
            ApiKeyName = value.ApiKey.Name,
            ModelId = value.CanonicalModelId,
            ModelCode = value.CanonicalModel.CanonicalCode,
            ProviderId = value.Attempts.OrderByDescending(attempt => attempt.Number)
                .Select(attempt => (Guid?)attempt.ProviderModel.ProviderId).FirstOrDefault(),
            ProviderCode = value.Attempts.OrderByDescending(attempt => attempt.Number)
                .Select(attempt => attempt.ProviderModel.Provider.Code).FirstOrDefault(),
            StartedAt = value.StartedAt,
            CompletedAt = value.CompletedAt,
            ExecutionState = value.ExecutionState,
            DeliveryState = value.DeliveryState,
            FinancialState = value.FinancialState,
            HttpStatus = value.HttpStatus,
            IsStream = value.IsStream,
            AttemptCount = value.Attempts.Count,
            TraceId = value.TraceId,
            RouteStrategy = value.RouteStrategy,
            InputTokens = dbContext.Set<UsageEvidenceEntity>()
                .Where(evidence => evidence.RequestId == value.Id && evidence.State == "Verified")
                .Sum(evidence => (long?)evidence.InputTokens),
            OutputTokens = dbContext.Set<UsageEvidenceEntity>()
                .Where(evidence => evidence.RequestId == value.Id && evidence.State == "Verified")
                .Sum(evidence => (long?)evidence.OutputTokens),
            HasVerifiedEvidence = dbContext.Set<UsageEvidenceEntity>()
                .Any(evidence => evidence.RequestId == value.Id && evidence.State == "Verified"),
            ChargedMicroUsd = dbContext.Set<BillingSettlementEntity>()
                .Where(settlement => settlement.RequestId == value.Id)
                .Select(settlement => (long?)settlement.ChargedMicroUsd).FirstOrDefault(),
            HasSettlement = dbContext.Set<BillingSettlementEntity>()
                .Any(settlement => settlement.RequestId == value.Id),
            ProviderCostMicroUsd = dbContext.Set<BillingSettlementEntity>()
                .Where(settlement => settlement.RequestId == value.Id)
                .Select(settlement => (long?)settlement.ProviderCostMicroUsd).FirstOrDefault(),
            PlatformExposureMicroUsd = dbContext.Set<BillingSettlementEntity>()
                .Where(settlement => settlement.RequestId == value.Id)
                .Select(settlement => (long?)settlement.PlatformExposureMicroUsd).FirstOrDefault(),
            UnresolvedUsage = dbContext.Set<BillingSettlementEntity>()
                .Where(settlement => settlement.RequestId == value.Id)
                .Select(settlement => (bool?)settlement.UnresolvedUsage).FirstOrDefault()
        });

    private static UsageActivityItem ToActivity(UsageFact value) => new(
        value.Id, value.ProjectId, value.ProjectName, value.ApiKeyId, value.ApiKeyName,
        value.ModelId, value.ModelCode, value.ProviderId, value.ProviderCode,
        value.StartedAt, value.CompletedAt, value.ExecutionState, value.DeliveryState,
        value.FinancialState, value.HttpStatus, value.IsStream, value.AttemptCount,
        value.CompletedAt is { } completed
            ? Math.Max(0, (long)(completed - value.StartedAt).TotalMilliseconds) : null,
        value.HasVerifiedEvidence ? value.InputTokens : null,
        value.HasVerifiedEvidence ? value.OutputTokens : null,
        value.HasSettlement ? Money(value.ChargedMicroUsd) : null);

    private static string? Money(long? amount) => amount?.ToString(CultureInfo.InvariantCulture);

    private sealed class UsageFact
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public string ProjectName { get; init; } = null!;
        public Guid ApiKeyId { get; init; }
        public string ApiKeyName { get; init; } = null!;
        public Guid ModelId { get; init; }
        public string ModelCode { get; init; } = null!;
        public Guid? ProviderId { get; init; }
        public string? ProviderCode { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string ExecutionState { get; init; } = null!;
        public string DeliveryState { get; init; } = null!;
        public string FinancialState { get; init; } = null!;
        public int? HttpStatus { get; init; }
        public bool IsStream { get; init; }
        public int AttemptCount { get; init; }
        public string? TraceId { get; init; }
        public string? RouteStrategy { get; init; }
        public long? InputTokens { get; init; }
        public long? OutputTokens { get; init; }
        public bool HasVerifiedEvidence { get; init; }
        public long? ChargedMicroUsd { get; init; }
        public bool HasSettlement { get; init; }
        public long? ProviderCostMicroUsd { get; init; }
        public long? PlatformExposureMicroUsd { get; init; }
        public bool? UnresolvedUsage { get; init; }
    }

    private sealed class BreakdownSource
    {
        public Guid RequestId { get; init; }
        public Guid? Id { get; init; }
        public string Name { get; init; } = null!;
        public int? HttpStatus { get; init; }
        public string ExecutionState { get; init; } = null!;
    }
}
