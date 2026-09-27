using System.Security.Cryptography;
using UZLLM.Modules.Audit.Contracts;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed record TelegramLink(string DeepLink, DateTimeOffset ExpiresAt);
public sealed record AlertRuleView(Guid Id, Guid OrganizationId, Guid? ProjectId, Guid? BudgetPolicyId,
    Guid DestinationId, string Type, long Threshold, bool Enabled, DateTimeOffset? LastTriggeredAt);
public sealed record AlertDestinationView(Guid Id, string Type, string Status, DateTimeOffset VerifiedAt);

public sealed class CustomerAlertService(FoundationDbContext db, IOrganizationAuthorizationService authorization,
    TelegramAlertOptions telegram, TelegramChatProtector chats, IAuditTrail audit, TimeProvider clock)
{
    public async Task<TelegramLink> BeginTelegramLinkAsync(Guid accountId, Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageBilling, cancellationToken: cancellationToken);
        telegram.RequireLinkConfiguration();
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = clock.GetUtcNow().AddMinutes(10);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.TelegramLinkChallenges.Add(new TelegramLinkChallengeEntity
        {
            TokenHash = SHA256.HashData(tokenBytes),
            OrganizationId = organizationId,
            AccountId = accountId,
            ExpiresAt = expiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(organizationId, accountId, "notification.telegram.link.started", "notification_destination",
            null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TelegramLink($"https://t.me/{telegram.BotUsername}?start={token}", expiresAt);
    }

    // Telegram does not sign update bodies. The secret header plus a random, one-use account-bound
    // challenge is the ownership proof; client-submitted chat IDs are never trusted.
    public async Task<bool> CompleteTelegramLinkAsync(string token, long chatId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0 || !TryDecodeToken(token, out var tokenBytes)) return false;
        var hash = SHA256.HashData(tokenBytes);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var challenge = await db.TelegramLinkChallenges
            .FromSqlInterpolated($"SELECT * FROM ops.telegram_link_challenge WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt <= clock.GetUtcNow()) return false;
        // Serialize concurrent links for the same organization and hold the member row
        // against role revocation until verification commits.
        _ = await db.Set<OrganizationEntity>()
            .FromSqlInterpolated($"SELECT * FROM org.organization WHERE id = {challenge.OrganizationId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var member = await db.Set<OrganizationMemberEntity>()
            .FromSqlInterpolated($"SELECT * FROM org.member WHERE organization_id = {challenge.OrganizationId} AND account_id = {challenge.AccountId} FOR SHARE")
            .SingleOrDefaultAsync(cancellationToken);
        if (member is not { Status: "Active", Role: "Owner" or "Admin" }) return false;
        var existing = await db.NotificationDestinations.SingleOrDefaultAsync(value =>
            value.OrganizationId == challenge.OrganizationId && value.Type == "Telegram", cancellationToken);
        var destination = existing ?? new NotificationDestinationEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = challenge.OrganizationId,
            Type = "Telegram", CreatedAt = clock.GetUtcNow()
        };
        var protectedChat = chats.Protect(challenge.OrganizationId, destination.Id, chatId);
        destination.EncryptedChatId = protectedChat.Ciphertext;
        destination.KeyVersion = protectedChat.Version;
        destination.Status = "Verified";
        destination.VerifiedAt = clock.GetUtcNow();
        if (existing is null) db.NotificationDestinations.Add(destination);
        challenge.ConsumedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(challenge.OrganizationId, challenge.AccountId, "notification.telegram.connected",
            "notification_destination", destination.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AlertDestinationView>> ListDestinationsAsync(Guid accountId, Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ReadBilling, cancellationToken: cancellationToken);
        return await db.NotificationDestinations.AsNoTracking().Where(value => value.OrganizationId == organizationId)
            .OrderBy(value => value.CreatedAt)
            .Select(value => new AlertDestinationView(value.Id, value.Type, value.Status, value.VerifiedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DisableDestinationAsync(Guid accountId, Guid organizationId, Guid destinationId,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageBilling, cancellationToken: cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.NotificationDestinations.Where(value => value.Id == destinationId &&
            value.OrganizationId == organizationId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.Status, "Disabled"), cancellationToken) == 1;
        if (changed)
        {
            await RecordAsync(organizationId, accountId, "notification.destination.disabled",
                "notification_destination", destinationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return changed;
    }

    public async Task<AlertRuleView> CreateRuleAsync(Guid accountId, Guid organizationId, Guid? projectId,
        Guid? budgetPolicyId, Guid destinationId, string type, long threshold,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageBilling, cancellationToken: cancellationToken);
        if (!ValidRule(type, projectId, budgetPolicyId, threshold))
            throw new ArgumentException("Invalid alert rule scope or threshold.");
        if (!await db.NotificationDestinations.AnyAsync(value => value.Id == destinationId &&
            value.OrganizationId == organizationId && value.Status == "Verified", cancellationToken))
            throw new KeyNotFoundException("Verified destination not found.");
        if (projectId is { } project && !await db.Set<ProjectEntity>().AnyAsync(value =>
            value.Id == project && value.OrganizationId == organizationId, cancellationToken))
            throw new KeyNotFoundException("Project not found.");
        if (budgetPolicyId is { } policy && !await db.Set<BillingBudgetPolicyEntity>().AnyAsync(value =>
            value.Id == policy && value.OrganizationId == organizationId && value.ProjectId == projectId, cancellationToken))
            throw new KeyNotFoundException("Budget policy not found.");
        var now = clock.GetUtcNow();
        var rule = new CustomerAlertRuleEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, ProjectId = projectId,
            BudgetPolicyId = budgetPolicyId, DestinationId = destinationId, Type = type,
            Threshold = threshold, CreatedAt = now, NextEvaluationAt = now
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.CustomerAlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(organizationId, accountId, "notification.rule.created", "alert_rule", rule.Id,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(rule);
    }

    public async Task<IReadOnlyList<AlertRuleView>> ListRulesAsync(Guid accountId, Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ReadBilling, cancellationToken: cancellationToken);
        var rules = await db.CustomerAlertRules.AsNoTracking().Where(value => value.OrganizationId == organizationId)
            .OrderBy(value => value.CreatedAt).ToListAsync(cancellationToken);
        return rules.Select(ToView).ToArray();
    }

    public async Task<bool> SetRuleEnabledAsync(Guid accountId, Guid organizationId, Guid ruleId, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await authorization.EnsurePermissionAsync(accountId, organizationId,
            OrganizationPermission.ManageBilling, cancellationToken: cancellationToken);
        var rule = await db.CustomerAlertRules.SingleOrDefaultAsync(value => value.Id == ruleId &&
            value.OrganizationId == organizationId, cancellationToken);
        if (rule is null) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        rule.Enabled = enabled;
        if (enabled) { rule.Armed = true; rule.NextEvaluationAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(cancellationToken);
        await RecordAsync(organizationId, accountId, enabled ? "notification.rule.enabled" : "notification.rule.disabled",
            "alert_rule", ruleId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static bool ValidRule(string type, Guid? projectId, Guid? budgetPolicyId, long threshold) => type switch
    {
        "LowBalance" => projectId is null && budgetPolicyId is null && threshold is >= 0 and <= 9007199254740991,
        "BudgetWarning" => projectId is not null && budgetPolicyId is not null && threshold is >= 1 and <= 10000,
        "ErrorSpike" => budgetPolicyId is null && threshold is >= 1 and <= 10000,
        _ => false
    };

    private static AlertRuleView ToView(CustomerAlertRuleEntity value) => new(value.Id, value.OrganizationId,
        value.ProjectId, value.BudgetPolicyId, value.DestinationId, value.Type, value.Threshold,
        value.Enabled, value.LastTriggeredAt);

    private static bool TryDecodeToken(string token, out byte[] bytes)
    {
        bytes = [];
        if (token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))) return false;
        try
        {
            bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            return bytes.Length == 32;
        }
        catch (FormatException) { return false; }
    }

    private async Task RecordAsync(Guid organizationId, Guid accountId, string action,
        string resourceType, Guid? resourceId, CancellationToken cancellationToken) =>
        _ = await audit.RecordAsync(new AuditEventInput(organizationId, accountId, action,
            resourceType, resourceId, null, "{}"), cancellationToken);
}
