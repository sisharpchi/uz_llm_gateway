using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

/// <summary>Owns its transactions; called only by a trusted command facade or leased Worker.</summary>
public sealed class PostgreSqlStatementPublicationStore(FoundationDbContext db, IStatementSourceReader sources,
    IStatementArtifactBuilder artifacts, IAuditTrail audit, IOutboxStore outbox, ILeasedJobStore jobs,
    TimeProvider clock, StatementBuildLimits limits) : IStatementPublicationStore
{
    public const string JobType = "billing.statement.build";
    public const string PublishedEvent = "billing.statement.published";

    public async Task<StatementBuildOperation> RequestAsync(StatementBuildCommand input, CancellationToken cancellationToken = default)
    {
        RequireNoTransaction();
        var command = StatementCommandPolicy.Normalize(input);
        var hash = StatementCommandPolicy.Hash(command);
        var kind = command.PredecessorId is null ? "Build" : "Revision";
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize receipt identities across periods as well as the report head. No wallet lock.
        var receiptLock = $"statement:{command.OrganizationId}:{kind}:{command.IdempotencyKey}";
        _ = await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_advisory_xact_lock(hashtextextended({receiptLock}, 0))").SingleAsync(cancellationToken);
        var prior = await db.Set<BillingStatementReceiptEntity>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.OrganizationId == command.OrganizationId && value.OperationKind == kind && value.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.CommandSha256 != hash) throw new StatementCommandConflictException();
            return (await FindOperationAsync(command.OrganizationId, prior.OperationId, cancellationToken))!;
        }
        var headId = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing.statement_head (id, organization_id, period_start, kind, generation)
            VALUES ({headId}, {command.OrganizationId}, {command.Period.From}, 'AccountStatement', 0)
            ON CONFLICT (organization_id, period_start, kind) DO NOTHING
            """, cancellationToken);
        headId = await db.Set<BillingStatementHeadEntity>().Where(value => value.OrganizationId == command.OrganizationId
            && value.PeriodStart == command.Period.From).Select(value => value.Id).SingleAsync(cancellationToken);
        await LockHeadAsync(headId, cancellationToken);
        var head = await db.Set<BillingStatementHeadEntity>().SingleAsync(value => value.Id == headId, cancellationToken);
        if (head.Generation != command.ExpectedGeneration || head.LatestVersionId != command.PredecessorId)
            throw new StatementCommandConflictException();
        if (head.ActiveOperationId is { } active && await db.Set<BillingStatementOperationEntity>().AnyAsync(value =>
            value.Id == active && value.Status == "Pending", cancellationToken)) throw new StatementCommandConflictException();
        var now = clock.GetUtcNow();
        var operation = new BillingStatementOperationEntity { Id = Guid.CreateVersion7(), OrganizationId = command.OrganizationId,
            HeadId = head.Id, ExpectedGeneration = head.Generation, PredecessorId = command.PredecessorId,
            RequestedByAccountId = command.ActorAccountId, Reason = command.Reason, CreatedAt = now };
        db.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        db.Add(new BillingStatementReceiptEntity { Id = Guid.CreateVersion7(), OrganizationId = command.OrganizationId,
            OperationId = operation.Id, OperationKind = kind, IdempotencyKey = command.IdempotencyKey, CommandSha256 = hash, CreatedAt = now });
        head.ActiveOperationId = operation.Id;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new(command.OrganizationId, command.ActorAccountId, "statement.requested", "statement_operation", operation.Id,
            null, JsonSerializer.Serialize(new { operation.Id, kind, command.ExpectedGeneration })), cancellationToken);
        await jobs.ScheduleAsync(JobType, JsonSerializer.Serialize(new { operationId = operation.Id }), operation.Id.ToString("N"),
            now, maxAttempts: 5, cancellationToken: cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToContract(operation);
    }

    public async Task<StatementBuildOperation?> FindOperationAsync(Guid organizationId, Guid operationId, CancellationToken cancellationToken = default)
    {
        var value = await db.Set<BillingStatementOperationEntity>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.OrganizationId == organizationId && value.Id == operationId, cancellationToken);
        return value is null ? null : ToContract(value);
    }

    public async Task<PublishedStatement?> FindPublishedAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken = default) =>
        await db.Set<BillingStatementVersionEntity>().AsNoTracking().Where(value => value.OrganizationId == organizationId && value.Id == versionId)
            .Select(value => new PublishedStatement(value.Id, value.OrganizationId, value.Revision, value.PredecessorId,
                value.SourceDigest, value.SnapshotJson, value.JsonArtifact, value.CsvArtifact, value.JsonSha256, value.CsvSha256)).SingleOrDefaultAsync(cancellationToken);

    public async Task BuildAsync(Guid operationId, int attemptNumber, CancellationToken cancellationToken = default)
    {
        RequireNoTransaction();
        limits.Validate();
        if (operationId == Guid.Empty || attemptNumber is < 1 or > 5) throw new ArgumentException("Invalid statement job identity.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.DeadlineMilliseconds);
        var token = deadline.Token;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            var operation = await db.Set<BillingStatementOperationEntity>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == operationId, token)
                ?? throw new StatementBuildRejectedException("NotFound");
            await LockHeadAsync(operation.HeadId, token);
            db.ChangeTracker.Clear();
            operation = await db.Set<BillingStatementOperationEntity>().SingleAsync(value => value.Id == operationId, token);
            if (operation.Status == "Published") return; // Lost response/duplicate delivery: no second publication.
            var head = await db.Set<BillingStatementHeadEntity>().SingleAsync(value => value.Id == operation.HeadId && value.OrganizationId == operation.OrganizationId, token);
            if (head.ActiveOperationId != operation.Id || head.Generation != operation.ExpectedGeneration || head.LatestVersionId != operation.PredecessorId)
                throw new StatementBuildRejectedException("Generation");
            var snapshot = await sources.ReadAsync(operation.OrganizationId, StatementPeriod.At(head.PeriodStart), token)
                ?? throw new StatementBuildRejectedException("NotFound");
            StatementSnapshot? predecessor = null;
            if (operation.PredecessorId is { } previousId)
            {
                var previous = await FindPublishedAsync(operation.OrganizationId, previousId, token) ?? throw new StatementBuildRejectedException("Generation");
                predecessor = StatementSnapshotCodec.Deserialize(previous.SnapshotJson);
                var verification = StatementReconciler.Reconcile(predecessor);
                if (!verification.FinanciallyReconciled || verification.SourceDigest != previous.SourceDigest)
                    throw new StatementBuildRejectedException("SourceDigest");
            }
            var versionId = Guid.CreateVersion7();
            var revision = checked(head.Generation + 1);
            var artifact = artifacts.Build(snapshot, versionId, revision, operation.PredecessorId, predecessor, limits);
            token.ThrowIfCancellationRequested();
            if (artifact.Reconciliation.SourceDigest != StatementReconciler.Reconcile(snapshot).SourceDigest
                || artifact.SnapshotJson != StatementSnapshotCodec.Serialize(snapshot)
                || !artifact.Sources.SequenceEqual(StatementArtifactBuilder.Sources(snapshot))) throw new StatementBuildRejectedException("SourceDigest");
            var now = clock.GetUtcNow();
            db.Add(new BillingStatementVersionEntity { Id = versionId, OrganizationId = head.OrganizationId, HeadId = head.Id,
                OperationId = operation.Id, Revision = revision, PredecessorId = operation.PredecessorId,
                CalculationVersion = StatementReconciliation.CalculationVersion, SnapshotCapturedAt = snapshot.CapturedAt,
                SourceDigest = artifact.Reconciliation.SourceDigest, SnapshotJson = artifact.SnapshotJson,
                JsonArtifact = artifact.Json, CsvArtifact = artifact.Csv, JsonSha256 = StatementSnapshotCodec.Hash(artifact.Json),
                CsvSha256 = StatementSnapshotCodec.Hash(artifact.Csv), SourceCount = artifact.Sources.Count,
                LineCount = artifact.Lines.Count, TotalBytes = artifact.TotalBytes, PublishedAt = now });
            await db.SaveChangesAsync(token);
            db.AddRange(artifact.Sources.Select(value => new BillingStatementSourceEntity { VersionId = versionId, OrganizationId = head.OrganizationId,
                Kind = value.Kind, SourceId = value.Id, PayloadJson = value.PayloadJson, PayloadSha256 = value.Sha256 }));
            db.AddRange(artifact.Lines.Select(value => new BillingStatementLineEntity { VersionId = versionId, OrganizationId = head.OrganizationId,
                Ordinal = value.Ordinal, LedgerId = value.LedgerId, PayloadJson = value.PayloadJson }));
            db.Add(new BillingStatementAttemptEntity { Id = Guid.CreateVersion7(), OrganizationId = head.OrganizationId,
                OperationId = operation.Id, AttemptNumber = attemptNumber, Outcome = "Published", RecordedAt = now });
            // Insert membership while the operation is still unpublished; DB seals further inserts after this transition.
            await db.SaveChangesAsync(token);
            head.Generation = revision; head.LatestVersionId = versionId;
            operation.Status = "Published"; operation.PublishedVersionId = versionId; operation.FailureKind = null;
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(head.OrganizationId, operation.RequestedByAccountId,
                operation.PredecessorId is null ? "statement.published" : "statement.revised", "statement_version", versionId,
                null, JsonSerializer.Serialize(new { operationId, revision, artifact.Reconciliation.SourceDigest })), token);
            await outbox.EnqueueAsync(PublishedEvent, JsonSerializer.Serialize(new { operationId, versionId, organizationId = head.OrganizationId }), cancellationToken: token);
            token.ThrowIfCancellationRequested();
            await tx.CommitAsync(token);
        }
        catch (Exception exception)
        {
            db.ChangeTracker.Clear();
            var code = exception is StatementBuildRejectedException rejected ? rejected.Code
                : exception is OperationCanceledException ? (cancellationToken.IsCancellationRequested ? "Canceled" : "Timeout") : "Dependency";
            // Own bounded cleanup; caller cancellation must not erase failure visibility or retry identity.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RecordFailureAsync(operationId, attemptNumber, code, cleanup.Token);
            // A concurrent winner or lost commit response may already have committed this exact operation.
            if (await db.Set<BillingStatementOperationEntity>().AsNoTracking().AnyAsync(value => value.Id == operationId && value.Status == "Published", cleanup.Token)) return;
            throw;
        }
    }

    private async Task RecordFailureAsync(Guid operationId, int attempt, string code, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var changed = await db.Set<BillingStatementOperationEntity>().Where(value => value.Id == operationId && value.Status != "Published")
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, "Failed").SetProperty(value => value.FailureKind, code), token);
        if (changed != 0 && !await db.Set<BillingStatementAttemptEntity>().AnyAsync(value => value.OperationId == operationId && value.AttemptNumber == attempt, token))
        {
            var org = await db.Set<BillingStatementOperationEntity>().Where(value => value.Id == operationId).Select(value => value.OrganizationId).SingleAsync(token);
            db.Add(new BillingStatementAttemptEntity { Id = Guid.CreateVersion7(), OrganizationId = org, OperationId = operationId,
                AttemptNumber = attempt, Outcome = "Failed", FailureKind = code, RecordedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }

    private Task<int> LockHeadAsync(Guid headId, CancellationToken token) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE billing.statement_head SET generation = generation WHERE id = {headId}", token);
    private void RequireNoTransaction()
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Statement publication owns its transaction; nested scopes are forbidden.");
    }
    private static StatementBuildOperation ToContract(BillingStatementOperationEntity value) => new(value.Id,
        value.OrganizationId, value.HeadId, value.Status, value.ExpectedGeneration, value.PublishedVersionId, value.FailureKind);
}
