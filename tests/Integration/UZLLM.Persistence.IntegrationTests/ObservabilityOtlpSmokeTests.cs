using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using UZLLM.Observability;

namespace UZLLM.Persistence.IntegrationTests;

public sealed class ObservabilityOtlpSmokeTests
{
    [Fact]
    public async Task Configured_http_protobuf_collector_receives_custom_metrics_and_traces()
    {
        var requests = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var collector = builder.Build();
        collector.MapPost("/{**path}", async (HttpContext http) =>
        {
            await http.Request.Body.CopyToAsync(Stream.Null);
            requests.Enqueue(http.Request.Path.ToString());
            return Results.Ok();
        });
        await collector.StartAsync();
        try
        {
            var address = collector.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:OtlpEndpoint"] = address,
                ["Observability:OtlpProtocol"] = "http/protobuf"
            }).Build();
            using var services = new ServiceCollection().AddLogging()
                .AddUzllmObservability(configuration, "UZLLM.OTLP.Smoke")
                .BuildServiceProvider();
            var metrics = services.GetRequiredService<MeterProvider>();
            var traces = services.GetRequiredService<TracerProvider>();
            UzllmTelemetry.GatewayRequests.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            using (var activity = UzllmTelemetry.ActivitySource.StartActivity("otlp.smoke"))
                activity?.SetTag("gateway.outcome", "success");
            Assert.True(metrics.ForceFlush());
            Assert.True(traces.ForceFlush());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (requests.Count < 2 && !timeout.IsCancellationRequested)
                await Task.Delay(50);
            Assert.Contains(requests, path => path.EndsWith("/v1/metrics", StringComparison.Ordinal));
            Assert.Contains(requests, path => path.EndsWith("/v1/traces", StringComparison.Ordinal));
        }
        finally { await collector.StopAsync(); }
    }
}
