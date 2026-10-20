using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Observability;

namespace UZLLM.Usage.Tests;

public sealed class SliCoverageAssessmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("normal", SliCoverageWarning.None)]
    [InlineData("epoch", SliCoverageWarning.EpochBoundary)]
    [InlineData("skipped-epoch-sequence", SliCoverageWarning.EpochBoundary | SliCoverageWarning.SequenceGap)]
    [InlineData("missing", SliCoverageWarning.MissingPredecessor)]
    [InlineData("sequence", SliCoverageWarning.SequenceGap)]
    [InlineData("interval", SliCoverageWarning.SequenceGap)]
    [InlineData("drop", SliCoverageWarning.Dropped)]
    [InlineData("clock", SliCoverageWarning.ClockUnverified)]
    [InlineData("terminal", SliCoverageWarning.TerminalPartial)]
    [InlineData("collector", SliCoverageWarning.CollectorDiscontinuous)]
    [InlineData("zero", SliCoverageWarning.ZeroTraffic)]
    [InlineData("probe", SliCoverageWarning.ProbeUnreachable)]
    [InlineData("future-boundary", SliCoverageWarning.None)]
    [InlineData("future-after", SliCoverageWarning.ClockSkew)]
    [InlineData("late-boundary", SliCoverageWarning.None)]
    [InlineData("late-after", SliCoverageWarning.LateArrival)]
    public void Quality_assessment_preserves_exact_warning_partitions_without_classifying_SLA(string variant, SliCoverageWarning expected)
    {
        var observation = new ExecutionObservation(1, Guid.NewGuid(), ObservationPhase.Probe,
            ObservationOperation.Readiness, ObservationMode.Unknown, Now, Now, false, 200, false, false, false, false,
            ObservationProviderFault.None, ObservationCredential.Unknown);
        var batch = new ObservationManifest(1, Guid.NewGuid(), 2, ObservationSource.Probe, "test",
            1, 1, Now, Now, 0, true, true, "NotApplicable", [observation]);
        SliImportPredecessor? previous = new(0, Now);
        var now = Now;
        switch (variant)
        {
            case "epoch": batch = batch with { BatchNumber = 1 }; break;
            case "skipped-epoch-sequence": batch = batch with { BatchNumber = 1, FirstSequence = 2, LastSequence = 2,
                Observations = [observation with { Sequence = 2 }] }; break;
            case "missing": previous = null; break;
            case "sequence": previous = new(2, Now); break;
            case "interval": previous = new(0, Now.AddTicks(-1)); break;
            case "drop": batch = batch with { Dropped = 1, LastSequence = 2 }; break;
            case "clock": batch = batch with { ClockSynchronized = false }; break;
            case "terminal": batch = batch with { TerminalCoverage = "Partial" }; break;
            case "collector": batch = batch with { CollectorContinuous = false }; break;
            case "zero": batch = batch with { LastSequence = 0, Observations = [] }; break;
            case "probe": batch = batch with { Observations = [observation with { HttpStatus = 0 }] }; break;
            case "future-boundary": batch = batch with { IntervalEnd = Now.AddMinutes(1) }; break;
            case "future-after": batch = batch with { IntervalEnd = Now.AddMinutes(1).AddTicks(1) }; break;
            case "late-boundary": now = Now.AddMinutes(2); break;
            case "late-after": now = Now.AddMinutes(2).AddTicks(1); break;
        }
        Assert.Equal(expected, SliCoverageAssessment.Evaluate(batch, previous, now));
    }
}
