namespace UZLLM.Modules.ApiKeys.Contracts;

public sealed record LimitScope(Guid OrganizationId, Guid ProjectId, Guid ApiKeyId);

public sealed record LimitPolicy(
    int ApiKeyRequestsPerMinute, int ProjectRequestsPerMinute,
    int ApiKeyConcurrency, int ProjectConcurrency, TimeSpan LeaseDuration);

public enum LimitOutcome
{
    Admitted, Duplicate, RateLimited, ConcurrencyLimited, RecoveryWindow, DependencyUnavailable
}

public sealed record LimitLease(LimitScope Scope, Guid RequestId);

public sealed record LimitDecision(LimitOutcome Outcome, LimitLease? Lease, TimeSpan? RetryAfter);

public enum LimitReleaseOutcome { Released, Missing, DependencyUnavailable }

public interface IDistributedAdmissionLimiter
{
    Task<LimitDecision> TryAcquireAsync(LimitScope scope, LimitPolicy policy, Guid requestId,
        CancellationToken cancellationToken = default);

    Task<LimitReleaseOutcome> ReleaseAsync(LimitLease lease, CancellationToken cancellationToken = default);
}

public enum ProviderQuotaOutcome { Available, Exhausted, DependencyUnavailable }

public sealed record ProviderQuotaDecision(ProviderQuotaOutcome Outcome, TimeSpan? RetryAfter);

public interface IProviderQuotaProtection
{
    Task<ProviderQuotaDecision> CheckAsync(Guid credentialId, CancellationToken cancellationToken = default);
    Task<bool> MarkExhaustedAsync(Guid credentialId, TimeSpan retryAfter,
        CancellationToken cancellationToken = default);
}

public sealed record RequestConstraintInput(
    int ContextLength, int ModelMaxOutputTokens, int RequestedMaxOutputTokens,
    int? EstimatedInputTokens, IReadOnlyCollection<string> RequiredCapabilities,
    IReadOnlyCollection<string> SupportedCapabilities);

public enum RequestConstraintOutcome
{
    Allowed, InvalidOutputLimit, ContextExceeded, UnsupportedCapability
}

public sealed record RequestConstraintDecision(RequestConstraintOutcome Outcome, string? Capability);

public interface IRequestConstraintValidator
{
    RequestConstraintDecision Validate(RequestConstraintInput input);
}
