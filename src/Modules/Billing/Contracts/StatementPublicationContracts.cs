namespace UZLLM.Modules.Billing.Contracts;

/// <summary>Internal trusted operator command, not an HTTP authorization facade.</summary>
public sealed record StatementBuildCommand(Guid OrganizationId, Guid ActorAccountId, StatementPeriod Period,
    Guid IdempotencyKey, long ExpectedGeneration, Guid? PredecessorId, string Reason, int SchemaVersion = 1);

public sealed record StatementBuildOperation(Guid Id, Guid OrganizationId, Guid HeadId, string Status,
    long ExpectedGeneration, Guid? VersionId, string? FailureKind);

public sealed record PublishedStatement(Guid Id, Guid OrganizationId, long Revision, Guid? PredecessorId,
    string SourceDigest, string SnapshotJson, byte[] JsonArtifact, byte[] CsvArtifact, string JsonSha256, string CsvSha256);

public interface IStatementPublicationStore
{
    Task<StatementBuildOperation> RequestAsync(StatementBuildCommand command, CancellationToken cancellationToken = default);
    Task<StatementBuildOperation?> FindOperationAsync(Guid organizationId, Guid operationId, CancellationToken cancellationToken = default);
    Task<PublishedStatement?> FindPublishedAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken = default);
    Task BuildAsync(Guid operationId, int attemptNumber, CancellationToken cancellationToken = default);
}

public sealed class StatementCommandConflictException() : InvalidOperationException("Statement command conflicts with recorded terms or generation.");
public sealed class StatementBuildRejectedException(string code) : InvalidOperationException("Statement build rejected.")
{
    public string Code { get; } = code;
}

public sealed record StatementBuildLimits(int MaximumSources = 100_000, int MaximumBytes = 32 * 1024 * 1024,
    int DeadlineMilliseconds = 30_000)
{
    public void Validate()
    {
        if (MaximumSources is < 1 or > 100_000 || MaximumBytes is < 1 or > 32 * 1024 * 1024
            || DeadlineMilliseconds is < 1 or > 30_000) throw new ArgumentOutOfRangeException(nameof(MaximumSources));
    }
}

public sealed record StatementSourceData(string Kind, Guid Id, string PayloadJson, string Sha256);
public sealed record StatementLineData(int Ordinal, Guid LedgerId, string PayloadJson);
public sealed record StatementArtifact(StatementReconciliation Reconciliation, string SnapshotJson,
    IReadOnlyList<StatementSourceData> Sources, IReadOnlyList<StatementLineData> Lines,
    byte[] Json, byte[] Csv, long TotalBytes);

public interface IStatementArtifactBuilder
{
    StatementArtifact Build(StatementSnapshot snapshot, Guid versionId, long revision,
        Guid? predecessorId, StatementSnapshot? predecessor, StatementBuildLimits limits);
}
