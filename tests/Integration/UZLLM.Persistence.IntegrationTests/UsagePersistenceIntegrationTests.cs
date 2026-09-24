using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class UsagePersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_idempotency_claims_return_one_request_and_reject_changed_payload()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        var clock = new MutableUsageTimeProvider(StartedAt);
        var key = Guid.NewGuid().ToString("D");
        var input = Input(seed, key, [1, 2, 3]);

        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var first = CreateService(firstScope.ServiceProvider, clock);
        var second = CreateService(secondScope.ServiceProvider, clock);
        var outcomes = await Task.WhenAll(first.PrepareAsync(input), second.PrepareAsync(input));

        Assert.Single(outcomes, outcome => outcome.Kind == ClaimResultKind.Created);
        Assert.Single(outcomes, outcome => outcome.Kind == ClaimResultKind.Duplicate);
        Assert.Equal(outcomes[0].RequestId, outcomes[1].RequestId);
        Assert.Equal(ClaimResultKind.PayloadConflict,
            (await first.PrepareAsync(Input(seed, key, [9]))).Kind);
        Assert.Equal(ClaimResultKind.Created,
            (await first.PrepareAsync(Input(seed, key, [1, 2, 3]) with { Operation = "embeddings" })).Kind);

        clock.UtcNow = clock.UtcNow.AddHours(24);
        var reused = await first.PrepareAsync(input);
        Assert.Equal(ClaimResultKind.Created, reused.Kind);
        Assert.NotEqual(outcomes[0].RequestId, reused.RequestId);
        Assert.Equal(3, await firstScope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<UsageRequestEntity>().CountAsync());
    }

    [Fact]
    public async Task Unknown_usage_remains_pending_until_verified_evidence_is_durable()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        var clock = new MutableUsageTimeProvider(StartedAt);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var usage = CreateService(services, clock);
        var request = await usage.PrepareAsync(Input(seed, null, [1]));
        var first = await usage.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        var second = await usage.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        Assert.Equal((1, 2), (first.Number, second.Number));
        Assert.True(await usage.MarkDispatchedAsync(first.Id));
        Assert.False(await usage.MarkDispatchedAsync(first.Id));

        var unknown = await usage.RecordUnknownAsync(request.RequestId, first.Id);
        Assert.NotNull(unknown);
        Assert.Null(unknown.InputTokens);
        Assert.Null(unknown.PriceVersionId);
        Assert.Equal(StartedAt.AddHours(24), unknown.ReconcileAfter);
        Assert.Equal(FinancialState.PendingEvidence, (await usage.FindRequestAsync(request.RequestId))?.Financial);
        Assert.Equal(ExecutionState.OutcomeUnknown,
            (await new PostgreSqlUsageStore(services.GetRequiredService<FoundationDbContext>()).FindAttemptAsync(first.Id))?.Execution);
        Assert.Null(await usage.RecordUnknownAsync(request.RequestId, first.Id));

        var verified = await usage.RecordVerifiedAsync(new VerifiedUsageInput(request.RequestId, first.Id,
            EvidenceSource.Reconciled, 100, 20, 10, 2, seed.PriceId, "upstream-123"));
        Assert.NotNull(verified);
        Assert.Equal(FinancialState.PendingSettlement, (await usage.FindRequestAsync(request.RequestId))?.Financial);
        Assert.Null(await usage.RecordVerifiedAsync(new VerifiedUsageInput(request.RequestId, first.Id,
            EvidenceSource.Reconciled, 100, 20, 10, 2, seed.PriceId, "upstream-123")));
        Assert.Equal(2, (await usage.ListEvidenceAsync(request.RequestId)).Count);
        Assert.Equal(2, await services.GetRequiredService<FoundationDbContext>().Database
            .SqlQueryRaw<int>("SELECT count(*) AS \"Value\" FROM ops.outbox WHERE event_type LIKE 'usage.evidence.%'")
            .SingleAsync());
    }

    [Fact]
    public async Task Evidence_and_outbox_survive_a_later_settlement_rollback_and_cannot_be_mutated()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var usage = CreateService(services, new MutableUsageTimeProvider(StartedAt));
        var request = await usage.PrepareAsync(Input(seed, null, [4]));
        var attempt = await usage.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        var evidence = await usage.RecordVerifiedAsync(new VerifiedUsageInput(request.RequestId, attempt.Id,
            EvidenceSource.Provider, 7, 3, 0, null, seed.PriceId, "provider-request"));
        Assert.NotNull(evidence);

        var db = services.GetRequiredService<FoundationDbContext>();
        await using (await services.GetRequiredService<ITransactionCoordinator>().BeginAsync())
        {
            await db.Set<UsageRequestEntity>().Where(value => value.Id == request.RequestId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FinancialState, "Settled"));
        }
        Assert.Equal("PendingSettlement", (await db.Set<UsageRequestEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == request.RequestId)).FinancialState);
        Assert.Equal(1, await db.Set<UsageEvidenceEntity>().CountAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT count(*) AS \"Value\" FROM ops.outbox WHERE event_type = 'usage.evidence.verified'")
            .SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE usage.evidence SET input_tokens = 0 WHERE id = {evidence.Id}"));
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_request_and_mismatched_evidence_mapping()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var wrongOrganization = Guid.CreateVersion7();
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = wrongOrganization, Name = "Other tenant", Status = "Active", CreatedAt = StartedAt
        });
        await db.SaveChangesAsync();
        db.Set<UsageRequestEntity>().Add(NewRequest(seed, wrongOrganization));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var usage = CreateService(services, new MutableUsageTimeProvider(StartedAt));
        var request = await usage.PrepareAsync(Input(seed, null, [1]));
        var attempt = await usage.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        var otherProviderModel = Guid.CreateVersion7();
        var otherProvider = Guid.CreateVersion7();
        db.Set<CatalogProviderEntity>().Add(new CatalogProviderEntity
        {
            Id = otherProvider, Code = "usage-test-other", Name = "Other provider",
            Status = "Active", CreatedAt = StartedAt
        });
        db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
        {
            Id = otherProviderModel, ProviderId = otherProvider, ModelId = seed.ModelId,
            UpstreamModelCode = "other", Status = "Active", CapabilityOverridesJson = "{}", CreatedAt = StartedAt
        });
        await db.SaveChangesAsync();
        db.Set<UsageEvidenceEntity>().Add(new UsageEvidenceEntity
        {
            Id = Guid.CreateVersion7(), RequestId = request.RequestId, AttemptId = attempt.Id,
            ProviderModelId = otherProviderModel, State = "Unknown", Source = "Unknown",
            CapturedAt = StartedAt, ReconcileAfter = StartedAt.AddHours(24)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Usage_service_rejects_invalid_claims_and_pre_dispatch_or_overlapping_token_evidence()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var usage = CreateService(scope.ServiceProvider, new MutableUsageTimeProvider(StartedAt));
        var input = Input(seed, Guid.NewGuid().ToString("D"), [1]);
        await Assert.ThrowsAsync<ArgumentException>(() => usage.PrepareAsync(input with { IdempotencyKey = "not-a-uuid" }));
        await Assert.ThrowsAsync<ArgumentException>(() => usage.PrepareAsync(input with { PayloadHash = [1] }));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<UsageRequestEntity>().CountAsync());

        var request = await usage.PrepareAsync(input);
        var attempt = await usage.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        await Assert.ThrowsAsync<ArgumentException>(() => usage.RecordVerifiedAsync(new VerifiedUsageInput(
            request.RequestId, attempt.Id, EvidenceSource.Provider, 4, 1, 0, null, seed.PriceId, null)));
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => usage.RecordVerifiedAsync(new VerifiedUsageInput(
            request.RequestId, attempt.Id, EvidenceSource.Provider, 4, 1, 5, null, seed.PriceId, null)));
        Assert.Empty(await usage.ListEvidenceAsync(request.RequestId));
    }

    [Fact]
    public async Task Concurrent_unknown_and_verified_evidence_never_regresses_financial_state()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var seed = await SeedAsync();
        var clock = new MutableUsageTimeProvider(StartedAt);
        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var first = CreateService(firstScope.ServiceProvider, clock);
        var second = CreateService(secondScope.ServiceProvider, clock);
        var request = await first.PrepareAsync(Input(seed, null, [1]));
        var attempt = await first.StartAttemptAsync(request.RequestId, seed.ProviderModelId);
        Assert.True(await first.MarkDispatchedAsync(attempt.Id));

        var unknown = Task.Run(async () =>
        {
            try { await first.RecordUnknownAsync(request.RequestId, attempt.Id); }
            catch (InvalidOperationException) { /* Verified usage won the race. */ }
        });
        var verified = second.RecordVerifiedAsync(new VerifiedUsageInput(request.RequestId, attempt.Id,
            EvidenceSource.Provider, 10, 5, 0, null, seed.PriceId, "upstream-1"));
        await Task.WhenAll(unknown, verified);

        Assert.NotNull(await verified);
        Assert.Equal(FinancialState.PendingSettlement, (await first.FindRequestAsync(request.RequestId))?.Financial);
        Assert.Single(await first.ListEvidenceAsync(request.RequestId), value => value.State == EvidenceState.Verified);
    }

    private async Task<UsageSeed> SeedAsync()
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var seed = new UsageSeed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var account = Guid.CreateVersion7();
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = account, Email = $"usage-{account:N}@example.uz", PasswordHash = "not-a-password",
            Status = "Active", CreatedAt = StartedAt, UpdatedAt = StartedAt
        });
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = seed.OrganizationId, Name = "Usage tenant", Status = "Active", CreatedAt = StartedAt
        });
        db.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = seed.ProjectId, OrganizationId = seed.OrganizationId, Name = "Production",
            Status = "Active", SettingsJson = "{}", CreatedAt = StartedAt
        });
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = seed.ApiKeyId, ProjectId = seed.ProjectId, Name = "Production", Prefix = "123456789012",
            SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
            CreatedByAccountId = account, CreatedAt = StartedAt
        });
        db.Set<CatalogProviderEntity>().Add(new CatalogProviderEntity
        {
            Id = seed.ProviderId, Code = "usage-test", Name = "Test provider", Status = "Active", CreatedAt = StartedAt
        });
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = seed.ModelId, CanonicalCode = "usage-test-model", DisplayName = "Test model",
            ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]", Status = "Active", CreatedAt = StartedAt
        });
        db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
        {
            Id = seed.ProviderModelId, ProviderId = seed.ProviderId, ModelId = seed.ModelId,
            UpstreamModelCode = "model", Status = "Active", CapabilityOverridesJson = "{}", CreatedAt = StartedAt
        });
        db.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
        {
            Id = seed.PriceId, ProviderModelId = seed.ProviderModelId, EffectiveFrom = StartedAt,
            InputPriceMicroUsdPerMillion = 1000, OutputPriceMicroUsdPerMillion = 2000,
            ExtraPricingJson = "{}", CreatedAt = StartedAt
        });
        await db.SaveChangesAsync();
        return seed;
    }

    private static UsageService CreateService(IServiceProvider provider, TimeProvider clock)
    {
        var db = provider.GetRequiredService<FoundationDbContext>();
        return new UsageService(new PostgreSqlUsageStore(db), provider.GetRequiredService<ITransactionCoordinator>(),
            provider.GetRequiredService<IOutboxStore>(), clock);
    }

    private static PrepareUsageRequest Input(UsageSeed seed, string? key, byte[] payload) => new(
        seed.OrganizationId, seed.ProjectId, seed.ApiKeyId, seed.ModelId, false,
        "chat.completions", key, SHA256.HashData(payload), null);

    private static UsageRequestEntity NewRequest(UsageSeed seed, Guid organizationId) => new()
    {
        Id = Guid.CreateVersion7(), OrganizationId = organizationId, ProjectId = seed.ProjectId,
        ApiKeyId = seed.ApiKeyId, CanonicalModelId = seed.ModelId, StartedAt = StartedAt,
        ExecutionState = "Prepared", DeliveryState = "NotStarted", FinancialState = "PendingAdmission",
        Operation = "chat.completions"
    };

    private sealed record UsageSeed(Guid OrganizationId, Guid ProjectId, Guid ApiKeyId,
        Guid ProviderId, Guid ModelId, Guid ProviderModelId, Guid PriceId);

    private sealed class MutableUsageTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
