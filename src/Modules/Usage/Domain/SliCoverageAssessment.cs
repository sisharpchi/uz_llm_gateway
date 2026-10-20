using UZLLM.Modules.Usage.Contracts;
using UZLLM.Observability;

namespace UZLLM.Modules.Usage.Domain;

public sealed record SliImportPredecessor(long LastSequence, DateTimeOffset IntervalEnd);

// Evidence-quality facts at import time, NOT H/G/P/C/U classification or a ratio.
// Later arrival may fill gaps; immutable old receipts are not rewritten.
public static class SliCoverageAssessment
{
    public static SliCoverageWarning Evaluate(ObservationManifest batch, SliImportPredecessor? previous, DateTimeOffset now)
    {
        var warnings = SliCoverageWarning.None;
        if (batch.BatchNumber == 1)
        {
            warnings |= SliCoverageWarning.EpochBoundary;
            if (batch.FirstSequence != 1) warnings |= SliCoverageWarning.SequenceGap;
        }
        else if (previous is null) warnings |= SliCoverageWarning.MissingPredecessor;
        if (previous is not null && (batch.FirstSequence != previous.LastSequence + 1
            || batch.IntervalStart > previous.IntervalEnd)) warnings |= SliCoverageWarning.SequenceGap;
        if (batch.Dropped > 0) warnings |= SliCoverageWarning.Dropped;
        if (!batch.ClockSynchronized) warnings |= SliCoverageWarning.ClockUnverified;
        if (batch.TerminalCoverage != "NotApplicable") warnings |= SliCoverageWarning.TerminalPartial;
        if (!batch.CollectorContinuous) warnings |= SliCoverageWarning.CollectorDiscontinuous;
        if (batch.Observations.Length == 0) warnings |= SliCoverageWarning.ZeroTraffic;
        if (batch.Source == ObservationSource.Probe && batch.Observations.Any(value => value.HttpStatus is < 200 or >= 300))
            warnings |= SliCoverageWarning.ProbeUnreachable;
        if (batch.IntervalEnd > now.AddMinutes(1)) warnings |= SliCoverageWarning.ClockSkew;
        if (now - batch.IntervalEnd > TimeSpan.FromMinutes(2)) warnings |= SliCoverageWarning.LateArrival;
        return warnings;
    }
}
