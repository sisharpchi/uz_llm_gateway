using UZLLM.Modules.Providers.Contracts;
using UZLLM.Provider.OpenAI;

namespace UZLLM.Gateway.Api.Inference;

public interface IProviderAdapterSelector
{
    ILlmProviderAdapter Get(string providerCode);
}

public sealed class ProviderAdapterSelector(IServiceProvider services) : IProviderAdapterSelector
{
    public ILlmProviderAdapter Get(string providerCode) => providerCode switch
    {
        "openai" => services.GetRequiredService<OpenAiChatAdapter>(),
        _ => throw new InvalidOperationException("Provider adapter is unavailable.")
    };
}
