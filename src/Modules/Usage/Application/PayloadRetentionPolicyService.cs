using System.Text.Json;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Application;

public sealed class PayloadRetentionPolicyService(IPayloadRetentionPolicyStore store,
    IAuditTrail audit, ITransactionCoordinator transactions) : IPayloadRetentionPolicyService
{
    public async Task<PayloadRetentionPolicy?> SetPolicyAsync(Guid organizationId, Guid projectId,
        Guid accountId, bool enabled, int retentionMinutes, string? ipAddress,
        CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var policy = await store.SetPolicyAsync(organizationId, projectId, accountId,
            enabled, retentionMinutes, cancellationToken);
        if (policy is null) return null;
        await audit.RecordAsync(new(organizationId, accountId, "payload_retention.updated",
            "project", projectId, ipAddress,
            JsonSerializer.Serialize(new { Enabled = enabled, RetentionMinutes = retentionMinutes })),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return policy;
    }
}
