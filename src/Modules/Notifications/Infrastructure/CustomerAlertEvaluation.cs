using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Persistence;

namespace UZLLM.Modules.Notifications.Infrastructure;

public sealed class CustomerAlertEvaluator(FoundationDbContext db, IOutboxStore outbox, TimeProvider clock)
{
    public async Task<int> EvaluateDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await db.TelegramLinkChallenges.Where(value => value.ExpiresAt < now.AddDays(-1))
            .ExecuteDeleteAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rules = await db.CustomerAlertRules.FromSqlInterpolated($"""
            SELECT * FROM ops.alert_rule WHERE enabled = TRUE AND next_evaluation_at <= {now}
            ORDER BY next_evaluation_at, id FOR UPDATE SKIP LOCKED LIMIT 20
            """).ToListAsync(cancellationToken);
        var created = 0;
        foreach (var rule in rules)
        {
            rule.NextEvaluationAt = now.AddMinutes(1);
            if (!await db.NotificationDestinations.AnyAsync(value => value.Id == rule.DestinationId &&
                value.OrganizationId == rule.OrganizationId && value.Status == "Verified", cancellationToken)) continue;
            var metric = await ReadMetricAsync(rule, now, cancellationToken);
            if (metric is null) continue;
            if (rule.Type == "BudgetWarning" && rule.LastWindowStart != metric.Value.WindowStart)
            {
                rule.Armed = true;
                rule.LastWindowStart = metric.Value.WindowStart;
            }
            if (!metric.Value.Breached)
            {
                rule.Armed = true;
                continue;
            }
            if (!rule.Armed) continue;
            rule.Armed = false;
            rule.Episode++;
            rule.LastTriggeredAt = now;
            var alertEvent = new CustomerAlertEventEntity
            {
                Id = Guid.CreateVersion7(), RuleId = rule.Id, Episode = rule.Episode,
                ObservedValue = metric.Value.Observed, TriggeredAt = now
            };
            db.CustomerAlertEvents.Add(alertEvent);
            await outbox.EnqueueAsync("customer.alert.telegram", JsonSerializer.Serialize(new { EventId = alertEvent.Id }),
                cancellationToken: cancellationToken);
            created++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    private async Task<AlertMetric?> ReadMetricAsync(CustomerAlertRuleEntity rule, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (rule.Type == "LowBalance")
        {
            var wallet = await db.Set<BillingWalletEntity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrganizationId == rule.OrganizationId, cancellationToken);
            if (wallet is null) return null;
            var available = wallet.PostedBalanceMicroUsd - wallet.ReservedBalanceMicroUsd;
            return new AlertMetric(available, available <= rule.Threshold, null);
        }
        if (rule.Type == "BudgetWarning")
        {
            var policy = await db.Set<BillingBudgetPolicyEntity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == rule.BudgetPolicyId &&
                    value.OrganizationId == rule.OrganizationId && value.ProjectId == rule.ProjectId,
                    cancellationToken);
            if (policy is null) return null;
            var start = WindowStart(policy.Period, now);
            var bucket = await db.Set<BillingBudgetBucketEntity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.PolicyId == policy.Id && value.WindowStart == start,
                    cancellationToken);
            var spend = (bucket?.CapturedMicroUsd ?? 0) + (bucket?.ReservedMicroUsd ?? 0);
            var basisPoints = policy.LimitMicroUsd == 0 ? (spend > 0 ? 10000L : 0L)
                : (long)Math.Min(10000m, decimal.Divide(spend * 10000m, policy.LimitMicroUsd));
            return new AlertMetric(basisPoints, basisPoints >= rule.Threshold, start);
        }
        if (rule.Type == "ErrorSpike")
        {
            var window = db.Set<UsageRequestEntity>().AsNoTracking().Where(value =>
                value.OrganizationId == rule.OrganizationId && value.CompletedAt >= now.AddMinutes(-5)
                && value.CompletedAt <= now);
            if (rule.ProjectId is { } projectId) window = window.Where(value => value.ProjectId == projectId);
            var total = await window.CountAsync(cancellationToken);
            if (total < 20) return new AlertMetric(0, false, null);
            var errors = await window.CountAsync(value => value.HttpStatus >= 500 ||
                value.ExecutionState == "OutcomeUnknown", cancellationToken);
            var basisPoints = errors * 10000L / total;
            return new AlertMetric(basisPoints, basisPoints >= rule.Threshold, null);
        }
        throw new InvalidOperationException("Unknown customer alert type.");
    }

    public static DateTimeOffset WindowStart(string period, DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return period switch
        {
            "Lifetime" => DateTimeOffset.UnixEpoch,
            "Daily" => new DateTimeOffset(utc.Date, TimeSpan.Zero),
            "Weekly" => new DateTimeOffset(utc.Date.AddDays(-((int)utc.DayOfWeek + 6) % 7), TimeSpan.Zero),
            "Monthly" => new DateTimeOffset(new DateTime(utc.Year, utc.Month, 1), TimeSpan.Zero),
            _ => throw new InvalidOperationException("Unknown budget period.")
        };
    }

    private readonly record struct AlertMetric(long Observed, bool Breached, DateTimeOffset? WindowStart);
}
