namespace UZLLM.Modules.Usage.Contracts;

/// <summary>Optional filters shared by organization-scoped usage reads.</summary>
public sealed record UsageReadQuery(
    DateTimeOffset? From = null, DateTimeOffset? To = null,
    Guid? ProjectId = null, Guid? ApiKeyId = null, Guid? ModelId = null,
    Guid? ProviderId = null, string? Status = null, bool? IsStream = null,
    Guid? RequestId = null);

public sealed record UsageReadFilter(
    Guid OrganizationId, DateTimeOffset From, DateTimeOffset To,
    Guid? ProjectId, Guid? ApiKeyId, Guid? ModelId, Guid? ProviderId,
    string? Status, bool? IsStream, Guid? RequestId);

public sealed record UsageActivityCursor(DateTimeOffset StartedAt, Guid RequestId);

/// <summary>One logical inference request, with verified usage and final customer charge when available.</summary>
public sealed record UsageActivityItem(
    Guid RequestId, Guid ProjectId, string ProjectName, Guid ApiKeyId, string ApiKeyName,
    Guid ModelId, string ModelCode, Guid? ProviderId, string? ProviderCode,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string ExecutionState, string DeliveryState, string FinancialState,
    int? HttpStatus, bool IsStream, int AttemptCount, long? DurationMs,
    long? InputTokens, long? OutputTokens, string? ChargedMicroUsd);

/// <summary>A stable keyset page ordered by start time and request ID, newest first.</summary>
public sealed record UsageActivityPage(
    IReadOnlyList<UsageActivityItem> Items, string? NextCursor, DateTimeOffset DataAsOf);

/// <summary>Organization usage totals over a UTC half-open time range.</summary>
public sealed record UsageSummary(
    DateTimeOffset From, DateTimeOffset To, DateTimeOffset DataAsOf,
    long RequestCount, long CompletedCount, long ErrorCount, long PendingCount,
    long InputTokens, long OutputTokens, string ChargedMicroUsd,
    decimal ErrorRatePercent,
    IReadOnlyList<UsageBreakdownItem> TopModels,
    IReadOnlyList<UsageBreakdownItem> TopProviders);

/// <summary>Daily usage rollup; days use UTC boundaries.</summary>
public sealed record UsageDailyBucket(
    DateOnly Day, long RequestCount, long ErrorCount,
    long InputTokens, long OutputTokens, string ChargedMicroUsd);

/// <summary>One model, provider, API-key, or project aggregate.</summary>
public sealed record UsageBreakdownItem(
    string Id, string Name, long RequestCount, long ErrorCount,
    long InputTokens, long OutputTokens, string ChargedMicroUsd);

/// <summary>A usage breakdown and the read timestamp for a specific dimension.</summary>
public sealed record UsageBreakdown(
    string Dimension, DateTimeOffset From, DateTimeOffset To,
    DateTimeOffset DataAsOf, IReadOnlyList<UsageBreakdownItem> Items);

/// <summary>A daily time series and the read timestamp.</summary>
public sealed record UsageTimeSeries(
    DateTimeOffset From, DateTimeOffset To, DateTimeOffset DataAsOf,
    IReadOnlyList<UsageDailyBucket> Items);

/// <summary>Safe provider-attempt metadata; no request or response payload.</summary>
public sealed record UsageAttemptDetail(
    Guid AttemptId, int Number, Guid ProviderModelId, string ProviderCode,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string ExecutionState, string? ProviderRequestId, string? ErrorCategory);

/// <summary>Immutable accounting evidence for one attempt.</summary>
public sealed record UsageEvidenceDetail(
    Guid EvidenceId, Guid AttemptId, string State, string Source,
    int? InputTokens, int? OutputTokens, int? CachedInputTokens,
    int? ReasoningTokens, DateTimeOffset CapturedAt,
    DateTimeOffset? ReconcileAfter);

/// <summary>Request trace and financial outcome without secrets or payload bodies.</summary>
public sealed record UsageRequestDetail(
    UsageActivityItem Request, string? TraceId, string? RouteStrategy,
    string? ProviderCostMicroUsd, string? ChargedMicroUsd,
    string? PlatformExposureMicroUsd, bool? UnresolvedUsage,
    IReadOnlyList<UsageAttemptDetail> Attempts,
    IReadOnlyList<UsageEvidenceDetail> Evidence);

public enum UsageBreakdownDimension { Model, Provider, ApiKey, Project }

public interface IUsageReadStore
{
    Task<IReadOnlyList<UsageActivityItem>> ListActivityAsync(
        UsageReadFilter filter, UsageActivityCursor? cursor, int take,
        CancellationToken cancellationToken = default);
    Task<UsageRequestDetail?> FindDetailAsync(Guid organizationId, Guid requestId,
        CancellationToken cancellationToken = default);
    Task<(long Requests, long Completed, long Errors, long Pending,
        long InputTokens, long OutputTokens, long ChargedMicroUsd)> GetTotalsAsync(
        UsageReadFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageDailyBucket>> ListDailyAsync(
        UsageReadFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageBreakdownItem>> ListBreakdownAsync(
        UsageReadFilter filter, UsageBreakdownDimension dimension,
        CancellationToken cancellationToken = default);
}

public interface IUsageReadService
{
    Task<UsageActivityPage> GetActivityAsync(Guid organizationId, UsageReadQuery query,
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default);
    Task<UsageRequestDetail?> GetDetailAsync(Guid organizationId, Guid requestId,
        CancellationToken cancellationToken = default);
    Task<UsageSummary> GetSummaryAsync(Guid organizationId, UsageReadQuery query,
        CancellationToken cancellationToken = default);
    Task<UsageTimeSeries> GetTimeSeriesAsync(Guid organizationId, UsageReadQuery query,
        CancellationToken cancellationToken = default);
    Task<UsageBreakdown> GetBreakdownAsync(Guid organizationId, UsageReadQuery query,
        UsageBreakdownDimension dimension, CancellationToken cancellationToken = default);
}
