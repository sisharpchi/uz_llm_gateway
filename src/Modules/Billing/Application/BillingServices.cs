using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Application;

public sealed class WalletLedgerService(
    IWalletLedgerStore store,
    ITransactionCoordinator transactionCoordinator,
    TimeProvider timeProvider) : IWalletLedgerService
{
    public Task<Wallet?> GetWalletAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        organizationId == Guid.Empty
            ? throw new ArgumentException("An organization is required.", nameof(organizationId))
            : store.FindWalletAsync(organizationId, cancellationToken);

    public async Task<LedgerPostingResult> PostAsync(
        LedgerPostingInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidatePosting(input);
        var entry = new LedgerEntry(
            Guid.CreateVersion7(),
            input.OrganizationId,
            input.Type,
            input.Amount,
            input.ReferenceType.Trim(),
            input.ReferenceId,
            NormalizeMetadata(input.MetadataJson),
            timeProvider.GetUtcNow());

        await using var transaction = await transactionCoordinator.BeginAsync(cancellationToken);
        var status = await store.TryAppendAsync(entry, cancellationToken);
        if (status is not LedgerPostingStatus.Posted)
        {
            return new LedgerPostingResult(status, null, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return new LedgerPostingResult(status, entry, await store.FindWalletAsync(entry.OrganizationId, cancellationToken));
    }

    private static void ValidatePosting(LedgerPostingInput input)
    {
        if (input.OrganizationId == Guid.Empty || input.ReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Organization and reference identifiers are required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.ReferenceType) || input.ReferenceType.Trim().Length > 100)
        {
            throw new ArgumentException("Reference type is required and must be at most 100 characters.", nameof(input));
        }

        var expectedPositive = input.Type is LedgerEntryType.TopUp
            or LedgerEntryType.Refund
            or LedgerEntryType.AdjustmentCredit
            or LedgerEntryType.PromotionalCredit;
        if (expectedPositive != (input.Amount.Value > 0))
        {
            throw new ArgumentException("Ledger amount direction does not match the entry type.", nameof(input));
        }
    }

    private static string NormalizeMetadata(string? metadataJson)
    {
        var normalized = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Ledger metadata must be a JSON object.", nameof(metadataJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Ledger metadata must be valid JSON.", nameof(metadataJson), exception);
        }

        return normalized;
    }
}

public sealed class PricingHistoryService(IPricingHistoryStore store, TimeProvider timeProvider) : IPricingHistoryService
{
    public async Task<FeePolicyVersion> AddFeePolicyVersionAsync(
        string policyCode,
        int markupBasisPoints,
        UsdMicroAmount fixedFee,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeText(policyCode, 100, nameof(policyCode));
        if (markupBasisPoints is < 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(markupBasisPoints));
        }

        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new ArgumentException("The fee policy effective end must be after its start.", nameof(effectiveTo));
        }

        var version = new FeePolicyVersion(
            Guid.CreateVersion7(),
            normalizedCode,
            markupBasisPoints,
            fixedFee,
            effectiveFrom,
            effectiveTo,
            timeProvider.GetUtcNow());
        await store.AppendFeePolicyVersionAsync(version, cancellationToken);
        return version;
    }

    public async Task<FxRateSnapshot> CaptureFxRateSnapshotAsync(
        string source,
        decimal uzsTiyinPerUsd,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeText(source, 100, nameof(source));
        if (uzsTiyinPerUsd <= 0 || decimal.Round(uzsTiyinPerUsd, 8) != uzsTiyinPerUsd)
        {
            throw new ArgumentOutOfRangeException(nameof(uzsTiyinPerUsd));
        }

        var snapshot = new FxRateSnapshot(Guid.CreateVersion7(), normalizedSource, uzsTiyinPerUsd, observedAt);
        await store.AppendFxRateSnapshotAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static string NormalizeText(string value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 100)
        {
            throw new ArgumentException($"A value up to {maximumLength} characters is required.", parameterName);
        }

        return normalized;
    }
}
