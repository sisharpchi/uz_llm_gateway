using System.Text.Json;
using System.Text;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class GatewayExecutionTests
{
    [Theory]
    [InlineData("max_cost")]
    [InlineData("max_cost_usd")]
    [InlineData("allowed_providers")]
    public void Future_cost_and_provider_request_extensions_remain_unsupported(string field)
    {
        var request = Encoding.UTF8.GetBytes("{\"model\":\"gpt-test\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"uzllm\":{\"" + field + "\":1}}");
        var exception = Assert.Throws<GatewayRequestException>(() => ChatRequestParser.Parse(request));
        Assert.Equal("unsupported_parameter", exception.Code);
    }

    [Theory]
    [InlineData(null, "anthropic")]
    [InlineData("priority", "openai")]
    [InlineData("price", "anthropic")]
    public async Task Project_routing_defaults_inherit_and_explicit_requests_override_only_strategy(string? requested, string provider)
    {
        var scenario = new Scenario(twoProviders: true, anthropicInputRate: 1, anthropicOutputRate: 1);
        scenario.Reads.Settings = new("price", "Production");
        scenario.Reads.Byok = new(scenario.ByokCredentialId, Guid.NewGuid());
        if (requested is not null) scenario.UsePerformanceRouting(requested);
        await scenario.RunAsync();
        Assert.Equal(200, scenario.Context.Response.StatusCode);
        Assert.Equal(provider, scenario.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal(1, scenario.Finance.ManagedReserves);
        Assert.Equal(0, scenario.Finance.ByokReserves);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("provider")]
    [InlineData("pinned-provider")]
    [InlineData("empty")]
    [InlineData("fallback-model")]
    [InlineData("fallback-provider")]
    [InlineData("byok")]
    [InlineData("hybrid")]
    [InlineData("tighten")]
    public async Task Project_forbidden_routes_never_reserve_or_dispatch_even_with_explicit_overrides(string denial)
    {
        var scenario = new Scenario(twoProviders: denial is "byok" or "hybrid" or "pinned-provider",
            modelCode: denial == "pinned-provider" ? "openai/gpt-test" : "gpt-test",
            fallbackModel: denial.StartsWith("fallback", StringComparison.Ordinal));
        scenario.Reads.Settings = denial switch {
            "model" => new("price", AllowedModels: ["other-test"]),
            "empty" => new("price", AllowedModels: []),
            "fallback-model" => new("price", AllowedModels: ["gpt-test"]),
            "fallback-provider" => new("price", AllowedProviders: ["openai"]),
            "tighten" => new("price"),
            _ => new("price", AllowedProviders: ["anthropic"])
        };
        scenario.UsePerformanceRouting("priority", denial == "pinned-provider" ? "openai/gpt-test" : "gpt-test");
        if (denial.StartsWith("fallback", StringComparison.Ordinal)) scenario.UseFallbackModel();
        if (denial is "byok" or "hybrid") scenario.UseByok(denial == "hybrid");
        if (denial == "tighten") scenario.Reads.AdmissionSettings = new(AllowedModels: []);
        await scenario.RunAsync();
        Assert.Equal(403, scenario.Context.Response.StatusCode);
        Assert.Contains("project_permission_denied", await ResponseBodyAsync(scenario.Context));
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Usage.Attempts);
        Assert.Equal(0, scenario.Adapter.CompleteCalls + scenario.AnthropicAdapter.CompleteCalls);
    }

    [Fact]
    public async Task Project_restrictions_filter_discovery_and_intersect_key_permissions_before_ranking()
    {
        var scenario = new Scenario(twoProviders: true, fallbackModel: true);
        scenario.Reads.Settings = new("price", AllowedModels: ["gpt-test"], AllowedProviders: ["openai"]);
        await scenario.ListAsync();
        using var json = JsonDocument.Parse(await ResponseBodyAsync(scenario.Context));
        var model = Assert.Single(json.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("gpt-test", model.GetProperty("id").GetString());
        Assert.Equal(1000, model.GetProperty("pricing").GetProperty("input_micro_usd_per_million").GetInt64());
        var request = new Scenario(twoProviders: true);
        request.Reads.Settings = new("price", AllowedProviders: ["openai"]);
        request.Authenticator.Permissions = new(AllowedProviders: ["anthropic"]);
        await request.RunAsync();
        Assert.Equal(403, request.Context.Response.StatusCode);
        Assert.Equal(0, request.Finance.Reserves);
        Assert.Equal(0, request.Adapter.CompleteCalls + request.AnthropicAdapter.CompleteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_defaults_preserve_explicit_BYOK_and_Hybrid_consent(bool hybrid)
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.Reads.Settings = new("price", "Staging");
        scenario.UseByok(hybrid);
        await scenario.RunAsync();
        Assert.Equal(200, scenario.Context.Response.StatusCode);
        Assert.Equal(1, scenario.Finance.ByokReserves);
        Assert.Equal(hybrid, scenario.Finance.LastByokInput!.AllowManagedFallback);
        Assert.Equal(scenario.ByokCredentialId, scenario.Finance.LastByokInput.CredentialId);
        Assert.Equal("openai", scenario.Context.Response.Headers["X-Uzllm-Provider"]);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Environment_metadata_does_not_override_operator_pause_or_enable_BYOK(string environment)
    {
        var scenario = new Scenario();
        scenario.Reads.Settings = new("auto", environment);
        scenario.Reads.Byok = new(scenario.ByokCredentialId, Guid.NewGuid());
        scenario.PlatformControls.Enabled = false;
        await scenario.RunAsync();
        Assert.Equal(503, scenario.Context.Response.StatusCode);
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
    }

    [Theory]
    [InlineData(AdmissionStatus.ProjectBudgetExceeded)]
    [InlineData(AdmissionStatus.ApiKeyBudgetExceeded)]
    [InlineData(AdmissionStatus.InsufficientWallet)]
    public async Task Project_routing_defaults_cannot_bypass_financial_denial(AdmissionStatus status)
    {
        var scenario = new Scenario();
        scenario.Reads.Settings = new("price", "Production");
        scenario.UsePerformanceRouting("priority");
        scenario.Finance.NextAdmission = status;
        await scenario.RunAsync();
        Assert.Equal(402, scenario.Context.Response.StatusCode);
        Assert.Contains("budget_exceeded", await ResponseBodyAsync(scenario.Context));
        Assert.Equal(1, scenario.Finance.ManagedReserves);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
        Assert.Equal(0, scenario.Usage.Attempts);
    }

    [Theory]
    [InlineData(LimitOutcome.RateLimited, 429)]
    [InlineData(LimitOutcome.ConcurrencyLimited, 429)]
    [InlineData(LimitOutcome.DependencyUnavailable, 503)]
    public async Task Project_defaults_cannot_bypass_distributed_operator_limits(LimitOutcome outcome, int status)
    {
        var scenario = new Scenario();
        scenario.Reads.Settings = new("price", "Development");
        scenario.Limiter.NextOutcome = outcome;
        await scenario.RunAsync();
        Assert.Equal(status, scenario.Context.Response.StatusCode);
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Project_denied_stream_never_starts_SSE_or_provider_execution()
    {
        var scenario = new Scenario(stream: true);
        scenario.Reads.Settings = new("auto", AllowedModels: []);
        await scenario.RunAsync();
        Assert.Equal(403, scenario.Context.Response.StatusCode);
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Adapter.StreamCalls);
        Assert.NotEqual("text/event-stream", scenario.Context.Response.ContentType);
    }
}
