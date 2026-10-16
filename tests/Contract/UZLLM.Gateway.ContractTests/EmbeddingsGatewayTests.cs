using System.Text;
using System.Text.Json;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class GatewayExecutionTests
{
    [Fact]
    public async Task Embeddings_batch_reserves_once_records_input_only_usage_and_returns_ordered_vectors()
    {
        var fixture = new Scenario(embeddingModel: true);
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(200, fixture.Context.Response.StatusCode);
        Assert.Equal("no-store", fixture.Context.Response.Headers.CacheControl);
        Assert.Equal("embeddings", fixture.Finance.LastPrepare!.Operation);
        Assert.False(fixture.Finance.LastPrepare.IsStream);
        Assert.Equal(17, fixture.Finance.LastMaximum!.Value.Value); // ceil(8192 * 2 * 1000 / 1M)
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Equal(DeliveryState.Completed, fixture.Usage.Delivery);
        Assert.Empty(fixture.Performance.Observations); // Never pollute chat TTFT/throughput ranking.
        fixture.Context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(fixture.Context.Response.Body);
        Assert.Equal("list", body.RootElement.GetProperty("object").GetString());
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(new[] { 0, 1 }, body.RootElement.GetProperty("data").EnumerateArray().Select(v => v.GetProperty("index").GetInt32()));
        Assert.Equal(-0.5f, body.RootElement.GetProperty("data")[1].GetProperty("embedding")[1].GetSingle());
        Assert.Equal(2, body.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Theory]
    [InlineData(AdmissionStatus.InsufficientWallet)]
    [InlineData(AdmissionStatus.ProjectBudgetExceeded)]
    [InlineData(AdmissionStatus.ApiKeyBudgetExceeded)]
    [InlineData(AdmissionStatus.SpendingHeld)]
    public async Task Embeddings_financial_denial_never_dispatches(AdmissionStatus denial)
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.Finance.NextAdmission = denial;
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(402, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(0, fixture.Usage.Attempts);
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Theory]
    [InlineData("key-model")]
    [InlineData("key-provider")]
    [InlineData("project-model")]
    [InlineData("project-provider")]
    [InlineData("byok-other-tenant")]
    public async Task Embeddings_permissions_reject_before_reservation_and_dispatch(string denial)
    {
        var fixture = new Scenario(embeddingModel: true);
        if (denial == "key-model") fixture.Authenticator.Permissions = new ApiKeyPermissions(AllowedModels: []);
        if (denial == "key-provider") fixture.Authenticator.Permissions = new ApiKeyPermissions(AllowedProviders: []);
        if (denial == "project-model") fixture.Reads.Settings = new ProjectSettings(AllowedModels: []);
        if (denial == "project-provider") fixture.Reads.Settings = new ProjectSettings(AllowedProviders: []);
        await fixture.RunEmbeddingsAsync(denial == "byok-other-tenant"
            ? JsonSerializer.Serialize(new { model = "gpt-test", input = "hello", uzllm = new { provider_key_id = Guid.NewGuid() } })
            : """{"model":"gpt-test","input":"hello"}""");
        Assert.Equal(403, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.EmbeddingCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embeddings_explicit_Byok_and_Hybrid_use_existing_grants_and_external_holds(bool hybrid)
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.UseByok(hybrid);
        await fixture.RunEmbeddingsAsync(JsonSerializer.Serialize(new { model = "gpt-test", input = "hello",
            uzllm = new { provider_key_id = fixture.ByokCredentialId, allow_managed_fallback = hybrid } }));
        Assert.Equal(200, fixture.Context.Response.StatusCode);
        Assert.Equal(1, fixture.Finance.ByokReserves);
        Assert.Equal(0, fixture.Finance.ManagedReserves);
        Assert.Equal(9, fixture.Finance.LastByokInput!.MaximumExternalSpend.Value);
        Assert.Equal("embeddings", fixture.Finance.LastByokInput.Request.Operation);
        Assert.NotNull(fixture.Adapter.LastContext!.OrganizationId);
        Assert.Equal("byok", fixture.Context.Response.Headers["X-Uzllm-Billing-Mode"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embeddings_missing_usage_and_settlement_failure_never_become_zero_charge(bool settlementFailure)
    {
        var fixture = new Scenario(embeddingModel: true);
        if (settlementFailure) fixture.Finance.FinalizationFailure = new InvalidOperationException("DB unavailable");
        else { fixture.Adapter.EmbeddingUsage = null; fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence; }
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(settlementFailure ? 503 : 200, fixture.Context.Response.StatusCode);
        Assert.Equal(settlementFailure ? 1 : 0, fixture.Usage.VerifiedEvidence);
        Assert.Equal(settlementFailure ? 0 : 1, fixture.Usage.UnknownEvidence);
        Assert.Equal(0, fixture.Finance.Releases);
        Assert.Equal(1, fixture.Finance.Finalizes);
    }

    [Fact]
    public async Task Embeddings_duplicate_is_operation_scoped_and_never_reserves_or_dispatches_again()
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.Context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        fixture.Usage.ExistingClaim = new ClaimResult(ClaimResultKind.Duplicate, fixture.RequestId);
        await fixture.RunEmbeddingsAsync();
        Assert.Equal("embeddings", fixture.Usage.LastOperation);
        Assert.Equal(409, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.EmbeddingCalls);
    }

    [Fact]
    public async Task Embeddings_disconnect_cancels_upstream_but_keeps_independent_unknown_evidence_cleanup()
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.Adapter.BeforeComplete = () => fixture.Writer.AbortSource!.Cancel();
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(1, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.False(fixture.Usage.EvidenceToken.IsCancellationRequested);
        Assert.False(fixture.Finance.FinalizationToken.IsCancellationRequested);
        Assert.Equal(DeliveryState.ClientDisconnected, fixture.Usage.Delivery);
        Assert.Equal(0, fixture.Finance.Releases);
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Fact]
    public async Task Embeddings_Hybrid_fails_over_only_after_verified_rejection_under_one_hold()
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.UseByok(true);
        fixture.Adapter.ByokError = new(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, "rejected", "Rate limited.");
        await fixture.RunEmbeddingsAsync(JsonSerializer.Serialize(new { model = "gpt-test", input = "Hello",
            uzllm = new { provider_key_id = fixture.ByokCredentialId, allow_managed_fallback = true } }));
        Assert.Equal(200, fixture.Context.Response.StatusCode);
        Assert.Equal(2, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Null(fixture.Adapter.LastContext!.OrganizationId);
        Assert.Equal("managed", fixture.Context.Response.Headers["X-Uzllm-Billing-Mode"]);
    }

    [Theory]
    [InlineData(ProviderErrorCategory.Timeout)]
    [InlineData(ProviderErrorCategory.Upstream5xx)]
    public async Task Embeddings_ambiguous_provider_failure_does_not_replay_or_release(ProviderErrorCategory category)
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.UseByok(true);
        fixture.Adapter.ByokError = new(category, ProviderExecutionCertainty.Unknown, false, false, 500, "ambiguous", "Outcome unknown.");
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        await fixture.RunEmbeddingsAsync(JsonSerializer.Serialize(new { model = "gpt-test", input = "Hello",
            uzllm = new { provider_key_id = fixture.ByokCredentialId, allow_managed_fallback = true } }));
        Assert.Equal(502, fixture.Context.Response.StatusCode);
        Assert.Equal(1, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(0, fixture.Finance.Releases);
        Assert.Equal(1, fixture.Finance.Finalizes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embeddings_chat_capability_and_output_pricing_are_not_silently_reused(bool badPrice)
    {
        var fixture = new Scenario(embeddingModel: badPrice, embeddingOutputRate: 1);
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(badPrice ? 503 : 400, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.EmbeddingCalls);
    }

    [Theory]
    [InlineData("{\"model\":\"m\",\"input\":[]}")]
    [InlineData("{\"model\":\"m\",\"input\":[1,2]}")]
    [InlineData("{\"model\":\"m\",\"input\":[[1,2]]}")]
    [InlineData("{\"model\":\"m\",\"input\":\" \"}")]
    [InlineData("{\"model\":\"m\",\"input\":\"hi\",\"encoding_format\":\"base64\"}")]
    [InlineData("{\"model\":\"m\",\"input\":\"hi\",\"dimensions\":3}")]
    [InlineData("{\"model\":\"m\",\"input\":\"hi\",\"stream\":false}")]
    [InlineData("{\"model\":\"m\",\"input\":\"hi\",\"input\":\"bye\"}")]
    [InlineData("{\"model\":\"m\",\"input\":\"hi\",\"uzllm\":{\"fallback_models\":[\"other\"]}}")]
    public void Embeddings_parser_rejects_unsupported_or_ambiguous_inputs(string json) =>
        Assert.Throws<GatewayRequestException>(() => EmbeddingsRequestParser.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Embeddings_parser_bounds_batch_and_UTF8_bytes_not_UTF16_characters()
    {
        ParsedChatRequest Parse(object input) => EmbeddingsRequestParser.Parse(JsonSerializer.SerializeToUtf8Bytes(new { model = "m", input }));
        Assert.Equal(32, Parse(Enumerable.Repeat("hi", 32).ToArray()).EmbeddingRequest!.Inputs.Count);
        Assert.Throws<GatewayRequestException>(() => Parse(Enumerable.Repeat("hi", 33).ToArray()));
        Assert.Equal(8192, Parse(new string('a', 8192)).EstimatedInputTokens);
        Assert.Throws<GatewayRequestException>(() => Parse(new string('a', 8193)));
        Assert.Throws<GatewayRequestException>(() => Parse(new string('Я', 4097)));
    }

    [Fact]
    public async Task Embeddings_project_price_default_is_inherited_but_cannot_enable_Byok()
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.Reads.Settings = new ProjectSettings(Routing: "price", Environment: "Production");
        await fixture.RunEmbeddingsAsync();
        Assert.Equal(200, fixture.Context.Response.StatusCode);
        Assert.Equal("price:openai", fixture.Usage.RouteStrategy);
        Assert.Equal(1, fixture.Finance.ManagedReserves);
        Assert.Equal(0, fixture.Finance.ByokReserves);
        Assert.Null(fixture.Adapter.LastContext!.OrganizationId);
    }

    [Fact]
    public async Task Embeddings_only_model_is_discoverable_but_cannot_be_used_for_chat_generation()
    {
        var discovery = new Scenario(embeddingModel: true);
        await discovery.ListAsync();
        discovery.Context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(discovery.Context.Response.Body);
        Assert.Equal(0, body.RootElement.GetProperty("data")[0].GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("Embeddings", body.RootElement.GetProperty("data")[0].GetProperty("capabilities")[0].GetString());
        var chat = new Scenario(embeddingModel: true);
        await chat.RunAsync();
        Assert.Equal(400, chat.Context.Response.StatusCode);
        Assert.Equal(0, chat.Finance.Reserves);
        Assert.Equal(0, chat.Adapter.CompleteCalls);
        Assert.Equal(0, chat.Adapter.EmbeddingCalls);
    }

    [Theory]
    [InlineData(9, 200)]
    [InlineData(10, 200)]
    [InlineData(11, 400)]
    public async Task Embeddings_catalog_context_limit_applies_per_input_not_to_the_whole_batch(int length, int status)
    {
        var fixture = new Scenario(embeddingModel: true, embeddingContextLength: 10);
        await fixture.RunEmbeddingsAsync(JsonSerializer.Serialize(new { model = "gpt-test", input = new[] { new string('a', length), new string('b', length) } }));
        Assert.Equal(status, fixture.Context.Response.StatusCode);
        Assert.Equal(status == 200 ? 1 : 0, fixture.Adapter.EmbeddingCalls);
        Assert.Equal(status == 200 ? 1 : 0, fixture.Finance.Reserves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embeddings_incompatible_project_strategy_requires_explicit_override(bool explicitOverride)
    {
        var fixture = new Scenario(embeddingModel: true);
        fixture.Reads.Settings = new ProjectSettings(Routing: "throughput");
        await fixture.RunEmbeddingsAsync(explicitOverride
            ? """{"model":"gpt-test","input":"Hello","uzllm":{"routing":"priority"}}"""
            : """{"model":"gpt-test","input":"Hello"}""");
        Assert.Equal(explicitOverride ? 200 : 400, fixture.Context.Response.StatusCode);
        Assert.Equal(explicitOverride ? 1 : 0, fixture.Adapter.EmbeddingCalls);
    }
}
