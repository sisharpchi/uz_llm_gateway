namespace UZLLM.Modules.Routing.Contracts;

public enum ProviderHealthState { Healthy, Open, DependencyUnavailable }

public interface IProviderHealthService
{
    Task<ProviderHealthState> CheckAsync(Guid providerModelId,
        CancellationToken cancellationToken = default);
    Task RecordSuccessAsync(Guid providerModelId, CancellationToken cancellationToken = default);
    Task RecordTransientFailureAsync(Guid providerModelId, CancellationToken cancellationToken = default);
}
