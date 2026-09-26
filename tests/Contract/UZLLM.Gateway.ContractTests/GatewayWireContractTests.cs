using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed class GatewayWireContractTests
{
    [Fact]
    public void Chat_parser_accepts_price_routing_without_byok_credential()
    {
        var parsed = Parse("""{"model":"m","messages":[{"role":"user","content":"x"}],"uzllm":{"routing":"price"}}""");
        Assert.Equal("price", parsed.Routing);
        Assert.Null(parsed.ProviderKeyId);
        Assert.False(parsed.AllowManagedFallback);
    }

    [Fact]
    public void Chat_parser_accepts_ordered_explicit_fallback_models()
    {
        var parsed = Parse("""{"model":"m","messages":[{"role":"user","content":"x"}],"uzllm":{"fallback_models":["m2","m3"]}}""");
        Assert.Equal(["m2", "m3"], parsed.FallbackModels);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"m\"]")]
    [InlineData("[\"m2\",\"m2\"]")]
    [InlineData("[\"provider/m2\"]")]
    [InlineData("[\"m2\",\"m3\",\"m4\"]")]
    public void Chat_parser_rejects_invalid_fallback_model_list(string list)
    {
        var json = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"uzllm\":{\"fallback_models\":"
            + list + "}}";
        Assert.Throws<GatewayRequestException>(() => Parse(json));
    }

    [Fact]
    public void Chat_parser_rejects_byok_only_cross_model_fallback()
    {
        var json = JsonSerializer.Serialize(new
        {
            model = "m", messages = new[] { new { role = "user", content = "x" } },
            uzllm = new { provider_key_id = Guid.NewGuid(),
                fallback_models = new[] { "m2" } }
        });
        Assert.Throws<GatewayRequestException>(() => Parse(json));
    }

    [Theory]
    [InlineData("cheapest")]
    [InlineData("latency")]
    public void Chat_parser_rejects_unimplemented_routing_strategies(string routing)
    {
        var json = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"uzllm\":{\"routing\":\""
            + routing + "\"}}";
        Assert.Equal("unsupported_parameter",
            Assert.Throws<GatewayRequestException>(() => Parse(json)).Code);
    }

    [Fact]
    public void Price_score_uses_estimated_input_and_frozen_customer_fee_without_shrinking_hold()
    {
        var now = DateTimeOffset.UtcNow;
        var model = new CanonicalModel(Guid.NewGuid(), "m", "M", 1000, 100,
            [CatalogCapability.Text], CatalogStatus.Active, now);
        var price = new ModelPrice(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-1), null,
            1_000_000, 2_000_000, 3_000_000, "{}", now);
        var fee = new FeePolicyVersion(Guid.NewGuid(), "default", 1000,
            new UsdMicroAmount(5), now.AddDays(-1), null, now);

        Assert.Equal(153.5m, GatewayCostEstimator.EstimatedCustomerCost(model, price, fee,
            estimatedInputTokens: 25, outputLimit: 30));
        Assert.Equal(3371, GatewayCostEstimator.MaximumCharge(model, price, fee, 30).Value);
    }

    [Fact]
    public void Chat_parser_requires_explicit_provider_key_for_hybrid_fallback()
    {
        var id = Guid.NewGuid();
        var parsed = Parse(JsonSerializer.Serialize(new
        {
            model = "m", messages = new[] { new { role = "user", content = "x" } },
            uzllm = new { provider_key_id = id, allow_managed_fallback = true }
        }));
        Assert.Equal(id, parsed.ProviderKeyId);
        Assert.True(parsed.AllowManagedFallback);
        Assert.Null(Parse("""{"model":"m","messages":[{"role":"user","content":"x"}]}""").ProviderKeyId);
    }

    [Theory]
    [InlineData("{\"provider_key_id\":\"not-a-uuid\"}")]
    [InlineData("{\"allow_managed_fallback\":true}")]
    [InlineData("{\"provider_key_id\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"provider_key_id\":\"11111111-1111-1111-1111-111111111111\",\"unapproved\":true}")]
    public void Chat_parser_rejects_invalid_byok_extension(string extension)
    {
        var json = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"uzllm\":"
            + extension + "}";
        Assert.Throws<GatewayRequestException>(() => Parse(json));
    }

    [Fact]
    public void Chat_parser_accepts_core_tools_and_explicit_output_limit()
    {
        var parsed = Parse("""
            {"model":"openai/gpt-4o-mini","messages":[{"role":"user","content":[{"type":"text","text":"Hello"},{"type":"image_url","image_url":{"url":"https://example.test/image.png"}}]}],"tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}],"tool_choice":{"type":"function","function":{"name":"lookup"}},"response_format":{"type":"json_schema","json_schema":{"name":"answer","schema":{"type":"object"},"strict":true}},"max_completion_tokens":100,"stream":true}
            """);

        Assert.True(parsed.Stream);
        Assert.Equal("openai/gpt-4o-mini", parsed.Model);
        Assert.Contains("Vision", parsed.RequiredCapabilities);
        Assert.Contains("Tools", parsed.RequiredCapabilities);
        Assert.Contains("StructuredOutput", parsed.RequiredCapabilities);
        Assert.Equal(100, parsed.ProviderRequest.MaxOutputTokens);
        Assert.Equal("lookup", parsed.ProviderRequest.ToolChoice!.Name);
    }

    [Theory]
    [InlineData("{\"model\":\"x\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"n\":2}", "unsupported_parameter")]
    [InlineData("{\"model\":\"x\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"temperature\":3}", "invalid_request")]
    [InlineData("{\"model\":\"x\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"max_tokens\":1,\"max_completion_tokens\":2}", "invalid_request")]
    public void Chat_parser_rejects_unsupported_or_unsafe_parameters(string json, string code)
    {
        var exception = Assert.Throws<GatewayRequestException>(() => Parse(json));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Reservation_ceiling_covers_full_context_output_fee_and_cached_rate()
    {
        var model = new CanonicalModel(Guid.NewGuid(), "m", "M", 1000, 100,
            [CatalogCapability.Text], CatalogStatus.Active, DateTimeOffset.UtcNow);
        var price = new ModelPrice(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            null, 1_000_000, 2_000_000, 3_000_000, "{}", DateTimeOffset.UtcNow);
        var fee = new FeePolicyVersion(Guid.NewGuid(), "default", 1000, new UsdMicroAmount(5),
            DateTimeOffset.UtcNow.AddDays(-1), null, DateTimeOffset.UtcNow);

        Assert.Equal(3525, GatewayCostEstimator.MaximumCharge(model, price, fee, 100).Value);
    }

    [Fact]
    public async Task Nonstream_writer_emits_openai_compatible_envelope()
    {
        var context = NewContext();
        var writer = new OpenAiCompletionWriter(context);
        var completion = new ProviderCompletion("upstream-id", "provider-model",
            new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "Hello")]),
            "stop", new ProviderUsage(12, 3, 2, 1), "provider-request-id");

        await writer.WriteCompletionAsync(completion, "canonical-model", Guid.NewGuid(), CancellationToken.None);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("chat.completion", document.RootElement.GetProperty("object").GetString());
        Assert.Equal("canonical-model", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("Hello", document.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(15, document.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public async Task Stream_writer_flushes_deltas_usage_and_done()
    {
        var context = NewContext();
        var writer = new OpenAiCompletionWriter(context);
        var id = Guid.NewGuid();
        await writer.WriteStreamEventAsync(new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "Hi"),
            "canonical", id, CancellationToken.None);
        await writer.WriteStreamEventAsync(new ProviderStreamEvent(ProviderStreamKind.Usage,
            Usage: new ProviderUsage(2, 1, 0, null)), "canonical", id, CancellationToken.None);
        await writer.FinishStreamAsync(CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("text/event-stream", context.Response.ContentType);
        Assert.Contains("\"role\":\"assistant\"", body);
        Assert.Contains("\"content\":\"Hi\"", body);
        Assert.Contains("\"prompt_tokens\":2", body);
        Assert.EndsWith("data: [DONE]\n\n", body);
    }

    private static ParsedChatRequest Parse(string json) =>
        ChatRequestParser.Parse(Encoding.UTF8.GetBytes(json));

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
