using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    [Fact]
    public async Task Statement_exact_financial_fixture_is_read_only_and_preserves_budget_debt_and_hold()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        var (maker, checker, _) = await AdjustmentActorsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 1_000_000, Start.AddMonths(-1), "promotion");
        var intentId = await StatementPaymentAsync(db, seed.OrganizationId, Start);
        await StatementCreditAsync(services, seed.OrganizationId, 8_000_000, Start, "payment_intent", intentId);
        var clock = new MutableFinancialClock(Start);
        var financial = Financial(services, clock);
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId, null, new(9_000_000)));
        var charged = await financial.ReserveAsync(Admission(seed, 3_000_000));
        var usage = Usage(services, clock);
        var attempt = await usage.StartAttemptAsync(charged.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new(charged.RequestId.Value, attempt.Id, EvidenceSource.Provider,
            3_000_000, 0, 0, null, seed.PriceId, "synthetic-statement-fixture")));
        var settlement = (await financial.FinalizeAsync(charged.Reservation!.Id)).Settlement!;
        Assert.Equal(3_000_000, settlement.Charged.Value);
        var held = await financial.ReserveAsync(Admission(seed, 1_500_000, payload: [2]));
        Assert.Equal(AdmissionStatus.Reserved, held.Status);
        var reversal = await financial.ApplyConfirmedReversalAsync(seed.OrganizationId, intentId, new(8_000_000));
        Assert.Equal(4_500_000, reversal.Recovered.Value);
        Assert.Equal(3_500_000, reversal.DebtCreated.Value);
        await using (var transaction = await services.GetRequiredService<ITransactionCoordinator>().BeginAsync())
        {
            await new PostgreSqlSettlementRefundStore(db, new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db))
                .ApplyAsync(maker, settlement.Id, "statement-refund-key", new(500_000), "Synthetic settlement refund", Start);
            await transaction.CommitAsync();
        }
        var adjustment = new PostgreSqlWalletAdjustmentStore(db, new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db));
        Guid adjustmentId;
        await using (var transaction = await services.GetRequiredService<ITransactionCoordinator>().BeginAsync())
        {
            adjustmentId = (await adjustment.RequestAsync(maker, seed.OrganizationId, "statement-credit-key", new(200_000),
                "Synthetic statement credit", Start, CancellationToken.None)).Id;
            await adjustment.ApproveAsync(checker, adjustmentId, "Independent synthetic approval", Start, CancellationToken.None);
            await transaction.CommitAsync();
        }
        var before = await StatementFinancialFingerprintAsync(db);
        await using (var snapshotTx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
            var sources = (await StatementReader(db).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
            var result = StatementReconciler.Reconcile(sources);
            Assert.Empty(result.Errors);
            Assert.Equal(1_000_000, result.OpeningMicroUsd);
            Assert.Equal(500_000, result.MovementMicroUsd);
            Assert.Equal(1_500_000, result.ClosingMicroUsd);
            Assert.Equal(3_000_000, result.GrossUsageMicroUsd);
            Assert.Equal(500_000, result.RefundedUsageMicroUsd);
            Assert.Equal(2_500_000, result.NetUsageMicroUsd);
            Assert.Equal(new StatementBalance(1_500_000, 1_500_000, 0, 2_800_000, 1_500_000, 2_800_000), sources.Balance);
            Assert.Equal(8_000_000, Assert.Single(sources.Payments.Items).CreditMicroUsd);
            Assert.Equal(10_000_000, Assert.Single(sources.Payments.Items).PrincipalTiyin);
            Assert.Equal(1_250_000m, Assert.Single(sources.Payments.Items).UzsTiyinPerUsd);
            Assert.All(sources.Links, value => Assert.NotEqual(Guid.Empty, value.SourceId));
            var serialized = JsonSerializer.Serialize(sources);
            Assert.DoesNotContain("\"Reason\":", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"MetadataJson\":", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"RequestedByAccountId\":", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"ApprovedByAccountId\":", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("merchant", JsonSerializer.Serialize(sources.Payments.Items), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, await StatementFinancialFingerprintAsync(db));
            await snapshotTx.CommitAsync();
        }
        Assert.Equal(before, await StatementFinancialFingerprintAsync(db));
        var budget = Assert.Single(await db.Set<BillingBudgetBucketEntity>().AsNoTracking().ToListAsync());
        Assert.Equal(3_000_000, budget.CapturedMicroUsd);
        Assert.Equal(1_500_000, budget.ReservedMicroUsd);
        var finances = await PublicationMoneyAsync(db);
        var publication = Publication(services);
        var operation = await publication.RequestAsync(new(seed.OrganizationId, maker, new(2026, 9), Guid.NewGuid(), 0, null, "Exact financial fixture"));
        await publication.BuildAsync(operation.Id, 1);
        var version = await db.Set<BillingStatementVersionEntity>().AsNoTracking().SingleAsync();
        using var report = JsonDocument.Parse(version.JsonArtifact);
        Assert.Equal("1500000", report.RootElement.GetProperty("Balance").GetProperty("HeldMicroUsd").GetString());
        Assert.Equal("2800000", report.RootElement.GetProperty("Balance").GetProperty("RecoveryDebtMicroUsd").GetString());
        Assert.Equal("2500000", report.RootElement.GetProperty("Totals").GetProperty("NetUsageMicroUsd").GetString());
        var annotation = Assert.Single(report.RootElement.GetProperty("PaymentAnnotations").EnumerateArray());
        Assert.Equal("10000000", annotation.GetProperty("PrincipalTiyin").GetString());
        Assert.Equal("1250000", annotation.GetProperty("FrozenTiyinPerUsd").GetString());
        Assert.Equal("8000000", annotation.GetProperty("CreditMicroUsd").GetString());
        Assert.Equal(finances, await PublicationMoneyAsync(db));
    }

    [Fact]
    public async Task Statement_repeatable_read_excludes_delayed_older_uuid_commit_and_reconciles_historical_bridge()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var writerScope = provider.CreateAsyncScope();
        await using var readerScope = provider.CreateAsyncScope();
        var writer = writerScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var reader = readerScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(writerScope.ServiceProvider, seed.OrganizationId, 10, Start.AddMonths(-1), "promotion");
        await using var delayed = await writer.Database.BeginTransactionAsync();
        var olderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Assert.Equal(LedgerPostingStatus.Posted, await new PostgreSqlWalletLedgerStore(writer).TryAppendAsync(
            new(olderId, seed.OrganizationId, LedgerEntryType.PromotionalCredit, new(5), "promotion", Guid.NewGuid(), "{}", Start.AddMonths(1))));
        await using var frozen = await reader.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await reader.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        // Explicit connection/transaction barriers: writer is uncommitted when the first snapshot is acquired.
        var first = (await StatementReader(reader).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
        Assert.Single(first.Ledger);
        Assert.DoesNotContain(first.Ledger, value => value.Id == olderId);
        await delayed.CommitAsync();
        var second = (await StatementReader(reader).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
        Assert.Same(frozen, reader.Database.CurrentTransaction);
        Assert.Equal(StatementReconciler.Reconcile(first).SourceDigest, StatementReconciler.Reconcile(second).SourceDigest);
        Assert.Equal(10, second.Balance.PostedMicroUsd);
        await frozen.CommitAsync();
        await using var next = await reader.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var fresh = (await StatementReader(reader).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
        var result = StatementReconciler.Reconcile(fresh);
        Assert.Empty(result.Errors);
        Assert.Equal(2, fresh.Ledger.Count);
        Assert.Contains(fresh.Ledger, value => value.Id == olderId);
        Assert.Equal(10, result.ClosingMicroUsd);
        Assert.Equal(5, result.PostPeriodMovementMicroUsd);
        Assert.Equal(15, fresh.Balance.PostedMicroUsd);
        Assert.NotEqual(StatementReconciler.Reconcile(first).SourceDigest, result.SourceDigest);
    }

    [Fact]
    public async Task Statement_late_settlement_and_refund_use_posting_month_not_request_analytics()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 100_000, Start, "promotion");
        var clock = new MutableFinancialClock(Start);
        var finance = Financial(services, clock);
        var admission = await finance.ReserveAsync(Admission(seed, 30_000));
        var usage = Usage(services, clock);
        var attempt = await usage.StartAttemptAsync(admission.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new(admission.RequestId.Value, attempt.Id, EvidenceSource.Provider,
            50_000, 0, 0, null, seed.PriceId, "capped-statement")));
        clock.UtcNow = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var settlement = (await finance.FinalizeAsync(admission.Reservation!.Id)).Settlement!;
        Assert.Equal(50_000, settlement.ProviderCost.Value);
        Assert.Equal(30_000, settlement.Charged.Value);
        var actor = await db.Set<IdentityAccountEntity>().Select(value => value.Id).SingleAsync();
        await using (var tx = await services.GetRequiredService<ITransactionCoordinator>().BeginAsync())
        {
            await new PostgreSqlSettlementRefundStore(db, new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db))
                .ApplyAsync(actor, settlement.Id, "late-statement-refund", new(5_000), "Synthetic later-month refund",
                    new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
            await tx.CommitAsync();
        }
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var reader = StatementReader(db);
        var september = StatementReconciler.Reconcile((await reader.ReadAsync(seed.OrganizationId, new(2026, 9)))!);
        var octoberSources = (await reader.ReadAsync(seed.OrganizationId, new(2026, 10)))!;
        var october = StatementReconciler.Reconcile(octoberSources);
        var november = StatementReconciler.Reconcile((await reader.ReadAsync(seed.OrganizationId, new(2026, 11)))!);
        Assert.Empty(september.Errors); Assert.Empty(october.Errors); Assert.Empty(november.Errors);
        Assert.Equal(0, september.GrossUsageMicroUsd);
        Assert.Equal(30_000, october.GrossUsageMicroUsd);
        Assert.Equal(0, october.RefundedUsageMicroUsd);
        Assert.Equal(-5_000, november.NetUsageMicroUsd);
        Assert.Equal(70_000, october.ClosingMicroUsd);
        Assert.Equal(5_000, october.PostPeriodMovementMicroUsd);
        Assert.Equal(Start, octoberSources.Links.Single(value => value.Kind == "settlement").RequestStartedAt);
    }

    [Fact]
    public async Task Statement_payment_port_is_tenant_safe_and_pending_missing_links_never_mutate_finances()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        var intentId = await StatementPaymentAsync(db, seed.OrganizationId, Start);
        await StatementCreditAsync(services, seed.OrganizationId, 8_000_000, Start, "payment_intent", intentId);
        var clock = new MutableFinancialClock(Start);
        var finance = Financial(services, clock);
        var admission = await finance.ReserveAsync(Admission(seed, 30_000));
        var usage = Usage(services, clock);
        var attempt = await usage.StartAttemptAsync(admission.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordUnknownAsync(admission.RequestId.Value, attempt.Id));
        Assert.Equal(FinalizationStatus.PendingEvidence, (await finance.FinalizeAsync(admission.Reservation!.Id)).Status);
        await db.Set<UsageRequestEntity>().Where(value => value.Id == admission.RequestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FinancialState, "PendingSettlement"));
        db.Set<PaymentReconciliationCaseEntity>().Add(new() { Id = Guid.NewGuid(), IntentId = intentId,
            Reason = "amount_mismatch", Status = "Open", CreatedAt = Start });
        await db.SaveChangesAsync();
        var publication = Publication(services);
        var command = await PublicationCommandAsync(db, seed.OrganizationId);
        var operation = await publication.RequestAsync(command);
        await publication.BuildAsync(operation.Id, 1);
        var published = await db.Set<BillingStatementVersionEntity>().AsNoTracking().SingleAsync();
        using (var document = JsonDocument.Parse(published.JsonArtifact))
        {
            var pending = document.RootElement.GetProperty("Pending");
            Assert.Equal(1, pending.GetProperty("Evidence").GetInt32());
            Assert.Equal(1, pending.GetProperty("Settlement").GetInt32());
            Assert.Equal(1, pending.GetProperty("MerchantMismatch").GetInt32());
        }
        await StatementPaymentAsync(db, seed.OrganizationId, Start.AddMinutes(1)); // Paid source without a posting.
        // Synthetic corrupt link, but wallet arithmetic remains internally balanced.
        var ledger = new WalletLedgerService(new PostgreSqlWalletLedgerStore(db), services.GetRequiredService<ITransactionCoordinator>(), clock);
        Assert.Equal(LedgerPostingStatus.Posted, (await ledger.PostAsync(new(seed.OrganizationId, LedgerEntryType.Refund,
            new(1), "settlement_refund", Guid.NewGuid()))).Status);
        var before = await StatementFinancialFingerprintAsync(db);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var port = new PostgreSqlStatementPaymentEvidenceReader(db);
        var foreign = await port.ReadAsync(Guid.NewGuid(), [intentId]);
        Assert.Empty(foreign.Items); Assert.Equal(0, foreign.OpenMismatchCount);
        Assert.Equal(0, foreign.UnmatchedPaidCount);
        Assert.Null(await StatementReader(db).ReadAsync(Guid.NewGuid(), new(2026, 9)));
        var sources = (await StatementReader(db).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
        Assert.Single(sources.Payments.Items);
        var result = StatementReconciler.Reconcile(sources);
        Assert.False(result.FinanciallyReconciled);
        Assert.Contains("missing_or_mismatched_financial_link", result.Errors);
        Assert.Equal(new StatementPending(1, 1, 1, 1), result.Pending);
        Assert.Contains("payment_without_posting", result.Errors);
        Assert.Null(Assert.Single(sources.Usage).InputTokens);
        Assert.Null(Assert.Single(sources.Usage).PriceVersionId);
        Assert.Equal(0, result.GrossUsageMicroUsd);
        Assert.Equal(before, await StatementFinancialFingerprintAsync(db));
        await tx.CommitAsync();
        var finances = await PublicationMoneyAsync(db);
        var revision = await publication.RequestAsync(command with { IdempotencyKey = Guid.NewGuid(),
            ExpectedGeneration = 1, PredecessorId = published.Id, Reason = "Reject missing financial links" });
        Assert.Equal("Sources", (await Assert.ThrowsAsync<StatementBuildRejectedException>(() => publication.BuildAsync(revision.Id, 1))).Code);
        Assert.Single(await db.Set<BillingStatementVersionEntity>().ToListAsync());
        Assert.Equal(finances, await PublicationMoneyAsync(db));
    }

    [Fact]
    public async Task Statement_source_limit_rejects_overflow_without_truncating_or_mutating()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Disposable PG fixture only: pin the last supported and first unsupported membership sizes.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing.ledger_entry (id, organization_id, type, amount_micro_usd,
                reference_type, reference_id, metadata_json, occurred_at)
            SELECT gen_random_uuid(), {seed.OrganizationId}, 'PromotionalCredit', 1,
                'promotion', gen_random_uuid(), jsonb_build_object(), {Start}
            FROM generate_series(1, 100000)
            """);
        await db.Set<BillingWalletEntity>().Where(value => value.OrganizationId == seed.OrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PostedBalanceMicroUsd, 100_000));
        await using (var accepted = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
            var sources = (await StatementReader(db).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
            Assert.Equal(100_000, sources.Ledger.Count);
            var result = StatementReconciler.Reconcile(sources);
            Assert.Empty(result.Errors);
            Assert.Equal(100_000, result.ClosingMicroUsd);
            await accepted.CommitAsync();
        }
        await StatementCreditAsync(scope.ServiceProvider, seed.OrganizationId, 1, Start, "promotion");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => StatementReader(db).ReadAsync(seed.OrganizationId, new(2026, 9)));
        Assert.Contains("source limit exceeded", failure.Message);
        Assert.Equal(100_001, await db.Set<BillingLedgerEntryEntity>().CountAsync());
        Assert.Equal(100_001, await db.Set<BillingWalletEntity>().Select(value => value.PostedBalanceMicroUsd).SingleAsync());
    }

    [Fact]
    public async Task Statement_byok_reports_only_wallet_fee_not_external_provider_spend()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        var credential = await SeedByokAsync(seed, 100_000);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FoundationDbContext>();
        await StatementCreditAsync(services, seed.OrganizationId, 10_000, Start, "promotion");
        var clock = new MutableFinancialClock(Start);
        var finance = Financial(services, clock);
        var admission = await finance.ReserveByokAsync(ByokAdmission(seed, credential, 2_000, 20_000, chargeByokFee: true));
        Assert.Equal(AdmissionStatus.Reserved, admission.Status);
        var usage = Usage(services, clock);
        var attempt = await usage.StartAttemptAsync(admission.RequestId!.Value, seed.ProviderModelId, credential);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new(admission.RequestId.Value, attempt.Id,
            EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, "byok-statement")));
        var settlement = (await finance.FinalizeAsync(admission.Reservation!.Id)).Settlement!;
        Assert.Equal(10_000, settlement.ExternalProviderSpend.Value);
        Assert.Equal(1_000, settlement.Charged.Value);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var sources = (await StatementReader(db).ReadAsync(seed.OrganizationId, new(2026, 9)))!;
        var result = StatementReconciler.Reconcile(sources);
        Assert.Empty(result.Errors);
        Assert.Equal(1_000, result.GrossUsageMicroUsd);
        Assert.Equal(9_000, result.ClosingMicroUsd);
        Assert.Equal(seed.ByokFeeId, sources.Links.Single(value => value.Kind == "settlement").ByokFeePolicyVersionId);
    }

    [Fact]
    public async Task Statement_readers_require_caller_repeatable_read_and_honor_cancellation_and_bounds()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var reader = StatementReader(db);
        var port = new PostgreSqlStatementPaymentEvidenceReader(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(seed.OrganizationId, new(2026, 9)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadAsync(seed.OrganizationId, []));
        await using (await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(seed.OrganizationId, new(2026, 9)));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(seed.OrganizationId, new(2026, 9), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.ReadAsync(seed.OrganizationId, [], cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => port.ReadAsync(seed.OrganizationId, new Guid[100_001]));
    }

    private static PostgreSqlStatementSourceReader StatementReader(FoundationDbContext db) =>
        new(db, new PostgreSqlStatementPaymentEvidenceReader(db));

    private static async Task StatementCreditAsync(IServiceProvider services, Guid org, long amount, DateTimeOffset at,
        string reference, Guid? referenceId = null)
    {
        var db = services.GetRequiredService<FoundationDbContext>();
        var ledger = new WalletLedgerService(new PostgreSqlWalletLedgerStore(db), services.GetRequiredService<ITransactionCoordinator>(),
            new MutableFinancialClock(at));
        Assert.Equal(LedgerPostingStatus.Posted, (await ledger.PostAsync(new(org,
            reference == "promotion" ? LedgerEntryType.PromotionalCredit : LedgerEntryType.TopUp,
            new(amount), reference, referenceId ?? Guid.NewGuid()))).Status);
    }

    private static async Task<Guid> StatementPaymentAsync(FoundationDbContext db, Guid org, DateTimeOffset at)
    {
        var fx = Guid.NewGuid(); var quote = Guid.NewGuid(); var intent = Guid.NewGuid();
        db.Set<BillingFxRateSnapshotEntity>().Add(new() { Id = fx, Source = "synthetic-reporting-fixture",
            UzsTiyinPerUsd = 1_250_000m, ObservedAt = at });
        db.Set<PaymentQuoteEntity>().Add(new() { Id = quote, OrganizationId = org, Provider = "Click",
            AmountTiyin = 10_000_000, FeeTiyin = 0, CreditMicroUsd = 8_000_000, FxSnapshotId = fx,
            UzsTiyinPerUsd = 1_250_000m, CreatedAt = at, ExpiresAt = at.AddMinutes(15) });
        db.Set<PaymentIntentEntity>().Add(new() { Id = intent, OrganizationId = org, QuoteId = quote, Provider = "Click",
            Status = "Paid", AmountTiyin = 10_000_000, FeeTiyin = 0, CreditMicroUsd = 8_000_000, FxSnapshotId = fx,
            UzsTiyinPerUsd = 1_250_000m, IdempotencyKey = intent.ToString(), MerchantScope = "synthetic-merchant-reference",
            CreatedAt = at, PaidAt = at });
        await db.SaveChangesAsync();
        return intent;
    }

    private static async Task<string> StatementFinancialFingerprintAsync(FoundationDbContext db)
    {
        return await db.Database.SqlQueryRaw<string>("""
            SELECT md5(COALESCE(string_agg(source, '|' ORDER BY source), '')) AS "Value" FROM (
              SELECT 'wallet:' || row::text AS source FROM billing.wallet row
              UNION ALL SELECT 'ledger:' || row::text FROM billing.ledger_entry row
              UNION ALL SELECT 'reservation:' || row::text FROM billing.reservation row
              UNION ALL SELECT 'settlement:' || row::text FROM billing.settlement row
              UNION ALL SELECT 'refund:' || row::text FROM billing.settlement_refund row
              UNION ALL SELECT 'budget:' || row::text FROM billing.budget_bucket row
              UNION ALL SELECT 'reversal:' || row::text FROM billing.reversal row
              UNION ALL SELECT 'debt_entry:' || row::text FROM billing.debt_entry row
              UNION ALL SELECT 'debt:' || row::text FROM billing.recovery_debt row
              UNION ALL SELECT 'adjustment:' || row::text FROM billing.wallet_adjustment row
              UNION ALL SELECT 'approval:' || row::text FROM billing.wallet_adjustment_approval row
              UNION ALL SELECT 'intent:' || row::text FROM payment.payment_intent row
              UNION ALL SELECT 'case:' || row::text FROM payment.reconciliation_case row
              UNION ALL SELECT 'request:' || row::text FROM usage.request row
              UNION ALL SELECT 'evidence:' || row::text FROM usage.evidence row
              UNION ALL SELECT 'outbox:' || row::text FROM ops.outbox row
              UNION ALL SELECT 'audit:' || row::text FROM audit.audit_event row
            ) sources
            """).SingleAsync();
    }
}
