using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

/// <summary>Replacement, not increments: replay and late changes rebuild the original hour.</summary>
public sealed class PostgreSqlUsageRollupStore(FoundationDbContext db) : IUsageRollupStore
{
    public async Task<UsageRollupBatchResult> RebuildBatchAsync(int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Rollup publication owns its transaction.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Only the short database rebuild is serialized. Never hold a lease across network I/O.
        var acquired = await db.Database.SqlQueryRaw<bool>(
            "SELECT pg_try_advisory_xact_lock(876312904) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!acquired) return new UsageRollupBatchResult(0, 0, Busy: true);
        // Do not hold queue-row locks while scanning an hour: source writers must
        // remain free to commit financial evidence. xmin fences acknowledgement.
        var queued = await db.Database.SqlQuery<QueueClaim>($"""
            SELECT request_id AS "RequestId", organization_id AS "OrganizationId", hour AS "Hour",
                xmin::text::bigint AS "Version"
            FROM usage.rollup_queue ORDER BY enqueued_at, request_id LIMIT {batchSize}
            """).ToListAsync(cancellationToken);
        var buckets = queued.Select(value => (value.OrganizationId, value.Hour)).Distinct().ToArray();
        foreach (var (organizationId, hour) in buckets)
        {
            var end = hour.AddHours(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO usage.rollup_bucket (organization_id, hour, data_as_of)
                VALUES ({organizationId}, {hour}, statement_timestamp())
                ON CONFLICT (organization_id, hour) DO UPDATE SET data_as_of = EXCLUDED.data_as_of;
                DELETE FROM usage.hourly_rollup WHERE organization_id = {organizationId} AND hour = {hour};
                INSERT INTO usage.hourly_rollup
                    (organization_id, hour, project_id, api_key_id, model_id, provider_id,
                     execution_state, is_stream, request_count, completed_count, error_count,
                     pending_count, input_tokens, output_tokens, charged_micro_usd, refunded_micro_usd)
                SELECT organization_id, {hour}, project_id, api_key_id, model_id, provider_id,
                    execution_state, is_stream, count(*), sum(completed_count), sum(error_count),
                    sum(pending_count), sum(input_tokens), sum(output_tokens),
                    sum(charged_micro_usd), sum(refunded_micro_usd)
                FROM usage.rollup_source
                WHERE organization_id = {organizationId} AND started_at >= {hour} AND started_at < {end}
                GROUP BY organization_id, project_id, api_key_id, model_id, provider_id, execution_state, is_stream;
                """, cancellationToken);
        }
        if (queued.Count > 0)
        {
            var ids = queued.Select(value => value.RequestId).ToArray();
            var versions = queued.Select(value => value.Version).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM usage.rollup_queue q USING unnest({ids}, {versions}) AS claim(request_id, version)
                WHERE q.request_id = claim.request_id AND q.xmin::text::bigint = claim.version
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new UsageRollupBatchResult(queued.Count, buckets.Length);
    }

    private sealed class QueueClaim
    {
        public Guid RequestId { get; set; }
        public Guid OrganizationId { get; set; }
        public DateTimeOffset Hour { get; set; }
        public long Version { get; set; }
    }
}
