using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Observability;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed class PostgreSqlSliReportSourceReader(FoundationDbContext db, TimeProvider clock,
    SliReportLimits limits) : ISliReportSourceReader
{
    public async Task<SliReportSnapshot> ReadAsync(SliReportScope scope, DateTimeOffset periodStart, CancellationToken ct)
    {
        limits.Validate(); SliReportPolicy.Validate(scope);
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.RepeatableRead)
            throw new InvalidOperationException("Caller Repeatable Read snapshot required.");
        var end = SliReportPolicy.Month(periodStart);
        var names = scope.Producers.Select(value => value.Producer).ToArray();
        var capturedAt = clock.GetUtcNow();
        // Late terminal evidence is selected by execution start, not its ingestion/end month.
        var manifests = await db.Set<SliManifestEntity>().AsNoTracking().Where(value => names.Contains(value.Producer)
            && value.Region == scope.Region && ((value.IntervalStart < end && value.IntervalEnd >= periodStart)
                || db.Set<SliObservationEntity>().Any(row => row.ManifestId == value.Id && row.StartedAt >= periodStart && row.StartedAt < end)))
            .OrderBy(value => value.Id).Take(limits.MaximumFacts + 1).ToArrayAsync(ct);
        if (manifests.Length > limits.MaximumFacts) throw new SliReportRejectedException("Bounds");
        var ids = manifests.Select(value => value.Id).ToArray();
        var rows = await db.Set<SliObservationEntity>().AsNoTracking().Where(value => ids.Contains(value.ManifestId))
            .OrderBy(value => value.ManifestId).ThenBy(value => value.Sequence).Take(limits.MaximumFacts + 1).ToArrayAsync(ct);
        var epochs = manifests.Select(value => value.Epoch).Distinct().ToArray();
        var conflicts = await db.Set<SliImportReceiptEntity>().AsNoTracking()
            .Where(value => names.Contains(value.Producer) && epochs.Contains(value.Epoch) && value.Status == "Conflict")
            .OrderBy(value => value.Id).Take(limits.MaximumFacts + 1).ToArrayAsync(ct);
        if ((long)rows.Length + manifests.Length + conflicts.Length > limits.MaximumFacts) throw new SliReportRejectedException("Bounds");
        var conflictIds = conflicts.GroupBy(value => (value.Producer, value.Epoch)).ToDictionary(value => value.Key,
            value => value.Select(receipt => receipt.Id).Order().ToArray());
        if ((long)rows.Length + manifests.Length + manifests.Sum(value => (long)(conflictIds.GetValueOrDefault((value.Producer, value.Epoch))?.Length ?? 0)) > limits.MaximumFacts)
            throw new SliReportRejectedException("Bounds");
        var counts = rows.GroupBy(value => value.ManifestId).ToDictionary(value => value.Key, value => value.Count());
        var sources = manifests.Select(value =>
        {
            ct.ThrowIfCancellationRequested();
            var receiptIds = conflictIds.GetValueOrDefault((value.Producer, value.Epoch)) ?? [];
            return new SliReportSource(value.Id, value.Producer, value.Epoch, value.BatchNumber, value.Source, value.Region,
                value.FirstSequence, value.LastSequence, value.ObservationCount, value.Dropped, value.IntervalStart, value.IntervalEnd,
                value.ReceivedAt, value.ClockSynchronized, value.CollectorContinuous, value.Sha256,
                counts.GetValueOrDefault(value.Id), receiptIds.Length > 0, receiptIds);
        }).ToArray();
        var lookup = manifests.ToDictionary(value => value.Id);
        var events = rows.Select(value =>
        {
            ct.ThrowIfCancellationRequested();
            return new SliObservedEvent(value.ManifestId, value.Producer, lookup[value.ManifestId].Source,
                System.Text.Json.JsonSerializer.Deserialize<ExecutionObservation>(value.PayloadJson, ObservationProtocol.Json)
                    ?? throw new SliReportRejectedException("Integrity"));
        }).ToArray();
        return new(scope, periodStart, capturedAt, sources, events);
    }
}
