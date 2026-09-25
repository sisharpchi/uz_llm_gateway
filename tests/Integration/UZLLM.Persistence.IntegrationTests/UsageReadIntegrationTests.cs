using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class UsageReadIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = Start.AddDays(3);
    private static readonly Guid FirstRequestId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondRequestId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid UnknownRequestId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task Activity_cursor_preserves_tied_timestamps_and_filters_selected_provider()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);
        var query = Query();

        var first = await read.GetActivityAsync(seed.OrganizationId, query, limit: 1);
        Assert.Single(first.Items);
        Assert.Equal(UnknownRequestId, first.Items[0].RequestId);
        Assert.NotNull(first.NextCursor);
        var second = await read.GetActivityAsync(seed.OrganizationId, query, limit: 1, cursor: first.NextCursor);
        Assert.Equal(SecondRequestId, Assert.Single(second.Items).RequestId);
        var third = await read.GetActivityAsync(seed.OrganizationId, query, limit: 1, cursor: second.NextCursor);
        Assert.Equal(FirstRequestId, Assert.Single(third.Items).RequestId);
        Assert.Null(third.NextCursor);

        var byProvider = await read.GetActivityAsync(seed.OrganizationId,
            query with { ProviderId = seed.AnthropicProviderId });
        Assert.Equal(SecondRequestId, Assert.Single(byProvider.Items).RequestId);
        var byProjectAndStatus = await read.GetActivityAsync(seed.OrganizationId,
            query with { ProjectId = seed.ProjectId, Status = "failed" });
        Assert.Equal(SecondRequestId, Assert.Single(byProjectAndStatus.Items).RequestId);
        var byKeyAndModel = await read.GetActivityAsync(seed.OrganizationId,
            query with { ApiKeyId = seed.ApiKeyId, ModelId = seed.MainModelId });
        Assert.Equal(2, byKeyAndModel.Items.Count);
        var byStream = await read.GetActivityAsync(seed.OrganizationId, query with { IsStream = true });
        Assert.Equal(FirstRequestId, Assert.Single(byStream.Items).RequestId);
        var byRequestId = await read.GetActivityAsync(seed.OrganizationId,
            query with { RequestId = FirstRequestId });
        Assert.Equal(FirstRequestId, Assert.Single(byRequestId.Items).RequestId);
    }

    [Fact]
    public async Task Summary_daily_and_breakdowns_count_requests_once_and_charge_only_settlements()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);

        var summary = await read.GetSummaryAsync(seed.OrganizationId, Query());
        Assert.Equal(3, summary.RequestCount);
        Assert.Equal(3, summary.CompletedCount);
        Assert.Equal(2, summary.ErrorCount);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(120, summary.InputTokens);
        Assert.Equal(25, summary.OutputTokens);
        Assert.Equal("3500", summary.ChargedMicroUsd);
        Assert.Equal(66.67m, summary.ErrorRatePercent);
        Assert.Equal(2, summary.TopModels[0].RequestCount);
        Assert.Equal("OpenAI", summary.TopProviders[0].Name);

        var daily = await read.GetTimeSeriesAsync(seed.OrganizationId, Query());
        Assert.Equal(2, daily.Items.Count);
        Assert.Equal(new DateOnly(2026, 9, 20), daily.Items[0].Day);
        Assert.Equal(2, daily.Items[0].RequestCount);
        Assert.Equal("3500", daily.Items[0].ChargedMicroUsd);
        Assert.Equal(new DateOnly(2026, 9, 21), daily.Items[1].Day);
        Assert.Equal(0, daily.Items[1].InputTokens);
        Assert.Equal("0", daily.Items[1].ChargedMicroUsd);

        var providers = await read.GetBreakdownAsync(seed.OrganizationId, Query(), UsageBreakdownDimension.Provider);
        Assert.Equal(2, providers.Items.Count);
        Assert.Equal(2, providers.Items[0].RequestCount);
        Assert.Equal("3000", providers.Items[0].ChargedMicroUsd);
        var keys = await read.GetBreakdownAsync(seed.OrganizationId, Query(), UsageBreakdownDimension.ApiKey);
        Assert.Single(keys.Items);
        Assert.Equal(3, keys.Items[0].RequestCount);
        var projects = await read.GetBreakdownAsync(seed.OrganizationId, Query(), UsageBreakdownDimension.Project);
        Assert.Single(projects.Items);
        Assert.Equal(seed.ProjectId.ToString("D"), projects.Items[0].Id);
        var midnight = new DateTimeOffset(Start.Date, TimeSpan.Zero);
        var singleDay = await read.GetSummaryAsync(seed.OrganizationId,
            Query() with { From = midnight, To = midnight.AddDays(1) });
        Assert.Equal(2, singleDay.RequestCount);
        Assert.Equal(1, singleDay.ErrorCount);
    }

    [Fact]
    public async Task Request_detail_exposes_safe_attempt_evidence_and_authoritative_charge_only_within_tenant()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);
        var detail = await read.GetDetailAsync(seed.OrganizationId, FirstRequestId);

        Assert.NotNull(detail);
        Assert.Equal("trace-safe", detail.TraceId);
        Assert.Equal("priority", detail.RouteStrategy);
        Assert.Equal("3000", detail.ChargedMicroUsd);
        Assert.Equal("2000", detail.ProviderCostMicroUsd);
        Assert.Equal(2, detail.Request.AttemptCount);
        Assert.Equal(2, detail.Attempts.Count);
        Assert.Equal("RejectedBeforeExecution", detail.Attempts[0].ExecutionState);
        Assert.Equal("OpenAI", detail.Attempts[1].ProviderCode);
        Assert.Equal(120, detail.Request.DurationMs);
        Assert.Equal(100, Assert.Single(detail.Evidence).InputTokens);
        Assert.DoesNotContain("prompt", System.Text.Json.JsonSerializer.Serialize(detail), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await read.GetDetailAsync(seed.OrganizationId, seed.ForeignRequestId));

        var unknown = await read.GetDetailAsync(seed.OrganizationId, UnknownRequestId);
        Assert.NotNull(unknown);
        Assert.Null(unknown.ChargedMicroUsd);
        Assert.Null(unknown.Request.InputTokens);
        Assert.Equal("Unknown", Assert.Single(unknown.Evidence).State);
    }

    [Fact]
    public async Task Tenant_authorization_and_filters_never_expose_foreign_rows()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var authorization = new OrganizationAuthorizationService(new PostgreSqlOrganizationStore(db));
        await authorization.EnsureOwnerAsync(seed.OwnerAccountId, seed.OrganizationId);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsureOwnerAsync(seed.OwnerAccountId, seed.ForeignOrganizationId));

        var read = Read(scope.ServiceProvider);
        Assert.Empty((await read.GetActivityAsync(seed.OrganizationId,
            Query() with { ProjectId = seed.ForeignProjectId })).Items);
        Assert.Empty((await read.GetActivityAsync(seed.OrganizationId,
            Query() with { RequestId = seed.ForeignRequestId })).Items);
        var foreign = await read.GetSummaryAsync(seed.ForeignOrganizationId, Query());
        Assert.Equal(1, foreign.RequestCount);
        Assert.Equal("999000", foreign.ChargedMicroUsd);
        Assert.Equal(3, (await read.GetSummaryAsync(seed.OrganizationId, Query())).RequestCount);
    }

    [Fact]
    public async Task Invalid_windows_cursors_limits_and_statuses_are_rejected_before_querying()
    {
        var seed = await ResetAndSeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var read = Read(scope.ServiceProvider);
        await Assert.ThrowsAsync<ArgumentException>(() => read.GetSummaryAsync(seed.OrganizationId,
            Query() with { From = Start, To = Start }));
        await Assert.ThrowsAsync<ArgumentException>(() => read.GetSummaryAsync(seed.OrganizationId,
            Query() with { From = Start.AddDays(-91) }));
        await Assert.ThrowsAsync<ArgumentException>(() => read.GetSummaryAsync(seed.OrganizationId,
            Query() with { Status = "not-a-status" }));
        await Assert.ThrowsAsync<ArgumentException>(() => read.GetActivityAsync(seed.OrganizationId,
            Query(), limit: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => read.GetActivityAsync(seed.OrganizationId,
            Query(), cursor: "broken%%cursor"));
    }

    private static UsageReadQuery Query() => new(From: Start.AddHours(-1), To: AsOf);

    private static UsageReadService Read(IServiceProvider services) => new(
        new PostgreSqlUsageReadStore(services.GetRequiredService<FoundationDbContext>()),
        new FixedClock(AsOf));

    private async Task<ReadSeed> ResetAndSeedAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();

        var ownerId = Guid.CreateVersion7();
        var foreignOwnerId = Guid.CreateVersion7();
        var orgId = Guid.CreateVersion7();
        var foreignOrgId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var foreignProjectId = Guid.CreateVersion7();
        var keyId = Guid.CreateVersion7();
        var foreignKeyId = Guid.CreateVersion7();
        var modelId = Guid.CreateVersion7();
        var otherModelId = Guid.CreateVersion7();
        var openAiId = Guid.CreateVersion7();
        var anthropicId = Guid.CreateVersion7();
        var openAiMappingId = Guid.CreateVersion7();
        var anthropicMappingId = Guid.CreateVersion7();
        var priceId = Guid.CreateVersion7();
        var otherPriceId = Guid.CreateVersion7();
        var foreignRequestId = Guid.CreateVersion7();
        var feeId = Guid.CreateVersion7();

        foreach (var (id, email) in new[] { (ownerId, "usage-owner@example.uz"), (foreignOwnerId, "usage-foreign@example.uz") })
            db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
                { Id = id, Email = email, PasswordHash = "not-a-password", Status = "Active", CreatedAt = Start, UpdatedAt = Start });
        foreach (var (id, name, owner, project, key) in new[]
            { (orgId, "First org", ownerId, projectId, keyId), (foreignOrgId, "Foreign org", foreignOwnerId, foreignProjectId, foreignKeyId) })
        {
            db.Set<OrganizationEntity>().Add(new OrganizationEntity
                { Id = id, Name = name, Status = "Active", CreatedAt = Start });
            db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
                { OrganizationId = id, AccountId = owner, Role = "Owner", Status = "Active", CreatedAt = Start });
            db.Set<ProjectEntity>().Add(new ProjectEntity
                { Id = project, OrganizationId = id, Name = name + " project", Status = "Active", CreatedAt = Start });
            db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
                { Id = key, ProjectId = project, Name = name + " key", Prefix = Guid.NewGuid().ToString("N")[..12],
                    SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
                    CreatedByAccountId = owner, CreatedAt = Start });
        }
        db.Set<CatalogProviderEntity>().AddRange(
            new CatalogProviderEntity { Id = openAiId, Code = "OpenAI", Name = "OpenAI", Status = "Active", CreatedAt = Start },
            new CatalogProviderEntity { Id = anthropicId, Code = "Anthropic", Name = "Anthropic", Status = "Active", CreatedAt = Start });
        db.Set<CatalogModelEntity>().AddRange(
            new CatalogModelEntity { Id = modelId, CanonicalCode = "model-main", DisplayName = "Main", Status = "Active",
                ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]", CreatedAt = Start },
            new CatalogModelEntity { Id = otherModelId, CanonicalCode = "model-other", DisplayName = "Other", Status = "Active",
                ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]", CreatedAt = Start });
        db.Set<CatalogProviderModelEntity>().AddRange(
            new CatalogProviderModelEntity { Id = openAiMappingId, ProviderId = openAiId, ModelId = modelId,
                UpstreamModelCode = "up-main", Status = "Active", CapabilityOverridesJson = "{}", CreatedAt = Start },
            new CatalogProviderModelEntity { Id = anthropicMappingId, ProviderId = anthropicId, ModelId = otherModelId,
                UpstreamModelCode = "up-other", Status = "Active", CapabilityOverridesJson = "{}", CreatedAt = Start });
        db.Set<CatalogModelPriceEntity>().AddRange(
            new CatalogModelPriceEntity { Id = priceId, ProviderModelId = openAiMappingId,
                EffectiveFrom = Start, InputPriceMicroUsdPerMillion = 1000, OutputPriceMicroUsdPerMillion = 2000,
                ExtraPricingJson = "{}", CreatedAt = Start },
            new CatalogModelPriceEntity { Id = otherPriceId, ProviderModelId = anthropicMappingId,
                EffectiveFrom = Start, InputPriceMicroUsdPerMillion = 2000, OutputPriceMicroUsdPerMillion = 4000,
                ExtraPricingJson = "{}", CreatedAt = Start });
        db.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity { Id = feeId, PolicyCode = "default",
            EffectiveFrom = Start, MarkupBasisPoints = 0, FixedFeeMicroUsd = 0, CreatedAt = Start });
        await db.SaveChangesAsync();

        var firstAttemptId = Guid.CreateVersion7();
        var successAttemptId = Guid.CreateVersion7();
        var failedAttemptId = Guid.CreateVersion7();
        var unknownAttemptId = Guid.CreateVersion7();
        var foreignAttemptId = Guid.CreateVersion7();
        db.Set<UsageRequestEntity>().AddRange(
            NewRequest(FirstRequestId, orgId, projectId, keyId, modelId, Start, Start.AddMilliseconds(120),
                "Succeeded", "Completed", "Settled", 200, true, "trace-safe"),
            NewRequest(SecondRequestId, orgId, projectId, keyId, otherModelId, Start, Start.AddMilliseconds(80),
                "Failed", "NotStarted", "Settled", 502, false, null),
            NewRequest(UnknownRequestId, orgId, projectId, keyId, modelId, Start.AddDays(1), Start.AddDays(1).AddSeconds(1),
                "OutcomeUnknown", "ClientDisconnected", "PendingEvidence", 503, false, null),
            NewRequest(foreignRequestId, foreignOrgId, foreignProjectId, foreignKeyId, modelId,
                Start, Start.AddSeconds(2), "Succeeded", "Completed", "Settled", 200, false, null));
        db.Set<UsageAttemptEntity>().AddRange(
            Attempt(firstAttemptId, FirstRequestId, 1, openAiMappingId, "RejectedBeforeExecution"),
            Attempt(successAttemptId, FirstRequestId, 2, openAiMappingId, "Succeeded"),
            Attempt(failedAttemptId, SecondRequestId, 1, anthropicMappingId, "Failed"),
            Attempt(unknownAttemptId, UnknownRequestId, 1, openAiMappingId, "OutcomeUnknown"),
            Attempt(foreignAttemptId, foreignRequestId, 1, openAiMappingId, "Succeeded"));
        await db.SaveChangesAsync();

        db.Set<UsageEvidenceEntity>().AddRange(
            Evidence(FirstRequestId, successAttemptId, openAiMappingId, priceId, 100, 20),
            Evidence(SecondRequestId, failedAttemptId, anthropicMappingId, otherPriceId, 20, 5),
            Evidence(foreignRequestId, foreignAttemptId, openAiMappingId, priceId, 900, 90),
            new UsageEvidenceEntity { Id = Guid.CreateVersion7(), RequestId = UnknownRequestId,
                AttemptId = unknownAttemptId, ProviderModelId = openAiMappingId, State = "Unknown", Source = "Unknown",
                CapturedAt = Start.AddDays(1), ReconcileAfter = Start.AddDays(2) });
        // Each settled request has one reservation and one final settlement;
        // the read projection must not infer customer charge from price or evidence.
        AddSettlement(db, FirstRequestId, orgId, projectId, keyId, feeId, 2_000, 3_000);
        AddSettlement(db, SecondRequestId, orgId, projectId, keyId, feeId, 250, 500);
        AddSettlement(db, foreignRequestId, foreignOrgId, foreignProjectId, foreignKeyId, feeId, 500_000, 999_000);
        await db.SaveChangesAsync();

        return new ReadSeed(orgId, foreignOrgId, ownerId, projectId, foreignProjectId,
            keyId, modelId, anthropicId, foreignRequestId);
    }

    private static UsageRequestEntity NewRequest(Guid id, Guid org, Guid project, Guid key, Guid model,
        DateTimeOffset started, DateTimeOffset completed, string execution, string delivery,
        string financial, int status, bool stream, string? trace) => new()
    {
        Id = id, OrganizationId = org, ProjectId = project, ApiKeyId = key,
        CanonicalModelId = model, StartedAt = started, CompletedAt = completed,
        ExecutionState = execution, DeliveryState = delivery, FinancialState = financial,
        IsStream = stream, Operation = "chat.completions", RouteStrategy = "priority",
        TraceId = trace, HttpStatus = status
    };

    private static UsageAttemptEntity Attempt(Guid id, Guid request, int number, Guid mapping, string state) => new()
    {
        Id = id, RequestId = request, Number = number, ProviderModelId = mapping,
        StartedAt = Start, CompletedAt = Start.AddMilliseconds(40),
        ExecutionState = state, ProviderRequestId = "safe-upstream-id",
        ErrorCategory = state == "Failed" ? "UpstreamUnavailable" : null
    };

    private static UsageEvidenceEntity Evidence(Guid request, Guid attempt, Guid mapping,
        Guid price, int input, int output) => new()
    {
        Id = Guid.CreateVersion7(), RequestId = request, AttemptId = attempt,
        ProviderModelId = mapping, State = "Verified", Source = "Provider",
        InputTokens = input, OutputTokens = output, CachedInputTokens = 0,
        PriceVersionId = price, CapturedAt = Start
    };

    private static void AddSettlement(FoundationDbContext db, Guid request, Guid org, Guid project,
        Guid key, Guid fee, long providerCost, long charge)
    {
        var reservationId = Guid.CreateVersion7();
        db.Set<BillingReservationEntity>().Add(new BillingReservationEntity
        {
            Id = reservationId, RequestId = request, OrganizationId = org, ProjectId = project,
            ApiKeyId = key, FeePolicyVersionId = fee, AmountMicroUsd = charge,
            Status = "Settled", CapturedMicroUsd = charge, CreatedAt = Start,
            ExpiresAt = Start.AddHours(1), FinalizedAt = Start.AddMinutes(1)
        });
        db.Set<BillingSettlementEntity>().Add(new BillingSettlementEntity
        {
            Id = Guid.CreateVersion7(), ReservationId = reservationId, RequestId = request,
            OrganizationId = org, ProviderCostMicroUsd = providerCost,
            UncappedCustomerChargeMicroUsd = charge, ChargedMicroUsd = charge,
            UncollectedChargeMicroUsd = 0, PlatformExposureMicroUsd = 0,
            UnresolvedUsage = false, Outcome = "Settled", CreatedAt = Start.AddMinutes(1)
        });
    }

    private sealed record ReadSeed(Guid OrganizationId, Guid ForeignOrganizationId,
        Guid OwnerAccountId, Guid ProjectId, Guid ForeignProjectId, Guid ApiKeyId,
        Guid MainModelId, Guid AnthropicProviderId, Guid ForeignRequestId);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
