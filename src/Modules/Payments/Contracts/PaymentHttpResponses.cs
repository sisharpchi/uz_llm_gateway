namespace UZLLM.Modules.Payments.Contracts;

// Wire amounts deliberately remain decimal strings, including beyond JS safe integers.
public sealed record PaymentQuoteResponse(Guid Id, Guid OrganizationId, string Provider,
    string AmountTiyin, string FeeTiyin, string CreditMicroUsd, Guid FxSnapshotId,
    string UzsTiyinPerUsd, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record PaymentIntentResponse(Guid Id, Guid OrganizationId, string Provider,
    Guid QuoteId, string Status, string AmountTiyin, string FeeTiyin, string CreditMicroUsd,
    Guid FxSnapshotId, string UzsTiyinPerUsd, string? ExternalTransactionId,
    DateTimeOffset CreatedAt, DateTimeOffset? BoundAt, DateTimeOffset? PaidAt,
    DateTimeOffset? CanceledAt);

public sealed record CreatedTopUpResponse(PaymentIntentResponse Intent, bool Duplicate, string? CheckoutUrl);

public sealed record WalletResponse(Guid OrganizationId, string PostedBalanceMicroUsd,
    string ReservedBalanceMicroUsd, string AvailableBalanceMicroUsd,
    string RecoveryDebtMicroUsd, bool SpendingHeld, long Version);
