using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UZLLM.Observability;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Routing.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Gateway.ContractTests;

public sealed class GatewayExecutionTests
{
    [Fact]
    public async Task Telemetry_success_records_request_tokens_ttft_and_redacted_bounded_trace_tags()
    {
        using var probe = new TelemetryProbe();
        var fixture = new Scenario();

        await fixture.RunAsync();

        Assert.Equal(1, probe.Sum("uzllm.gateway.requests", "success"));
        Assert.Equal(2, probe.Sum("uzllm.gateway.tokens", "success", "input"));
        Assert.Equal(1, probe.Sum("uzllm.gateway.tokens", "success", "output"));
        Assert.True(probe.HasPositive("uzllm.gateway.ttft.ms"));
        Assert.True(probe.HasPositive("uzllm.gateway.duration.ms"));
        var span = Assert.Single(probe.Spans, activity => activity.OperationName == "gateway.chat");
        Assert.Equal("success", span.GetTagItem("gateway.outcome"));
        Assert.Equal("openai", span.GetTagItem("provider.family"));
        Assert.All(probe.Measurements, measurement =>
            Assert.All(measurement.Tags.Keys, key => Assert.Contains(key,
                new[] { "outcome", "provider", "token.kind" })));
        Assert.DoesNotContain("Hello", string.Join(" ", probe.Measurements.SelectMany(value => value.Tags.Values)));
    }

    [Fact]
    public async Task Telemetry_sse_disconnect_balances_active_stream_and_records_canceled_request()
    {
        using var probe = new TelemetryProbe();
        var fixture = new Scenario(stream: true);
        fixture.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "partial")];
        fixture.Writer.DisconnectOnEvent = true;
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, probe.Sum("uzllm.gateway.requests", "canceled"));
        Assert.Equal(0, probe.Sum("uzllm.gateway.active_streams", "active"));
        Assert.Equal(2, probe.Measurements.Count(value => value.Name == "uzllm.gateway.active_streams"));
        Assert.True(probe.HasPositive("uzllm.gateway.ttft.ms"));
        Assert.Equal("canceled", Assert.Single(probe.Spans,
            activity => activity.OperationName == "gateway.chat").GetTagItem("gateway.outcome"));
    }

    [Fact]
    public async Task Telemetry_provider_and_settlement_errors_emit_bounded_failure_signals()
    {
        using var probe = new TelemetryProbe();
        var providerFailure = new Scenario();
        providerFailure.Adapter.Error = new ProviderError(ProviderErrorCategory.Timeout,
            ProviderExecutionCertainty.Unknown, false, false, null, null, "upstream secret");
        await providerFailure.RunAsync();
        var settlementFailure = new Scenario();
        settlementFailure.Finance.FinalizationFailure = new InvalidOperationException("database unavailable");
        await settlementFailure.RunAsync();

        Assert.Equal(2, probe.Sum("uzllm.gateway.requests", "error"));
        Assert.Equal(2, probe.Sum("uzllm.gateway.errors", "error"));
        Assert.Equal(1, probe.Sum("uzllm.provider.failures", "error"));
        Assert.Equal(1, probe.Sum("uzllm.billing.failures", "error"));
        Assert.DoesNotContain("upstream secret", string.Join(" ", probe.Spans.SelectMany(span =>
            span.TagObjects.Select(tag => tag.Value?.ToString()))));
    }

    private sealed record TelemetryMeasurement(string Name, double Value, IReadOnlyDictionary<string, string> Tags);

    private sealed class TelemetryProbe : IDisposable
    {
        private readonly MeterListener meter = new();
        private readonly ActivityListener traces;
        private readonly ConcurrentQueue<TelemetryMeasurement> measurements = new();
        private readonly ConcurrentQueue<Activity> spans = new();

        public TelemetryProbe()
        {
            meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == UzllmTelemetry.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Capture(instrument, value, tags));
            meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, value, tags));
            meter.Start();
            traces = new ActivityListener
            {
                ShouldListenTo = source => source.Name == UzllmTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => spans.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(traces);
        }

        public IReadOnlyList<TelemetryMeasurement> Measurements => measurements.ToArray();
        public IReadOnlyList<Activity> Spans => spans.ToArray();

        public long Sum(string name, string outcome, string? tokenKind = null) =>
            (long)Measurements.Where(value => value.Name == name
                && value.Tags.GetValueOrDefault("outcome") == outcome
                && (tokenKind is null || value.Tags.GetValueOrDefault("token.kind") == tokenKind))
                .Sum(value => value.Value);

        public bool HasPositive(string name) => Measurements.Any(value => value.Name == name && value.Value > 0);

        private void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags) values[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            measurements.Enqueue(new TelemetryMeasurement(instrument.Name, value, values));
        }

        public void Dispose()
        {
            traces.Dispose();
            meter.Dispose();
        }
    }

    [Fact]
    public async Task Byok_request_uses_tenant_credential_even_when_managed_traffic_is_paused()
    {
        var fixture = new Scenario();
        fixture.UseByok(false);
        fixture.PlatformControls.Enabled = false;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Finance.ByokReserves);
        Assert.Equal(0, fixture.Finance.ManagedReserves);
        Assert.Equal(0, fixture.Finance.LastMaximum!.Value.Value);
        Assert.Equal(fixture.ByokCredentialId, fixture.Adapter.LastContext!.CredentialId);
        Assert.NotNull(fixture.Adapter.LastContext.OrganizationId);
        Assert.NotNull(fixture.Adapter.LastContext.ProjectId);
        Assert.Equal("byok", fixture.Context.Response.Headers["X-Uzllm-Billing-Mode"]);
    }

    [Fact]
    public async Task Byok_ungranted_key_is_rejected_before_financial_admission()
    {
        var fixture = new Scenario();
        fixture.UseByok(false);
        fixture.Reads.Byok = null;

        await fixture.RunAsync();

        Assert.Equal(403, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.ByokReserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Hybrid_opt_in_falls_back_to_managed_only_after_verified_preexecution_rejection()
    {
        var fixture = new Scenario();
        fixture.UseByok(true);
        fixture.Adapter.ByokError = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "BYOK rate limited.");

        await fixture.RunAsync();

        Assert.Equal(2, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.Finance.ByokReserves);
        Assert.True(fixture.Finance.LastByokInput!.AllowManagedFallback);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Null(fixture.Adapter.LastContext!.OrganizationId);
        Assert.Equal("managed", fixture.Context.Response.Headers["X-Uzllm-Billing-Mode"]);
    }

    [Theory]
    [InlineData(ProviderErrorCategory.Authentication, ProviderExecutionCertainty.RejectedBeforeExecution, true)]
    [InlineData(ProviderErrorCategory.Timeout, ProviderExecutionCertainty.Unknown, true)]
    [InlineData(ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution, false)]
    public async Task Byok_auth_unknown_or_non_opted_rejection_never_uses_managed_fallback(
        ProviderErrorCategory category, ProviderExecutionCertainty certainty, bool optIn)
    {
        var fixture = new Scenario();
        fixture.UseByok(optIn);
        fixture.Adapter.ByokError = new ProviderError(category, certainty,
            true, true, 429, null, "BYOK request failed.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(fixture.ByokCredentialId, fixture.Adapter.LastContext!.CredentialId);
        Assert.NotEqual("managed", fixture.Context.Response.Headers["X-Uzllm-Billing-Mode"].ToString());
    }

    [Fact]
    public async Task Models_endpoint_lists_active_catalog_without_provider_secrets()
    {
        var fixture = new Scenario();

        await fixture.ListAsync();

        fixture.Context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(fixture.Context.Response.Body);
        var model = Assert.Single(response.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("gpt-test", model.GetProperty("id").GetString());
        Assert.Equal(8192, model.GetProperty("context_length").GetInt32());
        Assert.False(model.TryGetProperty("credential_id", out _));
    }

    [Fact]
    public async Task Models_endpoint_shows_conservative_price_when_failover_can_cost_more()
    {
        var fixture = new Scenario(twoProviders: true);
        await fixture.ListAsync();
        fixture.Context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(fixture.Context.Response.Body);
        var price = Assert.Single(response.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("pricing");
        Assert.Equal(2000, price.GetProperty("input_micro_usd_per_million").GetInt64());
        Assert.Equal(4000, price.GetProperty("output_micro_usd_per_million").GetInt64());
    }

    [Fact]
    public async Task Tenant_scope_failure_prevents_admission_and_catalog_exposure()
    {
        var fixture = new Scenario();
        fixture.Reads.Active = false;

        await fixture.RunAsync();

        Assert.Equal(403, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Successful_nonstream_request_reserves_dispatches_records_usage_then_settles()
    {
        var fixture = new Scenario();
        fixture.Adapter.Completion = new ProviderCompletion("u1", "gpt-test",
            new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "Hello")]),
            "stop", new ProviderUsage(10, 4, 0, null), "upstream-request");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.True(fixture.Writer.Completed);
        Assert.Equal(DeliveryState.Completed, fixture.Usage.Delivery);
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Fact]
    public async Task Redis_outage_fails_closed_before_reservation_or_provider_call()
    {
        var fixture = new Scenario();
        fixture.Limiter.NextOutcome = LimitOutcome.DependencyUnavailable;

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task PostgreSql_admission_failure_fails_closed_before_provider_call()
    {
        var fixture = new Scenario();
        fixture.Finance.ReserveFailure = new InvalidOperationException("database unavailable");

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Fact]
    public async Task Managed_traffic_incident_switch_rejects_before_reservation_or_dispatch()
    {
        var fixture = new Scenario();
        fixture.PlatformControls.Enabled = false;

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_never_dispatches_twice()
    {
        var fixture = new Scenario();
        fixture.Finance.NextAdmission = AdmissionStatus.Duplicate;
        fixture.Context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();

        await fixture.RunAsync();

        Assert.Equal(409, fixture.Context.Response.StatusCode);
        Assert.Equal(fixture.RequestId.ToString("N"),
            fixture.Context.Response.Headers["X-Original-Request-Id"].ToString());
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.Usage.Attempts);
        Assert.Equal(1, fixture.Limiter.Releases);
    }

    [Fact]
    public async Task Existing_idempotency_claim_is_reported_even_when_Redis_is_unavailable()
    {
        var fixture = new Scenario();
        fixture.Context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        fixture.Usage.ExistingClaim = new ClaimResult(ClaimResultKind.Duplicate, fixture.RequestId);
        fixture.Limiter.NextOutcome = LimitOutcome.DependencyUnavailable;

        await fixture.RunAsync();

        Assert.Equal(409, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Verified_preexecution_rejection_releases_without_unknown_usage()
    {
        var fixture = new Scenario();
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Finance.Releases);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Equal(ExecutionState.RejectedBeforeExecution, fixture.Usage.AttemptFinalState);
        Assert.Equal(502, fixture.Context.Response.StatusCode);
    }

    [Fact]
    public async Task Same_model_rate_limit_fails_over_once_with_one_worst_case_reservation()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, "openai-req",
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(2, fixture.Usage.Attempts);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Equal(0, fixture.Finance.Releases);
        Assert.Equal("anthropic", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal("deterministic:failover:anthropic", fixture.Usage.RouteStrategy);
        Assert.Equal(17, fixture.Finance.LastMaximum!.Value.Value);
        Assert.Equal(fixture.AnthropicPriceId, fixture.Usage.VerifiedPriceId);
        Assert.Equal(1, fixture.Health.Failures);
        Assert.Contains(fixture.Health.Outcomes, value =>
            value.Outcome == ProviderHealthOutcome.CredentialThrottled
            && value.Target.OrganizationId is null);
    }

    [Fact]
    public async Task Byok_authentication_rejection_is_scoped_to_tenant_credential()
    {
        var fixture = new Scenario();
        fixture.UseByok(allowManagedFallback: true);
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.Authentication,
            ProviderExecutionCertainty.RejectedBeforeExecution, false, false, 401, null,
            "Upstream credential rejected.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.Finance.Finalizes);
        Assert.Contains(fixture.Health.Outcomes, value =>
            value.Outcome == ProviderHealthOutcome.CredentialRejected
            && value.Target.CredentialId == fixture.ByokCredentialId
            && value.Target.OrganizationId is not null);
    }

    [Fact]
    public async Task Busy_half_open_primary_probe_uses_eligible_same_model_fallback()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.Health.BeginOpenPrimary = true;

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.Attempts);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal("anthropic", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
    }

    [Fact]
    public async Task Unknown_upstream_5xx_never_fails_over_or_releases_as_zero()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.Upstream5xx,
            ProviderExecutionCertainty.Unknown, false, false, 503, "openai-req",
            "Provider outcome is unknown.");
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(0, fixture.Finance.Releases);
        Assert.Equal(1, fixture.Finance.Finalizes);
    }

    [Fact]
    public async Task Pinned_provider_does_not_substitute_after_eligible_rejection()
    {
        var fixture = new Scenario(twoProviders: true, modelCode: "openai/gpt-test");
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Finance.Releases);
    }

    [Fact]
    public async Task Explicit_anthropic_route_uses_only_anthropic_mapping()
    {
        var fixture = new Scenario(twoProviders: true, modelCode: "anthropic/gpt-test");

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.Attempts);
        Assert.Equal("anthropic", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.Equal("deterministic:anthropic", fixture.Usage.RouteStrategy);
        Assert.Equal("anthropic/gpt-test", fixture.Writer.Model);
    }

    [Fact]
    public async Task Explicit_google_stream_records_one_verified_evidence_and_settles()
    {
        var fixture = new Scenario(stream: true, googleProvider: true,
            modelCode: "google/gpt-test");
        fixture.GoogleAdapter.StreamItems = [
            new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "Hello",
                ProviderRequestId: "google-response"),
            new ProviderStreamEvent(ProviderStreamKind.Finish, FinishReason: "stop",
                ProviderRequestId: "google-response"),
            new ProviderStreamEvent(ProviderStreamKind.Usage,
                Usage: new ProviderUsage(12, 5, 4, 2), ProviderRequestId: "google-response")
        ];

        await fixture.RunAsync();

        Assert.Equal(1, fixture.GoogleAdapter.StreamCalls);
        Assert.Equal(0, fixture.Adapter.StreamCalls);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Equal("google", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
        Assert.True(fixture.Writer.StreamFinished);
    }

    [Fact]
    public async Task Partial_google_stream_remains_unknown_and_never_replays()
    {
        var fixture = new Scenario(stream: true, googleProvider: true,
            modelCode: "google/gpt-test");
        fixture.GoogleAdapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta,
            Text: "partial", ProviderRequestId: "google-partial")];
        fixture.GoogleAdapter.FailAfterStreamItems = true;
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.GoogleAdapter.StreamCalls);
        Assert.Equal(0, fixture.Adapter.StreamCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(0, fixture.Usage.VerifiedEvidence);
        Assert.False(fixture.Writer.StreamFinished);
    }

    [Fact]
    public async Task DeepSeek_missing_cached_input_rate_fails_before_wallet_reservation()
    {
        var fixture = new Scenario(deepSeekProvider: true,
            deepSeekCachedInputRate: null, modelCode: "deepseek/gpt-test");

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.DeepSeekAdapter.CompleteCalls);
        Assert.Equal(0, fixture.Usage.Attempts);
    }

    [Fact]
    public async Task DeepSeek_with_frozen_cached_rate_records_usage_and_settles()
    {
        var fixture = new Scenario(deepSeekProvider: true,
            deepSeekCachedInputRate: 500, modelCode: "deepseek/gpt-test");
        fixture.DeepSeekAdapter.Completion = new ProviderCompletion("ds_001", "deepseek-flash",
            new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "ok")]),
            "stop", new ProviderUsage(20, 9, 8, 4), "ds_001");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.DeepSeekAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal(0, fixture.Usage.UnknownEvidence);
        Assert.Equal("deepseek", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
    }

    [Fact]
    public async Task Adapter_capability_rejection_happens_before_financial_reservation()
    {
        var fixture = new Scenario(twoProviders: true, modelCode: "anthropic/gpt-test");
        fixture.AnthropicAdapter.Eligible = false;

        await fixture.RunAsync();

        Assert.Equal(400, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(0, fixture.Usage.Attempts);
    }

    [Fact]
    public async Task Price_route_selects_cheapest_eligible_mapping_and_keeps_worst_case_hold()
    {
        var fixture = new Scenario(twoProviders: true,
            anthropicInputRate: 500, anthropicOutputRate: 1000);
        fixture.UsePriceRouting();

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal("price:anthropic", fixture.Usage.RouteStrategy);
        Assert.Equal(fixture.AnthropicPriceId, fixture.Usage.VerifiedPriceId);
        Assert.Equal(9, fixture.Finance.LastMaximum!.Value.Value);
    }

    [Fact]
    public async Task Price_route_uses_stable_provider_priority_when_estimates_tie()
    {
        var fixture = new Scenario(twoProviders: true,
            anthropicInputRate: 1000, anthropicOutputRate: 2000);
        fixture.UsePriceRouting();

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal("price:openai", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Price_route_does_not_select_cheaper_incapable_mapping()
    {
        var fixture = new Scenario(twoProviders: true,
            anthropicInputRate: 500, anthropicOutputRate: 1000,
            openAiTools: true);
        fixture.UsePriceRouting(withTools: true);

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal("price:openai", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Price_route_excludes_cheaper_open_circuit_before_scoring()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.UsePriceRouting();
        fixture.Health.OpenPrimary = true;

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal("price:anthropic", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Price_route_fails_over_within_same_model_under_one_ceiling()
    {
        var fixture = new Scenario(twoProviders: true,
            anthropicInputRate: 500, anthropicOutputRate: 1000);
        fixture.UsePriceRouting();
        fixture.AnthropicAdapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(9, fixture.Finance.LastMaximum!.Value.Value);
        Assert.Equal("price:failover:openai", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Price_route_keeps_explicit_byok_primary_even_if_managed_price_is_lower()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.UseByok(true, priceRouting: true);

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(fixture.ByokCredentialId, fixture.Adapter.LastContext!.CredentialId);
        Assert.Equal("price:byok:openai", fixture.Usage.RouteStrategy);
        Assert.Empty(fixture.Performance.Observations);
    }

    [Theory]
    [InlineData("latency")]
    [InlineData("throughput")]
    [InlineData("auto")]
    public async Task Performance_route_uses_complete_recent_windows_after_eligibility(string strategy)
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.UsePerformanceRouting(strategy);
        fixture.SetPerformanceWindows(primary: new(20, 20, 100m, 10m, 0m, DateTimeOffset.UtcNow),
            secondary: new(20, 20, 20m, 50m, 0m, DateTimeOffset.UtcNow));

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal($"{strategy}:anthropic", fixture.Usage.RouteStrategy);
        Assert.Equal(17, fixture.Finance.LastMaximum!.Value.Value);
        Assert.Single(fixture.Performance.Observations);
        Assert.True(fixture.Performance.Observations[0].Succeeded);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(20, true)]
    public async Task Performance_route_uses_deterministic_priority_when_samples_sparse_or_stale(
        int secondarySamples, bool stale)
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.UsePerformanceRouting("latency");
        fixture.SetPerformanceWindows(primary: new(20, 20, 100m, 10m, 0m, DateTimeOffset.UtcNow),
            secondary: new(secondarySamples, secondarySamples, 10m, 50m, 0m,
                stale ? DateTimeOffset.UtcNow.AddMinutes(-10) : DateTimeOffset.UtcNow));

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal("latency:openai", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Performance_route_excludes_open_circuit_even_when_metrics_favor_it()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.UsePerformanceRouting("latency");
        fixture.Health.OpenPrimary = true;
        fixture.SetPerformanceWindows(primary: new(20, 20, 1m, 100m, 0m, DateTimeOffset.UtcNow),
            secondary: new(20, 20, 100m, 10m, 0m, DateTimeOffset.UtcNow));

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
    }

    [Fact]
    public async Task Explicit_cross_model_fallback_reserves_changed_price_and_reports_selected_model()
    {
        var fixture = new Scenario(fallbackModel: true);
        fixture.UseFallbackModel();
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(43, fixture.Finance.LastMaximum!.Value.Value);
        Assert.Equal(fixture.FallbackPriceId, fixture.Usage.VerifiedPriceId);
        Assert.Equal("gpt-test", fixture.Context.Response.Headers["X-Uzllm-Requested-Model"]);
        Assert.Equal("other-test", fixture.Context.Response.Headers["X-Uzllm-Model"]);
        Assert.Equal("other-test", fixture.Writer.Model);
        Assert.Equal("deterministic:failover:model-fallback:anthropic", fixture.Usage.RouteStrategy);
    }

    [Fact]
    public async Task Cross_model_fallback_does_not_bypass_capability_or_output_eligibility()
    {
        var fixture = new Scenario(openAiTools: true, fallbackModel: true);
        fixture.UseFallbackModel(withTools: true);
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(9, fixture.Finance.LastMaximum!.Value.Value);

        var tooSmall = new Scenario(fallbackModel: true);
        tooSmall.UseFallbackModel(outputTokens: 200);
        tooSmall.Adapter.Error = fixture.Adapter.Error;
        await tooSmall.RunAsync();
        Assert.Equal(0, tooSmall.AnthropicAdapter.CompleteCalls);
    }

    [Fact]
    public async Task Cross_model_fallback_never_replays_unknown_execution()
    {
        var fixture = new Scenario(fallbackModel: true);
        fixture.UseFallbackModel();
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.Timeout,
            ProviderExecutionCertainty.Unknown, true, true, null, null,
            "Provider outcome is unknown.");
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
    }

    [Fact]
    public async Task Unknown_fallback_model_rejects_before_wallet_reservation()
    {
        var fixture = new Scenario();
        fixture.UseFallbackModel();

        await fixture.RunAsync();

        Assert.Equal(400, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Cross_model_stream_falls_back_only_before_output_and_labels_selected_model()
    {
        var fixture = new Scenario(stream: true, fallbackModel: true);
        fixture.UseFallbackModel(stream: true);
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");
        fixture.AnthropicAdapter.StreamItems =
        [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "answer"),
            new ProviderStreamEvent(ProviderStreamKind.Usage,
                Usage: new ProviderUsage(2, 1, 0, null))];

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.StreamCalls);
        Assert.Equal("other-test", fixture.Writer.Model);
        Assert.True(fixture.Writer.StreamFinished);
        Assert.Equal(1, fixture.Usage.VerifiedEvidence);
        Assert.Equal("other-test", fixture.Context.Response.Headers["X-Uzllm-Model"]);
    }

    [Fact]
    public async Task Cross_model_stream_never_falls_back_after_partial_output()
    {
        var fixture = new Scenario(stream: true, fallbackModel: true);
        fixture.UseFallbackModel(stream: true);
        fixture.Adapter.StreamItems =
            [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "partial")];
        fixture.Adapter.FailAfterStreamItems = true;
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.False(fixture.Writer.StreamFinished);
    }

    [Fact]
    public async Task Cross_model_fallback_uses_selected_models_default_output_limit()
    {
        var fixture = new Scenario(fallbackModel: true);
        fixture.Context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"model":"gpt-test","messages":[{"role":"user","content":"Hello"}],"uzllm":{"fallback_models":["other-test"]}}
            """));
        fixture.Adapter.Error = new ProviderError(ProviderErrorCategory.RateLimited,
            ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
            "Provider rate limit was reached.");

        await fixture.RunAsync();

        Assert.Equal(128, fixture.AnthropicAdapter.LastMaxOutputTokens);
        Assert.Equal(44, fixture.Finance.LastMaximum!.Value.Value);
    }

    [Fact]
    public async Task Open_circuit_excludes_primary_before_reservation_and_attempt()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.Health.OpenPrimary = true;

        await fixture.RunAsync();

        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(1, fixture.AnthropicAdapter.CompleteCalls);
        Assert.Equal(1, fixture.Usage.Attempts);
        Assert.Equal("anthropic", fixture.Context.Response.Headers["X-Uzllm-Provider"]);
    }

    [Fact]
    public async Task Health_dependency_failure_fails_closed_before_reservation()
    {
        var fixture = new Scenario(twoProviders: true);
        fixture.Health.Unavailable = true;

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
    }

    [Fact]
    public async Task Health_dependency_failure_after_reservation_releases_without_dispatch()
    {
        var fixture = new Scenario();
        fixture.Health.BeginUnavailable = true;

        await fixture.RunAsync();

        Assert.Equal(503, fixture.Context.Response.StatusCode);
        Assert.Equal(1, fixture.Finance.Reserves);
        Assert.Equal(1, fixture.Finance.Releases);
        Assert.Equal(0, fixture.Usage.Attempts);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
    }

    [Fact]
    public async Task Model_limit_rejection_remains_a_client_error_with_multiple_mappings()
    {
        var fixture = new Scenario(twoProviders: true, outputTokens: 10000);

        await fixture.RunAsync();

        Assert.Equal(400, fixture.Context.Response.StatusCode);
        Assert.Equal(0, fixture.Finance.Reserves);
        Assert.Equal(0, fixture.Adapter.CompleteCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.CompleteCalls);
    }

    [Fact]
    public async Task Partial_stream_rejection_does_not_replay_on_second_provider()
    {
        var fixture = new Scenario(stream: true, twoProviders: true);
        fixture.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "partial"),
            new ProviderStreamEvent(ProviderStreamKind.Error, Error: new ProviderError(
                ProviderErrorCategory.RateLimited, ProviderExecutionCertainty.RejectedBeforeExecution,
                true, true, 429, null, "Provider rate limit was reached."))];
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.False(fixture.Writer.StreamFinished);
    }

    [Fact]
    public async Task Sse_error_event_before_output_is_unknown_and_never_falls_back()
    {
        var fixture = new Scenario(stream: true, twoProviders: true);
        fixture.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.Error,
            Error: new ProviderError(ProviderErrorCategory.RateLimited,
                ProviderExecutionCertainty.RejectedBeforeExecution, true, true, 429, null,
                "Provider rate limit was reached."))];
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(0, fixture.AnthropicAdapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(0, fixture.Finance.Releases);
    }

    [Fact]
    public async Task Sse_disconnect_cancels_delivery_but_persists_unknown_evidence_and_finalizes()
    {
        var fixture = new Scenario(stream: true);
        fixture.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "partial")];
        fixture.Writer.DisconnectOnEvent = true;
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(1, fixture.Finance.Finalizes);
        Assert.Equal(DeliveryState.ClientDisconnected, fixture.Usage.Delivery);
        Assert.Equal(1, fixture.Limiter.Releases);
        Assert.True(fixture.Adapter.ObservedStreamToken.IsCancellationRequested);
        Assert.True(fixture.Adapter.StreamDisposed);
    }

    [Fact]
    public async Task Partial_stream_failure_never_falls_back_or_sends_done()
    {
        var fixture = new Scenario(stream: true);
        fixture.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.TextDelta, Text: "partial")];
        fixture.Adapter.FailAfterStreamItems = true;
        fixture.Finance.NextFinalization = FinalizationStatus.PendingEvidence;

        await fixture.RunAsync();

        Assert.Equal(1, fixture.Adapter.StreamCalls);
        Assert.Equal(1, fixture.Usage.UnknownEvidence);
        Assert.Equal(1, fixture.Writer.Errors);
        Assert.False(fixture.Writer.StreamFinished);
        Assert.Equal(DeliveryState.Partial, fixture.Usage.Delivery);
    }

    [Fact]
    public async Task Unknown_usage_and_known_usage_with_failed_settlement_have_distinct_recovery_states()
    {
        var unknown = new Scenario(stream: true);
        unknown.Adapter.StreamItems = [new ProviderStreamEvent(ProviderStreamKind.Finish, FinishReason: "stop")];
        unknown.Finance.NextFinalization = FinalizationStatus.PendingEvidence;
        await unknown.RunAsync();
        Assert.Equal(1, unknown.Usage.UnknownEvidence);
        Assert.Equal(0, unknown.Usage.VerifiedEvidence);
        Assert.True(unknown.Writer.StreamFinished);

        var known = new Scenario();
        known.Adapter.Completion = new ProviderCompletion("u", "gpt-test",
            new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "answer")]),
            "stop", new ProviderUsage(10, 2, 0, null), "provider-id");
        known.Finance.FinalizationFailure = new InvalidOperationException("database commit failed");
        await known.RunAsync();
        Assert.Equal(1, known.Usage.VerifiedEvidence);
        Assert.Equal(0, known.Usage.UnknownEvidence);
        Assert.Equal(503, known.Context.Response.StatusCode);
        Assert.False(known.Writer.Completed);
    }

    [Fact]
    public async Task Payload_capture_failure_releases_reservation_before_provider_dispatch()
    {
        var scenario = new Scenario();
        scenario.Reads.RetainPayload = true;
        scenario.PayloadRetention.FailCapture = true;

        await scenario.RunAsync();

        Assert.Equal(1, scenario.PayloadRetention.RequestCaptures);
        Assert.Equal(1, scenario.Finance.Releases);
        Assert.Equal(0, scenario.Usage.Attempts);
        Assert.Equal(DeliveryState.NotStarted, scenario.Usage.Delivery);
        Assert.Equal(503, scenario.Context.Response.StatusCode);
    }

    [Fact]
    public async Task Default_payload_policy_does_not_capture_or_add_response_work()
    {
        var scenario = new Scenario();

        await scenario.RunAsync();

        Assert.Equal(0, scenario.PayloadRetention.RequestCaptures);
        Assert.Equal(1, scenario.Usage.Attempts);
    }

    private sealed class Scenario
    {
        public readonly Guid RequestId = Guid.NewGuid();
        private readonly Guid organizationId = Guid.NewGuid();
        private readonly Guid projectId = Guid.NewGuid();
        private readonly Guid apiKeyId = Guid.NewGuid();
        private readonly Guid providerId = Guid.NewGuid();
        private readonly Guid mappingId = Guid.NewGuid();
        private readonly Guid anthropicMappingId = Guid.NewGuid();
        private readonly Guid googleMappingId = Guid.NewGuid();
        private readonly Guid deepSeekMappingId = Guid.NewGuid();
        public readonly Guid FallbackPriceId = Guid.NewGuid();
        private readonly Guid credentialId = Guid.NewGuid();
        public readonly Guid ByokCredentialId = Guid.NewGuid();
        private readonly Guid feeId = Guid.NewGuid();
        private readonly Guid priceId = Guid.NewGuid();
        public readonly Guid AnthropicPriceId = Guid.NewGuid();
        public readonly DefaultHttpContext Context = new();
        public readonly FakeLimiter Limiter = new();
        public readonly FakeFinance Finance;
        public readonly FakeUsage Usage;
        public readonly FakeAdapter Adapter = new("openai");
        public readonly FakeAdapter AnthropicAdapter = new("anthropic");
        public readonly FakeAdapter GoogleAdapter = new("google");
        public readonly FakeAdapter DeepSeekAdapter = new("deepseek");
        public readonly FakeHealth Health;
        public readonly FakePerformance Performance = new();
        public readonly FakePlatformControls PlatformControls = new();
        public readonly FakePayloadRetention PayloadRetention = new();
        public readonly FakeWriter Writer = new();
        private readonly CancellationTokenSource abort = new();
        public readonly FakeReadStore Reads;
        private readonly InferenceGateway gateway;

        public Scenario(bool stream = false, bool twoProviders = false,
            string modelCode = "gpt-test", int outputTokens = 100,
            long anthropicInputRate = 2000, long anthropicOutputRate = 4000,
            bool openAiTools = false, bool fallbackModel = false,
            bool fallbackTools = false, bool googleProvider = false,
            bool deepSeekProvider = false, long? deepSeekCachedInputRate = 500)
        {
            Finance = new FakeFinance(RequestId, organizationId, projectId, apiKeyId, feeId);
            Usage = new FakeUsage(RequestId);
            Health = new FakeHealth(mappingId);
            var now = DateTimeOffset.UtcNow;
            var model = new CanonicalModel(Guid.NewGuid(), "gpt-test", "GPT Test", 8192, 512,
                [CatalogCapability.Text], CatalogStatus.Active, now);
            var price = new ModelPrice(priceId, mappingId, now.AddDays(-1), null,
                1000, 2000, null, "{}", now);
            var mapping = new ProviderModel(mappingId, providerId, model.Id, "gpt-test", null,
                CatalogStatus.Active, openAiTools ? [CatalogCapability.Tools] : [], now);
            var provider = new CatalogProvider(providerId, "openai", "OpenAI", CatalogStatus.Active, now);
            var mappings = new List<CatalogProviderModelSummary>
            { new(mapping, provider, price) };
            var prices = new Dictionary<Guid, ModelPrice> { [mappingId] = price };
            if (twoProviders)
            {
                var anthropicId = Guid.NewGuid();
                var anthropicMapping = new ProviderModel(anthropicMappingId, anthropicId, model.Id,
                    "claude-test", null, CatalogStatus.Active, [], now);
                var anthropicPrice = new ModelPrice(AnthropicPriceId, anthropicMappingId,
                    now.AddDays(-1), null, anthropicInputRate, anthropicOutputRate, null, "{}", now);
                mappings.Add(new CatalogProviderModelSummary(anthropicMapping,
                    new CatalogProvider(anthropicId, "anthropic", "Anthropic", CatalogStatus.Active, now),
                    anthropicPrice));
                prices[anthropicMappingId] = anthropicPrice;
            }
            if (googleProvider)
            {
                var googleId = Guid.NewGuid();
                var googleMapping = new ProviderModel(googleMappingId, googleId, model.Id,
                    "gemini-test", null, CatalogStatus.Active, [], now);
                var googlePrice = new ModelPrice(Guid.NewGuid(), googleMappingId,
                    now.AddDays(-1), null, 1500, 3000, 500, "{}", now);
                mappings.Add(new CatalogProviderModelSummary(googleMapping,
                    new CatalogProvider(googleId, "google", "Google", CatalogStatus.Active, now),
                    googlePrice));
                prices[googleMappingId] = googlePrice;
            }
            if (deepSeekProvider)
            {
                var deepSeekId = Guid.NewGuid();
                var deepSeekMapping = new ProviderModel(deepSeekMappingId, deepSeekId, model.Id,
                    "deepseek-flash", null, CatalogStatus.Active, [], now);
                var deepSeekPrice = new ModelPrice(Guid.NewGuid(), deepSeekMappingId,
                    now.AddDays(-1), null, 1500, 3000, deepSeekCachedInputRate, "{}", now);
                mappings.Add(new CatalogProviderModelSummary(deepSeekMapping,
                    new CatalogProvider(deepSeekId, "deepseek", "DeepSeek", CatalogStatus.Active, now),
                    deepSeekPrice));
                prices[deepSeekMappingId] = deepSeekPrice;
            }
            var summaries = new List<CatalogModelSummary> { new(model, mappings) };
            if (fallbackModel)
            {
                var fallback = new CanonicalModel(Guid.NewGuid(), "other-test", "Other Test",
                    4096, 128, [CatalogCapability.Text], CatalogStatus.Active, now);
                var fallbackProvider = new CatalogProvider(Guid.NewGuid(), "anthropic",
                    "Anthropic", CatalogStatus.Active, now);
                var fallbackMapping = new ProviderModel(anthropicMappingId,
                    fallbackProvider.Id, fallback.Id, "other-upstream", null,
                    CatalogStatus.Active,
                    fallbackTools ? [CatalogCapability.Tools] : [], now);
                var fallbackPrice = new ModelPrice(FallbackPriceId, anthropicMappingId,
                    now.AddDays(-1), null, 10_000, 20_000, null, "{}", now);
                summaries.Add(new CatalogModelSummary(fallback,
                    [new CatalogProviderModelSummary(fallbackMapping, fallbackProvider, fallbackPrice)]));
                prices[anthropicMappingId] = fallbackPrice;
            }
            var catalog = new FakeCatalog(summaries, prices);
            var fee = new FeePolicyVersion(feeId, "default", 0, UsdMicroAmount.Zero,
                now.AddDays(-1), null, now);
            Reads = new FakeReadStore(new GatewayTenantScope(organizationId, projectId), fee, credentialId);
            Context.Request.Headers.Authorization = "Bearer valid";
            Context.Request.ContentType = "application/json";
            Context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""
                {"model":"MODEL","messages":[{"role":"user","content":"Hello"}],"max_completion_tokens":MAX,"stream":STREAM}
                """.Replace("STREAM", stream ? "true" : "false").Replace("MODEL", modelCode)
                    .Replace("MAX", outputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Context.Response.Body = new MemoryStream();
            Context.RequestAborted = abort.Token;
            Writer.AbortSource = abort;
            gateway = new InferenceGateway(new FakeAuthenticator(apiKeyId, projectId), Reads,
                catalog, Limiter, new FakeQuota(), new RequestConstraintValidator(), Finance,
                Usage, PayloadRetention, new FakeAdapterSelector(Adapter, AnthropicAdapter,
                    GoogleAdapter, DeepSeekAdapter), Health, Performance,
                ProviderPerformanceOptions.Default,
                new FakeWriterFactory(Writer), PlatformControls,
                new GatewayOptions("default", 1_048_576, TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(15), new LimitPolicy(60, 600, 8, 64, TimeSpan.FromMinutes(15))),
                TimeProvider.System, NullLogger<InferenceGateway>.Instance, new GatewayTelemetry());
        }

        public Task RunAsync() => gateway.ChatAsync(Context, Context.RequestAborted);
        public Task ListAsync() => gateway.ListModelsAsync(Context, Context.RequestAborted);
        public void UseByok(bool allowManagedFallback, bool priceRouting = false)
        {
            Reads.Byok = new GatewayByokCredential(ByokCredentialId, providerId);
            var request = priceRouting
                ? JsonSerializer.SerializeToUtf8Bytes(new
                {
                    model = "gpt-test", messages = new[] { new { role = "user", content = "Hello" } },
                    max_completion_tokens = 100,
                    uzllm = new { provider_key_id = ByokCredentialId,
                        allow_managed_fallback = allowManagedFallback, routing = "price" }
                })
                : JsonSerializer.SerializeToUtf8Bytes(new
                {
                    model = "gpt-test", messages = new[] { new { role = "user", content = "Hello" } },
                    max_completion_tokens = 100,
                    uzllm = new { provider_key_id = ByokCredentialId,
                        allow_managed_fallback = allowManagedFallback }
                });
            Context.Request.Body = new MemoryStream(request);
        }

        public void UsePriceRouting(bool withTools = false)
        {
            var json = withTools
                ? """{"model":"gpt-test","messages":[{"role":"user","content":"Hello"}],"max_completion_tokens":100,"tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}],"uzllm":{"routing":"price"}}"""
                : """{"model":"gpt-test","messages":[{"role":"user","content":"Hello"}],"max_completion_tokens":100,"uzllm":{"routing":"price"}}""";
            Context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        }

        public void UsePerformanceRouting(string strategy)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(new
            {
                model = "gpt-test", messages = new[] { new { role = "user", content = "Hello" } },
                max_completion_tokens = 100, uzllm = new { routing = strategy }
            });
            Context.Request.Body = new MemoryStream(json);
        }

        public void SetPerformanceWindows(ProviderPerformanceWindow primary,
            ProviderPerformanceWindow secondary)
        {
            Performance.Windows[mappingId] = primary;
            Performance.Windows[anthropicMappingId] = secondary;
        }

        public void UseFallbackModel(bool withTools = false, int outputTokens = 100,
            bool stream = false)
        {
            var tools = withTools
                ? """, "tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}]"""
                : "";
            var json = "{" + $"\"model\":\"gpt-test\",\"messages\":[{{\"role\":\"user\",\"content\":\"Hello\"}}],\"max_completion_tokens\":{outputTokens},\"stream\":{(stream ? "true" : "false")},\"uzllm\":{{\"fallback_models\":[\"other-test\"]}}"
                + tools + "}";
            Context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        }
    }

    private sealed class FakePayloadRetention : IPayloadRetentionService
    {
        public bool FailCapture;
        public int RequestCaptures;
        public Task<PayloadRetentionPolicy> GetPolicyAsync(Guid organizationId, Guid projectId,
            CancellationToken cancellationToken) => Task.FromResult(new PayloadRetentionPolicy(false, 1440));
        public Task<PayloadRetentionPolicy?> SetPolicyAsync(Guid organizationId, Guid projectId,
            Guid accountId, bool enabled, int retentionMinutes, CancellationToken cancellationToken) =>
            Task.FromResult<PayloadRetentionPolicy?>(null);
        public Task<bool> CaptureRequestAsync(Guid organizationId, Guid projectId, Guid requestId,
            byte[] payload, CancellationToken cancellationToken)
        {
            RequestCaptures++;
            if (FailCapture) throw new InvalidOperationException("Encryption unavailable");
            return Task.FromResult(false);
        }
        public Task CaptureResponseAsync(Guid organizationId, Guid projectId, Guid requestId,
            byte[] payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<RetainedPayload?> GetPayloadAsync(Guid organizationId, Guid projectId,
            Guid requestId, CancellationToken cancellationToken) => Task.FromResult<RetainedPayload?>(null);
        public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class FakeAuthenticator(Guid keyId, Guid projectId) : IApiKeyAuthenticator
    {
        public Task<GatewayApiKeyAuthentication?> AuthenticateAsync(string? presentedKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GatewayApiKeyAuthentication?>(presentedKey == "valid"
                ? new GatewayApiKeyAuthentication(keyId, projectId) : null);
    }

    private sealed class FakeReadStore(GatewayTenantScope tenant, FeePolicyVersion fee, Guid credentialId)
        : IGatewayReadStore
    {
        public bool Active = true;
        public bool RetainPayload;
        public GatewayByokCredential? Byok;
        public Task<GatewayTenantScope?> FindTenantAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<GatewayTenantScope?>(Active && projectId == tenant.ProjectId
                ? tenant with { RetainPayload = RetainPayload } : null);
        public Task<FeePolicyVersion?> FindFeePolicyAsync(string policyCode, DateTimeOffset at,
            CancellationToken cancellationToken) => Task.FromResult<FeePolicyVersion?>(fee);
        public Task<Guid?> FindPlatformCredentialAsync(Guid providerId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(credentialId);
        public Task<GatewayByokCredential?> FindByokCredentialAsync(Guid organizationId,
            Guid projectId, Guid byokId, string canonicalModelCode,
            CancellationToken cancellationToken) => Task.FromResult(Byok is { } value
                && organizationId == tenant.OrganizationId && projectId == tenant.ProjectId
                && byokId == value.Id && canonicalModelCode == "gpt-test" ? Byok : null);
    }

    private sealed class FakeCatalog(IReadOnlyList<CatalogModelSummary> summaries,
        IReadOnlyDictionary<Guid, ModelPrice> prices) : ICatalogService
    {
        public Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(summaries);
        public Task<ModelPrice?> FindEffectivePriceAsync(Guid providerModelId, DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(prices.GetValueOrDefault(providerModelId));
        public Task<CatalogProvider> AddProviderAsync(string code, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CanonicalModel> AddModelAsync(string canonicalCode, string displayName, int contextLength, int maxOutputTokens, IEnumerable<CatalogCapability> capabilities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderModel> AddProviderModelAsync(Guid providerId, Guid modelId, string upstreamModelCode, string? endpointReference, IEnumerable<CatalogCapability>? capabilityOverrides, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModelPrice> AddPriceAsync(Guid providerModelId, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, long inputPriceMicroUsdPerMillion, long outputPriceMicroUsdPerMillion, long? cachedInputPriceMicroUsdPerMillion, string? extraPricingJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetProviderStatusAsync(Guid providerId, CatalogStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetModelStatusAsync(Guid modelId, CatalogStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetProviderModelStatusAsync(Guid providerModelId, CatalogStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeLimiter : IDistributedAdmissionLimiter
    {
        public LimitOutcome NextOutcome = LimitOutcome.Admitted;
        public int Releases;
        public Task<LimitDecision> TryAcquireAsync(LimitScope scope, LimitPolicy policy, Guid requestId,
            CancellationToken cancellationToken = default) => Task.FromResult(new LimitDecision(NextOutcome,
                NextOutcome == LimitOutcome.Admitted ? new LimitLease(scope, requestId) : null, null));
        public Task<LimitReleaseOutcome> ReleaseAsync(LimitLease lease,
            CancellationToken cancellationToken = default)
        { Releases++; return Task.FromResult(LimitReleaseOutcome.Released); }
    }

    private sealed class FakeQuota : IProviderQuotaProtection
    {
        public Task<ProviderQuotaDecision> CheckAsync(Guid credentialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderQuotaDecision(ProviderQuotaOutcome.Available, null));
        public Task<bool> MarkExhaustedAsync(Guid credentialId, TimeSpan retryAfter,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeHealth(Guid primaryMappingId) : IProviderHealthService
    {
        public bool OpenPrimary, BeginOpenPrimary, BeginUnavailable, Unavailable;
        public int Failures;
        public readonly List<(ProviderHealthTarget Target, ProviderHealthOutcome Outcome)> Outcomes = [];
        public Task<ProviderHealthState> CheckAsync(ProviderHealthTarget target,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable
                ? ProviderHealthState.DependencyUnavailable
                : OpenPrimary && target.ProviderModelId == primaryMappingId ? ProviderHealthState.Open
                    : ProviderHealthState.Healthy);
        public async Task<ProviderHealthAttempt> BeginAttemptAsync(ProviderHealthTarget target,
            CancellationToken cancellationToken = default)
        {
            var state = await CheckAsync(target, cancellationToken);
            if (BeginUnavailable) state = ProviderHealthState.DependencyUnavailable;
            if (BeginOpenPrimary && target.ProviderModelId == primaryMappingId)
                state = ProviderHealthState.Open;
            return new ProviderHealthAttempt(state, state == ProviderHealthState.Healthy
                ? new ProviderHealthPermit(target, Guid.NewGuid()) : null);
        }
        public Task CompleteAttemptAsync(ProviderHealthPermit permit, ProviderHealthOutcome outcome,
            CancellationToken cancellationToken = default)
        {
            Outcomes.Add((permit.Target, outcome));
            if (outcome is ProviderHealthOutcome.CredentialThrottled or ProviderHealthOutcome.EndpointTransientFailure)
                Failures++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePerformance : IProviderPerformanceService
    {
        public readonly Dictionary<Guid, ProviderPerformanceWindow> Windows = new();
        public readonly List<ProviderPerformanceObservation> Observations = [];
        public Task<ProviderPerformanceWindow?> ReadAsync(Guid providerModelId, bool isStream,
            CancellationToken cancellationToken = default) => Task.FromResult(
                Windows.TryGetValue(providerModelId, out var value) ? value : null);
        public Task RecordAsync(ProviderPerformanceObservation observation,
            CancellationToken cancellationToken = default)
        { Observations.Add(observation); return Task.CompletedTask; }
    }

    private sealed class FakeFinance(Guid requestId, Guid organizationId, Guid projectId,
        Guid apiKeyId, Guid feeId) : IFinancialService
    {
        public int Reserves, ManagedReserves, ByokReserves, Finalizes, Releases;
        public UsdMicroAmount? LastMaximum;
        public ByokAdmissionInput? LastByokInput;
        public AdmissionStatus NextAdmission = AdmissionStatus.Reserved;
        public FinalizationStatus NextFinalization = FinalizationStatus.Settled;
        public Exception? ReserveFailure, FinalizationFailure;
        public Task<AdmissionResult> ReserveAsync(ManagedAdmissionInput input,
            CancellationToken cancellationToken = default)
        {
            Reserves++;
            ManagedReserves++;
            LastMaximum = input.MaximumCharge;
            if (ReserveFailure is not null) throw ReserveFailure;
            var reservation = NextAdmission == AdmissionStatus.Reserved
                ? new Reservation(Guid.NewGuid(), requestId, organizationId, projectId, apiKeyId,
                    feeId, input.MaximumCharge, DateTimeOffset.UtcNow, input.ExpiresAt, "Reserved") : null;
            return Task.FromResult(new AdmissionResult(NextAdmission, requestId, reservation));
        }
        public Task<AdmissionResult> ReserveByokAsync(ByokAdmissionInput input,
            CancellationToken cancellationToken = default)
        {
            Reserves++;
            ByokReserves++;
            LastMaximum = input.MaximumWalletCharge;
            LastByokInput = input;
            if (ReserveFailure is not null) throw ReserveFailure;
            var reservation = NextAdmission == AdmissionStatus.Reserved
                ? new Reservation(Guid.NewGuid(), requestId, organizationId, projectId, apiKeyId,
                    input.FeePolicyVersionId, input.MaximumWalletCharge, DateTimeOffset.UtcNow,
                    input.ExpiresAt, "Reserved", input.CredentialId, input.ByokFeePolicyVersionId,
                    input.MaximumExternalSpend, input.AllowManagedFallback) : null;
            return Task.FromResult(new AdmissionResult(NextAdmission, requestId, reservation));
        }
        public Task<FinalizationResult> FinalizeAsync(Guid reservationId, CancellationToken cancellationToken = default)
        {
            Finalizes++;
            if (FinalizationFailure is not null) throw FinalizationFailure;
            return Task.FromResult(new FinalizationResult(NextFinalization, null));
        }
        public Task<FinalizationResult> ReleaseUndispatchedAsync(Guid reservationId,
            CancellationToken cancellationToken = default)
        { Releases++; return Task.FromResult(new FinalizationResult(FinalizationStatus.Released, null)); }
        public Task<FinalizationResult> ReconcileAsync(Guid reservationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId, UsdMicroAmount limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
            UZLLM.Modules.Billing.Domain.BudgetPeriod period, UsdMicroAmount limit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BudgetPolicy>> ListBudgetsAsync(Guid organizationId, Guid projectId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId, UsdMicroAmount amount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FinancialWalletState?> GetWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RecordLateExposureAsync(Guid evidenceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUsage(Guid requestId) : IUsageService
    {
        public int Attempts, VerifiedEvidence, UnknownEvidence;
        public DeliveryState? Delivery;
        public string? RouteStrategy;
        public Guid? VerifiedPriceId;
        public ExecutionState? AttemptFinalState;
        public ClaimResult? ExistingClaim;
        public Task<UsageAttempt> StartAttemptAsync(Guid id, Guid providerModelId,
            CancellationToken cancellationToken = default)
        { Attempts++; return Task.FromResult(new UsageAttempt(Guid.NewGuid(), requestId, Attempts, providerModelId,
            DateTimeOffset.UtcNow, null, ExecutionState.Prepared, null, null)); }
        public Task<bool> MarkDispatchedAsync(Guid attemptId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> FinishAttemptAsync(Guid attemptId, ExecutionState next, string? providerRequestId,
            string? errorCategory, CancellationToken cancellationToken = default)
        { AttemptFinalState = next; return Task.FromResult(true); }
        public Task<bool> FinishRequestAsync(Guid id, ExecutionState execution, DeliveryState delivery,
            int httpStatus, string routeStrategy, CancellationToken cancellationToken = default)
        { Delivery = delivery; RouteStrategy = routeStrategy; return Task.FromResult(true); }
        public Task<UsageEvidence?> RecordVerifiedAsync(VerifiedUsageInput input,
            CancellationToken cancellationToken = default)
        { VerifiedEvidence++; VerifiedPriceId = input.PriceVersionId;
            return Task.FromResult<UsageEvidence?>(null); }
        public Task<UsageEvidence?> RecordUnknownAsync(Guid id, Guid attemptId,
            CancellationToken cancellationToken = default)
        { UnknownEvidence++; return Task.FromResult<UsageEvidence?>(null); }
        public Task<ClaimResult> PrepareAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ClaimResult?> TryPrepareInTransactionAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ClaimResult> ResolveDuplicateAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ClaimResult?> FindExistingClaimAsync(Guid organizationId, Guid apiKeyId,
            string operation, string idempotencyKey, byte[] payloadHash,
            CancellationToken cancellationToken = default) => Task.FromResult(ExistingClaim);
        public Task<UsageRequest?> FindRequestAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UsageEvidence>> ListEvidenceAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeAdapter(string providerCode) : ILlmProviderAdapter
    {
        public string ProviderCode => providerCode;
        public bool Eligible = true;
        public bool Supports(ProviderChatRequest request, bool stream) => Eligible;
        public int CompleteCalls, StreamCalls;
        public ProviderCompletion? Completion;
        public ProviderError? Error;
        public ProviderError? ByokError;
        public ProviderExecutionContext? LastContext;
        public int? LastMaxOutputTokens;
        public IReadOnlyList<ProviderStreamEvent> StreamItems = [];
        public bool FailAfterStreamItems;
        public CancellationToken ObservedStreamToken;
        public bool StreamDisposed;
        public Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
            ProviderExecutionContext context, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            LastContext = context;
            LastMaxOutputTokens = request.MaxOutputTokens;
            if (Error is not null) throw new ProviderExecutionException(Error);
            if (context.OrganizationId is not null && ByokError is not null)
                throw new ProviderExecutionException(ByokError);
            return Task.FromResult(Completion ?? new ProviderCompletion("u", "gpt-test",
                new ProviderMessage("assistant", [new ProviderContentPart(ProviderContentKind.Text, "ok")]),
                "stop", new ProviderUsage(2, 1, 0, null), "p"));
        }
        public async IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
            ProviderExecutionContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            ObservedStreamToken = cancellationToken;
            try
            {
                if (Error is not null) throw new ProviderExecutionException(Error);
                foreach (var item in StreamItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item;
                    await Task.Yield();
                }
                if (FailAfterStreamItems)
                    throw new ProviderExecutionException(new ProviderError(ProviderErrorCategory.Unknown,
                        ProviderExecutionCertainty.Unknown, false, false, null, null,
                        "Provider outcome is unknown."));
            }
            finally { StreamDisposed = true; }
        }
    }

    private sealed class FakeWriterFactory(FakeWriter writer) : ICompletionWriterFactory
    {
        public ICompletionWriter Create(HttpContext context) => writer;
    }

    private sealed class FakeAdapterSelector(FakeAdapter openAi, FakeAdapter anthropic,
        FakeAdapter google, FakeAdapter deepSeek)
        : IProviderAdapterSelector
    {
        public ILlmProviderAdapter Get(string providerCode) => providerCode switch
        {
            "anthropic" => anthropic,
            "google" => google,
            "deepseek" => deepSeek,
            _ => openAi
        };
    }

    private sealed class FakeWriter : ICompletionWriter
    {
        public CancellationTokenSource AbortSource = null!;
        public bool Started { get; private set; }
        public bool Completed, StreamFinished, DisconnectOnEvent;
        public string? Model;
        public int Errors;
        public Task WriteCompletionAsync(ProviderCompletion completion, string model, Guid requestId,
            CancellationToken cancellationToken)
        { Completed = true; Started = true; Model = model; return Task.CompletedTask; }
        public Task WriteStreamEventAsync(ProviderStreamEvent item, string model, Guid requestId,
            CancellationToken cancellationToken)
        {
            Started = true;
            Model = model;
            if (DisconnectOnEvent)
            {
                AbortSource.Cancel();
                throw new OperationCanceledException(AbortSource.Token);
            }
            return Task.CompletedTask;
        }
        public Task FinishStreamAsync(CancellationToken cancellationToken)
        { StreamFinished = true; Started = true; return Task.CompletedTask; }
        public Task WriteStreamErrorAsync(string message, string code, CancellationToken cancellationToken)
        { Errors++; return Task.CompletedTask; }
    }

    private sealed class FakePlatformControls : IPlatformControlStore
    {
        public bool Enabled = true;
        public Task<bool> IsEnabledAsync(PlatformFeature feature, CancellationToken cancellationToken = default) =>
            Task.FromResult(Enabled);
        public Task<IReadOnlyList<PlatformControl>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformControl>>([]);
        public Task<bool> SetEnabledAsync(PlatformFeature feature, bool enabled, DateTimeOffset now,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
