namespace UZLLM.Persistence;

public sealed class BillingWalletAdjustmentEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RequestedByAccountId { get; set; }
    public string AdjustmentKey { get; set; } = null!;
    public long AmountMicroUsd { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BillingWalletAdjustmentApprovalEntity
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RequestedByAccountId { get; set; }
    public Guid ApprovedByAccountId { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
