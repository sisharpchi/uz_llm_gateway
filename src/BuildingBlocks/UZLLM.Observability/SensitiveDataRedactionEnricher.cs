using Serilog.Core;
using Serilog.Events;

namespace UZLLM.Observability;

public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    private static readonly string[] SensitiveFragments =
    [
        "authorization", "api_key", "apikey", "secret", "password", "token",
        "prompt", "message", "payload", "payment", "card", "signature", "connection"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.Keys.ToArray())
        {
            if (IsSensitivePropertyName(property))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property, new ScalarValue("[REDACTED]")));
            }
        }
    }

    public static bool IsSensitivePropertyName(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        if (propertyName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Identifier", StringComparison.OrdinalIgnoreCase)
            || propertyName.EndsWith("Fingerprint", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SensitiveFragments.Any(fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record GatewayLogContext(
    string RequestId,
    string TraceId,
    Guid? OrganizationId = null,
    Guid? ProjectId = null,
    Guid? ApiKeyId = null,
    string? Provider = null,
    string? Model = null,
    string? Outcome = null)
{
    public IReadOnlyDictionary<string, object?> ToProperties() => new Dictionary<string, object?>
    {
        ["RequestId"] = RequestId,
        ["TraceId"] = TraceId,
        ["OrganizationId"] = OrganizationId,
        ["ProjectId"] = ProjectId,
        ["ApiKeyId"] = ApiKeyId,
        ["Provider"] = Provider,
        ["Model"] = Model,
        ["Outcome"] = Outcome
    };
}
