using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public interface IProviderAdapterSelector
{
    ILlmProviderAdapter Get(string providerCode);
}

public sealed class ProviderAdapterSelector(IEnumerable<ILlmProviderAdapter> adapters) : IProviderAdapterSelector
{
    public ILlmProviderAdapter Get(string providerCode) => adapters.SingleOrDefault(
        adapter => adapter.ProviderCode == providerCode)
        ?? throw new InvalidOperationException("Provider adapter is unavailable.");
}
