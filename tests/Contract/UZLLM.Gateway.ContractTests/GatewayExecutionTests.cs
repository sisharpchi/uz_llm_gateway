using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed class GatewayExecutionTests
{
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

    private sealed class Scenario
    {
        public readonly Guid RequestId = Guid.NewGuid();
        private readonly Guid organizationId = Guid.NewGuid();
        private readonly Guid projectId = Guid.NewGuid();
        private readonly Guid apiKeyId = Guid.NewGuid();
        private readonly Guid providerId = Guid.NewGuid();
        private readonly Guid mappingId = Guid.NewGuid();
        private readonly Guid anthropicMappingId = Guid.NewGuid();
        private readonly Guid credentialId = Guid.NewGuid();
        private readonly Guid feeId = Guid.NewGuid();
        private readonly Guid priceId = Guid.NewGuid();
        public readonly Guid AnthropicPriceId = Guid.NewGuid();
        public readonly DefaultHttpContext Context = new();
        public readonly FakeLimiter Limiter = new();
        public readonly FakeFinance Finance;
        public readonly FakeUsage Usage;
        public readonly FakeAdapter Adapter = new("openai");
        public readonly FakeAdapter AnthropicAdapter = new("anthropic");
        public readonly FakeHealth Health;
        public readonly FakeWriter Writer = new();
        private readonly CancellationTokenSource abort = new();
        public readonly FakeReadStore Reads;
        private readonly InferenceGateway gateway;

        public Scenario(bool stream = false, bool twoProviders = false,
            string modelCode = "gpt-test", int outputTokens = 100)
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
                CatalogStatus.Active, [], now);
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
                    now.AddDays(-1), null, 2000, 4000, null, "{}", now);
                mappings.Add(new CatalogProviderModelSummary(anthropicMapping,
                    new CatalogProvider(anthropicId, "anthropic", "Anthropic", CatalogStatus.Active, now),
                    anthropicPrice));
                prices[anthropicMappingId] = anthropicPrice;
            }
            var catalog = new FakeCatalog(new CatalogModelSummary(model, mappings), prices);
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
                Usage, new FakeAdapterSelector(Adapter, AnthropicAdapter), Health,
                new FakeWriterFactory(Writer),
                new GatewayOptions("default", 1_048_576, TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(15), new LimitPolicy(60, 600, 8, 64, TimeSpan.FromMinutes(15))),
                TimeProvider.System, NullLogger<InferenceGateway>.Instance);
        }

        public Task RunAsync() => gateway.ChatAsync(Context, Context.RequestAborted);
        public Task ListAsync() => gateway.ListModelsAsync(Context, Context.RequestAborted);
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
        public Task<GatewayTenantScope?> FindTenantAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult<GatewayTenantScope?>(Active && projectId == tenant.ProjectId ? tenant : null);
        public Task<FeePolicyVersion?> FindFeePolicyAsync(string policyCode, DateTimeOffset at,
            CancellationToken cancellationToken) => Task.FromResult<FeePolicyVersion?>(fee);
        public Task<Guid?> FindPlatformCredentialAsync(Guid providerId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(credentialId);
    }

    private sealed class FakeCatalog(CatalogModelSummary summary,
        IReadOnlyDictionary<Guid, ModelPrice> prices) : ICatalogService
    {
        public Task<IReadOnlyList<CatalogModelSummary>> ListActiveModelsAsync(DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogModelSummary>>([summary]);
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
        public bool OpenPrimary, Unavailable;
        public int Failures;
        public Task<ProviderHealthState> CheckAsync(Guid providerModelId,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable
                ? ProviderHealthState.DependencyUnavailable
                : OpenPrimary && providerModelId == primaryMappingId ? ProviderHealthState.Open
                    : ProviderHealthState.Healthy);
        public Task RecordSuccessAsync(Guid providerModelId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordTransientFailureAsync(Guid providerModelId,
            CancellationToken cancellationToken = default)
        { Failures++; return Task.CompletedTask; }
    }

    private sealed class FakeFinance(Guid requestId, Guid organizationId, Guid projectId,
        Guid apiKeyId, Guid feeId) : IFinancialService
    {
        public int Reserves, Finalizes, Releases;
        public UsdMicroAmount? LastMaximum;
        public AdmissionStatus NextAdmission = AdmissionStatus.Reserved;
        public FinalizationStatus NextFinalization = FinalizationStatus.Settled;
        public Exception? ReserveFailure, FinalizationFailure;
        public Task<AdmissionResult> ReserveAsync(ManagedAdmissionInput input,
            CancellationToken cancellationToken = default)
        {
            Reserves++;
            LastMaximum = input.MaximumCharge;
            if (ReserveFailure is not null) throw ReserveFailure;
            var reservation = NextAdmission == AdmissionStatus.Reserved
                ? new Reservation(Guid.NewGuid(), requestId, organizationId, projectId, apiKeyId,
                    feeId, input.MaximumCharge, DateTimeOffset.UtcNow, input.ExpiresAt, "Reserved") : null;
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
        public int CompleteCalls, StreamCalls;
        public ProviderCompletion? Completion;
        public ProviderError? Error;
        public IReadOnlyList<ProviderStreamEvent> StreamItems = [];
        public bool FailAfterStreamItems;
        public CancellationToken ObservedStreamToken;
        public bool StreamDisposed;
        public Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
            ProviderExecutionContext context, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            if (Error is not null) throw new ProviderExecutionException(Error);
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

    private sealed class FakeAdapterSelector(FakeAdapter openAi, FakeAdapter anthropic)
        : IProviderAdapterSelector
    {
        public ILlmProviderAdapter Get(string providerCode) => providerCode == "anthropic"
            ? anthropic : openAi;
    }

    private sealed class FakeWriter : ICompletionWriter
    {
        public CancellationTokenSource AbortSource = null!;
        public bool Started { get; private set; }
        public bool Completed, StreamFinished, DisconnectOnEvent;
        public int Errors;
        public Task WriteCompletionAsync(ProviderCompletion completion, string model, Guid requestId,
            CancellationToken cancellationToken)
        { Completed = true; Started = true; return Task.CompletedTask; }
        public Task WriteStreamEventAsync(ProviderStreamEvent item, string model, Guid requestId,
            CancellationToken cancellationToken)
        {
            Started = true;
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
}
