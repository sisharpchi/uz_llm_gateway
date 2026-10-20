using UZLLM.Observability;

namespace UZLLM.Modules.Usage.Contracts;

public enum SliImportStatus { Accepted, Duplicate, Conflict }
[Flags]
public enum SliCoverageWarning
{
    None = 0, MissingPredecessor = 1, SequenceGap = 2, Dropped = 4, ClockUnverified = 8,
    TerminalPartial = 16, EpochBoundary = 32, ClockSkew = 64, CollectorDiscontinuous = 128,
    ZeroTraffic = 256, ProbeUnreachable = 512, LateArrival = 1024
}
public sealed record SliImportResult(SliImportStatus Status, Guid? ManifestId, SliCoverageWarning Warnings);
public interface ISliManifestImporter
{
    Task<SliImportResult> ImportAsync(ReadOnlyMemory<byte> signedManifest, CancellationToken cancellationToken = default);
}
public sealed record SliCollectorTrust(IReadOnlyDictionary<string, ObservationProducerTrust> Producers);
