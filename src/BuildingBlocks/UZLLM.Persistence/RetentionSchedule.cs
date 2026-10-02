using Microsoft.Extensions.Configuration;

namespace UZLLM.Persistence;

/// <summary>
/// An externally approved schedule enables only minimization of diagnostic and
/// completed operational content. Financial and audit records remain on hold.
/// </summary>
public sealed record RetentionSchedule(
    string? ScheduleReference,
    int DiagnosticDays,
    int OperationalDays)
{
    public bool Enabled => ScheduleReference is not null;

    public static RetentionSchedule FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Retention");
        var reference = section["ScheduleReference"]?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            // No policy approval: never age out metadata or operational content.
            return new RetentionSchedule(null, 30, 90);
        }

        if (reference.Length > 200 || reference.Any(char.IsControl))
        {
            throw new InvalidOperationException("Retention:ScheduleReference must be a printable reference of at most 200 characters.");
        }

        var diagnosticDays = ReadDays(section, "DiagnosticDays", 30);
        var operationalDays = ReadDays(section, "OperationalDays", 90);
        if (section["AuditDisposition"] != "LegalHold" ||
            section["FinancialDisposition"] != "LegalHold")
        {
            throw new InvalidOperationException("Retention requires explicit AuditDisposition and FinancialDisposition of LegalHold.");
        }

        return new RetentionSchedule(reference, diagnosticDays, operationalDays);
    }

    private static int ReadDays(IConfigurationSection section, string key, int fallback)
    {
        var value = section[key];
        if (value is null) return fallback;
        if (!int.TryParse(value, out var days) || days is < 1 or > 3650)
        {
            throw new InvalidOperationException($"Retention:{key} must be between 1 and 3650 days.");
        }

        return days;
    }
}
