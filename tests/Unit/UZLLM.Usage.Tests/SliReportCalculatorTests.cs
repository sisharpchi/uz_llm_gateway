using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Observability;

namespace UZLLM.Usage.Tests;

public sealed class SliReportCalculatorTests
{
    private static readonly DateTimeOffset Month = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At = Month.AddMinutes(1);
    private static readonly SliReportScope Scope = new("test-chat", "test", "Chat", "All",
        [new("edge", "Edge"), new("gateway", "Gateway"), new("probe", "Probe")], IndependentProbeEvidence: "synthetic-independent");

    [Theory]
    [InlineData("success", "H", "ProtocolCompleted")]
    [InlineData("db", "G", "GatewayFailure")]
    [InlineData("redis", "G", "GatewayFailure")]
    [InlineData("shutdown", "G", "GatewayFailure")]
    [InlineData("capacity", "G", "GatewayFailure")]
    [InlineData("ambiguous429", "U", "OutcomeUnproven")]
    [InlineData("policy", "C", "PolicyRejected")]
    [InlineData("provider", "P", "VerifiedProviderRejection")]
    [InlineData("provider-gateway", "G", "GatewayFailure")]
    [InlineData("provider-unverified", "U", "ProviderOriginUnproven")]
    [InlineData("byok", "C", "ByokCredentialRejected")]
    [InlineData("managed", "G", "PlatformCredentialRejected")]
    [InlineData("hybrid", "G", "PlatformCredentialRejected")]
    [InlineData("timeout", "U", "ProviderOriginUnproven")]
    [InlineData("partial", "U", "OutcomeUnproven")]
    [InlineData("unclosed", "U", "UnclosedExecution")]
    [InlineData("edge", "G", "EdgeFailure")]
    [InlineData("orphan", "U", "CorrelationConflict")]
    [InlineData("conflict", "U", "ConflictingEvents")]
    [InlineData("forged", "U", "CorrelationConflict")]
    public void Classifier_preserves_origin_credential_capacity_and_protocol_partitions(string variant, string expected, string reason)
    {
        var terminal = Event(ObservationPhase.Terminal) with { TerminalSuccess = true };
        var edge = Event(ObservationPhase.Terminal) with { Mode = ObservationMode.Unknown };
        var attempt = Event(ObservationPhase.Attempt) with { AttemptId = Guid.NewGuid() };
        if (variant != "success") terminal = terminal with { TerminalSuccess = false };
        if (variant is "db" or "redis" or "shutdown" or "capacity") terminal = terminal with { GatewayFault = true, HttpStatus = variant == "capacity" ? 429 : 503 };
        if (variant == "ambiguous429") terminal = terminal with { HttpStatus = 429 };
        if (variant == "policy") terminal = terminal with { PolicyRejection = true, HttpStatus = 402 };
        if (variant is "provider" or "provider-gateway" or "provider-unverified") terminal = terminal with { ProviderFault = ObservationProviderFault.Capacity,
            UpstreamStatus = 429, GatewayFault = variant == "provider-gateway" };
        if (variant is "byok" or "managed" or "hybrid") terminal = terminal with { ProviderFault = ObservationProviderFault.Authentication,
            UpstreamStatus = 401, Credential = variant == "byok" ? ObservationCredential.Byok : variant == "hybrid" ? ObservationCredential.Hybrid : ObservationCredential.Managed };
        if (variant == "timeout") terminal = terminal with { ProviderFault = ObservationProviderFault.Timeout, Certainty = ObservationExecutionCertainty.Unknown };
        attempt = attempt with { ProviderFault = terminal.ProviderFault, UpstreamStatus = terminal.UpstreamStatus,
            Credential = terminal.Credential == ObservationCredential.Hybrid ? ObservationCredential.Managed : terminal.Credential,
            Certainty = ObservationExecutionCertainty.RejectedBeforeExecution };
        if (variant == "provider-unverified") attempt = attempt with { Certainty = ObservationExecutionCertainty.Unknown };
        var rows = new List<(string, ExecutionObservation)> { ("edge", edge), ("gateway", Event(ObservationPhase.Started)), ("gateway", attempt), ("gateway", terminal) };
        if (variant is "unclosed" or "edge") rows.RemoveAt(3);
        if (variant == "edge") rows[0] = ("edge", edge with { GatewayFault = true, HttpStatus = 502 });
        if (variant == "orphan") rows.RemoveAt(0);
        if (variant == "conflict") rows.Add(("gateway", terminal with { HttpStatus = 500 }));
        if (variant == "forged") rows[1] = ("gateway", Event(ObservationPhase.Started) with { TrustedEdge = false });
        var result = Calculate(Snapshot(rows));
        var classified = Assert.Single(result.Executions);
        Assert.Equal(expected, classified.Class); Assert.Equal(reason, classified.Reason);
        Assert.Equal(1, result.Metadata.ExecutionCount);
        Assert.NotEqual("Met", result.Metadata.TargetStatus); // No invented coverage from this tiny snapshot.
    }

    [Theory]
    [InlineData(-1, "C")]
    [InlineData(0, "U")]
    [InlineData(1, "G")]
    public void Cancellation_order_precedes_or_preserves_service_failure_without_financial_assumptions(int seconds, string expected)
    {
        var terminal = Event(ObservationPhase.Terminal) with { ClientDisconnected = true, GatewayFault = true,
            GatewayFailureAt = At.AddSeconds(2), ClientDisconnectedAt = At.AddSeconds(2 + seconds), At = At.AddSeconds(5) };
        var result = Calculate(Snapshot([("edge", Event(ObservationPhase.Terminal)), ("gateway", terminal)]));
        Assert.Equal(expected, Assert.Single(result.Executions).Class);
        Assert.DoesNotContain("cost", System.Text.Json.JsonSerializer.Serialize(result).ToLowerInvariant());
    }

    [Fact]
    public void Exact_populations_provider_attempts_and_unknown_bound_match_the_approved_fixture()
    {
        var rows = new List<(string, ExecutionObservation)>();
        Add(840, "H"); Add(10, "G"); Add(50, "P"); Add(100, "C");
        var result = Calculate(Snapshot(rows));
        Assert.Equal(new SliCounts(840, 10, 50, 100, 0), result.Metadata.Counts);
        Assert.Equal((840L, 850L), (result.Metadata.GatewayKnown.Numerator, result.Metadata.GatewayKnown.Denominator));
        Assert.Equal((840L, 900L), (result.Metadata.EndToEnd.Numerator, result.Metadata.EndToEnd.Denominator));
        Assert.Equal(1000, result.Metadata.ExecutionCount);
        Assert.Equal(50, result.Attempts.Sum(value => value.Count));
        Add(5, "U"); result = Calculate(Snapshot(rows));
        Assert.Equal((840L, 855L), (result.Metadata.GatewayConservative.Numerator, result.Metadata.GatewayConservative.Denominator));
        Assert.Equal((840L, 905L), (result.Metadata.EndToEnd.Numerator, result.Metadata.EndToEnd.Denominator));
        Assert.Equal("Incomplete", result.Metadata.TargetStatus);
        void Add(int count, string classification)
        {
            for (var index = 0; index < count; index++)
            {
                var id = Guid.NewGuid(); var terminal = Event(ObservationPhase.Terminal) with { ExecutionId = id,
                    TerminalSuccess = classification == "H", GatewayFault = classification == "G", PolicyRejection = classification == "C", HttpStatus = classification == "C" ? 400 : 200,
                    ProviderFault = classification == "P" ? ObservationProviderFault.Capacity : ObservationProviderFault.None,
                    UpstreamStatus = classification == "P" ? 429 : null };
                rows.Add(("edge", Event(ObservationPhase.Terminal) with { ExecutionId = id }));
                rows.Add(("gateway", Event(ObservationPhase.Started) with { ExecutionId = id }));
                rows.Add(("gateway", terminal));
                if (classification == "P") rows.Add(("gateway", terminal with { Phase = ObservationPhase.Attempt,
                    AttemptId = Guid.NewGuid(), Certainty = ObservationExecutionCertainty.RejectedBeforeExecution }));
            }
        }
    }

    [Theory]
    [InlineData(998, 1, false)]
    [InlineData(999, 1, true)]
    [InlineData(1000, 1, true)]
    [InlineData(long.MaxValue, long.MaxValue, false)]
    public void Target_comparison_uses_exact_integer_boundary_not_rounded_display(long h, long g, bool met) => Assert.Equal(met, SliReportCalculator.MeetsTarget(h, g));

    [Fact]
    public void Empty_and_policy_only_denominators_remain_NoData_not_100_percent()
    {
        var empty = Calculate(Snapshot([]));
        Assert.Equal("NoData", empty.Metadata.Coverage); Assert.Equal("NoData", empty.Metadata.TargetStatus);
        Assert.Null(empty.Metadata.GatewayKnown.Value); Assert.Equal("NoData", empty.Metadata.GatewayKnown.Reason);
        Assert.Throws<ArgumentException>(() => SliReportCalculator.MeetsTarget(0, 0));
        Assert.Throws<ArgumentException>(() => SliReportCalculator.Ratio(-1, 1));
        Assert.Throws<ArgumentException>(() => SliReportCalculator.Ratio(2, 1));
        var policy = Calculate(Snapshot([("edge", Event(ObservationPhase.Terminal)),
            ("gateway", Event(ObservationPhase.Terminal) with { PolicyRejection = true, HttpStatus = 400 })]));
        Assert.Equal(1, policy.Metadata.Counts.C); Assert.Null(policy.Metadata.GatewayKnown.Value);
        Assert.Equal("NoData", policy.Metadata.GatewayKnown.Reason); Assert.Null(policy.Metadata.EndToEnd.Value);
    }

    [Fact]
    public void Late_terminal_and_duplicate_delivery_keep_start_month_and_one_logical_execution()
    {
        var start = Month.AddMonths(1).AddSeconds(-2);
        var edge = Event(ObservationPhase.Terminal) with { StartedAt = start, At = start.AddSeconds(5) };
        var terminal = edge with { TerminalSuccess = true };
        var rows = new[] { ("edge", edge), ("gateway", terminal with { Phase = ObservationPhase.Started, At = start, TerminalSuccess = false }),
            ("gateway", terminal), ("gateway", terminal with { Sequence = 999 }),
            ("gateway", terminal with { Phase = ObservationPhase.Attempt, TerminalSuccess = false, AttemptId = Guid.NewGuid() }) };
        var result = Calculate(Snapshot(rows));
        Assert.Equal("H", Assert.Single(result.Executions).Class); Assert.Equal(start, result.Executions[0].StartedAt);
        Assert.Equal(1, result.Attempts.Sum(value => value.Count));
        Assert.Empty(Calculate(Snapshot(rows) with { PeriodStart = Month.AddMonths(1), CapturedAt = Month.AddMonths(2) }).Executions);
    }

    [Fact]
    public void Complete_source_and_independent_probe_coverage_qualifies_only_the_known_exact_target()
    {
        var result = Calculate(CompleteSnapshot());
        Assert.Equal("Complete", result.Metadata.Coverage); Assert.Equal("Met", result.Metadata.TargetStatus);
        Assert.Empty(result.Metadata.Warnings); Assert.Equal(new SliCounts(1, 0, 0, 0, 0), result.Metadata.Counts);
    }

    [Theory]
    [InlineData("clock", "ClockUnverified")]
    [InlineData("drop", "Dropped")]
    [InlineData("raw", "RawEvidenceMissing")]
    [InlineData("conflict", "SourceConflict")]
    [InlineData("collector", "CollectorDiscontinuous")]
    [InlineData("missing", "MissingInterval")]
    [InlineData("probe", "ProbeUnreachable")]
    [InlineData("probe-flag", "ProbeUnreachable")]
    [InlineData("no-approval", "ProbeIndependenceUnverified")]
    [InlineData("probe-gap", "MissingProbeInterval")]
    public void Missing_clock_source_loss_and_probe_disagreement_never_qualify_a_good_ratio(string variant, string warning)
    {
        var snapshot = CompleteSnapshot(); var sources = snapshot.Sources.ToArray();
        if (variant == "clock") sources[0] = sources[0] with { ClockSynchronized = false };
        if (variant == "drop") sources[0] = sources[0] with { Dropped = 1 };
        if (variant == "raw") sources[0] = sources[0] with { ObservationCount = sources[0].ObservationCount + 1 };
        if (variant == "conflict") sources[0] = sources[0] with { Conflict = true };
        if (variant == "collector") sources[0] = sources[0] with { CollectorContinuous = false };
        if (variant == "missing") sources[0] = sources[0] with { IntervalStart = sources[0].IntervalStart.AddTicks(1) };
        if (variant == "no-approval") snapshot = snapshot with { Scope = Scope with { IndependentProbeEvidence = null } };
        if (variant is "probe" or "probe-gap") snapshot = snapshot with { Events = snapshot.Events.Select(value => value.Source == "Probe" && value.Observation.At == At
            ? value with { Observation = value.Observation with { HttpStatus = 0 } } : value).ToArray() };
        if (variant == "probe-flag") snapshot = snapshot with { Events = snapshot.Events.Select(value => value.Source == "Probe" && value.Observation.At == At
            ? value with { Observation = value.Observation with { GatewayFault = true } } : value).ToArray() };
        if (variant == "probe-gap") snapshot = snapshot with { Scope = Scope with { ProbeMaximumGapSeconds = 299 } };
        var result = Calculate(snapshot with { Sources = sources });
        Assert.Contains(warning, result.Metadata.Warnings); Assert.Equal("Incomplete", result.Metadata.Coverage);
        Assert.Equal("Incomplete", result.Metadata.TargetStatus);
        if (variant == "conflict")
        {
            Assert.Null(result.Metadata.GatewayKnown.Value); Assert.Equal(1, result.Metadata.Counts.U);
            Assert.Equal("SourceConflict", Assert.Single(result.Executions).Reason);
        }
        else Assert.Equal(1, result.Metadata.GatewayKnown.Value);
    }

    [Fact]
    public void Mode_scope_does_not_hide_unknown_or_conflicting_execution_evidence()
    {
        var snapshot = Snapshot([("edge", Event(ObservationPhase.Terminal) with { Mode = ObservationMode.Unknown }), ("gateway", Event(ObservationPhase.Terminal) with { Mode = ObservationMode.Unknown })]);
        var result = Calculate(snapshot with { Scope = Scope with { Mode = "Sse" } });
        Assert.Equal("U", Assert.Single(result.Executions).Class);
        Assert.Equal("ModeUnresolved", result.Executions[0].Reason);
        Assert.Empty(Calculate(snapshot with { Scope = Scope with { Operation = "Responses" } }).Executions);
        var conflict = Snapshot([("edge", Event(ObservationPhase.Terminal) with { Mode = ObservationMode.Unknown }),
            ("gateway", Event(ObservationPhase.Started) with { Mode = ObservationMode.Json }),
            ("gateway", Event(ObservationPhase.Terminal) with { Mode = ObservationMode.Sse, TerminalSuccess = true })]);
        Assert.Equal("U", Assert.Single(Calculate(conflict).Executions).Class);
        Assert.Equal("ModeConflict", Calculate(conflict).Executions[0].Reason);
    }

    [Fact]
    public void Same_execution_failover_and_attempt_replay_do_not_multiply_either_population()
    {
        var first = Event(ObservationPhase.Attempt) with { AttemptId = Guid.NewGuid(), ProviderFault = ObservationProviderFault.Capacity,
            UpstreamStatus = 429, Certainty = ObservationExecutionCertainty.RejectedBeforeExecution };
        var second = Event(ObservationPhase.Attempt) with { AttemptId = Guid.NewGuid() };
        var snapshot = Snapshot([("edge", Event(ObservationPhase.Terminal)), ("gateway", Event(ObservationPhase.Started)),
            ("gateway", first), ("gateway", second), ("gateway", second with { Sequence = 999 }),
            ("gateway", Event(ObservationPhase.Terminal) with { TerminalSuccess = true })]);
        var result = Calculate(snapshot);
        Assert.Equal(new SliCounts(1, 0, 0, 0, 0), result.Metadata.Counts);
        Assert.Equal(2, result.Attempts.Sum(value => value.Count));
        Assert.Equal(1, Assert.Single(result.Attempts, value => value.Fault == "Capacity").Count);
    }

    private static SliReportArtifact Calculate(SliReportSnapshot snapshot) => SliReportCalculator.Calculate(snapshot, Guid.NewGuid(), 1, null);
    [Fact]
    public void Calculator_fact_limit_is_inclusive_and_does_not_truncate_the_next_fact()
    {
        var snapshot = Snapshot([("edge", Event(ObservationPhase.Terminal)), ("gateway", Event(ObservationPhase.Started)),
            ("gateway", Event(ObservationPhase.Attempt) with { AttemptId = Guid.NewGuid() }), ("gateway", Event(ObservationPhase.Terminal) with { TerminalSuccess = true })]);
        var accepted = SliReportCalculator.Calculate(snapshot, Guid.NewGuid(), 1, null, new(MaximumFacts: 7));
        Assert.Equal(1, accepted.Metadata.Counts.H); Assert.Equal("ObservedExecutionsOnly", accepted.Metadata.PopulationBasis);
        Assert.Equal("OperationalSliReport", accepted.Metadata.Kind);
        Assert.Equal("Bounds", Assert.Throws<SliReportRejectedException>(() => SliReportCalculator.Calculate(snapshot, Guid.NewGuid(), 1, null, new(MaximumFacts: 6))).Code);
    }
    private static readonly Guid Execution = Guid.NewGuid();
    private static ExecutionObservation Event(ObservationPhase phase) => new(1, Execution, phase, ObservationOperation.Chat,
        ObservationMode.Sse, At.AddSeconds(1), At, true, 200, false, false, false, false, ObservationProviderFault.None, ObservationCredential.Managed);
    private static SliReportSnapshot Snapshot(IEnumerable<(string Producer, ExecutionObservation Observation)> rows)
    {
        var input = rows.ToArray();
        var sources = Scope.Producers.Select(value => new SliReportSource(Guid.NewGuid(), value.Producer, Guid.NewGuid(), 1, value.Source,
            "test", 1, input.Count(row => row.Producer == value.Producer), input.Count(row => row.Producer == value.Producer), 0,
            Month, Month.AddHours(1), Month.AddHours(1), true, false, new string('a', 64), input.Count(row => row.Producer == value.Producer), false)).ToArray();
        return new(Scope, Month, Month.AddMonths(1).AddHours(1), sources,
            input.Select(row => { var source = sources.Single(value => value.Producer == row.Producer); return new SliObservedEvent(source.Id, row.Producer, source.Source, row.Observation); }).ToArray());
    }
    private static SliReportSnapshot CompleteSnapshot()
    {
        var rows = new List<(string, ExecutionObservation)> { ("edge", Event(ObservationPhase.Terminal)),
            ("gateway", Event(ObservationPhase.Started)), ("gateway", Event(ObservationPhase.Terminal) with { TerminalSuccess = true }),
            ("gateway", Event(ObservationPhase.Attempt) with { AttemptId = Guid.NewGuid() }) };
        for (var at = At; at < Month.AddMonths(1); at = at.AddMinutes(5)) rows.Add(("probe", Event(ObservationPhase.Probe) with {
            ExecutionId = Guid.NewGuid(), Operation = ObservationOperation.Readiness, At = at, StartedAt = at }));
        var sources = new List<SliReportSource>(); var events = new List<SliObservedEvent>();
        foreach (var producer in Scope.Producers)
        {
            var epoch = Guid.NewGuid(); long sequence = 0; long batch = 0;
            for (var hour = Month; hour < Month.AddMonths(1); hour = hour.AddHours(1))
            {
                var observations = rows.Where(value => value.Item1 == producer.Producer && value.Item2.At >= hour && value.Item2.At < hour.AddHours(1)).ToArray();
                var id = Guid.NewGuid(); var first = sequence + 1;
                foreach (var row in observations) events.Add(new(id, producer.Producer, producer.Source, row.Item2 with { Sequence = ++sequence }));
                sources.Add(new(id, producer.Producer, epoch, ++batch, producer.Source, "test", first, sequence,
                    observations.Length, 0, hour, hour.AddHours(1), hour.AddHours(1), true, true, new string('a', 64), observations.Length, false));
            }
        }
        return new(Scope, Month, Month.AddMonths(1).AddHours(1), sources.ToArray(), events.ToArray());
    }
}
