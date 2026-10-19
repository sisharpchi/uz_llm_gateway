namespace UZLLM.Modules.Billing.Contracts;

/// <summary>A UTC calendar month, never a request-start or display-timezone window.</summary>
public sealed record StatementPeriod
{
    public StatementPeriod(int year, int month)
    {
        if (year is < 1 or > 9998 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(year));
        From = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        To = From.AddMonths(1);
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public bool Contains(DateTimeOffset postingTime) => postingTime >= From && postingTime < To;
    public static StatementPeriod At(DateTimeOffset instant) => new(instant.UtcDateTime.Year, instant.UtcDateTime.Month);
}

// Internal read contracts, not customer DTOs. No raw ledger metadata, merchant references or actor reasons.
public sealed record StatementLedgerSource(Guid Id, Guid OrganizationId, LedgerEntryType Type,
    long AmountMicroUsd, string ReferenceType, Guid ReferenceId, DateTimeOffset OccurredAt);

public sealed record StatementSourceLink(Guid LedgerId, Guid OrganizationId, string Kind, Guid SourceId, Guid ReferenceId,
    long AmountMicroUsd, Guid? RelatedSourceId = null, DateTimeOffset? RequestStartedAt = null,
    Guid? FeePolicyVersionId = null, Guid? ByokFeePolicyVersionId = null,
    long? ExternalReversalMicroUsd = null, long? ReversalDebtCreatedMicroUsd = null);

public sealed record StatementPaymentEvidence(Guid IntentId, Guid OrganizationId, Guid QuoteId,
    Guid FxSnapshotId, long PrincipalTiyin, long FeeTiyin, decimal UzsTiyinPerUsd,
    long CreditMicroUsd, bool FrozenTermsMatch);

public sealed record StatementPaymentSources(IReadOnlyList<StatementPaymentEvidence> Items, int OpenMismatchCount,
    int UnmatchedPaidCount = 0);

/// <summary>Payments implements this Billing-owned port; enlists in the caller's Repeatable Read context.</summary>
public interface IStatementPaymentEvidenceReader
{
    Task<StatementPaymentSources> ReadAsync(Guid organizationId, IReadOnlyCollection<Guid> intentIds,
        CancellationToken cancellationToken = default);
}

public sealed record StatementBalance(long PostedMicroUsd, long HeldMicroUsd, long AvailableMicroUsd,
    long RecoveryDebtMicroUsd, long ActiveReservationMicroUsd, long DebtEntriesMicroUsd);

public sealed record StatementPending(int Evidence, int Settlement, int MerchantMismatch, int UnmatchedPayment = 0);

public sealed record StatementHoldSource(Guid Id, Guid OrganizationId, Guid RequestId, long AmountMicroUsd,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record StatementDebtSource(Guid Id, Guid OrganizationId, string Type, Guid ReferenceId,
    long AmountMicroUsd, DateTimeOffset CreatedAt);
public sealed record StatementReversalSource(Guid Id, Guid OrganizationId, Guid IntentId, long AmountMicroUsd,
    long RecoveredMicroUsd, long DebtCreatedMicroUsd, DateTimeOffset CreatedAt);
public sealed record StatementUsageSource(Guid Id, Guid OrganizationId, Guid RequestId, Guid AttemptId,
    Guid? SettlementId, string State, Guid? PriceVersionId, int? InputTokens, int? OutputTokens,
    int? CachedInputTokens, int? ReasoningTokens, DateTimeOffset CapturedAt);

public sealed record StatementSnapshot(Guid OrganizationId, StatementPeriod Period, DateTimeOffset CapturedAt,
    IReadOnlyList<StatementLedgerSource> Ledger, IReadOnlyList<StatementSourceLink> Links,
    StatementPaymentSources Payments, StatementBalance Balance, StatementPending Pending,
    IReadOnlyList<StatementHoldSource> Holds, IReadOnlyList<StatementDebtSource> DebtEntries,
    IReadOnlyList<StatementReversalSource> Reversals, IReadOnlyList<StatementUsageSource> Usage);

/// <summary>Internal source reader, not an authorization facade. Never starts/commits a transaction or posts money.</summary>
public interface IStatementSourceReader
{
    Task<StatementSnapshot?> ReadAsync(Guid organizationId, StatementPeriod period,
        CancellationToken cancellationToken = default);
}

public sealed record StatementReconciliation(long OpeningMicroUsd, long MovementMicroUsd, long ClosingMicroUsd,
    long PostPeriodMovementMicroUsd, long GrossUsageMicroUsd, long RefundedUsageMicroUsd,
    long NetUsageMicroUsd, string SourceDigest, IReadOnlyList<string> Errors, StatementPending Pending)
{
    public const string CalculationVersion = "account-statement-v1";
    public string Kind => "AccountStatement";
    public string Label => "Not a tax invoice / No payment due from this statement";
    // Eligibility of these inputs only; not a Published artifact, fiscal/SLA approval or external finality.
    public bool FinanciallyReconciled => Errors.Count == 0;
}
