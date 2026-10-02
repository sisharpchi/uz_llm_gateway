using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public interface IRetentionSweep
{
    Task<RetentionSweepResult> SweepAsync(int batchSize, CancellationToken cancellationToken = default);
}

public sealed record RetentionSweepResult(int RequestTraces, int OutboxMessages, int Jobs, int OperationalAlerts)
{
    public int Total => RequestTraces + OutboxMessages + Jobs + OperationalAlerts;
}

/// <summary>
/// Minimizes only completed diagnostic/operational content. Retains row IDs,
/// deduplication keys, usage evidence, financial records and append-only audit.
/// </summary>
public sealed class PostgreSqlRetentionSweep(
    FoundationDbContext db,
    RetentionSchedule schedule,
    TimeProvider timeProvider) : IRetentionSweep
{
    public async Task<RetentionSweepResult> SweepAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (!schedule.Enabled) return new RetentionSweepResult(0, 0, 0, 0);

        var now = timeProvider.GetUtcNow();
        var diagnosticCutoff = now.AddDays(-schedule.DiagnosticDays);
        var operationalCutoff = now.AddDays(-schedule.OperationalDays);

        var traces = await db.Database.ExecuteSqlRawAsync("""
            WITH candidates AS (
                SELECT id FROM usage.request
                WHERE completed_at < {0}
                  AND financial_state IN ('Settled', 'Released')
                  AND trace_id IS NOT NULL
                ORDER BY completed_at, id
                FOR UPDATE SKIP LOCKED LIMIT {1}
            )
            UPDATE usage.request AS request SET trace_id = NULL
            FROM candidates WHERE request.id = candidates.id;
            """, [diagnosticCutoff, batchSize], cancellationToken);

        var outbox = await db.Database.ExecuteSqlRawAsync("""
            WITH candidates AS (
                SELECT id FROM ops.outbox
                WHERE processed_at < {0} AND dead_lettered_at IS NULL
                  AND (payload <> jsonb_build_object() OR last_error IS NOT NULL)
                ORDER BY processed_at, id
                FOR UPDATE SKIP LOCKED LIMIT {1}
            )
            UPDATE ops.outbox AS message SET payload = jsonb_build_object(), last_error = NULL
            FROM candidates WHERE message.id = candidates.id;
            """, [operationalCutoff, batchSize], cancellationToken);

        var jobs = await db.Database.ExecuteSqlRawAsync("""
            WITH candidates AS (
                SELECT id FROM ops.job
                WHERE completed_at < {0} AND dead_lettered_at IS NULL
                  AND (payload <> jsonb_build_object() OR last_error IS NOT NULL)
                ORDER BY completed_at, id
                FOR UPDATE SKIP LOCKED LIMIT {1}
            )
            UPDATE ops.job AS job SET payload = jsonb_build_object(), last_error = NULL
            FROM candidates WHERE job.id = candidates.id;
            """, [operationalCutoff, batchSize], cancellationToken);

        var alerts = await db.Database.ExecuteSqlRawAsync("""
            WITH candidates AS (
                SELECT id FROM ops.operational_alert
                WHERE resolved_at < {0} AND notified_at IS NOT NULL
                  AND details <> jsonb_build_object()
                ORDER BY resolved_at, id
                FOR UPDATE SKIP LOCKED LIMIT {1}
            )
            UPDATE ops.operational_alert AS alert SET details = jsonb_build_object()
            FROM candidates WHERE alert.id = candidates.id;
            """, [operationalCutoff, batchSize], cancellationToken);

        return new RetentionSweepResult(traces, outbox, jobs, alerts);
    }
}
