namespace UZLLM.Persistence;

public sealed class BillingWalletEntity
{
    public Guid OrganizationId { get; set; }

    public long PostedBalanceMicroUsd { get; set; }

    public long ReservedBalanceMicroUsd { get; set; }

    public long Version { get; set; }

    public OrganizationEntity Organization { get; set; } = null!;
}

public sealed class BillingLedgerEntryEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Type { get; set; } = null!;

    public long AmountMicroUsd { get; set; }

    public string ReferenceType { get; set; } = null!;

    public Guid ReferenceId { get; set; }

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    public OrganizationEntity Organization { get; set; } = null!;
}

public sealed class BillingFeePolicyVersionEntity
{
    public Guid Id { get; set; }

    public string PolicyCode { get; set; } = null!;

    public int MarkupBasisPoints { get; set; }

    public long FixedFeeMicroUsd { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingFxRateSnapshotEntity
{
    public Guid Id { get; set; }

    public string Source { get; set; } = null!;

    public decimal UzsTiyinPerUsd { get; set; }

    public DateTimeOffset ObservedAt { get; set; }
}
