using System.Text;
using System.Text.Json;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class GatewayExecutionTests
{
    [Fact]
    public void Responses_parser_preserves_instructions_order_sampling_and_request_only_routing()
    {
        var parsed = ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes("""
            {"model":"openai/gpt-test","instructions":"Be concise","input":[
              {"type":"message","role":"developer","content":"Guidance"},
              {"role":"assistant","content":"Earlier"},
              {"role":"user","content":[{"type":"input_text","text":"Hello"},{"type":"input_text","text":"Again"}]}],
              "temperature":0.5,"top_p":0.8,"max_output_tokens":16,"store":false,"stream":false,
              "uzllm":{"routing":"price"}}
            """));
        Assert.Equal("responses", parsed.Operation);
        Assert.False(parsed.Stream);
        Assert.Equal("Be concise", parsed.Instructions);
        Assert.Equal(new[] { "system", "developer", "assistant", "user" }, parsed.ProviderRequest.Messages.Select(value => value.Role));
        Assert.Equal(new[] { "Hello", "Again" }, parsed.ProviderRequest.Messages[3].Content.Select(value => value.Value));
        Assert.Equal(16, parsed.ProviderRequest.MaxOutputTokens);
        Assert.Equal(0.5m, parsed.ProviderRequest.Temperature);
        Assert.Equal(0.8m, parsed.ProviderRequest.TopP);
        Assert.Equal("price", parsed.Routing);
        Assert.Null(parsed.ProviderKeyId);
        Assert.Equal(new[] { "Text" }, parsed.RequiredCapabilities);
        Assert.Equal(18, parsed.EstimatedInputTokens);
    }

    [Theory]
    [InlineData("\"stream\":true")]
    [InlineData("\"store\":true")]
    [InlineData("\"previous_response_id\":\"resp_old\"")]
    [InlineData("\"conversation\":\"conv_old\"")]
    [InlineData("\"background\":true")]
    [InlineData("\"tools\":[]")]
    [InlineData("\"text\":{\"format\":{\"type\":\"json_object\"}}")]
    [InlineData("\"reasoning\":{\"effort\":\"high\"}")]
    [InlineData("\"max_output_tokens\":15")]
    [InlineData("\"max_output_tokens\":2147483648")]
    [InlineData("\"max_output_tokens\":\"32\"")]
    [InlineData("\"temperature\":3")]
    [InlineData("\"top_p\":0")]
    [InlineData("\"input\":\"duplicate\"")]
    [InlineData("\"uzllm\":{\"allow_managed_fallback\":true}")]
    public async Task Responses_unsupported_or_invalid_parameters_never_reserve_or_dispatch(string fragment)
    {
        var scenario = new Scenario();
        await scenario.RunResponsesAsync("{\"model\":\"gpt-test\",\"input\":\"Hello\"," + fragment + "}");
        Assert.Equal(400, scenario.Context.Response.StatusCode);
        Assert.Contains("\"error\":", await ResponseBodyAsync(scenario.Context));
        Assert.Equal("no-store", scenario.Context.Response.Headers.CacheControl);
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Usage.Attempts);
        Assert.Equal(0, scenario.Adapter.CompleteCalls + scenario.Adapter.StreamCalls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("[]")]
    [InlineData("[{\"role\":\"tool\",\"content\":\"hi\"}]")]
    [InlineData("[{\"type\":\"function_call_output\",\"role\":\"user\",\"content\":\"hi\"}]")]
    [InlineData("[{\"role\":\"user\",\"content\":[{\"type\":\"input_image\",\"image_url\":\"https://example.test/image\"}]}]")]
    [InlineData("[{\"role\":\"user\",\"content\":\"hi\",\"unknown\":true}]")]
    [InlineData("[{\"role\":\"user\",\"role\":\"assistant\",\"content\":\"hi\"}]")]
    public void Responses_parser_rejects_invalid_or_unsupported_input_items(string input)
    {
        Assert.Throws<GatewayRequestException>(() => ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(
            "{\"model\":\"gpt-test\",\"input\":" + input + "}")));
    }

    [Theory]
    [InlineData(255, true, true)]
    [InlineData(256, false, true)]
    [InlineData(256, true, false)]
    [InlineData(257, false, false)]
    public void Responses_message_bound_includes_instructions(int count, bool instructions, bool accepted)
    {
        var body = "{\"model\":\"gpt-test\",\"input\":[" + string.Join(',', Enumerable.Repeat(
            "{\"role\":\"user\",\"content\":\"x\"}", count)) + "]" + (instructions ? ",\"instructions\":\"guide\"" : "") + "}";
        if (accepted) Assert.Equal(count + (instructions ? 1 : 0),
            ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(body)).ProviderRequest.Messages.Count);
        else Assert.Throws<GatewayRequestException>(() => ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("google")]
    [InlineData("deepseek")]
    public async Task Responses_text_uses_existing_provider_adapter_and_one_financial_lifecycle(string provider)
    {
        var scenario = new Scenario(twoProviders: true, googleProvider: true, deepSeekProvider: true);
        await scenario.RunResponsesAsync(JsonSerializer.Serialize(new { model = provider + "/gpt-test", input = "Hello", max_output_tokens = 32 }));
        Assert.Equal(200, scenario.Context.Response.StatusCode);
        var response = JsonDocument.Parse(await ResponseBodyAsync(scenario.Context)).RootElement;
        Assert.Equal("response", response.GetProperty("object").GetString());
        Assert.Equal("resp_" + scenario.RequestId.ToString("N"), response.GetProperty("id").GetString());
        Assert.Equal("completed", response.GetProperty("status").GetString());
        Assert.False(response.GetProperty("store").GetBoolean());
        Assert.Equal("ok", response.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(3, response.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal("responses", scenario.Finance.LastPrepare!.Operation);
        Assert.Equal(1, scenario.Finance.Reserves);
        Assert.Equal(1, scenario.Usage.VerifiedEvidence);
        Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Equal(0, scenario.Finance.Releases);
        Assert.Equal(DeliveryState.Completed, scenario.Usage.Delivery);
        Assert.Equal(provider, scenario.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal(1, scenario.Limiter.Releases);
    }

    [Theory]
    [InlineData("stop", "completed", null)]
    [InlineData("length", "incomplete", "max_output_tokens")]
    [InlineData("content_filter", "incomplete", "content_filter")]
    public async Task Responses_maps_terminal_status_refusal_and_usage_subsets(string finish, string status, string? reason)
    {
        var scenario = new Scenario();
        scenario.Adapter.Completion = new("up", "upstream", new("assistant", []), finish,
            new(12, 5, 4, 2), "provider-id", "Cannot help");
        await scenario.RunResponsesAsync();
        var body = JsonDocument.Parse(await ResponseBodyAsync(scenario.Context)).RootElement;
        Assert.Equal(status, body.GetProperty("status").GetString());
        if (reason is null) Assert.Equal(JsonValueKind.Null, body.GetProperty("incomplete_details").ValueKind);
        else Assert.Equal(reason, body.GetProperty("incomplete_details").GetProperty("reason").GetString());
        Assert.Equal("refusal", body.GetProperty("output")[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal(17, body.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal(4, body.GetProperty("usage").GetProperty("input_tokens_details").GetProperty("cached_tokens").GetInt32());
        Assert.Equal(2, body.GetProperty("usage").GetProperty("output_tokens_details").GetProperty("reasoning_tokens").GetInt32());
        Assert.Equal(1, scenario.Finance.Finalizes);
    }

    [Fact]
    public async Task Responses_duplicate_returns_original_id_before_limits_reservation_or_provider()
    {
        var scenario = new Scenario();
        scenario.Context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        scenario.Usage.ExistingClaim = new(ClaimResultKind.Duplicate, scenario.RequestId);
        await scenario.RunResponsesAsync();
        Assert.Equal(409, scenario.Context.Response.StatusCode);
        Assert.Equal(scenario.RequestId.ToString("N"), scenario.Context.Response.Headers["X-Original-Request-Id"]);
        Assert.Equal("responses", scenario.Usage.LastOperation);
        Assert.Equal(0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
        Assert.Equal(0, scenario.Limiter.Releases);
    }

    [Theory]
    [InlineData("key", 403)]
    [InlineData("project", 403)]
    [InlineData("tenant", 403)]
    [InlineData("auth", 401)]
    [InlineData("redis", 503)]
    [InlineData("wallet", 402)]
    [InlineData("project-budget", 402)]
    [InlineData("key-budget", 402)]
    public async Task Responses_authorization_and_financial_gates_precede_dispatch(string denial, int status)
    {
        var scenario = new Scenario();
        if (denial == "key") scenario.Authenticator.Permissions = new(AllowedModels: []);
        if (denial == "project") scenario.Reads.Settings = new ProjectSettings(AllowedProviders: []);
        if (denial == "tenant") scenario.Reads.Active = false;
        if (denial == "auth") scenario.Context.Request.Headers.Authorization = "Bearer invalid";
        if (denial == "redis") scenario.Limiter.NextOutcome = LimitOutcome.DependencyUnavailable;
        if (denial == "wallet") scenario.Finance.NextAdmission = AdmissionStatus.InsufficientWallet;
        if (denial == "project-budget") scenario.Finance.NextAdmission = AdmissionStatus.ProjectBudgetExceeded;
        if (denial == "key-budget") scenario.Finance.NextAdmission = AdmissionStatus.ApiKeyBudgetExceeded;
        await scenario.RunResponsesAsync();
        Assert.Equal(status, scenario.Context.Response.StatusCode);
        Assert.Equal(denial is "wallet" or "project-budget" or "key-budget" ? 1 : 0, scenario.Finance.Reserves);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
        Assert.Equal(0, scenario.Usage.Attempts);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("timeout")]
    [InlineData("settlement")]
    [InlineData("unexpected-tool")]
    public async Task Responses_unknown_execution_and_settlement_failure_never_release_or_retry(string failure)
    {
        var scenario = new Scenario(twoProviders: true);
        if (failure == "missing") {
            scenario.Adapter.Completion = new("u", "m", new("assistant", [new(ProviderContentKind.Text, "ok")]), "stop", null, "p");
            scenario.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        }
        if (failure == "timeout") {
            scenario.Adapter.Error = new(ProviderErrorCategory.Timeout, ProviderExecutionCertainty.Unknown, false, false, null, "p", "Provider timeout.");
            scenario.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        }
        if (failure == "settlement") scenario.Finance.FinalizationFailure = new InvalidOperationException("database unavailable");
        if (failure == "unexpected-tool") scenario.Adapter.Completion = new("u", "m",
            new("assistant", [], [new("call", "tool", "{}")]), "tool_calls", new(2, 1, 0, null), "p");
        await scenario.RunResponsesAsync();
        Assert.Equal(failure == "missing" ? 200 : failure == "settlement" ? 503 : 502, scenario.Context.Response.StatusCode);
        Assert.Equal(1, scenario.Adapter.CompleteCalls);
        Assert.Equal(0, scenario.AnthropicAdapter.CompleteCalls);
        Assert.Equal(0, scenario.Finance.Releases);
        Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Equal(failure is "missing" or "timeout" ? 1 : 0, scenario.Usage.UnknownEvidence);
        Assert.Equal(failure is "settlement" or "unexpected-tool" ? 1 : 0, scenario.Usage.VerifiedEvidence);
        if (failure == "missing") Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await ResponseBodyAsync(scenario.Context)).RootElement.GetProperty("usage").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Responses_client_disconnect_preserves_known_or_unknown_evidence_and_cleanup(bool knownUsage)
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.Finance.NextFinalization = knownUsage ? FinalizationStatus.Settled : FinalizationStatus.PendingEvidence;
        scenario.Adapter.BeforeComplete = () => {
            scenario.Writer.AbortSource!.Cancel();
            if (!knownUsage) throw new OperationCanceledException(scenario.Context.RequestAborted);
        };
        await scenario.RunResponsesAsync();
        Assert.Equal(knownUsage ? 0 : 1, scenario.Usage.UnknownEvidence);
        Assert.Equal(knownUsage ? 1 : 0, scenario.Usage.VerifiedEvidence);
        Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Equal(0, scenario.Finance.Releases);
        Assert.Equal(DeliveryState.ClientDisconnected, scenario.Usage.Delivery);
        Assert.Equal(0, scenario.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, scenario.Limiter.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Responses_byok_and_hybrid_remain_explicit_request_only_choices(bool hybrid)
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.UseByok(hybrid);
        if (hybrid) scenario.Adapter.ByokError = new(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, "rejected", "Rate limited.");
        await scenario.RunResponsesAsync(JsonSerializer.Serialize(new { model = "gpt-test", input = "Hello", max_output_tokens = 32,
            uzllm = new { provider_key_id = scenario.ByokCredentialId, allow_managed_fallback = hybrid } }));
        Assert.Equal(200, scenario.Context.Response.StatusCode);
        Assert.Equal(1, scenario.Finance.ByokReserves);
        Assert.Equal(0, scenario.Finance.ManagedReserves);
        Assert.Equal(hybrid ? "managed" : "byok", scenario.Context.Response.Headers["X-Uzllm-Billing-Mode"]);
        Assert.Equal(1, scenario.Finance.Finalizes);
    }

    [Fact]
    public async Task Responses_verified_preexecution_rejection_can_failover_under_one_reservation()
    {
        var scenario = new Scenario(twoProviders: true);
        scenario.Adapter.Error = new(ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution,
            true, true, 429, "rejected-id", "Rate limited.");
        await scenario.RunResponsesAsync();
        Assert.Equal(200, scenario.Context.Response.StatusCode);
        Assert.Equal("anthropic", scenario.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal(2, scenario.Usage.Attempts);
        Assert.Equal(1, scenario.Finance.Reserves);
        Assert.Equal(1, scenario.Finance.Finalizes);
        Assert.Equal(1, scenario.Usage.VerifiedEvidence);
        Assert.Equal(0, scenario.Usage.UnknownEvidence);
    }
}
