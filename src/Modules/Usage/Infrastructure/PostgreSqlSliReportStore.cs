using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed class PostgreSqlSliReportStore(FoundationDbContext db, ISliReportSourceReader reader,
    SliReportProfiles profiles, SliReportLimits limits, IAuditTrail audit, ILeasedJobStore jobs,
    IOutboxStore outbox, TimeProvider clock) : ISliReportStore
{
    public const string JobType = "usage.sli.build";
    public const string PublishedEvent = "usage.sli.published";

    public async Task<SliReportOperation> RequestAsync(SliReportCommand input, CancellationToken ct = default)
    {
        RequireNoTransaction();
        var command = SliReportPolicy.Normalize(input, clock.GetUtcNow());
        var kind = command.PredecessorId is null ? "Build" : "Revision";
        var digest = SliReportPolicy.Hash(SliReportPolicy.Serialize(command));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"sli-command:" + kind + command.IdempotencyKey}, 0))", ct);
        var receipt = await db.Set<SliReportReceiptEntity>().AsNoTracking().SingleOrDefaultAsync(value => value.Kind == kind && value.IdempotencyKey == command.IdempotencyKey, ct);
        if (receipt is not null)
        {
            if (receipt.CommandSha256 != digest) throw new SliReportConflictException();
            return (await OperationAsync(receipt.OperationId, ct))!;
        }
        if (!profiles.Scopes.TryGetValue(command.ScopeId, out var scope)) throw new ArgumentException("Unknown registered scope.");
        SliReportPolicy.Validate(scope);
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ops.sli_report_head (id,scope_id,period_start,generation)
            VALUES ({id},{scope.Id},{command.PeriodStart},0) ON CONFLICT (scope_id,period_start) DO NOTHING
            """, ct);
        id = await db.Set<SliReportHeadEntity>().Where(value => value.ScopeId == scope.Id && value.PeriodStart == command.PeriodStart).Select(value => value.Id).SingleAsync(ct);
        await LockHeadAsync(id, ct);
        var head = await db.Set<SliReportHeadEntity>().SingleAsync(value => value.Id == id, ct);
        if (head.Generation != command.ExpectedGeneration || head.LatestVersionId != command.PredecessorId
            || head.ActiveOperationId is { } active && await db.Set<SliReportOperationEntity>().AnyAsync(value => value.Id == active && value.Status == "Pending", ct))
            throw new SliReportConflictException();
        var now = clock.GetUtcNow();
        var operation = new SliReportOperationEntity { Id = Guid.CreateVersion7(), HeadId = id, ExpectedGeneration = head.Generation,
            PredecessorId = command.PredecessorId, ActorId = command.ActorId, Reason = command.Reason,
            ScopeJson = JsonSerializer.Serialize(scope, SliReportPolicy.Json), CreatedAt = now };
        db.Add(operation); await db.SaveChangesAsync(ct);
        db.Add(new SliReportReceiptEntity { Id = Guid.CreateVersion7(), OperationId = operation.Id, Kind = kind,
            IdempotencyKey = command.IdempotencyKey, CommandSha256 = digest, CreatedAt = now });
        head.ActiveOperationId = operation.Id; await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(null, command.ActorId, "sli.report.requested", "sli_report_operation", operation.Id,
            null, JsonSerializer.Serialize(new { command.ScopeId, kind, command.ExpectedGeneration })), ct);
        await jobs.ScheduleAsync(JobType, JsonSerializer.Serialize(new { operationId = operation.Id }), operation.Id.ToString("N"), now, maxAttempts: 5, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return ToContract(operation, head);
    }

    public async Task BuildAsync(Guid operationId, int attemptNumber, CancellationToken ct = default)
    {
        RequireNoTransaction(); limits.Validate();
        if (operationId == Guid.Empty || attemptNumber is < 1 or > 5) throw new ArgumentException("Invalid SLI job.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(limits.DeadlineMilliseconds);
        var token = deadline.Token;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            var operation = await db.Set<SliReportOperationEntity>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, token)
                ?? throw new SliReportRejectedException("NotFound");
            await LockHeadAsync(operation.HeadId, token); db.ChangeTracker.Clear();
            operation = await db.Set<SliReportOperationEntity>().SingleAsync(value => value.Id == operationId, token);
            if (operation.Status == "Published") return;
            var head = await db.Set<SliReportHeadEntity>().SingleAsync(value => value.Id == operation.HeadId, token);
            if (head.ActiveOperationId != operationId || head.Generation != operation.ExpectedGeneration || head.LatestVersionId != operation.PredecessorId)
                throw new SliReportRejectedException("Generation");
            var scope = SliReportPolicy.Read<SliReportScope>(Encoding.UTF8.GetBytes(operation.ScopeJson));
            var snapshot = await reader.ReadAsync(scope, head.PeriodStart, token);
            if (SliReportPolicy.Hash(SliReportPolicy.Serialize(snapshot.Scope)) != SliReportPolicy.Hash(SliReportPolicy.Serialize(scope))
                || snapshot.PeriodStart != head.PeriodStart) throw new SliReportRejectedException("Integrity");
            var id = Guid.CreateVersion7(); var revision = checked(head.Generation + 1);
            var artifact = SliReportCalculator.Calculate(snapshot, id, revision, operation.PredecessorId, limits, token);
            var bytes = SliReportPolicy.Serialize(artifact, limits.MaximumBytes, token);
            if (bytes.Length > limits.MaximumBytes || (long)snapshot.Sources.Length + snapshot.Events.Length > limits.MaximumFacts) throw new SliReportRejectedException("Bounds");
            var now = clock.GetUtcNow();
            db.Add(new SliReportVersionEntity { Id = id, HeadId = head.Id, OperationId = operationId, Revision = revision,
                PredecessorId = operation.PredecessorId, ScopeJson = operation.ScopeJson, PolicyVersion = SliReportPolicy.CalculationVersion,
                MetadataJson = JsonSerializer.Serialize(artifact.Metadata, SliReportPolicy.Json), SourceDigest = artifact.Metadata.SourceDigest,
                SourceCount = artifact.Sources.Length, Artifact = bytes, ArtifactSha256 = SliReportPolicy.Hash(bytes), PublishedAt = now });
            await db.SaveChangesAsync(token);
            db.AddRange(artifact.Sources.Select(value => new SliReportSourceEntity { VersionId = id, ManifestId = value.Id,
                SourceJson = JsonSerializer.Serialize(value, SliReportPolicy.Json) }));
            // A retry may replace a previous failed attempt number only by a fresh job attempt.
            if (!await db.Set<SliReportAttemptEntity>().AnyAsync(value => value.OperationId == operationId && value.AttemptNumber == attemptNumber, token))
                db.Add(new SliReportAttemptEntity { Id = Guid.CreateVersion7(), OperationId = operationId,
                    AttemptNumber = attemptNumber, Outcome = "Published", RecordedAt = now });
            await db.SaveChangesAsync(token);
            operation.Status = "Published"; operation.VersionId = id; operation.FailureKind = null;
            head.Generation = revision; head.LatestVersionId = id;
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(null, operation.ActorId, operation.PredecessorId is null ? "sli.report.published" : "sli.report.revised",
                "sli_report_version", id, null, JsonSerializer.Serialize(new { operationId, revision, artifact.Metadata.SourceDigest })), token);
            await outbox.EnqueueAsync(PublishedEvent, JsonSerializer.Serialize(new { operationId, versionId = id }), cancellationToken: token);
            token.ThrowIfCancellationRequested(); await tx.CommitAsync(token);
        }
        catch (Exception exception)
        {
            db.ChangeTracker.Clear();
            var code = exception is SliReportRejectedException rejected ? rejected.Code
                : exception is OperationCanceledException ? ct.IsCancellationRequested ? "Canceled" : "Timeout" : "Dependency";
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RecordFailureAsync(operationId, attemptNumber, code, cleanup.Token);
            if (await db.Set<SliReportOperationEntity>().AsNoTracking().AnyAsync(value => value.Id == operationId && value.Status == "Published", cleanup.Token)) return;
            throw;
        }
    }

    public async Task<SliReportOperation?> OperationAsync(Guid id, CancellationToken ct = default) =>
        await (from op in db.Set<SliReportOperationEntity>().AsNoTracking() join head in db.Set<SliReportHeadEntity>() on op.HeadId equals head.Id
            where op.Id == id select new SliReportOperation(op.Id, head.ScopeId, head.PeriodStart, op.Status,
                op.ExpectedGeneration, op.VersionId, op.FailureKind)).SingleOrDefaultAsync(ct);

    public async Task<SliReportArtifact?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Set<SliReportVersionEntity>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, ct);
        if (row is null) return null;
        if (row.Artifact.Length > limits.MaximumBytes || SliReportPolicy.Hash(row.Artifact) != row.ArtifactSha256) throw new SliReportRejectedException("Integrity");
        var result = SliReportPolicy.Read<SliReportArtifact>(row.Artifact);
        if (SliReportPolicy.Hash(SliReportPolicy.Serialize(result.Sources)) != row.SourceDigest) throw new SliReportRejectedException("Integrity");
        return result;
    }

    public async Task<IReadOnlyList<SliReportMetadata>> ListAsync(string scopeId, DateTimeOffset beforeMonth, long beforeRevision, int take, CancellationToken ct = default)
    {
        if (!ObservationLabel(scopeId) || take is < 1 or > 101) throw new ArgumentException("Invalid list bounds.");
        var rows = await (from version in db.Set<SliReportVersionEntity>().AsNoTracking() join head in db.Set<SliReportHeadEntity>() on version.HeadId equals head.Id
            where head.ScopeId == scopeId && (head.PeriodStart < beforeMonth || head.PeriodStart == beforeMonth && version.Revision < beforeRevision)
            orderby head.PeriodStart descending, version.Revision descending select version.MetadataJson).Take(take).ToArrayAsync(ct);
        return rows.Select(value => SliReportPolicy.Read<SliReportMetadata>(Encoding.UTF8.GetBytes(value))).ToArray();
    }
    private static bool ObservationLabel(string value) => UZLLM.Observability.ObservationProtocol.IsLabel(value);
    private Task<int> LockHeadAsync(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops.sli_report_head SET generation=generation WHERE id={id}", ct);
    private void RequireNoTransaction()
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("SLI publication owns its transaction.");
    }
    private static SliReportOperation ToContract(SliReportOperationEntity op, SliReportHeadEntity head) =>
        new(op.Id, head.ScopeId, head.PeriodStart, op.Status, op.ExpectedGeneration, op.VersionId, op.FailureKind);
    private async Task RecordFailureAsync(Guid id, int attempt, string code, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Set<SliReportOperationEntity>().Where(value => value.Id == id && value.Status != "Published")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Failed").SetProperty(value => value.FailureKind, code), ct);
        if (changed != 0 && !await db.Set<SliReportAttemptEntity>().AnyAsync(value => value.OperationId == id && value.AttemptNumber == attempt, ct))
        {
            db.Add(new SliReportAttemptEntity { Id = Guid.CreateVersion7(), OperationId = id, AttemptNumber = attempt,
                Outcome = "Failed", FailureKind = code, RecordedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
