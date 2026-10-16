using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed class EmbeddingsCostTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly CanonicalModel Model = new(Guid.NewGuid(), "embedding", "Embedding", 8192, 0,
        [CatalogCapability.Embeddings], CatalogStatus.Active, Now);
    private static readonly ModelPrice Price = new(Guid.NewGuid(), Guid.NewGuid(), Now, null, 1000, 0, null, "{}", Now);
    private static readonly FeePolicyVersion Fee = new(Guid.NewGuid(), "default", 2500, new(3), Now, null, Now);

    [Fact]
    public void Embeddings_reservation_covers_all_batch_inputs_but_adds_fixed_fee_once()
    {
        Assert.Equal(9, GatewayCostEstimator.MaximumEmbeddingProviderCost(Model, Price, 1).Value);
        Assert.Equal(17, GatewayCostEstimator.MaximumEmbeddingProviderCost(Model, Price, 2).Value);
        Assert.Equal(25, GatewayCostEstimator.MaximumEmbeddingCharge(Model, Price, Fee, 2).Value);
        // Ranking uses the whole batch estimate, not one per-input context clamp.
        Assert.Equal(28m, GatewayCostEstimator.EstimatedCustomerCost(Model, Price, Fee, 20_000, 0));
    }

    [Theory]
    [InlineData(0, 0, false, "{}")]
    [InlineData(33, 0, false, "{}")]
    [InlineData(1, 1, false, "{}")]
    [InlineData(1, 0, true, "{}")]
    [InlineData(1, 0, false, "{\"per_image\":1}")]
    public void Embeddings_reservation_rejects_unbounded_batches_and_non_input_pricing(int count, long outputRate, bool cache, string extras)
    {
        var invalid = Price with { OutputPriceMicroUsdPerMillion = outputRate,
            CachedInputPriceMicroUsdPerMillion = cache ? 0 : null, ExtraPricingJson = extras };
        Assert.Throws<InvalidOperationException>(() => GatewayCostEstimator.MaximumEmbeddingProviderCost(Model, invalid, count));
    }
}
