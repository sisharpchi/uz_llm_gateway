namespace UZLLM.Modules.Billing.Contracts;

public readonly record struct UsdMicroAmount
{
    public UsdMicroAmount(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "USD micro-amounts cannot be negative.");
        }

        Value = value;
    }

    public long Value { get; }

    public static UsdMicroAmount Zero => new(0);
}

public readonly record struct SignedUsdMicroAmount
{
    public SignedUsdMicroAmount(long value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A ledger amount cannot be zero.");
        }

        Value = value;
    }

    public long Value { get; }
}

public readonly record struct UzsTiyinAmount
{
    public UzsTiyinAmount(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "UZS tiyin amounts cannot be negative.");
        }

        Value = value;
    }

    public long Value { get; }
}

public enum LedgerEntryType
{
    TopUp,
    UsageCharge,
    Refund,
    AdjustmentCredit,
    AdjustmentDebit,
    PromotionalCredit
}

public sealed record Wallet(
    Guid OrganizationId,
    UsdMicroAmount PostedBalance,
    UsdMicroAmount ReservedBalance,
    long Version)
{
    public UsdMicroAmount AvailableBalance => new(checked(PostedBalance.Value - ReservedBalance.Value));
}

public sealed record LedgerEntry(
    Guid Id,
    Guid OrganizationId,
    LedgerEntryType Type,
    SignedUsdMicroAmount Amount,
    string ReferenceType,
    Guid ReferenceId,
    string MetadataJson,
    DateTimeOffset OccurredAt);

public sealed record LedgerPostingInput(
    Guid OrganizationId,
    LedgerEntryType Type,
    SignedUsdMicroAmount Amount,
    string ReferenceType,
    Guid ReferenceId,
    string? MetadataJson = null);

public enum LedgerPostingStatus
{
    Posted,
    Duplicate,
    InsufficientFunds,
    WalletNotFound
}

public sealed record LedgerPostingResult(LedgerPostingStatus Status, LedgerEntry? Entry, Wallet? Wallet);

public sealed record FeePolicyVersion(
    Guid Id,
    string PolicyCode,
    int MarkupBasisPoints,
    UsdMicroAmount FixedFee,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset CreatedAt)
{
    public UsdMicroAmount CalculateCustomerCharge(UsdMicroAmount providerCost)
    {
        var markup = checked((providerCost.Value * (long)MarkupBasisPoints + 9_999) / 10_000);
        return new UsdMicroAmount(checked(providerCost.Value + markup + FixedFee.Value));
    }
}

public sealed record FxRateSnapshot(
    Guid Id,
    string Source,
    decimal UzsTiyinPerUsd,
    DateTimeOffset ObservedAt);

public interface IWalletLedgerStore
{
    Task<Wallet?> FindWalletAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<LedgerPostingStatus> TryAppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default);
}

public interface IPricingHistoryStore
{
    Task AppendFeePolicyVersionAsync(FeePolicyVersion version, CancellationToken cancellationToken = default);

    Task AppendFxRateSnapshotAsync(FxRateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IWalletLedgerService
{
    Task<Wallet?> GetWalletAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<LedgerPostingResult> PostAsync(LedgerPostingInput input, CancellationToken cancellationToken = default);
}

public interface IPricingHistoryService
{
    Task<FeePolicyVersion> AddFeePolicyVersionAsync(
        string policyCode,
        int markupBasisPoints,
        UsdMicroAmount fixedFee,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken = default);

    Task<FxRateSnapshot> CaptureFxRateSnapshotAsync(
        string source,
        decimal uzsTiyinPerUsd,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
