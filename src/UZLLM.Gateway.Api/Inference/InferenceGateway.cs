using System.Security.Cryptography;
using System.Text.Json;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Routing.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public interface IInferenceGateway
{
    Task ListModelsAsync(HttpContext context, CancellationToken cancellationToken);
    Task ChatAsync(HttpContext context, CancellationToken cancellationToken);
}

public sealed class InferenceGateway(
    IApiKeyAuthenticator authenticator, IGatewayReadStore readStore,
    ICatalogService catalog, IDistributedAdmissionLimiter limiter,
    IProviderQuotaProtection quota, IRequestConstraintValidator constraints,
    IFinancialService finance, IUsageService usage,
    IProviderAdapterSelector adapters, IProviderHealthService health,
    ICompletionWriterFactory writerFactory,
    GatewayOptions options, TimeProvider clock, ILogger<InferenceGateway> logger) : IInferenceGateway
{
    public async Task ListModelsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        SetGatewayRequestId(context, Guid.CreateVersion7());
        try
        {
            var identity = await AuthenticateAsync(context, cancellationToken);
            if (identity is null)
            {
                await WriteErrorAsync(context, new(401, "invalid_api_key", "authentication_error",
                    "Invalid API key."), cancellationToken);
                return;
            }
            if (await readStore.FindTenantAsync(identity.ProjectId, cancellationToken) is null)
            {
                await WriteErrorAsync(context, new(403, "project_unavailable", "permission_error",
                    "Project is unavailable."), cancellationToken);
                return;
            }
            var models = await catalog.ListActiveModelsAsync(clock.GetUtcNow(), cancellationToken);
            await context.Response.WriteAsJsonAsync(new
            {
                @object = "list",
                data = models.Where(model => model.ProviderMappings.Any(mapping => SupportedProvider(mapping.Provider.Code)))
                    .Select(model => new
                    {
                        id = model.Model.CanonicalCode, @object = "model", owned_by = "uzllm",
                        name = model.Model.DisplayName,
                        context_length = model.Model.ContextLength,
                        max_output_tokens = model.Model.MaxOutputTokens,
                        capabilities = model.Model.Capabilities.Select(capability => capability.ToString()).ToArray(),
                        pricing = new
                        {
                            input_micro_usd_per_million = model.ProviderMappings
                                .Where(mapping => SupportedProvider(mapping.Provider.Code))
                                .Max(mapping => mapping.Price.InputPriceMicroUsdPerMillion),
                            output_micro_usd_per_million = model.ProviderMappings
                                .Where(mapping => SupportedProvider(mapping.Provider.Code))
                                .Max(mapping => mapping.Price.OutputPriceMicroUsdPerMillion),
                            cached_input_micro_usd_per_million = model.ProviderMappings
                                .Where(mapping => SupportedProvider(mapping.Provider.Code))
                                .Max(mapping => mapping.Price.CachedInputPriceMicroUsdPerMillion)
                        }
                    }).ToArray()
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Model listing failed");
            await WriteErrorAsync(context, new(503, "gateway_dependency_unavailable", "server_error",
                "Gateway dependency is unavailable."), CancellationToken.None);
        }
    }

    public async Task ChatAsync(HttpContext context, CancellationToken cancellationToken)
    {
        SetGatewayRequestId(context, Guid.CreateVersion7());
        LimitLease? lease = null;
        try
        {
            var identity = await AuthenticateAsync(context, cancellationToken);
            if (identity is null)
            {
                await WriteErrorAsync(context, new(401, "invalid_api_key", "authentication_error",
                    "Invalid API key."), cancellationToken);
                return;
            }
            var tenant = await readStore.FindTenantAsync(identity.ProjectId, cancellationToken);
            if (tenant is null)
            {
                await WriteErrorAsync(context, new(403, "project_unavailable", "permission_error",
                    "Project is unavailable."), cancellationToken);
                return;
            }
            if (!context.Request.HasJsonContentType())
                throw new GatewayRequestException("Content-Type must be application/json.", "unsupported_media_type");
            var raw = await ReadRequestBodyAsync(context.Request, options.MaximumRequestBytes, cancellationToken);
            var parsed = ChatRequestParser.Parse(raw);
            var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
            if (idempotencyKey.Length == 0) idempotencyKey = null;
            if (idempotencyKey is not null
                && (!Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty))
                throw new GatewayRequestException("Idempotency-Key must be a non-empty UUID.");
            var payloadHash = SHA256.HashData(raw);
            if (idempotencyKey is not null)
            {
                var existing = await usage.FindExistingClaimAsync(tenant.OrganizationId,
                    identity.ApiKeyId, "chat.completions", idempotencyKey, payloadHash,
                    cancellationToken);
                if (existing is not null)
                {
                    await WriteErrorAsync(context, new(409, "idempotency_conflict", "conflict_error",
                        "Idempotency key was already used.", existing.RequestId), cancellationToken);
                    return;
                }
            }

            var provisionalRequestId = Guid.CreateVersion7();
            var limit = await limiter.TryAcquireAsync(new LimitScope(tenant.OrganizationId,
                identity.ProjectId, identity.ApiKeyId), options.Limits, provisionalRequestId, cancellationToken);
            if (limit.Outcome != LimitOutcome.Admitted || limit.Lease is null)
            {
                var error = limit.Outcome switch
                {
                    LimitOutcome.RateLimited or LimitOutcome.ConcurrencyLimited =>
                        new GatewayError(429, "rate_limit_exceeded", "rate_limit_error", "Request limit exceeded."),
                    _ => new GatewayError(503, "admission_unavailable", "server_error",
                        "Admission service is unavailable.")
                };
                if (limit.RetryAfter is { } retry)
                    context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString();
                await WriteErrorAsync(context, error, cancellationToken);
                return;
            }
            lease = limit.Lease;

            var models = await catalog.ListActiveModelsAsync(clock.GetUtcNow(), cancellationToken);
            var separator = parsed.Model.IndexOf('/');
            var pinnedProvider = separator >= 0 ? parsed.Model[..separator] : null;
            var requestedCode = separator >= 0 ? parsed.Model[(separator + 1)..] : parsed.Model;
            var model = models.SingleOrDefault(value => value.Model.CanonicalCode == requestedCode);
            if (model is null || pinnedProvider is not null && !SupportedProvider(pinnedProvider))
            {
                await WriteErrorAsync(context, new(404, "model_not_found", "invalid_request_error",
                    "Model is unavailable."), cancellationToken);
                return;
            }
            var outputLimit = parsed.ProviderRequest.MaxOutputTokens ?? model.Model.MaxOutputTokens;
            var fee = await readStore.FindFeePolicyAsync(options.FeePolicyCode, clock.GetUtcNow(), cancellationToken);
            if (fee is null)
            {
                await WriteErrorAsync(context, new(503, "pricing_unavailable", "server_error",
                    "Pricing is unavailable."), cancellationToken);
                return;
            }
            var candidates = new List<RouteCandidate>();
            var constraintAllowed = false;
            RequestConstraintOutcome? constraintFailure = null;
            var mappings = model.ProviderMappings
                .Where(value => SupportedProvider(value.Provider.Code)
                    && (pinnedProvider is null || value.Provider.Code == pinnedProvider))
                .OrderBy(value => value.Provider.Code == "openai" ? 0 : 1)
                .ThenBy(value => value.Mapping.Id);
            foreach (var mapping in mappings)
            {
                if (candidates.Count == 2) break;
                var supported = model.Model.Capabilities.Concat(mapping.Mapping.CapabilityOverrides)
                    .Select(value => value.ToString()).ToArray();
                var allowed = constraints.Validate(new RequestConstraintInput(model.Model.ContextLength,
                    model.Model.MaxOutputTokens, outputLimit, parsed.EstimatedInputTokens,
                    parsed.RequiredCapabilities, supported));
                if (allowed.Outcome != RequestConstraintOutcome.Allowed)
                {
                    constraintFailure ??= allowed.Outcome;
                    continue;
                }
                constraintAllowed = true;
                var credentialId = await readStore.FindPlatformCredentialAsync(mapping.Provider.Id, cancellationToken);
                if (credentialId is null) continue;
                var quotaDecision = await quota.CheckAsync(credentialId.Value, cancellationToken);
                if (quotaDecision.Outcome == ProviderQuotaOutcome.DependencyUnavailable)
                {
                    await WriteErrorAsync(context, new(503, "provider_health_unavailable", "server_error",
                        "Provider eligibility is unavailable."), cancellationToken);
                    return;
                }
                if (quotaDecision.Outcome != ProviderQuotaOutcome.Available) continue;
                var healthState = await health.CheckAsync(mapping.Mapping.Id, cancellationToken);
                if (healthState == ProviderHealthState.DependencyUnavailable)
                {
                    await WriteErrorAsync(context, new(503, "provider_health_unavailable", "server_error",
                        "Provider eligibility is unavailable."), cancellationToken);
                    return;
                }
                if (healthState == ProviderHealthState.Open) continue;
                try
                {
                    var maximumCharge = GatewayCostEstimator.MaximumCharge(model.Model, mapping.Price,
                        fee, outputLimit);
                    candidates.Add(new RouteCandidate(mapping, credentialId.Value, maximumCharge));
                }
                catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
                { logger.LogError(exception, "Unsupported price dimensions for mapping {MappingId}", mapping.Mapping.Id); }
            }
            if (candidates.Count == 0)
            {
                var constraintError = !constraintAllowed && constraintFailure is not null;
                await WriteErrorAsync(context, constraintError
                    ? new GatewayError(400, constraintFailure == RequestConstraintOutcome.ContextExceeded
                        ? "context_length_exceeded" : "unsupported_model_capability",
                        "invalid_request_error", "Request exceeds model limits or capabilities.")
                    : new GatewayError(503, "provider_unavailable", "server_error",
                        "No eligible provider is available."), cancellationToken);
                return;
            }
            var maximum = new UsdMicroAmount(candidates.Max(value => value.MaximumCharge.Value));
            var prepare = new PrepareUsageRequest(tenant.OrganizationId, identity.ProjectId,
                identity.ApiKeyId, model.Model.Id, parsed.Stream, "chat.completions",
                idempotencyKey, payloadHash, System.Diagnostics.Activity.Current?.TraceId.ToString());
            var admission = await finance.ReserveAsync(new ManagedAdmissionInput(prepare,
                maximum, fee.Id, clock.GetUtcNow().Add(options.ReservationLifetime)), cancellationToken);
            if (admission.Status != AdmissionStatus.Reserved || admission.Reservation is null
                || admission.RequestId is null)
            {
                await WriteErrorAsync(context, AdmissionError(admission), cancellationToken);
                return;
            }
            SetGatewayRequestId(context, admission.RequestId.Value);
            context.Response.Headers["X-Uzllm-Model"] = model.Model.CanonicalCode;
            await ExecuteReservedAsync(context, parsed, model.Model, candidates,
                pinnedProvider is not null, outputLimit,
                admission.Reservation, cancellationToken);
        }
        catch (GatewayRequestException exception)
        {
            await WriteErrorAsync(context, new(exception.Code switch
                {
                    "request_too_large" => 413,
                    "unsupported_media_type" => 415,
                    _ => 400
                },
                exception.Code, "invalid_request_error",
                exception.Message), CancellationToken.None);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Gateway request failed");
            await WriteErrorAsync(context, new(503, "gateway_dependency_unavailable", "server_error",
                "Gateway dependency is unavailable."), CancellationToken.None);
        }
        finally
        {
            if (lease is not null)
            {
                try { await limiter.ReleaseAsync(lease, CancellationToken.None); }
                catch (Exception exception) { logger.LogWarning(exception, "Admission lease release failed"); }
            }
        }
    }

    private async Task ExecuteReservedAsync(HttpContext context, ParsedChatRequest parsed,
        CanonicalModel model, IReadOnlyList<RouteCandidate> candidates, bool pinned,
        int outputLimit,
        Reservation reservation, CancellationToken cancellationToken)
    {
        var writer = writerFactory.Create(context);
        var mapping = candidates[0].Mapping;
        var credentialId = candidates[0].CredentialId;
        UsageAttempt? attempt = null;
        ProviderUsage? providerUsage = null;
        string? providerRequestId = null;
        ProviderError? providerError = null;
        ProviderCompletion? completion = null;
        var dispatched = false;
        var completed = false;
        var disconnected = false;
        var httpStatus = 200;
        var execution = ExecutionState.Succeeded;
        var fallbackCount = 0;
        var executionStartedAt = clock.GetUtcNow();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.ProviderTimeout);
        try
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                mapping = candidate.Mapping;
                credentialId = candidate.CredentialId;
                attempt = null;
                providerUsage = null;
                providerRequestId = null;
                providerError = null;
                completion = null;
                dispatched = completed = false;
                if (index > 0)
                {
                    var currentHealth = await health.CheckAsync(mapping.Mapping.Id, cancellationToken);
                    var currentQuota = await quota.CheckAsync(credentialId, cancellationToken);
                    if (currentHealth != ProviderHealthState.Healthy
                        || currentQuota.Outcome != ProviderQuotaOutcome.Available)
                    {
                        providerError = new ProviderError(ProviderErrorCategory.Capacity,
                            ProviderExecutionCertainty.NotDispatched, false, false, null, null,
                            "Fallback provider is unavailable.");
                        break;
                    }
                }
                var remaining = options.ProviderTimeout - (clock.GetUtcNow() - executionStartedAt);
                if (remaining <= TimeSpan.Zero || deadline.IsCancellationRequested)
                    throw new ProviderExecutionException(new ProviderError(ProviderErrorCategory.Timeout,
                        ProviderExecutionCertainty.NotDispatched, false, false, null, null,
                        "Provider attempt deadline expired."));
                attempt = await usage.StartAttemptAsync(reservation.RequestId, mapping.Mapping.Id, cancellationToken);
                var effectivePrice = await catalog.FindEffectivePriceAsync(mapping.Mapping.Id,
                    attempt.StartedAt, cancellationToken);
                if (effectivePrice?.Id != mapping.Price.Id)
                    throw new GatewayRequestException("Model pricing changed; retry the request.", "pricing_changed");
                var adapter = adapters.Get(mapping.Provider.Code);
                dispatched = await usage.MarkDispatchedAsync(attempt.Id, cancellationToken);
                if (!dispatched) throw new InvalidOperationException("Attempt dispatch could not be persisted.");
                context.Response.Headers["X-Uzllm-Provider"] = mapping.Provider.Code;
                var providerContext = new ProviderExecutionContext(reservation.RequestId,
                    mapping.Provider.Id, mapping.Mapping.Id, credentialId,
                    mapping.Mapping.UpstreamModelCode, remaining);
                var adapterRequest = parsed.ProviderRequest with { MaxOutputTokens = outputLimit };
                try
                {
                    if (parsed.Stream)
                    {
                        await foreach (var item in adapter.StreamAsync(adapterRequest,
                            providerContext, deadline.Token))
                        {
                            providerRequestId ??= item.ProviderRequestId;
                            if (item.Kind == ProviderStreamKind.Usage) providerUsage = item.Usage;
                            if (item.Kind == ProviderStreamKind.Error)
                            {
                                providerError = item.Error ?? new ProviderError(ProviderErrorCategory.Unknown,
                                    ProviderExecutionCertainty.Unknown, false, false, null,
                                    providerRequestId, "Provider stream failed.");
                                providerError = providerError with
                                {
                                    Certainty = ProviderExecutionCertainty.Unknown,
                                    Retryable = false,
                                    FallbackEligible = false
                                };
                                break;
                            }
                            await writer.WriteStreamEventAsync(item, model.CanonicalCode,
                                reservation.RequestId, deadline.Token);
                        }
                        completed = providerError is null;
                    }
                    else
                    {
                        completion = await adapter.CompleteAsync(adapterRequest, providerContext,
                            deadline.Token);
                        providerUsage = completion.Usage;
                        providerRequestId = completion.ProviderRequestId;
                        completed = true;
                    }
                }
                catch (ProviderExecutionException exception)
                {
                    providerError = exception.Error;
                    providerRequestId = exception.Error.ProviderRequestId;
                }
                await RecordHealthAsync(mapping.Mapping.Id, providerError, completed);
                if (!pinned && providerError is { FallbackEligible: true,
                        Certainty: ProviderExecutionCertainty.RejectedBeforeExecution }
                    && index + 1 < candidates.Count && !writer.Started && !deadline.IsCancellationRequested)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    if (!await usage.FinishAttemptAsync(attempt.Id,
                        ExecutionState.RejectedBeforeExecution, providerRequestId,
                        providerError.Category.ToString(), cleanup.Token))
                        throw new InvalidOperationException("Rejected attempt could not be finalized.");
                    fallbackCount++;
                    continue;
                }
                break;
            }
        }
        catch (ProviderExecutionException exception)
        {
            providerError = exception.Error;
            providerRequestId = exception.Error.ProviderRequestId;
        }
        catch (GatewayRequestException exception)
        {
            providerError = new ProviderError(ProviderErrorCategory.InvalidRequest,
                ProviderExecutionCertainty.NotDispatched, false, false, null, null, exception.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            disconnected = true;
            execution = ExecutionState.Canceled;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Provider execution or delivery failed for {RequestId}", reservation.RequestId);
            providerError = new ProviderError(ProviderErrorCategory.Unknown,
                ProviderExecutionCertainty.Unknown, false, false, null, providerRequestId,
                "Provider outcome is unknown.");
        }

        if (providerError is not null)
        {
            if (writer.Started && providerError.Certainty != ProviderExecutionCertainty.Unknown)
                providerError = providerError with
                {
                    Certainty = ProviderExecutionCertainty.Unknown,
                    Retryable = false,
                    FallbackEligible = false,
                    SafeMessage = "Provider outcome is unknown after partial delivery."
                };
            if (!dispatched)
                providerError = providerError with
                {
                    Certainty = ProviderExecutionCertainty.NotDispatched,
                    SafeMessage = "Gateway request was not dispatched."
                };
            execution = providerError.Certainty == ProviderExecutionCertainty.Unknown
                ? ExecutionState.OutcomeUnknown : ExecutionState.Failed;
            httpStatus = !dispatched ? 503
                : providerError.Category == ProviderErrorCategory.ContextExceeded ? 400 : 502;
            if (providerError.Category == ProviderErrorCategory.QuotaExhausted)
                try { await quota.MarkExhaustedAsync(credentialId, TimeSpan.FromMinutes(1), CancellationToken.None); }
                catch (Exception exception) { logger.LogWarning(exception, "Provider quota cooldown failed"); }
        }
        else if (disconnected)
            httpStatus = 499;

        var cleanupSucceeded = false;
        using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try
            {
                if (attempt is null || !dispatched || providerError?.Certainty is
                    ProviderExecutionCertainty.NotDispatched or ProviderExecutionCertainty.RejectedBeforeExecution)
                {
                    if (attempt is not null && dispatched)
                        await usage.FinishAttemptAsync(attempt.Id, ExecutionState.RejectedBeforeExecution,
                            providerRequestId, providerError?.Category.ToString(), cleanup.Token);
                    var released = await finance.ReleaseUndispatchedAsync(reservation.Id, cleanup.Token);
                    if (released.Status is not (FinalizationStatus.Released or FinalizationStatus.AlreadyFinalized))
                        throw new InvalidOperationException("Undispatched reservation remains pending.");
                }
                else
                {
                    if (providerUsage is not null)
                        await usage.RecordVerifiedAsync(new VerifiedUsageInput(reservation.RequestId,
                            attempt.Id, EvidenceSource.Provider, providerUsage.InputTokens,
                            providerUsage.OutputTokens, providerUsage.CachedInputTokens,
                            providerUsage.ReasoningTokens, mapping.Price.Id, providerRequestId), cleanup.Token);
                    else
                        await usage.RecordUnknownAsync(reservation.RequestId, attempt.Id, cleanup.Token);
                    await usage.FinishAttemptAsync(attempt.Id,
                        providerError is null && completed ? ExecutionState.Succeeded
                            : providerUsage is null ? ExecutionState.OutcomeUnknown : ExecutionState.Failed,
                        providerRequestId, providerError?.Category.ToString(), cleanup.Token);
                    var finalized = await finance.FinalizeAsync(reservation.Id, cleanup.Token);
                    if (finalized.Status is not (FinalizationStatus.Settled or FinalizationStatus.Released
                        or FinalizationStatus.AlreadyFinalized or FinalizationStatus.PendingEvidence))
                        throw new InvalidOperationException("Reservation finalization did not complete.");
                }
                cleanupSucceeded = true;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Financial finalization failed for {RequestId}; reconciliation remains scheduled",
                    reservation.RequestId);
            }
        }

        var delivered = false;
        try
        {
            if (disconnected) return;
            if (!cleanupSucceeded)
            {
                if (writer.Started)
                    await writer.WriteStreamErrorAsync("Finalization is pending reconciliation.",
                        "finalization_pending", CancellationToken.None);
                else await WriteErrorAsync(context, new(503, "finalization_pending", "server_error",
                    "Finalization is pending reconciliation."), CancellationToken.None);
                return;
            }
            if (providerError is not null)
            {
                if (writer.Started)
                    await writer.WriteStreamErrorAsync(providerError.SafeMessage, "provider_error", CancellationToken.None);
                else await WriteErrorAsync(context, new(httpStatus, "provider_error", "upstream_error",
                    providerError.SafeMessage), CancellationToken.None);
                return;
            }
            if (parsed.Stream)
                await writer.FinishStreamAsync(cancellationToken);
            else if (completion is not null)
                await writer.WriteCompletionAsync(completion, parsed.Model, reservation.RequestId, cancellationToken);
            delivered = true;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            disconnected = true;
            execution = ExecutionState.Canceled;
            httpStatus = 499;
        }
        finally
        {
            using var activityToken = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await usage.FinishRequestAsync(reservation.RequestId, execution,
                    disconnected ? DeliveryState.ClientDisconnected
                        : delivered ? DeliveryState.Completed
                        : writer.Started ? DeliveryState.Partial : DeliveryState.NotStarted,
                    cleanupSucceeded ? httpStatus : 503,
                    fallbackCount == 0 ? $"deterministic:{mapping.Provider.Code}"
                        : $"deterministic:failover:{mapping.Provider.Code}", activityToken.Token);
            }
            catch (Exception exception)
            { logger.LogError(exception, "Request activity finalization failed for {RequestId}", reservation.RequestId); }
        }
    }

    private async Task RecordHealthAsync(Guid mappingId, ProviderError? error, bool completed)
    {
        try
        {
            if (completed && error is null)
                await health.RecordSuccessAsync(mappingId, CancellationToken.None);
            else if (error?.Category is ProviderErrorCategory.RateLimited or ProviderErrorCategory.Capacity
                or ProviderErrorCategory.Timeout or ProviderErrorCategory.Upstream5xx)
                await health.RecordTransientFailureAsync(mappingId, CancellationToken.None);
        }
        catch (Exception exception)
        { logger.LogWarning(exception, "Provider health update failed for mapping {MappingId}", mappingId); }
    }

    private static bool SupportedProvider(string code) => code is "openai" or "anthropic";

    private sealed record RouteCandidate(CatalogProviderModelSummary Mapping, Guid CredentialId,
        UsdMicroAmount MaximumCharge);

    private Task<GatewayApiKeyAuthentication?> AuthenticateAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        return authenticator.AuthenticateAsync(authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim() : null, cancellationToken);
    }

    private static GatewayError AdmissionError(AdmissionResult admission) => admission.Status switch
    {
        AdmissionStatus.Duplicate or AdmissionStatus.PayloadConflict =>
            new(409, "idempotency_conflict", "conflict_error",
                "Idempotency key was already used.", admission.RequestId),
        AdmissionStatus.InsufficientWallet or AdmissionStatus.ProjectBudgetExceeded
            or AdmissionStatus.ApiKeyBudgetExceeded or AdmissionStatus.SpendingHeld =>
            new(402, "budget_exceeded", "billing_error", "Insufficient available budget."),
        AdmissionStatus.InvalidScope =>
            new(403, "scope_forbidden", "permission_error", "Project or API key is unavailable."),
        _ => new(503, "admission_unavailable", "server_error", "Financial admission is unavailable.")
    };

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, int maximum,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximum)
            throw new GatewayRequestException("Request body is too large.", "request_too_large");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await request.Body.ReadAsync(chunk, cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > maximum)
                throw new GatewayRequestException("Request body is too large.", "request_too_large");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static void SetGatewayRequestId(HttpContext context, Guid requestId) =>
        context.Response.Headers["X-Request-Id"] = requestId.ToString("N");

    private static Task WriteErrorAsync(HttpContext context, GatewayError error,
        CancellationToken cancellationToken) => GatewayErrorResponse.WriteAsync(context, error, cancellationToken);
}
