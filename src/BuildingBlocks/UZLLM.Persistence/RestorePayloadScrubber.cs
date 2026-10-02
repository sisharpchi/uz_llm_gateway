using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed record RestorePayloadScrubResult(int DisabledPolicies, int DeletedPayloads);

/// <summary>
/// One-shot restore quarantine step: a backup may predate a customer's opt-out.
/// The conservative recovery policy discards every retained body and disables
/// all restored opt-ins before traffic can be reopened.
/// </summary>
public sealed class RestorePayloadScrubber(FoundationDbContext db, TimeProvider timeProvider)
{
    public async Task<RestorePayloadScrubResult> ScrubAsync(Guid operatorAccountId,
        CancellationToken cancellationToken = default)
    {
        if (operatorAccountId == Guid.Empty) throw new ArgumentException("An operator account is required.", nameof(operatorAccountId));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Set<IdentityOperatorAccessEntity>().AnyAsync(value =>
                value.AccountId == operatorAccountId && value.IsActive && value.Account.Status == "Active",
                cancellationToken))
        {
            throw new InvalidOperationException("An active operator account must exist in the restored database.");
        }

        // This is a quarantine command, not a live cleanup operation. The
        // database transaction makes the reset and evidence atomic.
        var now = timeProvider.GetUtcNow();
        var policies = await db.Set<UsagePayloadRetentionPolicyEntity>()
            .Where(value => value.Enabled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Enabled, false)
                .SetProperty(value => value.UpdatedByAccountId, operatorAccountId)
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
        var payloads = await db.Set<UsagePayloadEntity>().ExecuteDeleteAsync(cancellationToken);
        db.Set<AuditEventEntity>().Add(new AuditEventEntity
        {
            Id = Guid.CreateVersion7(), ActorAccountId = operatorAccountId,
            Action = "Privacy.RestorePayloadScrub", ResourceType = "Database",
            MetadataJson = JsonSerializer.Serialize(new { disabledPolicies = policies, deletedPayloads = payloads }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RestorePayloadScrubResult(policies, payloads);
    }
}
