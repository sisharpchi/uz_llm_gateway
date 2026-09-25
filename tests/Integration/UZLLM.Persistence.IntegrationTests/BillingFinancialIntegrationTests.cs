using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class BillingFinancialIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fifty_concurrent_thirty_cent_holds_on_one_dollar_admit_only_thirty_three()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 1_000_000);
        var providers = new List<ServiceProvider>();
        var scopes = new List<AsyncServiceScope>();
        try
        {
            for (var i = 0; i < 50; i++)
            {
                var provider = fixture.CreateServiceProvider();
                providers.Add(provider);
                scopes.Add(provider.CreateAsyncScope());
            }
            var results = await Task.WhenAll(scopes.Select((scope, index) =>
                Financial(scope.ServiceProvider, new MutableFinancialClock(Start))
                    .ReserveAsync(Admission(seed, 30_000, null, [checked((byte)index)]))));
            Assert.Equal(33, results.Count(value => value.Status == AdmissionStatus.Reserved));
            Assert.Equal(17, results.Count(value => value.Status == AdmissionStatus.InsufficientWallet));
            await using var verifier = fixture.CreateServiceProvider();
            await using var verifierScope = verifier.CreateAsyncScope();
            var wallet = await Financial(verifierScope.ServiceProvider, new MutableFinancialClock(Start))
                .GetWalletStateAsync(seed.OrganizationId);
            Assert.NotNull(wallet);
            Assert.Equal(990_000, wallet.Wallet.ReservedBalance.Value);
            Assert.Equal(10_000, wallet.Wallet.AvailableBalance.Value);
            Assert.Equal(33, await verifierScope.ServiceProvider.GetRequiredService<FoundationDbContext>()
                .Set<BillingReservationEntity>().CountAsync());
        }
        finally
        {
            foreach (var scope in scopes) await scope.DisposeAsync();
            foreach (var provider in providers) await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Api_key_and_project_hard_budgets_block_admission_even_with_wallet_credit()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 1_000_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        var keyPolicy = await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            seed.ApiKeyId, new UsdMicroAmount(20_000));
        Assert.NotNull(keyPolicy);
        Assert.Equal(AdmissionStatus.ApiKeyBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 30_000))).Status);
        Assert.Equal(AdmissionStatus.Reserved,
            (await financial.ReserveAsync(Admission(seed, 20_000))).Status);
        Assert.Equal(AdmissionStatus.ApiKeyBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 1))).Status);

        var projectPolicy = await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, new UsdMicroAmount(10_000));
        Assert.Null(projectPolicy); // New caps cannot silently omit active holds.
        Assert.Equal(20_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
    }

    [Fact]
    public async Task Concurrent_admission_holds_every_applicable_window_without_overspend()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 2_000_000);
        await using (var setup = fixture.CreateServiceProvider())
        await using (var scope = setup.CreateAsyncScope())
        {
            var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
            Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
                null, BudgetPeriod.Daily, new UsdMicroAmount(1_000_000)));
            Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
                seed.ApiKeyId, BudgetPeriod.Weekly, new UsdMicroAmount(900_000)));
        }

        var providers = new List<ServiceProvider>();
        var scopes = new List<AsyncServiceScope>();
        try
        {
            for (var i = 0; i < 50; i++)
            {
                var provider = fixture.CreateServiceProvider();
                providers.Add(provider);
                scopes.Add(provider.CreateAsyncScope());
            }
            var results = await Task.WhenAll(scopes.Select((scope, index) =>
                Financial(scope.ServiceProvider, new MutableFinancialClock(Start))
                    .ReserveAsync(Admission(seed, 30_000, payload: [checked((byte)index)]))));
            Assert.Equal(30, results.Count(value => value.Status == AdmissionStatus.Reserved));
            Assert.Equal(20, results.Count(value => value.Status == AdmissionStatus.ApiKeyBudgetExceeded));

            await using var verifier = fixture.CreateServiceProvider();
            await using var verifierScope = verifier.CreateAsyncScope();
            var financial = Financial(verifierScope.ServiceProvider, new MutableFinancialClock(Start));
            var budgets = await financial.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId);
            Assert.Equal(900_000, budgets.Single(value => value.Period == BudgetPeriod.Daily).Reserved.Value);
            Assert.Equal(900_000, budgets.Single(value => value.Period == BudgetPeriod.Weekly).Reserved.Value);
            Assert.Equal(900_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
        }
        finally
        {
            foreach (var scope in scopes) await scope.DisposeAsync();
            foreach (var provider in providers) await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Late_settlement_updates_admission_day_not_current_day()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 200_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, BudgetPeriod.Daily, new UsdMicroAmount(30_000)));

        clock.UtcNow = Start.AddDays(1).AddSeconds(-1);
        var old = await financial.ReserveAsync(Admission(seed, 30_000, payload: [3]) with
        { ExpiresAt = clock.UtcNow.AddHours(2) });
        Assert.Equal(AdmissionStatus.Reserved, old.Status);
        var usage = Usage(scope.ServiceProvider, clock);
        var attempt = await usage.StartAttemptAsync(old.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(old.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, null)));

        clock.UtcNow = Start.AddDays(1).AddMinutes(1);
        var next = await financial.ReserveAsync(Admission(seed, 30_000, payload: [4]) with
        { ExpiresAt = clock.UtcNow.AddHours(2) });
        Assert.Equal(AdmissionStatus.Reserved, next.Status);
        Assert.Equal(FinalizationStatus.Settled, (await financial.FinalizeAsync(old.Reservation!.Id)).Status);
        Assert.Equal(FinalizationStatus.AlreadyFinalized, (await financial.FinalizeAsync(old.Reservation.Id)).Status);

        var current = (await financial.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId)).Single();
        Assert.Equal(Start.AddDays(1), current.Window.Start);
        Assert.Equal(30_000, current.Reserved.Value);
        Assert.Equal(0, current.Captured.Value);
        var oldBucket = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<BillingBudgetBucketEntity>().AsNoTracking()
            .SingleAsync(value => value.PolicyId == current.Id && value.WindowStart == Start);
        Assert.Equal(10_000, oldBucket.CapturedMicroUsd);
        Assert.Equal(0, oldBucket.ReservedMicroUsd);
    }

    [Fact]
    public async Task Every_project_and_key_window_is_a_hard_gate_and_releases_with_the_reservation()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 200_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        foreach (var period in new[] { BudgetPeriod.Lifetime, BudgetPeriod.Monthly })
            Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
                null, period, new UsdMicroAmount(20_000)));
        foreach (var period in new[] { BudgetPeriod.Daily, BudgetPeriod.Weekly })
            Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
                seed.ApiKeyId, period, new UsdMicroAmount(20_000)));

        var hold = await financial.ReserveAsync(Admission(seed, 20_000, payload: [10]));
        Assert.Equal(AdmissionStatus.Reserved, hold.Status);
        Assert.Equal(AdmissionStatus.ProjectBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 1, payload: [11]))).Status);
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, BudgetPeriod.Lifetime, new UsdMicroAmount(21_000)));
        Assert.Equal(AdmissionStatus.ProjectBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 1, payload: [12]))).Status);
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, BudgetPeriod.Monthly, new UsdMicroAmount(21_000)));
        Assert.Equal(AdmissionStatus.ApiKeyBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 1, payload: [13]))).Status);
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            seed.ApiKeyId, BudgetPeriod.Daily, new UsdMicroAmount(21_000)));
        Assert.Equal(AdmissionStatus.ApiKeyBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 1, payload: [14]))).Status);

        var budgets = await financial.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId);
        Assert.Equal(4, budgets.Count);
        Assert.All(budgets, value => Assert.Equal(20_000, value.Reserved.Value));
        Assert.Equal(FinalizationStatus.Released,
            (await financial.ReleaseUndispatchedAsync(hold.Reservation!.Id)).Status);
        Assert.All(await financial.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId),
            value => Assert.Equal(0, value.Reserved.Value));
    }

    [Fact]
    public async Task New_daily_policy_includes_existing_captured_spend_and_rejects_active_holds()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var first = await financial.ReserveAsync(Admission(seed, 30_000, payload: [15]));
        Assert.Equal(AdmissionStatus.Reserved, first.Status);
        var attempt = await usage.StartAttemptAsync(first.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(first.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, null)));
        Assert.Equal(FinalizationStatus.Settled, (await financial.FinalizeAsync(first.Reservation!.Id)).Status);

        var daily = await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, BudgetPeriod.Daily, new UsdMicroAmount(15_000));
        Assert.NotNull(daily);
        Assert.Equal(10_000, daily.Captured.Value);
        Assert.Equal(AdmissionStatus.ProjectBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 5_001, payload: [16]))).Status);
        var held = await financial.ReserveAsync(Admission(seed, 5_000, payload: [17]));
        Assert.Equal(AdmissionStatus.Reserved, held.Status);
        Assert.Null(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, BudgetPeriod.Weekly, new UsdMicroAmount(25_000)));
        Assert.Equal(5_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
    }

    [Fact]
    public async Task Migration_roundtrip_preserves_existing_lifetime_policy_and_reservation_hold()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        Guid reservationId;
        await using (var before = fixture.CreateServiceProvider())
        await using (var scope = before.CreateAsyncScope())
        {
            var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
            Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
                null, new UsdMicroAmount(30_000)));
            var hold = await financial.ReserveAsync(Admission(seed, 30_000));
            Assert.Equal(AdmissionStatus.Reserved, hold.Status);
            reservationId = hold.Reservation!.Id;
        }

        await using (var migrationProvider = fixture.CreateServiceProvider())
        await using (var migrationScope = migrationProvider.CreateAsyncScope())
        {
            var migrator = migrationScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync("20260925135939_AddOperatorControls");
            await migrator.MigrateAsync();
        }

        await using var after = fixture.CreateServiceProvider();
        await using var afterScope = after.CreateAsyncScope();
        var resumed = Financial(afterScope.ServiceProvider, new MutableFinancialClock(Start));
        var policy = (await resumed.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId)).Single();
        Assert.Equal(BudgetPeriod.Lifetime, policy.Period);
        Assert.Equal(DateTimeOffset.UnixEpoch, policy.Window.Start);
        Assert.Equal(30_000, policy.Reserved.Value);
        Assert.Equal(FinalizationStatus.Released,
            (await resumed.ReleaseUndispatchedAsync(reservationId)).Status);
        Assert.Equal(0, (await resumed.ListBudgetsAsync(seed.OrganizationId, seed.ProjectId))
            .Single().Reserved.Value);
    }

    [Fact]
    public async Task Project_cap_and_historical_key_spend_are_enforced()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var projectPolicy = await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            null, new UsdMicroAmount(25_000));
        Assert.NotNull(projectPolicy);
        var first = await financial.ReserveAsync(Admission(seed, 20_000));
        Assert.Equal(AdmissionStatus.Reserved, first.Status);
        Assert.Equal(AdmissionStatus.ProjectBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 10_000))).Status);
        Assert.Equal(FinalizationStatus.Released,
            (await financial.ReleaseUndispatchedAsync(first.Reservation!.Id)).Status);
        var next = await financial.ReserveAsync(Admission(seed, 10_000));
        Assert.Equal(AdmissionStatus.Reserved, next.Status);
        var usage = Usage(scope.ServiceProvider, clock);
        var attempt = await usage.StartAttemptAsync(next.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(next.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, null)));
        Assert.Equal(FinalizationStatus.Settled,
            (await financial.FinalizeAsync(next.Reservation!.Id)).Status);

        var keyPolicy = await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId,
            seed.ApiKeyId, new UsdMicroAmount(15_000));
        Assert.NotNull(keyPolicy);
        Assert.Equal(10_000, keyPolicy.Captured.Value);
        Assert.Equal(AdmissionStatus.ApiKeyBudgetExceeded,
            (await financial.ReserveAsync(Admission(seed, 5_001))).Status);
        Assert.Equal(AdmissionStatus.Reserved,
            (await financial.ReserveAsync(Admission(seed, 5_000))).Status);
    }

    [Fact]
    public async Task Charge_is_capped_by_the_hold_and_excess_provider_cost_is_exposure()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 1_000_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var admitted = await financial.ReserveAsync(Admission(seed, 30_000));
        Assert.Equal(AdmissionStatus.Reserved, admitted.Status);
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(admitted.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 50_000, 0, 0, null, seed.PriceId, "upstream")));

        var final = await financial.FinalizeAsync(admitted.Reservation!.Id);
        Assert.Equal(FinalizationStatus.Settled, final.Status);
        Assert.Equal(50_000, final.Settlement!.ProviderCost.Value);
        Assert.Equal(30_000, final.Settlement.Charged.Value);
        Assert.Equal(20_000, final.Settlement.PlatformExposure.Value);
        Assert.Equal(20_000, final.Settlement.UncollectedCharge.Value);
        var wallet = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(970_000, wallet!.Wallet.PostedBalance.Value);
        Assert.Equal(0, wallet.Wallet.ReservedBalance.Value);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<BillingLedgerEntryEntity>().Where(value => value.Type == "UsageCharge").ToListAsync());
    }

    [Fact]
    public async Task Missing_usage_waits_for_reconciliation_window_then_releases_without_zero_usage_claim()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var admitted = await financial.ReserveAsync(Admission(seed, 30_000));
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.Equal(FinalizationStatus.PendingEvidence,
            (await financial.FinalizeAsync(admitted.Reservation!.Id)).Status);

        clock.UtcNow = Start.AddHours(1);
        Assert.Equal(FinalizationStatus.PendingEvidence,
            (await financial.ReconcileAsync(admitted.Reservation.Id)).Status);
        var evidence = Assert.Single(await usage.ListEvidenceAsync(admitted.RequestId.Value));
        Assert.Equal(EvidenceState.Unknown, evidence.State);
        Assert.Null(evidence.InputTokens);
        Assert.Equal(FinancialState.PendingEvidence,
            (await usage.FindRequestAsync(admitted.RequestId.Value))!.Financial);
        clock.UtcNow = Start.AddHours(24);
        Assert.Equal(FinalizationStatus.PendingEvidence,
            (await financial.ReconcileAsync(admitted.Reservation.Id)).Status);
        clock.UtcNow = Start.AddHours(25).AddMinutes(1);
        var result = await financial.ReconcileAsync(admitted.Reservation.Id);
        Assert.Equal(FinalizationStatus.Released, result.Status);
        Assert.True(result.Settlement!.UnresolvedUsage);
        Assert.Equal(0, result.Settlement.Charged.Value);
        Assert.Equal(0, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<BillingLedgerEntryEntity>().Where(value => value.Type == "UsageCharge").ToListAsync());

        var late = await usage.RecordVerifiedAsync(new VerifiedUsageInput(admitted.RequestId.Value,
            attempt.Id, EvidenceSource.Reconciled, 10_000, 0, 0, null, seed.PriceId, "late-provider-id"));
        Assert.NotNull(late);
        Assert.True(await financial.RecordLateExposureAsync(late.Id));
        Assert.False(await financial.RecordLateExposureAsync(late.Id));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var exposure = Assert.Single(await db.Set<BillingPlatformExposureEntity>().ToListAsync());
        Assert.Equal(10_000, exposure.ProviderCostMicroUsd);
        Assert.Equal(100_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.PostedBalance.Value);
        Assert.Equal(FinancialState.Released,
            (await usage.FindRequestAsync(admitted.RequestId.Value))!.Financial);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing.platform_exposure WHERE id = {exposure.Id}"));
    }

    [Fact]
    public async Task Verified_evidence_survives_a_settlement_calculation_failure()
    {
        await ResetAsync();
        var seed = await SeedAsync(extraPricingJson: "{\"unimplemented_extra\":1}");
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var admitted = await financial.ReserveAsync(Admission(seed, 30_000));
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(admitted.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, null)));

        await Assert.ThrowsAsync<NotSupportedException>(() => financial.FinalizeAsync(admitted.Reservation!.Id));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<UsageEvidenceEntity>().CountAsync());
        Assert.Equal(0, await db.Set<BillingSettlementEntity>().CountAsync());
        Assert.Equal(30_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
        Assert.Equal(FinancialState.PendingSettlement,
            (await usage.FindRequestAsync(admitted.RequestId.Value))!.Financial);
    }

    [Fact]
    public async Task Recovery_after_restart_distinguishes_unknown_evidence_from_pending_settlement()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 200_000);
        Guid unknownReservation;
        Guid knownReservation;
        Guid unknownRequest;
        Guid knownRequest;

        await using (var firstNode = fixture.CreateServiceProvider())
        await using (var firstScope = firstNode.CreateAsyncScope())
        {
            var clock = new MutableFinancialClock(Start);
            var financial = Financial(firstScope.ServiceProvider, clock);
            var usage = Usage(firstScope.ServiceProvider, clock);
            var unknown = await financial.ReserveAsync(Admission(seed, 30_000, payload: [1]));
            var known = await financial.ReserveAsync(Admission(seed, 30_000, payload: [2]));
            unknownReservation = unknown.Reservation!.Id;
            knownReservation = known.Reservation!.Id;
            unknownRequest = unknown.RequestId!.Value;
            knownRequest = known.RequestId!.Value;
            var unknownAttempt = await usage.StartAttemptAsync(unknownRequest, seed.ProviderModelId);
            var knownAttempt = await usage.StartAttemptAsync(knownRequest, seed.ProviderModelId);
            Assert.True(await usage.MarkDispatchedAsync(unknownAttempt.Id));
            Assert.True(await usage.MarkDispatchedAsync(knownAttempt.Id));
            Assert.Equal(FinalizationStatus.PendingEvidence,
                (await financial.FinalizeAsync(unknownReservation)).Status);
            Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(knownRequest,
                knownAttempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, "known-upstream")));
            Assert.Equal(FinancialState.PendingSettlement,
                (await usage.FindRequestAsync(knownRequest))!.Financial);
        }

        // A new provider/scope models a worker taking over after the Gateway died.
        await using (var recoveryNode = fixture.CreateServiceProvider())
        await using (var recoveryScope = recoveryNode.CreateAsyncScope())
        {
            var clock = new MutableFinancialClock(Start.AddHours(2));
            var financial = Financial(recoveryScope.ServiceProvider, clock);
            var usage = Usage(recoveryScope.ServiceProvider, clock);
            Assert.Equal(FinalizationStatus.PendingEvidence,
                (await financial.ReconcileAsync(unknownReservation)).Status);
            Assert.Equal(FinalizationStatus.Settled,
                (await financial.ReconcileAsync(knownReservation)).Status);
            Assert.Equal(FinalizationStatus.AlreadyFinalized,
                (await financial.ReconcileAsync(knownReservation)).Status);
            Assert.Equal(FinancialState.PendingEvidence,
                (await usage.FindRequestAsync(unknownRequest))!.Financial);
            Assert.Equal(FinancialState.Settled,
                (await usage.FindRequestAsync(knownRequest))!.Financial);
            var held = await financial.GetWalletStateAsync(seed.OrganizationId);
            Assert.Equal(190_000, held!.Wallet.PostedBalance.Value);
            Assert.Equal(30_000, held.Wallet.ReservedBalance.Value);

            clock.UtcNow = Start.AddHours(26).AddMinutes(1);
            var released = await financial.ReconcileAsync(unknownReservation);
            Assert.Equal(FinalizationStatus.Released, released.Status);
            Assert.True(released.Settlement!.UnresolvedUsage);
            Assert.Equal(0, released.Settlement.Charged.Value);
            Assert.Equal(0, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
            var db = recoveryScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Equal(1, await db.Set<BillingLedgerEntryEntity>()
                .CountAsync(value => value.Type == "UsageCharge"));
        }
    }

    [Fact]
    public async Task Verified_preexecution_rejection_releases_hold_without_usage_evidence()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var admitted = await financial.ReserveAsync(Admission(seed, 30_000));
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));

        Assert.True(await usage.FinishAttemptAsync(attempt.Id,
            ExecutionState.RejectedBeforeExecution, "upstream-rejection", "RateLimited"));
        var released = await financial.ReleaseUndispatchedAsync(admitted.Reservation!.Id);

        Assert.Equal(FinalizationStatus.Released, released.Status);
        Assert.Equal(0, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
        Assert.Empty(await usage.ListEvidenceAsync(admitted.RequestId.Value));
        var stored = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .Set<UsageAttemptEntity>().AsNoTracking().SingleAsync();
        Assert.Equal("RejectedBeforeExecution", stored.ExecutionState);
    }

    [Fact]
    public async Task Reversal_preserves_active_holds_records_debt_and_blocks_spend_until_release_recovers_it()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        var admitted = await financial.ReserveAsync(Admission(seed, 60));
        Assert.Equal(AdmissionStatus.Reserved, admitted.Status);
        var reversalReference = Guid.CreateVersion7();
        var reversal = await financial.ApplyConfirmedReversalAsync(seed.OrganizationId,
            reversalReference, new UsdMicroAmount(80));
        Assert.Equal(40, reversal.Recovered.Value);
        Assert.Equal(40, reversal.DebtCreated.Value);
        var duplicate = await financial.ApplyConfirmedReversalAsync(seed.OrganizationId,
            reversalReference, new UsdMicroAmount(80));
        Assert.True(duplicate.Duplicate);
        var held = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(60, held!.Wallet.PostedBalance.Value);
        Assert.Equal(60, held.Wallet.ReservedBalance.Value);
        Assert.Equal(40, held.RecoveryDebt.Value);
        Assert.True(held.SpendingHeld);
        Assert.Equal(AdmissionStatus.SpendingHeld,
            (await financial.ReserveAsync(Admission(seed, 1))).Status);

        Assert.Equal(FinalizationStatus.Released,
            (await financial.ReleaseUndispatchedAsync(admitted.Reservation!.Id)).Status);
        var after = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(20, after!.Wallet.PostedBalance.Value);
        Assert.Equal(0, after.Wallet.ReservedBalance.Value);
        Assert.Equal(0, after.RecoveryDebt.Value);
        Assert.False(after.SpendingHeld);
        Assert.Equal(AdmissionStatus.Reserved,
            (await financial.ReserveAsync(Admission(seed, 1))).Status);
    }

    [Fact]
    public async Task Duplicate_claim_gets_original_request_and_no_second_reservation()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        var key = Guid.NewGuid().ToString("D");
        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var first = Financial(firstScope.ServiceProvider, clock);
        var second = Financial(secondScope.ServiceProvider, clock);
        var input = Admission(seed, 30_000, key, [1, 2]);
        var results = await Task.WhenAll(first.ReserveAsync(input), second.ReserveAsync(input));
        Assert.Single(results, value => value.Status == AdmissionStatus.Reserved);
        Assert.Single(results, value => value.Status == AdmissionStatus.Duplicate);
        Assert.Equal(results[0].RequestId, results[1].RequestId);
        Assert.Equal(AdmissionStatus.PayloadConflict,
            (await second.ReserveAsync(Admission(seed, 30_000, key, [9]))).Status);
        var db = firstScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingReservationEntity>().CountAsync());
        Assert.Equal(1, await db.Set<UsageRequestEntity>().CountAsync());
    }

    [Fact]
    public async Task Gateway_idempotency_precheck_distinguishes_live_duplicate_conflict_and_expiry()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(scope.ServiceProvider, clock);
        var usage = Usage(scope.ServiceProvider, clock);
        var key = Guid.NewGuid().ToString("D");
        var first = await financial.ReserveAsync(Admission(seed, 30_000, key, [1, 2]));
        Assert.Equal(AdmissionStatus.Reserved, first.Status);

        var duplicate = await usage.FindExistingClaimAsync(seed.OrganizationId, seed.ApiKeyId,
            "chat.completions", key, SHA256.HashData([1, 2]));
        var conflict = await usage.FindExistingClaimAsync(seed.OrganizationId, seed.ApiKeyId,
            "chat.completions", key, SHA256.HashData([9]));
        Assert.Equal(ClaimResultKind.Duplicate, duplicate!.Kind);
        Assert.Equal(first.RequestId, duplicate.RequestId);
        Assert.Equal(ClaimResultKind.PayloadConflict, conflict!.Kind);
        clock.UtcNow = Start.AddHours(24).AddSeconds(1);
        Assert.Null(await usage.FindExistingClaimAsync(seed.OrganizationId, seed.ApiKeyId,
            "chat.completions", key, SHA256.HashData([1, 2])));
    }

    [Fact]
    public async Task Concurrent_terminal_calls_create_one_settlement_and_released_request_cannot_dispatch()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var clock = new MutableFinancialClock(Start);
        var first = Financial(firstScope.ServiceProvider, clock);
        var second = Financial(secondScope.ServiceProvider, clock);
        var usage = Usage(firstScope.ServiceProvider, clock);
        var admitted = await first.ReserveAsync(Admission(seed, 30_000));
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        var results = await Task.WhenAll(first.ReleaseUndispatchedAsync(admitted.Reservation!.Id),
            second.FinalizeAsync(admitted.Reservation.Id));
        Assert.Single(results, value => value.Status == FinalizationStatus.Released);
        Assert.Single(results, value => value.Status == FinalizationStatus.AlreadyFinalized);
        Assert.False(await usage.MarkDispatchedAsync(attempt.Id));
        var db = firstScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingSettlementEntity>().CountAsync());
        Assert.Equal(0, (await first.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
    }

    [Fact]
    public async Task Settlement_and_debt_history_are_database_immutable()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        var admitted = await financial.ReserveAsync(Admission(seed, 60));
        var reversal = await financial.ApplyConfirmedReversalAsync(seed.OrganizationId,
            Guid.CreateVersion7(), new UsdMicroAmount(80));
        var final = await financial.ReleaseUndispatchedAsync(admitted.Reservation!.Id);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM billing.settlement WHERE id = {final.Settlement!.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.reversal SET recovered_micro_usd = 0 WHERE id = {reversal.Id}"));
    }

    [Fact]
    public async Task New_credit_repays_recovery_debt_without_consuming_active_holds()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        var admitted = await financial.ReserveAsync(Admission(seed, 60));
        await financial.ApplyConfirmedReversalAsync(seed.OrganizationId, Guid.CreateVersion7(),
            new UsdMicroAmount(80));

        await CreditAsync(seed.OrganizationId, 25);
        var partial = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(60, partial!.Wallet.PostedBalance.Value);
        Assert.Equal(60, partial.Wallet.ReservedBalance.Value);
        Assert.Equal(15, partial.RecoveryDebt.Value);
        Assert.True(partial.SpendingHeld);

        await CreditAsync(seed.OrganizationId, 20);
        var recovered = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(65, recovered!.Wallet.PostedBalance.Value);
        Assert.Equal(60, recovered.Wallet.ReservedBalance.Value);
        Assert.Equal(0, recovered.RecoveryDebt.Value);
        Assert.False(recovered.SpendingHeld);
        Assert.Equal(AdmissionStatus.Reserved, (await financial.ReserveAsync(Admission(seed, 5))).Status);
        Assert.NotNull(admitted.Reservation);
    }

    [Fact]
    public async Task Financial_alert_consumer_replay_creates_only_one_alert()
    {
        await ResetAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var handler = new BillingFinancialAlertHandler(
            services.GetRequiredService<IOperationalAlertPublisher>(),
            services.GetRequiredService<ITransactionCoordinator>(),
            services.GetRequiredService<IConsumerInboxStore>(), "billing.late_exposure.recorded");
        var message = new OutboxMessage(Guid.CreateVersion7(), "billing.late_exposure.recorded",
            System.Text.Json.JsonSerializer.Serialize(new { evidenceId = Guid.CreateVersion7(),
                exposureMicroUsd = 10_000 }), Start, 0, 10);
        await handler.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);
        var db = services.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.operational_alert").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM ops.consumer_inbox").SingleAsync());
    }

    private async Task ResetAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
    }

    private async Task<BillingSeed> SeedAsync(string extraPricingJson = "{}")
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var seed = new BillingSeed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var providerId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = accountId, Email = $"billing-financial-{accountId:N}@example.uz", PasswordHash = "not-a-password",
            Status = "Active", CreatedAt = Start, UpdatedAt = Start
        });
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = seed.OrganizationId, Name = "Financial tenant", Status = "Active", CreatedAt = Start
        });
        db.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = seed.ProjectId, OrganizationId = seed.OrganizationId, Name = "Production",
            Status = "Active", SettingsJson = "{}", CreatedAt = Start
        });
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = seed.ApiKeyId, ProjectId = seed.ProjectId, Name = "Production", Prefix = "123456789012",
            SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
            CreatedByAccountId = accountId, CreatedAt = Start
        });
        db.Set<CatalogProviderEntity>().Add(new CatalogProviderEntity
        {
            Id = providerId, Code = "billing-test", Name = "Test provider", Status = "Active", CreatedAt = Start
        });
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = seed.ModelId, CanonicalCode = "billing-test-model", DisplayName = "Billing test model",
            ContextLength = 100_000, MaxOutputTokens = 50_000, CapabilitiesJson = "[]",
            Status = "Active", CreatedAt = Start
        });
        db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
        {
            Id = seed.ProviderModelId, ProviderId = providerId, ModelId = seed.ModelId,
            UpstreamModelCode = "model", Status = "Active", CapabilityOverridesJson = "{}", CreatedAt = Start
        });
        db.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
        {
            Id = seed.PriceId, ProviderModelId = seed.ProviderModelId, EffectiveFrom = Start,
            InputPriceMicroUsdPerMillion = 1_000_000, OutputPriceMicroUsdPerMillion = 1_000_000,
            ExtraPricingJson = extraPricingJson, CreatedAt = Start
        });
        db.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
        {
            Id = seed.FeeId, PolicyCode = "managed", MarkupBasisPoints = 0,
            FixedFeeMicroUsd = 0, EffectiveFrom = Start.AddDays(-1), CreatedAt = Start.AddDays(-1)
        });
        await db.SaveChangesAsync();
        return seed;
    }

    private async Task CreditAsync(Guid organizationId, long amount)
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var financialStore = new PostgreSqlFinancialStore(db);
        var service = new WalletLedgerService(new PostgreSqlWalletLedgerStore(db),
            services.GetRequiredService<ITransactionCoordinator>(), new MutableFinancialClock(Start), financialStore);
        Assert.Equal(LedgerPostingStatus.Posted, (await service.PostAsync(new LedgerPostingInput(
            organizationId, LedgerEntryType.TopUp, new SignedUsdMicroAmount(amount),
            "payment", Guid.CreateVersion7()))).Status);
    }

    private static FinancialService Financial(IServiceProvider provider, TimeProvider clock)
    {
        var db = provider.GetRequiredService<FoundationDbContext>();
        return new FinancialService(new PostgreSqlFinancialStore(db), Usage(provider, clock),
            provider.GetRequiredService<ITransactionCoordinator>(), provider.GetRequiredService<ILeasedJobStore>(),
            provider.GetRequiredService<IOutboxStore>(), clock);
    }

    private static UsageService Usage(IServiceProvider provider, TimeProvider clock)
    {
        var db = provider.GetRequiredService<FoundationDbContext>();
        return new UsageService(new PostgreSqlUsageStore(db), provider.GetRequiredService<ITransactionCoordinator>(),
            provider.GetRequiredService<IOutboxStore>(), clock);
    }

    private static ManagedAdmissionInput Admission(BillingSeed seed, long amount,
        string? idempotencyKey = null, byte[]? payload = null) => new(
        new PrepareUsageRequest(seed.OrganizationId, seed.ProjectId, seed.ApiKeyId, seed.ModelId,
            false, "chat.completions", idempotencyKey, SHA256.HashData(payload ?? [1]), null),
        new UsdMicroAmount(amount), seed.FeeId, Start.AddHours(1));

    private sealed record BillingSeed(Guid OrganizationId, Guid ProjectId, Guid ApiKeyId,
        Guid ModelId, Guid ProviderModelId, Guid PriceId, Guid FeeId);

    private sealed class MutableFinancialClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
