using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public interface IRetentionSweep
{
    Task<RetentionSweepResult> SweepAsync(int batchSize, CancellationToken cancellationToken = default);
}

public sealed record RetentionSweepResult(int RequestTraces, int OutboxMessages, int Jobs, int OperationalAlerts, int SliObservations = 0)
{
    public int Total => RequestTraces + OutboxMessages + Jobs + OperationalAlerts + SliObservations;
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

        // Raw diagnostic rows may expire; immutable source/count/digest and expiry
        // receipts remain. Future reports must disclose missing raw membership.
        var sliCounts = await db.Database.SqlQuery<int>($"""
            WITH candidates AS (
              SELECT producer, epoch, sequence FROM ops.sli_observation WHERE at < {diagnosticCutoff}
              ORDER BY at, producer, epoch, sequence FOR UPDATE SKIP LOCKED LIMIT {batchSize}
            ), deleted AS (
              DELETE FROM ops.sli_observation o USING candidates c
              WHERE o.producer = c.producer AND o.epoch = c.epoch AND o.sequence = c.sequence
              RETURNING o.manifest_id
            ), receipts AS (INSERT INTO ops.sli_import_receipt
              (id, manifest_id, producer, epoch, batch_number, sha256, status, received_at, observation_count)
            SELECT gen_random_uuid(), m.id, m.producer, m.epoch, m.batch_number, m.sha256,
              'Expired', {now}, count(*)::int FROM deleted d JOIN ops.sli_manifest m ON m.id = d.manifest_id
              GROUP BY m.id RETURNING id)
            SELECT count(*)::int AS "Value" FROM deleted
            """).ToListAsync(cancellationToken);
        return new RetentionSweepResult(traces, outbox, jobs, alerts, sliCounts.Single());
    }
}
