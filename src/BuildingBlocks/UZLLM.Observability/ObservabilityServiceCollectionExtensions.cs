using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace UZLLM.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var configuredEndpoint = configuration["Observability:OtlpEndpoint"];
        var endpoint = Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var parsedEndpoint) ? parsedEndpoint : null;

        services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Console());

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(UzllmTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (endpoint is not null)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(UzllmTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (endpoint is not null)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = endpoint);
                }
            });

        services.AddSingleton<GatewayTelemetry>();
        return services;
    }

    public static WebApplication UseUzllmRequestCorrelation(this WebApplication application)
    {
        application.UseMiddleware<RequestCorrelationMiddleware>();
        application.UseSerilogRequestLogging();
        return application;
    }

    public static IEndpointRouteBuilder MapUzllmHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains("ready", StringComparer.Ordinal)
        });
        return endpoints;
    }
}

public sealed class RequestCorrelationMiddleware(RequestDelegate next, ILogger<RequestCorrelationMiddleware> logger)
{
    public const string RequestIdHeaderName = "X-Request-Id";
    public const string CorrelationContextItemName = "UZLLM.CorrelationContext";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.CreateVersion7().ToString("N");
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        var correlation = new CorrelationContext(requestId, traceId);
        context.Items[CorrelationContextItemName] = correlation;
        context.Response.Headers[RequestIdHeaderName] = requestId;

        using (logger.BeginScope(correlation.ToLogProperties()))
        {
            await next(context);
        }
    }
}

public sealed record CorrelationContext(string RequestId, string TraceId)
{
    public IReadOnlyDictionary<string, object> ToLogProperties() => new Dictionary<string, object>
    {
        ["RequestId"] = RequestId,
        ["TraceId"] = TraceId
    };
}

public static class UzllmTelemetry
{
    public const string ActivitySourceName = "UZLLM";
    public const string MeterName = "UZLLM";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> GatewayRequests = Meter.CreateCounter<long>("uzllm.gateway.requests");

    public static readonly Counter<long> GatewayErrors = Meter.CreateCounter<long>("uzllm.gateway.errors");

    public static readonly Histogram<double> GatewayDurationMilliseconds = Meter.CreateHistogram<double>("uzllm.gateway.duration.ms");

    public static readonly Histogram<double> TimeToFirstTokenMilliseconds = Meter.CreateHistogram<double>("uzllm.gateway.ttft.ms");

    public static readonly Histogram<double> TokensPerSecond = Meter.CreateHistogram<double>("uzllm.gateway.tokens_per_second");

    public static readonly UpDownCounter<long> ActiveStreams = Meter.CreateUpDownCounter<long>("uzllm.gateway.active_streams");

    public static readonly Counter<long> TokenUsage = Meter.CreateCounter<long>("uzllm.gateway.tokens");

    public static readonly Counter<long> SpendMicroUsd = Meter.CreateCounter<long>("uzllm.billing.spend.micro_usd");

    public static readonly Counter<long> ProviderFailures = Meter.CreateCounter<long>("uzllm.provider.failures");

    public static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("uzllm.routing.fallbacks");

    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("uzllm.gateway.cache_hits");

    public static readonly Counter<long> FinancialFailures = Meter.CreateCounter<long>("uzllm.billing.failures");
}

public sealed record GatewayTelemetryContext(
    string Outcome,
    string? Provider = null,
    string? Model = null);

public sealed class GatewayTelemetry
{
    public void RecordRequest(GatewayTelemetryContext context, TimeSpan duration)
    {
        var tags = CreateTags(context);
        UzllmTelemetry.GatewayRequests.Add(1, tags);
        UzllmTelemetry.GatewayDurationMilliseconds.Record(duration.TotalMilliseconds, tags);
        if (!string.Equals(context.Outcome, "success", StringComparison.OrdinalIgnoreCase))
        {
            UzllmTelemetry.GatewayErrors.Add(1, tags);
        }
    }

    public void RecordTimeToFirstToken(GatewayTelemetryContext context, TimeSpan elapsed) =>
        UzllmTelemetry.TimeToFirstTokenMilliseconds.Record(elapsed.TotalMilliseconds, CreateTags(context));

    public void RecordTokens(long tokenCount, GatewayTelemetryContext context) =>
        UzllmTelemetry.TokenUsage.Add(tokenCount, CreateTags(context));

    public void RecordThroughput(GatewayTelemetryContext context, double tokensPerSecond) =>
        UzllmTelemetry.TokensPerSecond.Record(tokensPerSecond, CreateTags(context));

    public void RecordStreamDelta(GatewayTelemetryContext context, long delta) =>
        UzllmTelemetry.ActiveStreams.Add(delta, CreateTags(context));

    public void RecordSpend(long microUsd, GatewayTelemetryContext context) =>
        UzllmTelemetry.SpendMicroUsd.Add(microUsd, CreateTags(context));

    public void RecordProviderFailure(GatewayTelemetryContext context) =>
        UzllmTelemetry.ProviderFailures.Add(1, CreateTags(context));

    public void RecordFallback(GatewayTelemetryContext context) =>
        UzllmTelemetry.Fallbacks.Add(1, CreateTags(context));

    public void RecordCacheHit(GatewayTelemetryContext context) =>
        UzllmTelemetry.CacheHits.Add(1, CreateTags(context));

    public void RecordFinancialFailure(GatewayTelemetryContext context) =>
        UzllmTelemetry.FinancialFailures.Add(1, CreateTags(context));

    private static TagList CreateTags(GatewayTelemetryContext context)
    {
        var tags = new TagList { { "outcome", context.Outcome } };
        if (!string.IsNullOrWhiteSpace(context.Provider))
        {
            tags.Add("provider", context.Provider);
        }

        if (!string.IsNullOrWhiteSpace(context.Model))
        {
            tags.Add("model", context.Model);
        }

        return tags;
    }
}
