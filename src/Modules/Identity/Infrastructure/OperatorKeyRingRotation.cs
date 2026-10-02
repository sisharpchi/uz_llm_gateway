using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Identity.Infrastructure;

/// <summary>Audited, operator-attributed creation of a new shared Data Protection key.</summary>
public sealed class OperatorKeyRingRotation(
    FoundationDbContext db, IKeyManager keyManager, IAuditTrail audit, TimeProvider clock)
{
    public async Task<Guid> RotateAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || !await db.Set<IdentityOperatorAccessEntity>()
            .AsNoTracking().AnyAsync(value => value.AccountId == actorId && value.IsActive
                && value.MfaEnabledAt != null && value.Account.Status == "Active",
                cancellationToken))
            throw new UnauthorizedAccessException("An active MFA-enrolled operator is required.");

        await audit.RecordAsync(new AuditEventInput(null, actorId,
            "identity.keyring.rotation.started", "data_protection_keyring", null, null, "{}"),
            cancellationToken);
        var now = clock.GetUtcNow();
        var key = keyManager.CreateNewKey(now.AddMinutes(-1), now.AddDays(90));
        await audit.RecordAsync(new AuditEventInput(null, actorId,
            "identity.keyring.rotation.completed", "data_protection_keyring", key.KeyId,
            null, JsonSerializer.Serialize(new { keyId = key.KeyId })), cancellationToken);
        return key.KeyId;
    }
}
