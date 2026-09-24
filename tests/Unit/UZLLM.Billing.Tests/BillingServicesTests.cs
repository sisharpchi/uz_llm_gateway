using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Billing.Tests;

public sealed class BillingServicesTests
{
    [Fact]
    public void Money_types_reject_negative_or_zero_values_and_wallet_exposes_available_balance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UsdMicroAmount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UzsTiyinAmount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignedUsdMicroAmount(0));

        var wallet = new Wallet(Guid.CreateVersion7(), new UsdMicroAmount(900), new UsdMicroAmount(250), 3);

        Assert.Equal(650, wallet.AvailableBalance.Value);
    }

    [Fact]
    public async Task PostAsync_records_a_signed_ledger_entry_and_returns_the_updated_wallet()
    {
        var organizationId = Guid.CreateVersion7();
        var store = new InMemoryWalletLedgerStore(new Wallet(organizationId, UsdMicroAmount.Zero, UsdMicroAmount.Zero, 0));
        var service = new WalletLedgerService(store, new InMemoryTransactionCoordinator(), new FixedBillingTimeProvider());

        var result = await service.PostAsync(new LedgerPostingInput(
            organizationId,
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(1_000_000),
            "payment",
            Guid.CreateVersion7(),
            "{\"provider\":\"test\"}"));

        Assert.Equal(LedgerPostingStatus.Posted, result.Status);
        Assert.NotNull(result.Entry);
        Assert.Equal(1_000_000, result.Entry.Amount.Value);
        Assert.Equal(1_000_000, result.Wallet!.PostedBalance.Value);
        Assert.Equal(1, result.Wallet.Version);
        Assert.Equal(result.Entry, Assert.Single(store.Entries));
    }

    [Theory]
    [InlineData(LedgerEntryType.TopUp, -1)]
    [InlineData(LedgerEntryType.UsageCharge, 1)]
    public async Task PostAsync_rejects_ledger_amounts_with_the_wrong_direction(LedgerEntryType type, long amount)
    {
        var organizationId = Guid.CreateVersion7();
        var service = new WalletLedgerService(
            new InMemoryWalletLedgerStore(new Wallet(organizationId, UsdMicroAmount.Zero, UsdMicroAmount.Zero, 0)),
            new InMemoryTransactionCoordinator(),
            new FixedBillingTimeProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => service.PostAsync(new LedgerPostingInput(
            organizationId,
            type,
            new SignedUsdMicroAmount(amount),
            "usage",
            Guid.CreateVersion7())));
    }

    [Fact]
    public async Task PostAsync_returns_duplicate_without_changing_the_wallet()
    {
        var organizationId = Guid.CreateVersion7();
        var store = new InMemoryWalletLedgerStore(new Wallet(organizationId, new UsdMicroAmount(10), UsdMicroAmount.Zero, 0))
        {
            NextStatus = LedgerPostingStatus.Duplicate
        };
        var service = new WalletLedgerService(store, new InMemoryTransactionCoordinator(), new FixedBillingTimeProvider());

        var result = await service.PostAsync(new LedgerPostingInput(
            organizationId,
            LedgerEntryType.TopUp,
            new SignedUsdMicroAmount(1),
            "payment",
            Guid.CreateVersion7()));

        Assert.Equal(LedgerPostingStatus.Duplicate, result.Status);
        Assert.Null(result.Entry);
        Assert.Null(result.Wallet);
        Assert.Empty(store.Entries);
        Assert.Equal(10, store.Wallet.PostedBalance.Value);
    }

    [Fact]
    public async Task Pricing_history_creates_immutable_value_snapshots_and_rounds_markup_once()
    {
        var store = new InMemoryPricingHistoryStore();
        var service = new PricingHistoryService(store, new FixedBillingTimeProvider());
        var effectiveFrom = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var policy = await service.AddFeePolicyVersionAsync(" managed-default ", 250, new UsdMicroAmount(2), effectiveFrom, null);
        var snapshot = await service.CaptureFxRateSnapshotAsync(" cbu ", 1_250_000.125m, effectiveFrom);

        Assert.Equal("managed-default", policy.PolicyCode);
        Assert.Equal(105, policy.CalculateCustomerCharge(new UsdMicroAmount(100)).Value);
        Assert.Equal("cbu", snapshot.Source);
        Assert.Equal(1_250_000.125m, snapshot.UzsTiyinPerUsd);
        Assert.Equal(policy, Assert.Single(store.Policies));
        Assert.Equal(snapshot, Assert.Single(store.Snapshots));
    }

    [Fact]
    public async Task Pricing_history_rejects_invalid_effective_ranges_and_non_fixed_precision_fx()
    {
        var service = new PricingHistoryService(new InMemoryPricingHistoryStore(), new FixedBillingTimeProvider());
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddFeePolicyVersionAsync("managed", 0, UsdMicroAmount.Zero, at, at));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CaptureFxRateSnapshotAsync("cbu", 1.123456789m, at));
    }
}

internal sealed class InMemoryWalletLedgerStore(Wallet wallet) : IWalletLedgerStore
{
    public Wallet Wallet { get; private set; } = wallet;

    public List<LedgerEntry> Entries { get; } = [];

    public LedgerPostingStatus NextStatus { get; init; } = LedgerPostingStatus.Posted;

    public Task<Wallet?> FindWalletAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Wallet?>(Wallet.OrganizationId == organizationId ? Wallet : null);

    public Task<LedgerPostingStatus> TryAppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        if (NextStatus is not LedgerPostingStatus.Posted)
        {
            return Task.FromResult(NextStatus);
        }

        Wallet = Wallet with
        {
            PostedBalance = new UsdMicroAmount(checked(Wallet.PostedBalance.Value + entry.Amount.Value)),
            Version = Wallet.Version + 1
        };
        Entries.Add(entry);
        return Task.FromResult(LedgerPostingStatus.Posted);
    }
}

internal sealed class InMemoryPricingHistoryStore : IPricingHistoryStore
{
    public List<FeePolicyVersion> Policies { get; } = [];

    public List<FxRateSnapshot> Snapshots { get; } = [];

    public Task AppendFeePolicyVersionAsync(FeePolicyVersion version, CancellationToken cancellationToken = default)
    {
        Policies.Add(version);
        return Task.CompletedTask;
    }

    public Task AppendFxRateSnapshotAsync(FxRateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTransactionCoordinator : ITransactionCoordinator
{
    public Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ITransactionScope>(new InMemoryTransactionScope());
}

internal sealed class InMemoryTransactionScope : ITransactionScope
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FixedBillingTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 15, 30, 0, TimeSpan.Zero);
}
