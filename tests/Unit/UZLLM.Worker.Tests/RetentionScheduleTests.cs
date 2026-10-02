using Microsoft.Extensions.Configuration;
using UZLLM.Persistence;

namespace UZLLM.Worker.Tests;

public sealed class RetentionScheduleTests
{
    [Fact]
    public void Missing_approval_reference_disables_metadata_minimization()
    {
        var schedule = RetentionSchedule.FromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["Retention:DiagnosticDays"] = "1",
            ["Retention:OperationalDays"] = "1"
        }));

        Assert.False(schedule.Enabled);
        Assert.Null(schedule.ScheduleReference);
        Assert.Equal(30, schedule.DiagnosticDays);
        Assert.Equal(90, schedule.OperationalDays);
    }

    [Fact]
    public void Approved_schedule_uses_distinct_configured_durations_and_explicit_holds()
    {
        var schedule = RetentionSchedule.FromConfiguration(Configuration(ApprovedSettings(12, 80)));

        Assert.True(schedule.Enabled);
        Assert.Equal("policy-2026-10", schedule.ScheduleReference);
        Assert.Equal(12, schedule.DiagnosticDays);
        Assert.Equal(80, schedule.OperationalDays);
    }

    [Theory]
    [InlineData("Retention:DiagnosticDays", "0")]
    [InlineData("Retention:DiagnosticDays", "3651")]
    [InlineData("Retention:OperationalDays", "not-a-number")]
    [InlineData("Retention:AuditDisposition", "Delete")]
    [InlineData("Retention:FinancialDisposition", "Delete")]
    public void Invalid_approved_schedule_fails_closed(string key, string value)
    {
        var settings = ApprovedSettings(30, 90);
        settings[key] = value;

        Assert.Throws<InvalidOperationException>(() => RetentionSchedule.FromConfiguration(Configuration(settings)));
    }

    [Fact]
    public void Control_character_in_approval_reference_is_rejected()
    {
        var settings = ApprovedSettings(30, 90);
        settings["Retention:ScheduleReference"] = "policy-2026\nforged";

        Assert.Throws<InvalidOperationException>(() => RetentionSchedule.FromConfiguration(Configuration(settings)));
    }

    private static Dictionary<string, string?> ApprovedSettings(int diagnosticDays, int operationalDays) => new()
    {
        ["Retention:ScheduleReference"] = "policy-2026-10",
        ["Retention:DiagnosticDays"] = diagnosticDays.ToString(),
        ["Retention:OperationalDays"] = operationalDays.ToString(),
        ["Retention:AuditDisposition"] = "LegalHold",
        ["Retention:FinancialDisposition"] = "LegalHold"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
