using Serilog;
using Serilog.Core;
using Serilog.Events;
using UZLLM.Observability;

namespace UZLLM.Observability.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Sensitive_data_enricher_redacts_secrets_and_preserves_safe_operational_context()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information(
            "Gateway request {Authorization} {ApiKey} {Prompt} {ConnectionString} {RequestId} {Provider}",
            "Bearer uzllm_live_secret",
            "uzllm_live_key",
            "raw customer prompt",
            "Host=db;Password=secret",
            "request-123",
            "openai");

        var entry = Assert.Single(sink.Events);
        Assert.Equal("[REDACTED]", GetScalarValue(entry, "Authorization"));
        Assert.Equal("[REDACTED]", GetScalarValue(entry, "ApiKey"));
        Assert.Equal("[REDACTED]", GetScalarValue(entry, "Prompt"));
        Assert.Equal("[REDACTED]", GetScalarValue(entry, "ConnectionString"));
        Assert.Equal("request-123", GetScalarValue(entry, "RequestId"));
        Assert.Equal("openai", GetScalarValue(entry, "Provider"));
    }

    [Fact]
    public void Gateway_log_context_contains_required_identifiers_without_secret_fields()
    {
        var context = new GatewayLogContext(
            "request-123",
            "trace-456",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai",
            "gpt-5",
            "success");

        var properties = context.ToProperties();

        Assert.Equal("request-123", properties["RequestId"]);
        Assert.Equal("trace-456", properties["TraceId"]);
        Assert.Equal("openai", properties["Provider"]);
        Assert.Equal("gpt-5", properties["Model"]);
        Assert.Equal("success", properties["Outcome"]);
        Assert.Contains("OrganizationId", properties.Keys);
        Assert.Contains("ProjectId", properties.Keys);
        Assert.Contains("ApiKeyId", properties.Keys);
        Assert.DoesNotContain(properties.Keys, key => SensitiveDataRedactionEnricher.IsSensitivePropertyName(key));
    }

    [Fact]
    public void Correlation_context_keeps_request_and_trace_ids_as_safe_log_properties()
    {
        var context = new CorrelationContext("request-123", "trace-456");

        var properties = context.ToLogProperties();

        Assert.Equal("request-123", properties["RequestId"]);
        Assert.Equal("trace-456", properties["TraceId"]);
        Assert.Equal(2, properties.Count);
    }

    private static string GetScalarValue(LogEvent logEvent, string propertyName) =>
        Assert.IsType<ScalarValue>(logEvent.Properties[propertyName]).Value as string
        ?? throw new InvalidOperationException($"Property '{propertyName}' was not a string scalar.");

    private sealed class InMemoryLogSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
