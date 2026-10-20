using System.Numerics;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Observability;

namespace UZLLM.Modules.Usage.Domain;

/// <summary>Conservative evidence attribution; never infers money, maintenance exclusions or SLA entitlement.</summary>
public static class SliReportCalculator
{
    public static SliReportArtifact Calculate(SliReportSnapshot snapshot, Guid id, long revision, Guid? predecessor,
        SliReportLimits? limits = null, CancellationToken ct = default)
    {
        limits ??= new(); limits.Validate(); ct.ThrowIfCancellationRequested();
        if ((long)snapshot.Sources.Length + snapshot.Events.Length + snapshot.Sources.Sum(value => (long)(value.ConflictReceipts?.Length ?? 0)) > limits.MaximumFacts)
            throw new SliReportRejectedException("Bounds");
        SliReportPolicy.Validate(snapshot.Scope);
        var end = SliReportPolicy.Month(snapshot.PeriodStart);
        if (snapshot.CapturedAt < end) throw new ArgumentException("Month is not closed.");
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        var required = snapshot.Scope.Producers.ToDictionary(value => value.Producer, value => value.Source, StringComparer.Ordinal);
        var sources = snapshot.Sources.OrderBy(value => value.Producer, StringComparer.Ordinal)
            .ThenBy(value => value.Epoch).ThenBy(value => value.BatchNumber).ToArray();
        if (sources.Any(value => !required.TryGetValue(value.Producer, out var source) || source != value.Source
                || value.Region != snapshot.Scope.Region) || sources.Select(value => value.Id).Distinct().Count() != sources.Length)
            throw new ArgumentException("Invalid source membership.");
        var sourceIds = sources.ToDictionary(value => value.Id);
        if (snapshot.Events.Any(value => !sourceIds.TryGetValue(value.ManifestId, out var source)
            || source.Producer != value.Producer || source.Source != value.Source)) throw new ArgumentException("Unbound source event.");
        var observedCounts = snapshot.Events.GroupBy(value => value.ManifestId).ToDictionary(value => value.Key, value => value.Count());
        if (sources.Any(value => observedCounts.GetValueOrDefault(value.Id) != value.ReadCount)) throw new ArgumentException("Invalid raw membership count.");
        foreach (var producer in snapshot.Scope.Producers)
        {
            ct.ThrowIfCancellationRequested();
            var batches = sources.Where(value => value.Producer == producer.Producer).ToArray();
            if (batches.Length == 0) { warnings.Add("MissingSource"); continue; }
            if (batches.Any(value => !value.ClockSynchronized)) warnings.Add("ClockUnverified");
            if (batches.Any(value => !value.CollectorContinuous)) warnings.Add("CollectorDiscontinuous");
            if (batches.Any(value => value.Conflict)) warnings.Add("SourceConflict");
            if (batches.Any(value => value.Dropped != 0)) warnings.Add("Dropped");
            if (batches.Any(value => value.ReadCount != value.ObservationCount)) warnings.Add("RawEvidenceMissing");
            if (batches.Any(value => value.IntervalEnd > snapshot.CapturedAt.AddMinutes(1))) warnings.Add("ClockSkew");
            if (batches.Any(value => value.ReceivedAt - value.IntervalEnd > TimeSpan.FromMinutes(2))) warnings.Add("LateImport");
            foreach (var epoch in batches.GroupBy(value => value.Epoch))
            {
                var ordered = epoch.OrderBy(value => value.BatchNumber).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                    if (ordered[index].BatchNumber != ordered[index - 1].BatchNumber + 1
                        || ordered[index].FirstSequence != ordered[index - 1].LastSequence + 1) warnings.Add("SequenceGap");
            }
            if (batches.Select(value => value.Epoch).Distinct().Count() > 1) warnings.Add("EpochBoundary");
            var until = snapshot.PeriodStart;
            foreach (var batch in batches.OrderBy(value => value.IntervalStart))
            {
                if (batch.IntervalStart > until && until < end) warnings.Add("MissingInterval");
                if (batch.IntervalEnd > until) until = batch.IntervalEnd;
            }
            if (until < end) warnings.Add("MissingInterval");
        }
        if (snapshot.Scope.IndependentProbeEvidence is null) warnings.Add("ProbeIndependenceUnverified");
        var probeEvents = snapshot.Events.Where(value => value.Source == "Probe"
            && value.Observation.At >= snapshot.PeriodStart && value.Observation.At < end).ToArray();
        if (probeEvents.Length == 0) warnings.Add("MissingProbe");
        if (probeEvents.Any(value => value.Observation.HttpStatus is < 200 or >= 300
            || value.Observation.GatewayFault || value.Observation.ClientDisconnected)) warnings.Add("ProbeUnreachable");
        foreach (var producer in snapshot.Scope.Producers.Where(value => value.Source == "Probe"))
        {
            var until = snapshot.PeriodStart;
            foreach (var probe in probeEvents.Where(value => value.Producer == producer.Producer).OrderBy(value => value.Observation.At))
            {
                if (probe.Observation.At - until > TimeSpan.FromSeconds(snapshot.Scope.ProbeMaximumGapSeconds)) warnings.Add("MissingProbeInterval");
                until = probe.Observation.At;
            }
            if (end - until > TimeSpan.FromSeconds(snapshot.Scope.ProbeMaximumGapSeconds)) warnings.Add("MissingProbeInterval");
        }
        var classifications = new List<SliClassifiedExecution>();
        var attempts = new List<ExecutionObservation>();
        foreach (var group in snapshot.Events.Where(value => value.Source != "Probe").GroupBy(value => value.Observation.ExecutionId))
        {
            ct.ThrowIfCancellationRequested();
            var items = group.ToArray();
            var first = items.Min(value => value.Observation.StartedAt);
            if (first < snapshot.PeriodStart || first >= end) continue;
            var modes = items.Select(value => value.Observation.Mode).Where(value => value != ObservationMode.Unknown).Distinct().ToArray();
            var disputedSource = items.Any(value => sourceIds[value.ManifestId].Conflict);
            if (snapshot.Scope.Mode != "All" && modes.Length == 1 && modes[0].ToString() != snapshot.Scope.Mode && !disputedSource) continue;
            if (!items.Any(value => value.Observation.Operation.ToString() == snapshot.Scope.Operation)) continue;
            var mode = modes.Length == 1 ? modes[0].ToString() : "Unknown";
            var outcome = disputedSource ? (Class: "U", Reason: "SourceConflict")
                : modes.Length > 1 ? (Class: "U", Reason: "ModeConflict") : Classify(items);
            if (items.Any(value => value.Source == "Gateway" && value.Observation.TerminalSuccess)
                && !items.Any(value => value.Source == "Gateway" && value.Observation.Phase == ObservationPhase.Attempt))
                warnings.Add("MissingProviderAttempt");
            if (snapshot.Scope.Mode != "All" && mode == "Unknown") outcome = ("U", "ModeUnresolved");
            classifications.Add(new(group.Key, first, mode, outcome.Class, outcome.Reason));
            attempts.AddRange(items.Where(value => value.Source == "Gateway" && value.Observation.Phase == ObservationPhase.Attempt)
                .Select(value => value.Observation with { Sequence = 0 }).Distinct());
        }
        var executions = classifications.OrderBy(value => value.ExecutionId).ToArray();
        var uniqueAttempts = attempts.GroupBy(value => value.AttemptId).Select(group =>
        {
            var evidence = group.Distinct().ToArray();
            if (evidence.Length > 1) warnings.Add("AttemptConflict");
            return evidence.Length == 1 ? evidence[0] : evidence[0] with { Credential = ObservationCredential.Unknown, ProviderFault = ObservationProviderFault.Unknown };
        }).ToArray();
        var counts = new SliCounts(executions.LongCount(value => value.Class == "H"), executions.LongCount(value => value.Class == "G"),
            executions.LongCount(value => value.Class == "P"), executions.LongCount(value => value.Class == "C"), executions.LongCount(value => value.Class == "U"));
        if (counts.U != 0) warnings.Add("UnknownExecutions");
        var coverage = executions.Length == 0 ? "NoData" : warnings.Count == 0 ? "Complete" : "Incomplete";
        var known = Ratio(counts.H, checked(counts.H + counts.G));
        var conservative = Ratio(counts.H, checked(counts.H + counts.G + counts.U));
        var delivery = Ratio(counts.H, checked(counts.H + counts.G + counts.P + counts.U));
        var target = coverage != "Complete" ? coverage : known.Denominator == 0 ? "NoData"
            : MeetsTarget(counts.H, counts.G) ? "Met" : "Missed";
        var digest = SliReportPolicy.Hash(SliReportPolicy.Serialize(sources, limits.MaximumBytes, ct));
        return new(new(id, snapshot.Scope.Id, snapshot.PeriodStart, end, revision, predecessor,
            SliReportPolicy.CalculationVersion, snapshot.CapturedAt, digest, counts, known, conservative, delivery,
            coverage, target, warnings.ToArray(), sources.Length, executions.Length), snapshot.Scope, sources, executions,
            uniqueAttempts.GroupBy(value => (value.Credential, value.ProviderFault))
                .OrderBy(value => value.Key.Credential).ThenBy(value => value.Key.ProviderFault)
                .Select(value => new SliAttemptCounts(value.Key.Credential.ToString(), value.Key.ProviderFault.ToString(), value.LongCount())).ToArray());
    }

    public static SliRatio Ratio(long numerator, long denominator)
    {
        if (numerator < 0 || denominator < numerator) throw new ArgumentException("Invalid fraction.");
        return new(numerator, denominator, denominator == 0 ? null : (decimal)numerator / denominator, denominator == 0 ? "NoData" : null);
    }
    public static bool MeetsTarget(long handled, long gatewayBad)
    {
        if (handled < 0 || gatewayBad < 0 || handled == 0 && gatewayBad == 0) throw new ArgumentException("No eligible population.");
        return (BigInteger)handled * 1000 >= ((BigInteger)handled + gatewayBad) * 999;
    }

    private static (string Class, string Reason) Classify(SliObservedEvent[] input)
    {
        var edges = input.Where(value => value.Source == "Edge").Select(value => value.Observation with { Sequence = 0 }).Distinct().ToArray();
        var gateway = input.Where(value => value.Source == "Gateway").Select(value => value.Observation with { Sequence = 0 }).Distinct().ToArray();
        if (edges.Length != 1 || input.Any(value => !value.Observation.TrustedEdge)
            || input.Select(value => value.Observation.Operation).Distinct().Count() != 1) return ("U", "CorrelationConflict");
        var terminals = gateway.Where(value => value.Phase == ObservationPhase.Terminal).ToArray();
        var starts = gateway.Where(value => value.Phase == ObservationPhase.Started).ToArray();
        if (terminals.Length > 1 || starts.Length > 1
            || gateway.Where(value => value.Phase == ObservationPhase.Attempt).GroupBy(value => value.AttemptId).Any(value => value.Count() > 1))
            return ("U", "ConflictingEvents");
        var edge = edges[0];
        var terminal = terminals.SingleOrDefault();
        if (terminal is null) return edge.GatewayFault ? ("G", "EdgeFailure") : ("U", "UnclosedExecution");
        var cancel = terminal.ClientDisconnectedAt;
        if (terminal.ClientDisconnected)
        {
            if (edge.GatewayFault && edge.GatewayFailureAt is null) return ("U", "CancellationOrderUnknown");
            var failures = new[] { terminal.GatewayFailureAt, terminal.ProviderFailureAt }.Where(value => value is not null).Select(value => value!.Value).ToArray();
            if (cancel is not null && failures.Length > 0 && failures.All(value => cancel < value)) return ("C", "ClientCanceledFirst");
            if (failures.Length == 0 && cancel is not null && !terminal.GatewayFault && terminal.ProviderFault == ObservationProviderFault.None && !edge.GatewayFault)
                return ("C", "ClientCanceledFirst");
            if (cancel is null || failures.Any(value => value == cancel)
                || terminal.GatewayFault && terminal.GatewayFailureAt is null
                || terminal.ProviderFault != ObservationProviderFault.None && terminal.ProviderFailureAt is null) return ("U", "CancellationOrderUnknown");
        }
        if (edge.GatewayFault || terminal.GatewayFault) return ("G", "GatewayFailure");
        var provider = terminal.ProviderFault;
        var evidence = gateway.Where(value => value.Phase == ObservationPhase.Attempt && value.ProviderFault == provider
            && value.UpstreamStatus == terminal.UpstreamStatus && value.Certainty == ObservationExecutionCertainty.RejectedBeforeExecution).ToArray();
        if (provider == ObservationProviderFault.Authentication && terminal.UpstreamStatus is 401 or 403 && evidence.Length > 0)
        {
            var credentials = evidence.Select(value => value.Credential).Distinct().ToArray();
            var credential = terminal.Credential == ObservationCredential.Hybrid && credentials.Length == 1 ? credentials[0] : terminal.Credential;
            if (credentials.Length != 1 || credentials[0] != credential) return ("U", "CredentialOriginUnknown");
            return credential == ObservationCredential.Byok ? ("C", "ByokCredentialRejected")
                : credential == ObservationCredential.Managed ? ("G", "PlatformCredentialRejected") : ("U", "CredentialOriginUnknown");
        }
        if (provider is ObservationProviderFault.Capacity or ObservationProviderFault.RateLimited or ObservationProviderFault.Upstream5xx
            && evidence.Length > 0 && terminal.UpstreamStatus is 429 or >= 500 and <= 599) return ("P", "VerifiedProviderRejection");
        // Timeouts/transport ambiguity are not proven upstream exclusions in v1.
        if (provider != ObservationProviderFault.None) return ("U", "ProviderOriginUnproven");
        if (terminal.PolicyRejection && terminal.HttpStatus is >= 400 and < 500 && terminal.HttpStatus != 429)
            return ("C", "PolicyRejected");
        if (starts.Length == 1 && terminal.TerminalSuccess && !edge.ClientDisconnected && edge.HttpStatus is >= 200 and < 300
            && !terminal.ClientDisconnected && terminal.Mode != ObservationMode.Unknown) return ("H", "ProtocolCompleted");
        return ("U", "OutcomeUnproven");
    }
}
