namespace UZLLM.Persistence;

public sealed class PaymentQuoteEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Provider { get; set; } = null!;
    public long AmountTiyin { get; set; }
    public long FeeTiyin { get; set; }
    public Guid FxSnapshotId { get; set; }
    public decimal UzsTiyinPerUsd { get; set; }
    public long CreditMicroUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PaymentIntentEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid QuoteId { get; set; }
    public string Provider { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long AmountTiyin { get; set; }
    public long FeeTiyin { get; set; }
    public Guid FxSnapshotId { get; set; }
    public decimal UzsTiyinPerUsd { get; set; }
    public long CreditMicroUsd { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string MerchantScope { get; set; } = null!;
    public string? ExternalTransactionId { get; set; }
    public int? ClickPrepareId { get; set; }
    public long? ProviderCreatedTimeUnixMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public int? CancelReason { get; set; }
}

public sealed class PaymentCallbackReceiptEntity
{
    public Guid Id { get; set; }
    public Guid? IntentId { get; set; }
    public string Provider { get; set; } = null!;
    public string ExternalRequestId { get; set; } = null!;
    public byte[] RequestHash { get; set; } = [];
    public string ResponseCode { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class PaymentReconciliationCaseEntity
{
    public Guid Id { get; set; }
    public Guid IntentId { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "Open";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
