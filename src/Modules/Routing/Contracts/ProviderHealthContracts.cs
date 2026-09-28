namespace UZLLM.Modules.Routing.Contracts;

public enum ProviderHealthState { Healthy, Open, DependencyUnavailable }

public sealed record ProviderHealthTarget(Guid ProviderModelId, Guid CredentialId,
    Guid? OrganizationId = null);

public sealed record ProviderHealthPermit(ProviderHealthTarget Target, Guid Token);

public sealed record ProviderHealthAttempt(ProviderHealthState State, ProviderHealthPermit? Permit);

public enum ProviderHealthOutcome
{
    Success,
    Neutral,
    CredentialRejected,
    CredentialThrottled,
    EndpointTransientFailure
}

public interface IProviderHealthService
{
    // Eligibility is advisory; BeginAttemptAsync atomically admits at most one
    // half-open probe across Gateway nodes before provider dispatch.
    Task<ProviderHealthState> CheckAsync(ProviderHealthTarget target,
        CancellationToken cancellationToken = default);
    Task<ProviderHealthAttempt> BeginAttemptAsync(ProviderHealthTarget target,
        CancellationToken cancellationToken = default);
    Task CompleteAttemptAsync(ProviderHealthPermit permit, ProviderHealthOutcome outcome,
        CancellationToken cancellationToken = default);
}
