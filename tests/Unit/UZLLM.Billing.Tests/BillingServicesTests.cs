using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Persistence;

namespace UZLLM.Billing.Tests;

public sealed class BillingServicesTests
{
    [Fact]
    public void Byok_cost_tracks_external_provider_spend_but_charges_only_bounded_platform_fee()
    {
        var at = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var evidence = new PricedUsageEvidence(Guid.NewGuid(), Guid.NewGuid(), 10_000, 0,
            0, null, 1_000_000, 0, null, "{}", at, at, null);
        var free = new FeePolicyVersion(Guid.NewGuid(), "byok-free", 0,
            UsdMicroAmount.Zero, at, null, at);
        var paid = free with { MarkupBasisPoints = 1_000,
            FixedFee = new UsdMicroAmount(100) };

        var freeCharge = RequestCostCalculator.CalculateByok([evidence], free,
            UsdMicroAmount.Zero);
        Assert.Equal(10_000, freeCharge.ProviderCost.Value);
        Assert.Equal(10_000, freeCharge.ExternalProviderSpend.Value);
        Assert.Equal(0, freeCharge.Charged.Value);
        Assert.Equal(0, freeCharge.PlatformExposure.Value);

        var paidCharge = RequestCostCalculator.CalculateByok([evidence], paid,
            new UsdMicroAmount(1_050));
        Assert.Equal(1_100, paidCharge.UncappedCustomerCharge.Value);
        Assert.Equal(1_050, paidCharge.Charged.Value);
        Assert.Equal(50, paidCharge.UncollectedCharge.Value);
        Assert.Equal(0, paidCharge.PlatformExposure.Value);
        Assert.Equal(10_000, paidCharge.ExternalProviderSpend.Value);
    }

    [Fact]
    public void Request_cost_rounds_once_and_does_not_double_count_cached_or_reasoning_tokens()
    {
        var at = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var fee = new FeePolicyVersion(Guid.CreateVersion7(), "managed", 1_000,
            new UsdMicroAmount(2), at, null, at);
        var evidence = new[]
        {
            new PricedUsageEvidence(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1,
                1, 1, 500_000, 1_000_000, 100_000, "{}", at, at, null),
            new PricedUsageEvidence(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 0,
                0, null, 400_000, 0, null, "{}", at, at, null)
        };

        var cost = RequestCostCalculator.Calculate(evidence, fee, new UsdMicroAmount(10));

        Assert.Equal(2, cost.ProviderCost.Value); // Exact sum: 0.5 + 0.1 + 1 + 0.4.
        Assert.Equal(5, cost.UncappedCustomerCharge.Value); // Ceil(2 * 1.1 + 2).
        Assert.Equal(5, cost.Charged.Value);
        Assert.Equal(0, cost.PlatformExposure.Value);
    }

    [Fact]
    public void DeepSeek_cache_hit_and_reasoning_usage_use_frozen_tariff_without_double_charge()
    {
        var at = new DateTimeOffset(2026, 9, 26, 3, 0, 0, TimeSpan.Zero);
        var fee = new FeePolicyVersion(Guid.CreateVersion7(), "managed", 0,
            UsdMicroAmount.Zero, at, null, at);
        var evidence = new PricedUsageEvidence(Guid.CreateVersion7(), Guid.CreateVersion7(),
            20, 9, 8, 4, 1_000_000, 2_000_000, 250_000, "{}",
            at, at.AddMinutes(-1), at.AddMinutes(1));

        var charge = RequestCostCalculator.Calculate([evidence], fee, new UsdMicroAmount(100));

        Assert.Equal(32, charge.ProviderCost.Value); // 12 miss + 8 cached at 0.25 + 9 output.
        Assert.Equal(32, charge.Charged.Value);
    }

    [Fact]
    public void Request_cost_rejects_stale_price_and_unknown_extra_pricing()
    {
        var at = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var fee = new FeePolicyVersion(Guid.CreateVersion7(), "managed", 0,
            UsdMicroAmount.Zero, at, null, at);
        var item = new PricedUsageEvidence(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 0,
            0, null, 1_000_000, 0, null, "{}", at, at.AddSeconds(1), null);

        Assert.Throws<InvalidOperationException>(() => RequestCostCalculator.Calculate(
            [item], fee, new UsdMicroAmount(10)));
        Assert.Throws<NotSupportedException>(() => RequestCostCalculator.Calculate(
            [item with { PriceEffectiveFrom = at, ExtraPricingJson = "{\"tool\":1}" }],
            fee, new UsdMicroAmount(10)));
    }

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

    [Fact]
    public async Task Operator_publication_requires_future_fee_window_and_bounded_UTC_FX_time()
    {
        var store = new InMemoryPricingHistoryStore();
        var clock = new FixedBillingTimeProvider();
        var service = new PricingHistoryService(store, clock);
        var now = clock.GetUtcNow();
        await Assert.ThrowsAsync<ArgumentException>(() => service.ScheduleFeePolicyVersionAsync(
            "default", 100, UsdMicroAmount.Zero, now, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ScheduleFeePolicyVersionAsync(
            "default", 100, new UsdMicroAmount(-1), now.AddHours(1), null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PublishFxRateSnapshotAsync(
            "cbu", 1_000_000m, now.AddDays(-2)));
        var fee = await service.ScheduleFeePolicyVersionAsync("default", 100,
            new UsdMicroAmount(5), now.AddHours(1), null);
        var fx = await service.PublishFxRateSnapshotAsync("cbu", 1_000_000m, now);
        Assert.Equal(fee, Assert.Single(await service.ListFeePolicyVersionsAsync("default")));
        Assert.Equal(fx, Assert.Single(await service.ListFxRateSnapshotsAsync(5)));
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

    public Task ScheduleFeePolicyVersionAsync(FeePolicyVersion version, CancellationToken cancellationToken = default) =>
        AppendFeePolicyVersionAsync(version, cancellationToken);

    public Task<IReadOnlyList<FeePolicyVersion>> ListFeePolicyVersionsAsync(string policyCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FeePolicyVersion>>(Policies.Where(value => value.PolicyCode == policyCode).ToArray());

    public Task PublishFxRateSnapshotAsync(FxRateSnapshot snapshot, CancellationToken cancellationToken = default) =>
        AppendFxRateSnapshotAsync(snapshot, cancellationToken);

    public Task<IReadOnlyList<FxRateSnapshot>> ListFxRateSnapshotsAsync(int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FxRateSnapshot>>(Snapshots.Take(limit).ToArray());

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
