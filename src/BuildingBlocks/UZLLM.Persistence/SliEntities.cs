namespace UZLLM.Persistence;

public sealed class SliManifestEntity
{
    public Guid Id { get; set; }
    public string Producer { get; set; } = "";
    public Guid Epoch { get; set; }
    public long BatchNumber { get; set; }
    public string Source { get; set; } = "";
    public string Region { get; set; } = "";
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public string Sha256 { get; set; } = "";
    public int ObservationCount { get; set; }
    public long Dropped { get; set; }
    public DateTimeOffset IntervalStart { get; set; }
    public DateTimeOffset IntervalEnd { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool ClockSynchronized { get; set; }
    public bool CollectorContinuous { get; set; }
    public string TerminalCoverage { get; set; } = "";
    public int CoverageWarnings { get; set; }
}

public sealed class SliObservationEntity
{
    public Guid ManifestId { get; set; }
    public string Producer { get; set; } = "";
    public Guid Epoch { get; set; }
    public long Sequence { get; set; }
    public Guid ExecutionId { get; set; }
    public string EventKey { get; set; } = "";
    public string Operation { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset At { get; set; }
    public string PayloadJson { get; set; } = "";
}

// Append-only import/duplicate/conflict/expiry audit, independent of customer accounts.
public sealed class SliImportReceiptEntity
{
    public Guid Id { get; set; }
    public Guid? ManifestId { get; set; }
    public string Producer { get; set; } = "";
    public Guid Epoch { get; set; }
    public long BatchNumber { get; set; }
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public int ObservationCount { get; set; }
}
