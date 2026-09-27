namespace UZLLM.Persistence;

public sealed class NotificationDestinationEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Type { get; set; } = "Telegram";
    public byte[]? EncryptedChatId { get; set; }
    public string? KeyVersion { get; set; }
    public string? EndpointUrl { get; set; }
    public byte[]? EncryptedWebhookSecret { get; set; }
    public string? WebhookKeyVersion { get; set; }
    public string Status { get; set; } = "Verified";
    public DateTimeOffset VerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TelegramLinkChallengeEntity
{
    public byte[] TokenHash { get; set; } = [];
    public Guid OrganizationId { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class CustomerAlertRuleEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? BudgetPolicyId { get; set; }
    public Guid DestinationId { get; set; }
    public string Type { get; set; } = null!;
    public long Threshold { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Armed { get; set; } = true;
    public long Episode { get; set; }
    public DateTimeOffset? LastWindowStart { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset NextEvaluationAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CustomerAlertEventEntity
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public long Episode { get; set; }
    public long ObservedValue { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset TriggeredAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
