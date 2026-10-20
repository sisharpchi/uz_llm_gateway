using System.Text.Json;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Observability;

namespace UZLLM.Worker;

public sealed record SliImportOptions(bool Enabled, string Directory, SliCollectorTrust Trust)
{
    public static SliImportOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Sli:Import");
        if (!section.GetValue<bool>("Enabled")) return new(false, "", new(new Dictionary<string, ObservationProducerTrust>()));
        var directory = section["Directory"];
        var trusted = new Dictionary<string, ObservationProducerTrust>(StringComparer.Ordinal);
        foreach (var producer in section.GetSection("Producers").GetChildren())
        {
            var key = Convert.FromBase64String(producer["Key"] ?? "");
            if (!ObservationProtocol.IsLabel(producer.Key) || !ObservationProtocol.IsLabel(producer["Region"])
                || key.Length < 32 || !Enum.TryParse<ObservationSource>(producer["Source"], out var source)
                || !Enum.IsDefined(source)) throw new InvalidOperationException("Invalid Sli:Import producer trust.");
            trusted.Add(producer.Key, new(key, source, producer["Region"]!));
        }
        if (directory is null || !Path.IsPathFullyQualified(directory) || trusted.Count == 0)
            throw new InvalidOperationException("Invalid Sli:Import private inbox configuration.");
        return new(true, directory, new(trusted));
    }
}

public sealed class SliImportCycle(SliImportOptions options, ISliManifestImporter importer)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || !Directory.Exists(options.Directory)) return 0;
        var processed = 0;
        foreach (var file in Directory.EnumerateFiles(options.Directory, "*.manifest").Take(16))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Collector symbolic links are not accepted.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > ObservationProtocol.MaximumBytes) throw new ArgumentException("Collector file exceeds bound.");
                var bytes = new byte[ObservationProtocol.MaximumBytes + 1];
                var length = 0;
                while (length < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(length), deadline.Token);
                    if (read == 0) break;
                    length += read;
                }
                if (length > ObservationProtocol.MaximumBytes) throw new ArgumentException("Collector file exceeds bound.");
                var result = await importer.ImportAsync(bytes.AsMemory(0, length), deadline.Token);
                if (result.Status == SliImportStatus.Conflict) File.Move(file, file + ".conflict");
                else File.Delete(file); // Only this successfully imported private spool file.
                processed++;
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or OverflowException)
            {
                File.Move(file, file + ".rejected");
                processed++;
            }
            catch (FileNotFoundException) { /* Another Worker may have imported/deleted this same delivery. */ }
        }
        return processed;
    }
}

public sealed class SliImportWorker(IServiceScopeFactory scopes, SliImportOptions options,
    ILogger<SliImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SliImportCycle>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("SLI import unavailable; private spool retained for retry; coverage incomplete"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
