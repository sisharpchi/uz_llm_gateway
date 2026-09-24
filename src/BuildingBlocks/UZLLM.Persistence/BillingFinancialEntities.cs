namespace UZLLM.Persistence;

public sealed class BillingReservationEntity
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid FeePolicyVersionId { get; set; }
    public long AmountMicroUsd { get; set; }
    public string Status { get; set; } = null!;
    public long? CapturedMicroUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
}

public sealed class BillingBudgetPolicyEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public long LimitMicroUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BillingBudgetBucketEntity
{
    public Guid PolicyId { get; set; }
    public long CapturedMicroUsd { get; set; }
    public long ReservedMicroUsd { get; set; }
}

public sealed class BillingReservationBudgetEntity
{
    public Guid ReservationId { get; set; }
    public Guid PolicyId { get; set; }
    public long AmountMicroUsd { get; set; }
}

public sealed class BillingSettlementEntity
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid RequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public long ProviderCostMicroUsd { get; set; }
    public long UncappedCustomerChargeMicroUsd { get; set; }
    public long ChargedMicroUsd { get; set; }
    public long UncollectedChargeMicroUsd { get; set; }
    public long PlatformExposureMicroUsd { get; set; }
    public bool UnresolvedUsage { get; set; }
    public string Outcome { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingSettlementEvidenceEntity
{
    public Guid SettlementId { get; set; }
    public Guid EvidenceId { get; set; }
}

public sealed class BillingRecoveryDebtEntity
{
    public Guid OrganizationId { get; set; }
    public long OutstandingMicroUsd { get; set; }
    public bool SpendingHeld { get; set; }
    public long Version { get; set; }
}

public sealed class BillingDebtEntryEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Type { get; set; } = null!;
    public long AmountMicroUsd { get; set; }
    public Guid ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingReversalEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ExternalReferenceId { get; set; }
    public long AmountMicroUsd { get; set; }
    public long RecoveredMicroUsd { get; set; }
    public long DebtCreatedMicroUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingPlatformExposureEntity
{
    public Guid Id { get; set; }
    public Guid SettlementId { get; set; }
    public Guid EvidenceId { get; set; }
    public long ProviderCostMicroUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
