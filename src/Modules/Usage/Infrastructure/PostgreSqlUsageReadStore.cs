using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

/// <summary>
/// Read-only cross-module projection. The settlement row, not a recalculated price,
/// is authoritative for the customer's final charge.
/// </summary>
public sealed partial class PostgreSqlUsageReadStore(FoundationDbContext dbContext) : IUsageReadStore
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
                value.ExecutionState, value.ProviderRequestId, value.ErrorCategory,
                value.ProviderModel.Model.CanonicalCode))
            .ToListAsync(cancellationToken);
        var evidence = await dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
            .Where(value => value.RequestId == requestId)
            .OrderBy(value => value.CapturedAt).ThenBy(value => value.Id)
            .Select(value => new UsageEvidenceDetail(value.Id, value.AttemptId,
                value.State, value.Source, value.InputTokens, value.OutputTokens,
                value.CachedInputTokens, value.ReasoningTokens, value.CapturedAt, value.ReconcileAfter))
            .ToListAsync(cancellationToken);
        var lateExternalSpend = await dbContext.Set<BillingExternalSpendAdjustmentEntity>()
            .AsNoTracking().Where(value => dbContext.Set<BillingSettlementEntity>()
                .Any(settlement => settlement.RequestId == requestId
                    && settlement.Id == value.SettlementId))
            .SumAsync(value => (long?)value.ProviderCostMicroUsd, cancellationToken) ?? 0;

        return new UsageRequestDetail(ToActivity(fact), fact.TraceId, fact.RouteStrategy,
            fact.HasSettlement ? Money(fact.ProviderCostMicroUsd) : null,
            fact.HasSettlement ? Money(fact.ChargedMicroUsd) : null,
            fact.HasSettlement ? Money(fact.PlatformExposureMicroUsd) : null, fact.UnresolvedUsage,
            attempts, evidence, fact.HasSettlement ? Money(fact.ExternalProviderSpendMicroUsd) : null,
            fact.HasSettlement ? lateExternalSpend.ToString(CultureInfo.InvariantCulture) : null);
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
            SelectedModelId = value.Attempts.OrderByDescending(attempt => attempt.Number)
                .Select(attempt => (Guid?)attempt.ProviderModel.ModelId).FirstOrDefault(),
            SelectedModelCode = value.Attempts.OrderByDescending(attempt => attempt.Number)
                .Select(attempt => attempt.ProviderModel.Model.CanonicalCode).FirstOrDefault(),
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
            ExternalProviderSpendMicroUsd = dbContext.Set<BillingSettlementEntity>()
                .Where(settlement => settlement.RequestId == value.Id)
                .Select(settlement => (long?)settlement.ExternalProviderSpendMicroUsd).FirstOrDefault(),
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
        value.HasSettlement ? Money(value.ChargedMicroUsd) : null,
        value.SelectedModelId, value.SelectedModelCode);

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
        public Guid? SelectedModelId { get; init; }
        public string? SelectedModelCode { get; init; }
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
        public long? ExternalProviderSpendMicroUsd { get; init; }
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
