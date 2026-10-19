using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class PostgreSqlStatementReadStore(FoundationDbContext db) : IStatementReadStore
{
    public Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Set<OrganizationEntity>().AnyAsync(value => value.Id == organizationId, cancellationToken);
    // Strip unbounded annex/lines in PostgreSQL: a page never transfers 100 full 32-MiB artifacts.
    // Typed deserialization below is an additional allowlist, not raw artifact serialization.
    public async Task<IReadOnlyList<StatementMetadataResponse>> ListAsync(Guid organizationId, DateTimeOffset from,
        DateTimeOffset to, DateTimeOffset beforePeriod, long beforeRevision, int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 101) throw new ArgumentOutOfRangeException(nameof(take));
        var json = await db.Database.SqlQuery<string>($"""
            SELECT ((convert_from(v.json_artifact, 'UTF8')::jsonb - 'Lines' - 'PaymentAnnotations')
                || jsonb_build_object('LineCount', v.line_count, 'PublishedAt', v.published_at))::text AS "Value"
            FROM billing.statement_version v JOIN billing.statement_head h ON h.id = v.head_id AND h.organization_id = v.organization_id
            WHERE v.organization_id = {organizationId} AND h.period_start >= {from} AND h.period_start < {to}
                AND (h.period_start < {beforePeriod} OR (h.period_start = {beforePeriod} AND v.revision < {beforeRevision}))
            ORDER BY h.period_start DESC, v.revision DESC LIMIT {take}
            """).ToListAsync(cancellationToken);
        return json.Select(Read).ToArray();
    }

    public async Task<StatementMetadataResponse?> FindAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT ((convert_from(json_artifact, 'UTF8')::jsonb - 'Lines' - 'PaymentAnnotations')
                || jsonb_build_object('LineCount', line_count, 'PublishedAt', published_at))::text AS "Value"
            FROM billing.statement_version WHERE organization_id = {organizationId} AND id = {versionId}
            """).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : Read(json);
    }

    public async Task<IReadOnlyList<StatementLineResponse>> LinesAsync(Guid organizationId, Guid versionId, int afterOrdinal,
        int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 101 || afterOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(take));
        var rows = await db.Set<BillingStatementLineEntity>().AsNoTracking().Where(value => value.OrganizationId == organizationId
                && value.VersionId == versionId && value.Ordinal > afterOrdinal).OrderBy(value => value.Ordinal).Take(take)
            .Select(value => new { value.Ordinal, value.PayloadJson }).ToListAsync(cancellationToken);
        return rows.Select(value => JsonSerializer.Deserialize<StatementLineResponse>(value.PayloadJson)!
            with { Ordinal = value.Ordinal }).ToArray();
    }

    public Task<StatementCsv?> CsvAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken) =>
        db.Set<BillingStatementVersionEntity>().AsNoTracking().Where(value => value.OrganizationId == organizationId && value.Id == versionId)
            .Select(value => new StatementCsv(value.CsvArtifact, value.CsvSha256)).SingleOrDefaultAsync(cancellationToken);

    private static StatementMetadataResponse Read(string json) => JsonSerializer.Deserialize<StatementMetadataResponse>(json)
        ?? throw new InvalidOperationException("Invalid published statement metadata.");
}
