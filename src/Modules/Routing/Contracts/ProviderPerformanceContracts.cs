namespace UZLLM.Modules.Routing.Contracts;

public sealed record ProviderPerformanceObservation(Guid AttemptId, Guid ProviderModelId,
    bool IsStream, bool Succeeded, long? TimeToFirstTokenMs, int? OutputTokens, long DurationMs);

public sealed record ProviderPerformanceWindow(int Attempts, int SuccessfulSamples,
    decimal MedianTimeToFirstTokenMs, decimal MedianOutputTokensPerSecond,
    decimal ErrorRate, DateTimeOffset LatestAt);

/// <summary>Short-lived advisory measurements; never a financial or eligibility source.</summary>
public interface IProviderPerformanceService
{
    Task<ProviderPerformanceWindow?> ReadAsync(Guid providerModelId, bool isStream,
        CancellationToken cancellationToken = default);
    Task RecordAsync(ProviderPerformanceObservation observation,
        CancellationToken cancellationToken = default);
}
