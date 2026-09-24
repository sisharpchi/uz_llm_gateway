using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class BillingPersistenceIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Billing_migration_backfills_existing_organizations_and_trigger_creates_one_wallet_for_new_organizations()
    {
        await fixture.ResetMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync("20260924145817_AddAppendOnlyAuditTrail");
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var existingOrganizationId = await SeedOrganizationAsync(dbContext, "Existing billing tenant");

        await migrator.MigrateAsync();
        dbContext.ChangeTracker.Clear();

        var existingWallet = await dbContext.Set<BillingWalletEntity>().SingleAsync(wallet => wallet.OrganizationId == existingOrganizationId);
        Assert.Equal(0, existingWallet.PostedBalanceMicroUsd);
        Assert.Equal(0, existingWallet.ReservedBalanceMicroUsd);
        Assert.Equal(0, existingWallet.Version);

        var newOrganizationId = await SeedOrganizationAsync(dbContext, "New billing tenant");
        Assert.Equal(1, await dbContext.Set<BillingWalletEntity>().CountAsync(wallet => wallet.OrganizationId == newOrganizationId));
    }

    [Fact]
    public async Task PostAsync_updates_the_wallet_and_ledger_together_and_rejects_duplicate_references()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var organizationId = await SeedOrganizationAsync(dbContext, "Ledger tenant");
        var service = CreateWalletLedgerService(scope.ServiceProvider);
        var referenceId = Guid.CreateVersion7();

        var posted = await service.PostAsync(new LedgerPostingInput(
            organizationId,
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(1_000_000),
            "payment",
            referenceId));
        var duplicate = await service.PostAsync(new LedgerPostingInput(
            organizationId,
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(1_000_000),
            "payment",
            referenceId));

        Assert.Equal(LedgerPostingStatus.Posted, posted.Status);
        Assert.Equal(LedgerPostingStatus.Duplicate, duplicate.Status);
        var wallet = await service.GetWalletAsync(organizationId);
        Assert.NotNull(wallet);
        Assert.Equal(1_000_000, wallet.PostedBalance.Value);
        Assert.Equal(1, wallet.Version);
        Assert.Equal(1, await dbContext.Set<BillingLedgerEntryEntity>().CountAsync());
    }

    [Fact]
    public async Task Concurrent_debits_cannot_reduce_a_wallet_below_its_reserved_balance()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        var organizationId = Guid.CreateVersion7();

        await using (var writerProvider = fixture.CreateServiceProvider())
        await using (var writerScope = writerProvider.CreateAsyncScope())
        {
            var dbContext = writerScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
            {
                Id = organizationId,
                Name = "Concurrent ledger tenant",
                Status = "Active",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
            var service = CreateWalletLedgerService(writerScope.ServiceProvider);
            Assert.Equal(LedgerPostingStatus.Posted, (await service.PostAsync(new LedgerPostingInput(
                organizationId,
                LedgerEntryType.TopUp,
                new SignedUsdMicroAmount(100),
                "payment",
                Guid.CreateVersion7()))).Status);
        }

        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var first = CreateWalletLedgerService(firstScope.ServiceProvider);
        var second = CreateWalletLedgerService(secondScope.ServiceProvider);

        var outcomes = await Task.WhenAll(
            first.PostAsync(new LedgerPostingInput(organizationId, LedgerEntryType.AdjustmentDebit, new SignedUsdMicroAmount(-60), "adjustment", Guid.CreateVersion7())),
            second.PostAsync(new LedgerPostingInput(organizationId, LedgerEntryType.AdjustmentDebit, new SignedUsdMicroAmount(-60), "adjustment", Guid.CreateVersion7())));

        Assert.Single(outcomes, outcome => outcome.Status == LedgerPostingStatus.Posted);
        Assert.Single(outcomes, outcome => outcome.Status == LedgerPostingStatus.InsufficientFunds);

        await using var verifierProvider = fixture.CreateServiceProvider();
        await using var verifierScope = verifierProvider.CreateAsyncScope();
        var wallet = await CreateWalletLedgerService(verifierScope.ServiceProvider).GetWalletAsync(organizationId);
        Assert.NotNull(wallet);
        Assert.Equal(40, wallet.PostedBalance.Value);
        Assert.Equal(0, wallet.ReservedBalance.Value);
    }

    [Fact]
    public async Task Financial_history_database_triggers_reject_ledger_fee_policy_and_fx_snapshot_mutation()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var organizationId = await SeedOrganizationAsync(dbContext, "Immutable billing tenant");
        var walletService = CreateWalletLedgerService(scope.ServiceProvider);
        var posting = await walletService.PostAsync(new LedgerPostingInput(
            organizationId,
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(10),
            "payment",
            Guid.CreateVersion7()));
        var pricingService = new PricingHistoryService(new PostgreSqlPricingHistoryStore(dbContext), TimeProvider.System);
        var fee = await pricingService.AddFeePolicyVersionAsync("managed-default", 100, UsdMicroAmount.Zero, DateTimeOffset.UtcNow, null);
        var fx = await pricingService.CaptureFxRateSnapshotAsync("cbu", 1_250_000m, DateTimeOffset.UtcNow);

        var ledgerUpdate = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.ledger_entry SET amount_micro_usd = 11 WHERE id = {posting.Entry!.Id}"));
        var feeUpdate = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.fee_policy_version SET markup_basis_points = 200 WHERE id = {fee.Id}"));
        var fxDelete = await Assert.ThrowsAsync<PostgresException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM billing.fx_rate_snapshot WHERE id = {fx.Id}"));

        Assert.Equal(PostgresErrorCodes.RaiseException, ledgerUpdate.SqlState);
        Assert.Equal(PostgresErrorCodes.RaiseException, feeUpdate.SqlState);
        Assert.Equal(PostgresErrorCodes.RaiseException, fxDelete.SqlState);
    }

    [Fact]
    public async Task Ledger_post_for_an_unknown_organization_rolls_back_its_unowned_entry()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var service = CreateWalletLedgerService(scope.ServiceProvider);

        var result = await service.PostAsync(new LedgerPostingInput(
            Guid.CreateVersion7(),
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(1),
            "payment",
            Guid.CreateVersion7()));

        Assert.Equal(LedgerPostingStatus.WalletNotFound, result.Status);
        Assert.Empty(await dbContext.Set<BillingLedgerEntryEntity>().ToListAsync());
    }

    [Fact]
    public async Task Financial_database_constraints_reject_invalid_ledger_direction_and_fee_values()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();

        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var organizationId = await SeedOrganizationAsync(dbContext, "Constrained billing tenant");
        dbContext.Set<BillingLedgerEntryEntity>().Add(new BillingLedgerEntryEntity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Type = "TopUp",
            AmountMicroUsd = -1,
            ReferenceType = "payment",
            ReferenceId = Guid.CreateVersion7(),
            MetadataJson = "{}",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.ChangeTracker.Clear();
        dbContext.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
        {
            Id = Guid.CreateVersion7(),
            PolicyCode = "managed-default",
            MarkupBasisPoints = -1,
            FixedFeeMicroUsd = 0,
            EffectiveFrom = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static WalletLedgerService CreateWalletLedgerService(IServiceProvider services) =>
        new(
            new PostgreSqlWalletLedgerStore(services.GetRequiredService<FoundationDbContext>()),
            services.GetRequiredService<ITransactionCoordinator>(),
            TimeProvider.System);

    private static async Task<Guid> SeedOrganizationAsync(FoundationDbContext dbContext, string name)
    {
        var organizationId = Guid.CreateVersion7();
        dbContext.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organizationId,
            Name = name,
            Status = "Active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return organizationId;
    }
}
