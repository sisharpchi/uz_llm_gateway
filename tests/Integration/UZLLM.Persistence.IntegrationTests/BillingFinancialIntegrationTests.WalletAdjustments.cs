using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Management.Api.Administration;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    [Fact]
    public async Task WalletAdjustment_different_checkers_race_and_cancelled_audit_never_duplicate_credit()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100);
        var (maker, checker, otherChecker) = await AdjustmentActorsAsync();
        var request = await AdjustmentRequestAsync(maker, new(seed.OrganizationId, "checker-race-key", "1", "Documented service credit"));
        async Task<AdminWalletAdjustmentResponse?> TryApproveAsync(Guid actor)
        {
            try { return await AdjustmentApproveAsync(actor, request.Id); }
            catch (InvalidOperationException) { return null; }
        }
        var outcomes = await Task.WhenAll(TryApproveAsync(checker), TryApproveAsync(otherChecker));
        var approved = Assert.Single(outcomes, value => value is not null)!;
        Assert.Single(outcomes, value => value is null);
        Assert.Contains(approved.Approval!.ApprovedByAccountId, new[] { checker, otherChecker });
        Assert.Equal(101, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        var pending = await AdjustmentRequestAsync(maker, new(seed.OrganizationId, "cancelled-credit-key", "1", "Documented service credit"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => AdjustmentApproveAsync(checker, pending.Id, audit: new CancelledAdjustmentAudit()));
        Assert.Equal(101, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Null((await AdjustmentService(scope.ServiceProvider).FindAsync(pending.Id, CancellationToken.None))!.Approval);
        Assert.Single(await db.Set<BillingWalletAdjustmentApprovalEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingLedgerEntryEntity>().Where(value => value.ReferenceType == "wallet_adjustment").ToListAsync());
    }

    [Fact]
    public async Task WalletAdjustment_parallel_requests_and_approvals_post_once_with_immutable_terms()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        var (maker, checker, otherChecker) = await AdjustmentActorsAsync();
        var input = new CreateWalletAdjustmentRequest(seed.OrganizationId, "support-credit-1", "6000", "Documented service credit");
        var requests = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AdjustmentRequestAsync(maker, input)));
        var request = Assert.Single(requests, value => !value.Duplicate);
        Assert.All(requests, value => Assert.Equal(request.Id, value.Id));
        Assert.All(requests, value => Assert.Null(value.Approval));
        Assert.Equal(100_000, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => AdjustmentApproveAsync(maker, request.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentRequestAsync(checker, input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentRequestAsync(maker, input with { AmountMicroUsd = "6001" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentRequestAsync(maker, input with { Reason = "Different credit terms" }));

        var approvals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AdjustmentApproveAsync(checker, request.Id)));
        Assert.Single(approvals, value => !value.Duplicate);
        Assert.All(approvals, value =>
        {
            Assert.Equal("Approved", value.Status);
            Assert.Equal(checker, value.Approval!.ApprovedByAccountId);
            Assert.Equal("6000", value.AmountMicroUsd);
        });
        Assert.True((await AdjustmentRequestAsync(maker, input)).Duplicate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentApproveAsync(otherChecker, request.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentApproveAsync(checker, request.Id, "Changed approval reason"));
        Assert.Equal(106_000, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.Set<BillingWalletAdjustmentEntity>().ToListAsync());
        Assert.Single(await db.Set<BillingWalletAdjustmentApprovalEntity>().ToListAsync());
        var credit = Assert.Single(await db.Set<BillingLedgerEntryEntity>().Where(value => value.ReferenceType == "wallet_adjustment").ToListAsync());
        Assert.Equal("AdjustmentCredit", credit.Type);
        Assert.Equal(request.Id, credit.ReferenceId);
        Assert.Equal(6_000, credit.AmountMicroUsd);
        var audit = await db.Set<AuditEventEntity>().Where(value => value.ResourceId == request.Id).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, value => value.Action == "wallet-adjustment.requested" && value.ActorAccountId == maker && value.MetadataJson.Contains(input.Reason));
        Assert.Contains(audit, value => value.Action == "wallet-adjustment.approved" && value.ActorAccountId == checker && value.MetadataJson.Contains("Independent credit approval"));
        Assert.Empty(await db.Set<BillingSettlementRefundEntity>().ToListAsync());
        Assert.Empty(await db.Set<UsageRequestEntity>().ToListAsync());
        foreach (var sql in new[]
        {
            "UPDATE billing.wallet_adjustment SET amount_micro_usd = 1",
            "DELETE FROM billing.wallet_adjustment",
            "UPDATE billing.wallet_adjustment_approval SET reason = 'rewritten approval'",
            "DELETE FROM billing.wallet_adjustment_approval",
            "UPDATE billing.ledger_entry SET amount_micro_usd = 1 WHERE reference_type = 'wallet_adjustment'"
        })
            Assert.Equal(PostgresErrorCodes.RaiseException,
                (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Contains("refuse to discard wallet adjustment history",
            (await Assert.ThrowsAsync<PostgresException>(() => scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>()
                .MigrateAsync("20261017010000_AddProviderDataPolicies"))).MessageText);
        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var command = runtime.CreateCommand();
        command.CommandText = "TRUNCATE billing.wallet_adjustment_approval, billing.wallet_adjustment";
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
            (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task WalletAdjustment_audit_failure_rolls_back_request_approval_and_credit()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        var (maker, checker, _) = await AdjustmentActorsAsync();
        var input = new CreateWalletAdjustmentRequest(seed.OrganizationId, "audit-credit-key", "6000", "Documented service credit");
        await Assert.ThrowsAsync<IOException>(() => AdjustmentRequestAsync(maker, input, new FailingAdjustmentAudit()));
        var request = await AdjustmentRequestAsync(maker, input);
        Assert.False(request.Duplicate);
        await Assert.ThrowsAsync<IOException>(() => AdjustmentApproveAsync(checker, request.Id, audit: new FailingAdjustmentAudit()));
        Assert.Equal(100_000, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        await using (var provider = fixture.CreateServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Null((await AdjustmentService(scope.ServiceProvider).FindAsync(request.Id, CancellationToken.None))!.Approval);
            Assert.Empty(await db.Set<BillingWalletAdjustmentApprovalEntity>().ToListAsync());
            Assert.Empty(await db.Set<BillingLedgerEntryEntity>().Where(value => value.ReferenceType == "wallet_adjustment").ToListAsync());
            Assert.Single(await db.Set<AuditEventEntity>().Where(value => value.ResourceId == request.Id).ToListAsync());
            await using (await scope.ServiceProvider.GetRequiredService<ITransactionCoordinator>().BeginAsync())
                await new PostgreSqlWalletAdjustmentStore(db, new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db))
                    .ApproveAsync(checker, request.Id, "Uncommitted approval", DateTimeOffset.UtcNow, CancellationToken.None);
        }
        Assert.Equal(100_000, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        Assert.False((await AdjustmentApproveAsync(checker, request.Id)).Duplicate);
        Assert.Equal(106_000, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
    }

    [Fact]
    public async Task WalletAdjustment_recovers_debt_preserves_hold_and_does_not_restore_spend_budget()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, 100_000);
        var (maker, checker, _) = await AdjustmentActorsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var financial = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
        Assert.NotNull(await financial.SetBudgetAsync(seed.OrganizationId, seed.ProjectId, null, new UsdMicroAmount(100_000)));
        var billed = await financial.ReserveAsync(Admission(seed, 10_000));
        var usage = Usage(scope.ServiceProvider, new MutableFinancialClock(Start));
        var attempt = await usage.StartAttemptAsync(billed.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        Assert.NotNull(await usage.RecordVerifiedAsync(new VerifiedUsageInput(billed.RequestId.Value,
            attempt.Id, EvidenceSource.Provider, 10_000, 0, 0, null, seed.PriceId, null)));
        Assert.Equal(FinalizationStatus.Settled, (await financial.FinalizeAsync(billed.Reservation!.Id)).Status);
        var held = await financial.ReserveAsync(Admission(seed, 80_000));
        Assert.Equal(AdmissionStatus.Reserved, held.Status);
        var reversed = await financial.ApplyConfirmedReversalAsync(seed.OrganizationId, Guid.CreateVersion7(), new UsdMicroAmount(50_000));
        Assert.Equal(40_000, reversed.DebtCreated.Value);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var buckets = await db.Set<BillingBudgetBucketEntity>().AsNoTracking().ToListAsync();
        Assert.Equal(10_000, Assert.Single(buckets).CapturedMicroUsd);
        Assert.Equal(80_000, Assert.Single(buckets).ReservedMicroUsd);
        var request = await AdjustmentRequestAsync(maker, new(seed.OrganizationId, "reversal-credit-key", "6000", "Documented service credit"));
        await AdjustmentApproveAsync(checker, request.Id);
        var state = await AdjustmentWalletAsync(seed.OrganizationId);
        Assert.Equal(80_000, state.Wallet.PostedBalance.Value);
        Assert.Equal(80_000, state.Wallet.ReservedBalance.Value);
        Assert.Equal(34_000, state.RecoveryDebt.Value);
        Assert.True(state.SpendingHeld);
        Assert.Equal(buckets.Select(value => (value.PolicyId, value.WindowStart, value.CapturedMicroUsd, value.ReservedMicroUsd)),
            (await db.Set<BillingBudgetBucketEntity>().AsNoTracking().ToListAsync())
                .Select(value => (value.PolicyId, value.WindowStart, value.CapturedMicroUsd, value.ReservedMicroUsd)));
        var recovered = Assert.Single(await db.Set<BillingDebtEntryEntity>().Where(value => value.ReferenceId == request.Id).ToListAsync());
        Assert.Equal("Recovered", recovered.Type);
        Assert.Equal(-6_000, recovered.AmountMicroUsd);
        Assert.Equal(FinalizationStatus.Released, (await financial.ReleaseUndispatchedAsync(held.Reservation!.Id)).Status);
        state = await AdjustmentWalletAsync(seed.OrganizationId);
        Assert.Equal(46_000, state.Wallet.PostedBalance.Value);
        Assert.Equal(0, state.RecoveryDebt.Value);
        Assert.False(state.SpendingHeld);
    }

    [Fact]
    public async Task WalletAdjustment_organization_keys_are_isolated_and_overflow_cannot_post()
    {
        await ResetAsync();
        var seed = await SeedAsync();
        await CreditAsync(seed.OrganizationId, long.MaxValue);
        var (maker, checker, _) = await AdjustmentActorsAsync();
        var input = new CreateWalletAdjustmentRequest(seed.OrganizationId, "organization-key", "1", "Documented service credit");
        var request = await AdjustmentRequestAsync(maker, input);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdjustmentApproveAsync(checker, request.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => AdjustmentRequestAsync(maker, input with { OrganizationId = Guid.NewGuid() }));
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var otherOrg = Guid.NewGuid();
        db.Set<OrganizationEntity>().Add(new() { Id = otherOrg, Name = "Other tenant", Status = "Active", CreatedAt = Start });
        await db.SaveChangesAsync();
        await CreditAsync(otherOrg, 100);
        var other = await AdjustmentRequestAsync(maker, input with { OrganizationId = otherOrg });
        Assert.NotEqual(request.Id, other.Id);
        await AdjustmentApproveAsync(checker, other.Id);
        Assert.Equal(long.MaxValue, (await AdjustmentWalletAsync(seed.OrganizationId)).Wallet.PostedBalance.Value);
        Assert.Equal(101, (await AdjustmentWalletAsync(otherOrg)).Wallet.PostedBalance.Value);
        var service = AdjustmentService(scope.ServiceProvider);
        Assert.Equal(request.Id, Assert.Single(await service.ListAsync(seed.OrganizationId, 50, CancellationToken.None)).Id);
        Assert.Equal(other.Id, Assert.Single(await service.ListAsync(otherOrg, 50, CancellationToken.None)).Id);
        Assert.Null((await service.FindAsync(request.Id, CancellationToken.None))!.Approval);
        foreach (var invalid in new[] { "0", "-1", "1.0", "1e3", "9223372036854775808", " 1" })
            await Assert.ThrowsAsync<ArgumentException>(() => AdjustmentRequestAsync(maker, input with { AmountMicroUsd = invalid }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(null, 101, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(null, 0, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => AdjustmentApproveAsync(checker, Guid.NewGuid()));
        // Composite FK prevents binding an approval to a request in another organization.
        var badApproval = new BillingWalletAdjustmentApprovalEntity { Id = Guid.NewGuid(), RequestId = request.Id,
            OrganizationId = otherOrg, RequestedByAccountId = maker, ApprovedByAccountId = checker,
            Reason = "Invalid tenant binding", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<BillingWalletAdjustmentApprovalEntity>().Add(badApproval);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,
            (await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException is PostgresException pg ? pg.SqlState : null);
        db.ChangeTracker.Clear();
        badApproval.Id = Guid.NewGuid(); badApproval.OrganizationId = seed.OrganizationId; badApproval.ApprovedByAccountId = maker;
        db.Set<BillingWalletAdjustmentApprovalEntity>().Add(badApproval);
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException is PostgresException self ? self.SqlState : null);
        db.ChangeTracker.Clear();
        var unicodeTerms = string.Concat(Enumerable.Repeat("😀", 8));
        var unicode = await AdjustmentRequestAsync(maker, new(otherOrg, unicodeTerms, "1", unicodeTerms));
        Assert.Equal(unicodeTerms, unicode.AdjustmentKey);
        Assert.Equal(unicodeTerms, unicode.Reason);
        Assert.Equal("Pending", unicode.Status);
    }

    private async Task<(Guid Maker, Guid Checker, Guid OtherChecker)> AdjustmentActorsAsync()
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
            db.Set<IdentityAccountEntity>().Add(new() { Id = id, Email = $"adjustment-{id:N}@example.uz",
                PasswordHash = "test-only", Status = "Active", CreatedAt = Start, UpdatedAt = Start });
        await db.SaveChangesAsync();
        return (ids[0], ids[1], ids[2]);
    }

    private async Task<AdminWalletAdjustmentResponse> AdjustmentRequestAsync(Guid actor, CreateWalletAdjustmentRequest input, IAuditTrail? audit = null)
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await AdjustmentService(scope.ServiceProvider, audit).RequestAsync(actor, input, CancellationToken.None);
    }

    private async Task<AdminWalletAdjustmentResponse> AdjustmentApproveAsync(Guid actor, Guid id,
        string reason = "Independent credit approval", IAuditTrail? audit = null)
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await AdjustmentService(scope.ServiceProvider, audit).ApproveAsync(actor, id, new(reason), CancellationToken.None);
    }

    private async Task<FinancialWalletState> AdjustmentWalletAsync(Guid organizationId)
    {
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return (await Financial(scope.ServiceProvider, new MutableFinancialClock(Start)).GetWalletStateAsync(organizationId))!;
    }

    private static WalletAdjustmentAdminService AdjustmentService(IServiceProvider services, IAuditTrail? audit = null)
    {
        var db = services.GetRequiredService<FoundationDbContext>();
        return new(new PostgreSqlWalletAdjustmentStore(db, new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db)),
            audit ?? new AuditTrail(new PostgreSqlAuditEventStore(db), TimeProvider.System),
            services.GetRequiredService<ITransactionCoordinator>(), TimeProvider.System);
    }

    private sealed class FailingAdjustmentAudit : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new IOException("Injected audit write failure");
    }

    private sealed class CancelledAdjustmentAudit : IAuditTrail
    {
        public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException("Injected cancellation after posting before commit");
    }
}
