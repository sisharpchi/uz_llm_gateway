using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UZLLM.Observability;

// The lock protects only a bounded in-memory queue; never I/O, awaiting, or a sink.
public sealed class ObservationBuffer(TimeProvider clock, int capacity = ObservationProtocol.MaximumObservations)
{
    private readonly Queue<ExecutionObservation> queue = new();
    private readonly object gate = new();
    private readonly int limit = capacity is >= 1 and <= ObservationProtocol.MaximumObservations
        ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Guid epoch = Guid.NewGuid();
    private DateTimeOffset intervalStart = clock.GetUtcNow();
    private long sequence, drainedSequence, batch;
    public bool TryRecord(ExecutionObservation observation)
    {
        lock (gate)
        {
            var next = checked(++sequence);
            if (queue.Count >= limit) return false;
            queue.Enqueue(observation with { Sequence = next });
            return true;
        }
    }

    public ObservationManifest Drain(string region, bool clockSynchronized)
    {
        lock (gate)
        {
            var end = clock.GetUtcNow();
            var items = queue.ToArray();
            queue.Clear();
            var manifest = new ObservationManifest(1, epoch, checked(++batch), ObservationSource.Gateway,
                region, drainedSequence + 1, sequence, intervalStart, end,
                sequence - drainedSequence - items.LongLength, false, clockSynchronized, "Partial", items);
            drainedSequence = sequence;
            intervalStart = end;
            return manifest;
        }
    }
}

public sealed record ObservationCaptureOptions(bool Enabled, string Producer, string Region,
    string Directory, byte[] Key, bool ClockSynchronized, ObservationEdgeTrust EdgeTrust)
{
    public static ObservationCaptureOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Sli:Capture");
        var enabled = section.GetValue<bool>("Enabled");
        if (!enabled) return new(false, "disabled", "unknown", "", [], false, new([]));
        var producer = section["Producer"];
        var region = section["Region"];
        var directory = section["Directory"];
        var key = Convert.FromBase64String(section["Key"] ?? "");
        var peers = section.GetSection("TrustedEdgePeers").Get<string[]>() ?? [];
        if (!ObservationProtocol.IsLabel(producer) || !ObservationProtocol.IsLabel(region)
            || directory is null || !Path.IsPathFullyQualified(directory) || key.Length < 32
            || peers.Length == 0 || peers.Any(value => !IPAddress.TryParse(value, out _)))
            throw new InvalidOperationException("Invalid Sli:Capture private collector configuration.");
        return new(true, producer!, region!, directory, key, section.GetValue<bool>("ClockSynchronized"),
            new(peers.Select(IPAddress.Parse)));
    }
}

public static class ObservationCaptureRegistration
{
    public static IServiceCollection AddUzllmExecutionObservations(this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = ObservationCaptureOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.AddSingleton(provider => new ObservationBuffer(provider.GetRequiredService<TimeProvider>()));
        if (options.Enabled) services.AddHostedService<ObservationSpoolExporter>();
        return services;
    }
    public static WebApplication UseUzllmExecutionObservations(this WebApplication app)
    {
        app.UseMiddleware<ExecutionObservationMiddleware>();
        return app;
    }
}

public sealed class ExecutionObservationMiddleware(RequestDelegate next, ObservationBuffer buffer,
    ObservationCaptureOptions options, TimeProvider clock)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // ASP.NET endpoint matching accepts casing and one trailing slash too.
        var path = context.Request.Path.Value ?? "";
        if (path.EndsWith('/')) path = path[..^1];
        ObservationOperation? operation = path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? ObservationOperation.Chat : path.Equals("/v1/responses", StringComparison.OrdinalIgnoreCase)
                ? ObservationOperation.Responses : path.Equals("/v1/embeddings", StringComparison.OrdinalIgnoreCase)
                    ? ObservationOperation.Embeddings : null;
        if (!options.Enabled || operation is null) { await next(context); return; }
        var edge = options.EdgeTrust.Resolve(context);
        // Caller headers never become identities, even on direct Kestrel access.
        context.Request.Headers.Remove(ObservationEdgeTrust.Header);
        var state = new ExecutionObservationState(edge ?? Guid.NewGuid(), edge is not null,
            operation.Value, clock.GetUtcNow(), clock);
        using var disconnect = context.RequestAborted.Register(state.ObserveDisconnect);
        context.Items[ExecutionObservationState.ItemName] = state;
        buffer.TryRecord(state.Snapshot(context, ObservationPhase.Started, clock.GetUtcNow()));
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch { state.GatewayFault = true; throw; }
        finally { buffer.TryRecord(state.Snapshot(context, ObservationPhase.Terminal, clock.GetUtcNow())); }
    }
}

public sealed class ObservationSpoolExporter(ObservationBuffer buffer, ObservationCaptureOptions options,
    ILogger<ObservationSpoolExporter> logger) : BackgroundService
{
    private byte[]? pending;
    private string? name;

    public async Task ExportOnceAsync(CancellationToken cancellationToken)
    {
        if (pending is null)
        {
            pending = ObservationProtocol.Sign(options.Producer,
                buffer.Drain(options.Region, options.ClockSynchronized), options.Key);
            name = Path.Combine(options.Directory, Guid.NewGuid().ToString("N") + ".manifest");
        }
        Directory.CreateDirectory(options.Directory);
        if (Directory.EnumerateFileSystemEntries(options.Directory).Take(129).Count() >= 128)
            throw new IOException("Observation spool full.");
        await File.WriteAllBytesAsync(name + ".tmp", pending, cancellationToken);
        File.Move(name + ".tmp", name!);
        pending = null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // One pending batch plus the bounded queue, never unbounded memory/disk.
                await ExportOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // No path, payload, key or raw exception can leak into ordinary logs.
                logger.LogWarning("SLI capture spool unavailable; queued evidence is bounded and coverage may be incomplete");
            }
        }
    }
}
