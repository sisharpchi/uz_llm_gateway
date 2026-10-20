using System.Net;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Observability;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class GatewayExecutionTests
{
    [Theory]
    [InlineData("auth", 401, true, false)]
    [InlineData("redis", 503, false, true)]
    [InlineData("db", 503, false, true)]
    [InlineData("pause", 503, false, true)]
    [InlineData("rate", 429, false, false)]
    [InlineData("budget", 402, true, false)]
    public async Task Observation_pre_admission_denials_preserve_policy_vs_dependency_vs_ambiguous_capacity(
        string failure, int status, bool policy, bool gatewayFault)
    {
        var scenario = new Scenario();
        if (failure == "auth") scenario.Context.Request.Headers.Remove("Authorization");
        if (failure == "redis") scenario.Limiter.NextOutcome = LimitOutcome.DependencyUnavailable;
        if (failure == "db") scenario.Finance.ReserveFailure = new IOException("sensitive database diagnostics");
        if (failure == "pause") scenario.PlatformControls.Enabled = false;
        if (failure == "rate") scenario.Limiter.NextOutcome = LimitOutcome.RateLimited;
        if (failure == "budget") scenario.Finance.NextAdmission = AdmissionStatus.ProjectBudgetExceeded;
        var items = await CaptureAsync(scenario, scenario.RunAsync);
        var terminal = items.Last();
        Assert.Equal(status, terminal.HttpStatus);
        Assert.Equal(policy, terminal.PolicyRejection);
        Assert.Equal(gatewayFault, terminal.GatewayFault);
        Assert.False(terminal.TerminalSuccess);
        Assert.Equal(2, items.Length);
        Assert.Equal(0, scenario.Adapter.CompleteCalls);
        Assert.Equal(0, scenario.Usage.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observation_same_model_failover_keeps_one_execution_with_separate_attempts(bool stream)
    {
        var scenario = new Scenario(stream: stream, twoProviders: true);
        scenario.Adapter.Error = new ProviderError(ProviderErrorCategory.Capacity,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null, "private upstream message");
        var items = await CaptureAsync(scenario, scenario.RunAsync);
        Assert.Single(items.Select(value => value.ExecutionId).Distinct());
        Assert.Single(items, value => value.Phase == ObservationPhase.Started);
        Assert.Equal(2, items.Count(value => value.Phase == ObservationPhase.Attempt));
        Assert.Equal(2, items.Where(value => value.Phase == ObservationPhase.Attempt).Select(value => value.AttemptId).Distinct().Count());
        Assert.Equal(ObservationProviderFault.Capacity, items.First(value => value.Phase == ObservationPhase.Attempt).ProviderFault);
        Assert.Equal(429, items.First(value => value.Phase == ObservationPhase.Attempt).UpstreamStatus);
        Assert.Equal(ObservationExecutionCertainty.RejectedBeforeExecution,
            items.First(value => value.Phase == ObservationPhase.Attempt).Certainty);
        Assert.True(items.Last().TerminalSuccess);
        Assert.Equal(ObservationProviderFault.None, items.Last().ProviderFault);
        Assert.Equal(scenario.RequestId, items.Last().RequestId);
        Assert.Equal(1, scenario.Finance.Reserves);
    }

    [Theory]
    [InlineData("partial", false, false, ObservationProviderFault.Unknown)]
    [InlineData("cancel", true, false, ObservationProviderFault.None)]
    [InlineData("settlement", false, true, ObservationProviderFault.None)]
    public async Task Observation_stream_error_disconnect_and_settlement_failure_never_become_HTTP200_success(
        string failure, bool disconnected, bool gatewayFault, ObservationProviderFault providerFault)
    {
        var scenario = new Scenario(stream: true);
        scenario.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "not retained")];
        if (failure == "partial") scenario.Adapter.FailAfterStreamItems = true;
        if (failure == "cancel") scenario.Writer.DisconnectOnEvent = true;
        if (failure == "settlement") scenario.Finance.FinalizationFailure = new IOException("private failure");
        var terminal = (await CaptureAsync(scenario, scenario.RunAsync)).Last();
        Assert.False(terminal.TerminalSuccess);
        Assert.Equal(disconnected, terminal.ClientDisconnected);
        Assert.Equal(gatewayFault, terminal.GatewayFault);
        Assert.Equal(providerFault, terminal.ProviderFault);
        Assert.Equal(ObservationMode.Sse, terminal.Mode);
        Assert.Equal(1, scenario.Adapter.StreamCalls);
        Assert.False(scenario.Writer.StreamFinished);
    }

    [Fact]
    public async Task Observation_BYOK_authentication_remains_distinct_from_platform_dependency_failure()
    {
        var scenario = new Scenario();
        scenario.UseByok(allowManagedFallback: false);
        scenario.Adapter.ByokError = new ProviderError(ProviderErrorCategory.Authentication,
            ProviderExecutionCertainty.RejectedBeforeExecution, false, false, 401, null, "private credential rejection");
        var terminal = (await CaptureAsync(scenario, scenario.RunAsync)).Last();
        Assert.Equal(ObservationCredential.Byok, terminal.Credential);
        Assert.Equal(ObservationProviderFault.Authentication, terminal.ProviderFault);
        Assert.False(terminal.GatewayFault);
        Assert.False(terminal.TerminalSuccess);
        Assert.Equal(0, scenario.Finance.ManagedReserves);
    }

    [Fact]
    public async Task Observation_native_responses_SSE_records_terminal_protocol_success_without_content()
    {
        var scenario = new Scenario(stream: true);
        scenario.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "private output"),
            new ProviderStreamEvent(ProviderStreamKind.Finish, FinishReason: "stop")];
        var items = await CaptureAsync(scenario,
            () => scenario.RunResponsesAsync("{\"model\":\"gpt-test\",\"input\":\"private prompt\",\"max_output_tokens\":32,\"stream\":true}"),
            "/v1/responses");
        Assert.Equal(ObservationOperation.Responses, items.Last().Operation);
        Assert.True(items.Last().TerminalSuccess);
        Assert.Equal(ObservationMode.Sse, items.Last().Mode);
        var json = System.Text.Json.JsonSerializer.Serialize(items, ObservationProtocol.Json);
        Assert.DoesNotContain("private", json);
        scenario.Context.Response.Body.Position = 0;
        Assert.Contains("event: response.completed", await new StreamReader(scenario.Context.Response.Body).ReadToEndAsync());
    }

    private static async Task<ExecutionObservation[]> CaptureAsync(Scenario scenario, Func<Task> action,
        string path = "/v1/chat/completions")
    {
        var buffer = new ObservationBuffer(TimeProvider.System);
        using var services = new ServiceCollection().AddSingleton(buffer).BuildServiceProvider();
        scenario.Context.RequestServices = services;
        scenario.Context.Request.Path = path;
        scenario.Context.Request.Scheme = "https";
        scenario.Context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var id = Guid.NewGuid();
        scenario.Context.Request.Headers[ObservationEdgeTrust.Header] = id.ToString("N");
        var options = new ObservationCaptureOptions(true, "gateway-a", "test", "unused", new byte[32], false,
            new([IPAddress.Loopback]));
        await new ExecutionObservationMiddleware(_ => action(), buffer, options, TimeProvider.System).InvokeAsync(scenario.Context);
        var items = buffer.Drain("test", false).Observations;
        Assert.All(items, item => Assert.Equal(id, item.ExecutionId));
        return items;
    }
}
