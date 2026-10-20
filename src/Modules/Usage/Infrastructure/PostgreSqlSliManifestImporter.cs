using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Observability;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed class PostgreSqlSliManifestImporter(FoundationDbContext db, ITransactionCoordinator transactions,
    SliCollectorTrust trusted, TimeProvider clock) : ISliManifestImporter
{
    public async Task<SliImportResult> ImportAsync(ReadOnlyMemory<byte> signedManifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (producer, digest, batch) = ObservationProtocol.Verify(signedManifest.Span, trusted.Producers);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        // Serialize only this producer/epoch's import, never a wallet or inference row.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({producer + ":" + batch.Epoch}, 7381021))", cancellationToken);
        var manifests = db.Set<SliManifestEntity>().AsNoTracking()
            .Where(value => value.Producer == producer && value.Epoch == batch.Epoch);
        var existing = await manifests.SingleOrDefaultAsync(value => value.BatchNumber == batch.BatchNumber, cancellationToken);
        SliImportStatus status;
        Guid? manifestId = existing?.Id;
        var warnings = (SliCoverageWarning)(existing?.CoverageWarnings ?? 0);
        if (existing is not null) status = existing.Sha256 == digest ? SliImportStatus.Duplicate : SliImportStatus.Conflict;
        else
        {
            var eventKeys = batch.Observations.Select(EventKey).ToArray();
            var overlap = batch.LastSequence >= batch.FirstSequence && await manifests.AnyAsync(value =>
                value.LastSequence >= value.FirstSequence && value.FirstSequence <= batch.LastSequence
                && value.LastSequence >= batch.FirstSequence, cancellationToken);
            var duplicateEvent = await db.Set<SliObservationEntity>().AsNoTracking().AnyAsync(value =>
                value.Producer == producer && value.Epoch == batch.Epoch && eventKeys.Contains(value.EventKey), cancellationToken);
            if (overlap || duplicateEvent) status = SliImportStatus.Conflict;
            else
            {
                var previous = await manifests.SingleOrDefaultAsync(value => value.BatchNumber == batch.BatchNumber - 1, cancellationToken);
                warnings = SliCoverageAssessment.Evaluate(batch, previous is null ? null
                    : new(previous.LastSequence, previous.IntervalEnd), clock.GetUtcNow());
                manifestId = Guid.CreateVersion7();
                db.Add(new SliManifestEntity
                {
                    Id = manifestId.Value, Producer = producer, Epoch = batch.Epoch, BatchNumber = batch.BatchNumber,
                    Source = batch.Source.ToString(), Region = batch.Region, FirstSequence = batch.FirstSequence,
                    LastSequence = batch.LastSequence, Sha256 = digest, ObservationCount = batch.Observations.Length,
                    Dropped = batch.Dropped, IntervalStart = batch.IntervalStart, IntervalEnd = batch.IntervalEnd,
                    ReceivedAt = clock.GetUtcNow(), ClockSynchronized = batch.ClockSynchronized,
                    CollectorContinuous = batch.CollectorContinuous, TerminalCoverage = batch.TerminalCoverage,
                    CoverageWarnings = (int)warnings
                });
                db.AddRange(batch.Observations.Select(item => new SliObservationEntity
                {
                    ManifestId = manifestId.Value, Producer = producer, Epoch = batch.Epoch,
                    Sequence = item.Sequence, ExecutionId = item.ExecutionId, EventKey = EventKey(item),
                    Operation = item.Operation.ToString(), StartedAt = item.StartedAt, At = item.At,
                    PayloadJson = JsonSerializer.Serialize(item, ObservationProtocol.Json)
                }));
                status = SliImportStatus.Accepted;
            }
        }
        // The accepted receipt seals membership; insert it only after all rows exist.
        await db.SaveChangesAsync(cancellationToken);
        db.Add(new SliImportReceiptEntity
        {
            Id = Guid.CreateVersion7(), ManifestId = manifestId, Producer = producer, Epoch = batch.Epoch,
            BatchNumber = batch.BatchNumber, Sha256 = digest, Status = status.ToString(),
            ReceivedAt = clock.GetUtcNow(), ObservationCount = batch.Observations.Length
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(status, manifestId, warnings);
    }

    private static string EventKey(ExecutionObservation item) =>
        $"{item.ExecutionId:N}:{item.Phase}:{item.AttemptId?.ToString("N") ?? "-"}";

}
