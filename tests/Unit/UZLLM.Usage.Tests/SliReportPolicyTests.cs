using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Observability;
using UZLLM.Modules.Usage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UZLLM.Usage.Tests;

public sealed class SliReportPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2025, 2, 28)]
    [InlineData(2025, 12, 31)]
    public void UTC_months_preserve_leap_year_and_year_rollover(int year, int month, int days)
    {
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromDays(days), SliReportPolicy.Month(from) - from);
        Assert.Equal(TimeSpan.Zero, SliReportPolicy.Month(from).Offset);
    }
    [Theory]
    [InlineData("offset")]
    [InlineData("day")]
    [InlineData("time")]
    [InlineData("open")]
    [InlineData("actor")]
    [InlineData("key")]
    [InlineData("scope")]
    [InlineData("generation")]
    [InlineData("predecessor")]
    [InlineData("empty-predecessor")]
    [InlineData("reason")]
    [InlineData("control")]
    [InlineData("reason-long")]
    public void Invalid_command_partitions_cannot_change_period_identity_or_immutable_terms(string variant)
    {
        var value = new SliReportCommand(Guid.NewGuid(), "test", Now.AddDays(-19).AddMonths(-1), Guid.NewGuid(), 0, null, "Monthly operational evidence");
        value = variant switch {
            "offset" => value with { PeriodStart = value.PeriodStart.ToOffset(TimeSpan.FromHours(5)) },
            "day" => value with { PeriodStart = value.PeriodStart.AddDays(1) },
            "time" => value with { PeriodStart = value.PeriodStart.AddTicks(1) },
            "open" => value with { PeriodStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) },
            "actor" => value with { ActorId = Guid.Empty }, "key" => value with { IdempotencyKey = Guid.Empty },
            "scope" => value with { ScopeId = "arbitrary/sql" }, "generation" => value with { ExpectedGeneration = -1 },
            "predecessor" => value with { ExpectedGeneration = 1 }, "empty-predecessor" => value with { PredecessorId = Guid.Empty },
            "reason" => value with { Reason = "short" }, "control" => value with { Reason = "line\ncontrol" },
            _ => value with { Reason = new string('x', 501) }
        };
        Assert.Throws<ArgumentException>(() => SliReportPolicy.Normalize(value, Now));
    }
    [Fact]
    public void Normalization_and_hash_are_stable_but_changed_terms_do_not_replay()
    {
        var input = new SliReportCommand(Guid.NewGuid(), "test", new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid(), 0, null, " Monthly operational evidence ");
        var normalized = SliReportPolicy.Normalize(input, Now); Assert.Equal("Monthly operational evidence", normalized.Reason);
        Assert.Equal(Hash(normalized), Hash(SliReportPolicy.Normalize(normalized, Now)));
        Assert.NotEqual(Hash(normalized), Hash(normalized with { Reason = "Explicit corrected evidence" }));
        static string Hash(SliReportCommand value) => SliReportPolicy.Hash(SliReportPolicy.Serialize(value));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("source")]
    [InlineData("mode")]
    [InlineData("operation")]
    [InlineData("region")]
    [InlineData("version")]
    [InlineData("probe-bound")]
    public void Only_finite_registered_profiles_can_define_report_scope(string variant)
    {
        var scope = new SliReportScope("test", "test", "Chat", "All", [new("edge", "Edge"), new("gateway", "Gateway"), new("probe", "Probe")]);
        scope = variant switch {
            "duplicate" => scope with { Producers = [new("edge", "Edge"), new("edge", "Gateway"), new("probe", "Probe")] },
            "source" => scope with { Producers = [new("edge", "Edge"), new("gateway", "Gateway"), new("custom", "Custom")] },
            "mode" => scope with { Mode = "SQL" }, "operation" => scope with { Operation = "*" }, "region" => scope with { Region = "../private" },
            "version" => scope with { Version = 2 }, _ => scope with { ProbeMaximumGapSeconds = 301 }
        };
        Assert.Throws<ArgumentException>(() => SliReportPolicy.Validate(scope));
    }
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(100001, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 33554433, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 30001)]
    public void Build_limits_cannot_disable_bounded_work(int facts, int bytes, int milliseconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SliReportLimits(facts, bytes, milliseconds).Validate());

    [Fact]
    public void Serialization_stops_at_exact_byte_bound_and_honors_cancellation_without_allocating_unbounded_output()
    {
        Assert.Equal("\"abc\"", System.Text.Encoding.UTF8.GetString(SliReportPolicy.Serialize("abc", 5)));
        Assert.Equal("Bounds", Assert.Throws<SliReportRejectedException>(() => SliReportPolicy.Serialize("abc", 4)).Code);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SliReportPolicy.Serialize("abc", 5, canceled.Token));
    }

    [Fact]
    public void Report_registration_is_empty_by_default_and_freezes_valid_deployment_profile_configuration()
    {
        using var disabled = new ServiceCollection().AddUzllmSliReports(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.Empty(disabled.GetRequiredService<SliReportProfiles>().Scopes);
        var values = new Dictionary<string, string?> { ["Sli:Reports:Scopes:0:Id"] = "test-chat", ["Sli:Reports:Scopes:0:Region"] = "test",
            ["Sli:Reports:Scopes:0:Operation"] = "Chat", ["Sli:Reports:Scopes:0:Mode"] = "All" };
        foreach (var (source, index) in new[] { ("Edge", 0), ("Gateway", 1), ("Probe", 2) })
        { values[$"Sli:Reports:Scopes:0:Producers:{index}:Producer"] = source.ToLowerInvariant(); values[$"Sli:Reports:Scopes:0:Producers:{index}:Source"] = source; }
        using var configured = new ServiceCollection().AddUzllmSliReports(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        var profile = Assert.Single(configured.GetRequiredService<SliReportProfiles>().Scopes).Value;
        Assert.Equal("test-chat", profile.Id); Assert.Equal(300, profile.ProbeMaximumGapSeconds); Assert.Null(profile.IndependentProbeEvidence);
        Assert.Equal(3, profile.Producers.Length);
    }
}
