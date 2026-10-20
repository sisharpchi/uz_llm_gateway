using UZLLM.Observability;

namespace UZLLM.Modules.Usage.Contracts;

/// <summary>Versioned deployment scope, never a caller-supplied query or exclusion.</summary>
public sealed record SliReportScope(string Id, string Region, string Operation, string Mode,
    SliRequiredProducer[] Producers, int Version = 1, string? IndependentProbeEvidence = null,
    int ProbeMaximumGapSeconds = 300);
/// <summary>Required deployment producer and authenticated source kind, not an inferred customer label.</summary>
public sealed record SliRequiredProducer(string Producer, string Source);
public sealed record SliReportProfiles(IReadOnlyDictionary<string, SliReportScope> Scopes);
/// <summary>Internal trusted command; Management must authorize the real actor.</summary>
public sealed record SliReportCommand(Guid ActorId, string ScopeId, DateTimeOffset PeriodStart,
    Guid IdempotencyKey, long ExpectedGeneration, Guid? PredecessorId, string Reason);
/// <summary>Stable asynchronous operation; failure does not replace its identity.</summary>
public sealed record SliReportOperation(Guid Id, string ScopeId, DateTimeOffset PeriodStart,
    string Status, long ExpectedGeneration, Guid? VersionId, string? FailureKind);
/// <summary>Exact request population, not a count of provider attempts or charges.</summary>
public sealed record SliCounts(long H, long G, long P, long C, long U);
/// <summary>Exact integer fraction; display rounding never determines target compliance.</summary>
public sealed record SliRatio(long Numerator, long Denominator, decimal? Value, string? Reason);
/// <summary>Engineering evidence only, never a contractual SLA certificate.</summary>
public sealed record SliReportMetadata(Guid Id, string ScopeId, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    long Revision, Guid? PredecessorId, string PolicyVersion, DateTimeOffset CapturedAt,
    string SourceDigest, SliCounts Counts, SliRatio GatewayKnown, SliRatio GatewayConservative,
    SliRatio EndToEnd, string Coverage, string TargetStatus, string[] Warnings,
    int SourceCount, int ExecutionCount, string Label = "Operational SLI — not a contractual SLA",
    string PopulationBasis = "ObservedExecutionsOnly", string Kind = "OperationalSliReport");
/// <summary>Manifest membership survives raw diagnostic expiry.</summary>
public sealed record SliReportSource(Guid Id, string Producer, Guid Epoch, long BatchNumber,
    string Source, string Region, long FirstSequence, long LastSequence, int ObservationCount,
    long Dropped, DateTimeOffset IntervalStart, DateTimeOffset IntervalEnd, DateTimeOffset ReceivedAt,
    bool ClockSynchronized, bool CollectorContinuous, string Sha256, int ReadCount, bool Conflict,
    Guid[]? ConflictReceipts = null);
/// <summary>Allowlisted classification proof; no customer content or financial inference.</summary>
public sealed record SliClassifiedExecution(Guid ExecutionId, DateTimeOffset StartedAt, string Mode, string Class, string Reason);
/// <summary>Attempt facts have their own population; absence of error is not protocol success.</summary>
public sealed record SliAttemptCounts(string Credential, string Fault, long Count);
public sealed record SliObservedEvent(Guid ManifestId, string Producer, string Source, ExecutionObservation Observation);
public sealed record SliReportSnapshot(SliReportScope Scope, DateTimeOffset PeriodStart, DateTimeOffset CapturedAt,
    SliReportSource[] Sources, SliObservedEvent[] Events);
public sealed record SliReportArtifact(SliReportMetadata Metadata, SliReportScope Scope, SliReportSource[] Sources,
    SliClassifiedExecution[] Executions, SliAttemptCounts[] Attempts);
public interface ISliReportSourceReader
{
    Task<SliReportSnapshot> ReadAsync(SliReportScope scope, DateTimeOffset periodStart, CancellationToken ct);
}
public sealed record SliReportLimits(int MaximumFacts = 100_000, int MaximumBytes = 32 * 1024 * 1024,
    int DeadlineMilliseconds = 30_000)
{
    public void Validate()
    {
        if (MaximumFacts is < 1 or > 100_000 || MaximumBytes is < 1 or > 32 * 1024 * 1024
            || DeadlineMilliseconds is < 1 or > 30_000) throw new ArgumentOutOfRangeException(nameof(MaximumFacts));
    }
}
public interface ISliReportStore
{
    Task<SliReportOperation> RequestAsync(SliReportCommand command, CancellationToken ct = default);
    Task BuildAsync(Guid operationId, int attemptNumber, CancellationToken ct = default);
    Task<SliReportOperation?> OperationAsync(Guid id, CancellationToken ct = default);
    Task<SliReportArtifact?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SliReportMetadata>> ListAsync(string scopeId, DateTimeOffset beforeMonth,
        long beforeRevision, int take, CancellationToken ct = default);
}
public sealed class SliReportConflictException : Exception;
public sealed class SliReportRejectedException(string code) : Exception("SLI report rejected.")
{
    public string Code { get; } = code;
}
