using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace UZLLM.Observability;

public enum ObservationSource { Gateway, Edge, Probe }
public enum ObservationPhase { Started, Terminal, Attempt, Probe }
public enum ObservationOperation { Chat, Responses, Embeddings, Readiness }
public enum ObservationMode { Unknown, Json, Sse }
public enum ObservationCredential { Unknown, Managed, Byok, Hybrid }
public enum ObservationProviderFault { None, RateLimited, Capacity, Authentication, Timeout, Upstream5xx, ContextExceeded, InvalidRequest, Unknown, QuotaExhausted, ContentRejected }
public enum ObservationExecutionCertainty { Unknown, NotDispatched, RejectedBeforeExecution, Dispatched }

// Deliberately not an HTTP/log/domain entity: no body, URL, header, error text,
// tenant, model, provider credential or financial amount can be represented.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecutionObservation(long Sequence, Guid ExecutionId,
    ObservationPhase Phase, ObservationOperation Operation, ObservationMode Mode,
    DateTimeOffset At, DateTimeOffset StartedAt, bool TrustedEdge, int HttpStatus,
    bool TerminalSuccess, bool ClientDisconnected, bool GatewayFault, bool PolicyRejection,
    ObservationProviderFault ProviderFault, ObservationCredential Credential,
    Guid? RequestId = null, Guid? AttemptId = null,
    ObservationExecutionCertainty Certainty = ObservationExecutionCertainty.Unknown,
    DateTimeOffset? GatewayFailureAt = null, DateTimeOffset? ProviderFailureAt = null,
    DateTimeOffset? ClientDisconnectedAt = null, int? UpstreamStatus = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObservationManifest(int Version, Guid Epoch, long BatchNumber,
    ObservationSource Source, string Region, long FirstSequence, long LastSequence,
    DateTimeOffset IntervalStart, DateTimeOffset IntervalEnd, long Dropped,
    bool CollectorContinuous, bool ClockSynchronized, string TerminalCoverage,
    ExecutionObservation[] Observations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SignedObservationManifest(string Producer, string Payload,
    string Sha256, string Signature);

public sealed record ObservationProducerTrust(byte[] Key, ObservationSource Source, string Region);

public static class ObservationProtocol
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const int MaximumObservations = 1024;
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8,
            RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static bool IsLabel(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static byte[] Sign(string producer, ObservationManifest manifest, byte[] key)
    {
        if (!IsLabel(producer) || key.Length < 32) throw new ArgumentException("Invalid collector identity/key.");
        Validate(manifest);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(payload)));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Material(producer, digest, payload)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new SignedObservationManifest(producer, payload, digest, signature), Json);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("Collector manifest exceeds byte limit.");
        return bytes;
    }

    public static (string Producer, string Digest, ObservationManifest Manifest) Verify(
        ReadOnlySpan<byte> bytes, IReadOnlyDictionary<string, ObservationProducerTrust> trusted)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new ArgumentException("Invalid manifest size.");
        var envelope = JsonSerializer.Deserialize<SignedObservationManifest>(bytes, Json)
            ?? throw new ArgumentException("Invalid collector envelope.");
        if (!IsLabel(envelope.Producer) || !trusted.TryGetValue(envelope.Producer, out var trust)
            || trust.Key.Length < 32 || envelope.Payload is null || envelope.Sha256 is not { Length: 64 }
            || envelope.Signature is not { Length: 64 }) throw new ArgumentException("Untrusted collector.");
        var payload = Convert.FromBase64String(envelope.Payload);
        var digest = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(envelope.Sha256))
            || !CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(trust.Key, Material(envelope.Producer, envelope.Sha256, envelope.Payload)),
                Convert.FromHexString(envelope.Signature))) throw new ArgumentException("Invalid collector authentication.");
        var manifest = JsonSerializer.Deserialize<ObservationManifest>(payload, Json)
            ?? throw new ArgumentException("Invalid collector manifest.");
        Validate(manifest);
        if (manifest.Source != trust.Source || manifest.Region != trust.Region)
            throw new ArgumentException("Collector source/scope mismatch.");
        return (envelope.Producer, Convert.ToHexStringLower(digest), manifest);
    }

    public static void Validate(ObservationManifest manifest)
    {
        if (manifest.Version != 1 || manifest.Epoch == Guid.Empty || manifest.BatchNumber < 1
            || !Enum.IsDefined(manifest.Source) || !IsLabel(manifest.Region)
            || manifest.IntervalStart.Offset != TimeSpan.Zero || manifest.IntervalEnd.Offset != TimeSpan.Zero
            || manifest.IntervalEnd < manifest.IntervalStart || manifest.IntervalEnd - manifest.IntervalStart > TimeSpan.FromHours(1)
            || manifest.FirstSequence < 1 || manifest.LastSequence < manifest.FirstSequence - 1
            || manifest.Observations is null || manifest.Observations.Length > MaximumObservations
            || manifest.Dropped < 0 || manifest.TerminalCoverage is not ("Partial" or "Unknown" or "NotApplicable"))
            throw new ArgumentException("Invalid collector manifest fields.");
        var range = checked(manifest.LastSequence - manifest.FirstSequence + 1);
        if (range != checked(manifest.Observations.LongLength + manifest.Dropped))
            throw new ArgumentException("Collector sequence/count mismatch.");
        long previous = manifest.FirstSequence - 1;
        var identities = new HashSet<(Guid, ObservationPhase, Guid?)>();
        foreach (var item in manifest.Observations)
        {
            if (item.Sequence <= previous || item.Sequence > manifest.LastSequence
                || item.ExecutionId == Guid.Empty || !Enum.IsDefined(item.Phase) || !Enum.IsDefined(item.Operation)
                || !Enum.IsDefined(item.Mode) || !Enum.IsDefined(item.ProviderFault) || !Enum.IsDefined(item.Credential)
                || !Enum.IsDefined(item.Certainty)
                || new[] { item.GatewayFailureAt, item.ProviderFailureAt, item.ClientDisconnectedAt }
                    .Any(value => value is { } time && (time.Offset != TimeSpan.Zero || time < item.StartedAt || time > item.At))
                || item.At.Offset != TimeSpan.Zero || item.StartedAt.Offset != TimeSpan.Zero || item.At < item.StartedAt
                || item.At > manifest.IntervalEnd || item.At < manifest.IntervalStart
                || item.HttpStatus is < 0 or > 599 || item.HttpStatus is > 0 and < 100
                || item.UpstreamStatus is < 100 or > 599
                || item.RequestId == Guid.Empty || item.AttemptId == Guid.Empty
                || !identities.Add((item.ExecutionId, item.Phase, item.AttemptId))
                || item.TerminalSuccess && (manifest.Source != ObservationSource.Gateway
                    || item.Phase != ObservationPhase.Terminal || item.Mode == ObservationMode.Unknown
                    || item.HttpStatus is < 200 or > 299 || item.ClientDisconnected || item.GatewayFault
                    || item.ProviderFault != ObservationProviderFault.None || item.PolicyRejection)
                || manifest.Source == ObservationSource.Probe && (item.Phase != ObservationPhase.Probe
                    || item.Operation != ObservationOperation.Readiness)
                || manifest.Source != ObservationSource.Probe && item.Phase == ObservationPhase.Probe
                || manifest.Source != ObservationSource.Probe && item.Operation == ObservationOperation.Readiness
                || item.Phase == ObservationPhase.Attempt && item.AttemptId is null
                || manifest.Source == ObservationSource.Edge && item.Phase != ObservationPhase.Terminal)
                throw new ArgumentException("Invalid collector observation.");
            previous = item.Sequence;
        }
    }

    private static byte[] Material(string producer, string digest, string payload) =>
        Encoding.UTF8.GetBytes(producer + "\n" + digest + "\n" + payload);
}

public sealed class ExecutionObservationState(Guid executionId, bool trustedEdge,
    ObservationOperation operation, DateTimeOffset startedAt, TimeProvider clock)
{
    public const string ItemName = "UZLLM.ExecutionObservation";
    public ObservationMode Mode { get; set; }
    public ObservationCredential Credential { get; set; }
    public bool TerminalSuccess { get; set; }
    private DateTimeOffset? gatewayFailureAt, providerFailureAt;
    private bool gatewayFault;
    private ObservationProviderFault providerFault;
    private long disconnectTicks;
    public bool GatewayFault
    {
        get => gatewayFault;
        set { gatewayFault = value; if (value) gatewayFailureAt ??= clock.GetUtcNow(); }
    }
    public bool PolicyRejection { get; set; }
    public ObservationProviderFault ProviderFault
    {
        get => providerFault;
        set
        {
            if (value == ObservationProviderFault.None) providerFailureAt = null;
            else if (value != providerFault || providerFailureAt is null) providerFailureAt = clock.GetUtcNow();
            providerFault = value;
        }
    }
    public ObservationExecutionCertainty Certainty { get; set; }
    public int? UpstreamStatus { get; set; }
    public void ObserveDisconnect() => Interlocked.CompareExchange(ref disconnectTicks, clock.GetUtcNow().UtcTicks, 0);
    public static ExecutionObservationState? From(HttpContext context) =>
        context.Items.TryGetValue(ItemName, out var item) ? item as ExecutionObservationState : null;
    public ExecutionObservation Snapshot(HttpContext context, ObservationPhase phase, DateTimeOffset at,
        Guid? attemptId = null)
    {
        var ticks = Volatile.Read(ref disconnectTicks);
        DateTimeOffset? disconnectedAt = ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        var disconnected = ticks > 0 || context.RequestAborted.IsCancellationRequested;
        // Cancellation can race the caller's timestamp; never emit a future-within-event timestamp.
        if (disconnectedAt > at) at = disconnectedAt.Value;
        return new(0, executionId, phase, operation, Mode, at, startedAt,
            trustedEdge, context.Response.StatusCode, TerminalSuccess && !disconnected,
            disconnected, GatewayFault, PolicyRejection, ProviderFault,
            Credential, Guid.TryParse(context.Response.Headers[RequestCorrelationMiddleware.RequestIdHeaderName],
                out var requestId) ? requestId : null, attemptId, Certainty, gatewayFailureAt, providerFailureAt,
            disconnectedAt, UpstreamStatus);
    }
}

public sealed class ObservationEdgeTrust(IEnumerable<IPAddress> peers)
{
    public const string Header = "X-UZLLM-Edge-Execution-Id";
    private readonly HashSet<IPAddress> addresses = peers.Select(value => value.MapToIPv6()).ToHashSet();
    public Guid? Resolve(HttpContext context) => context.Request.IsHttps
        && context.Connection.RemoteIpAddress is { } peer && addresses.Contains(peer.MapToIPv6())
        && context.Request.Headers[Header].Count == 1
        && Guid.TryParseExact(context.Request.Headers[Header].ToString(), "N", out var id) && id != Guid.Empty
            ? id : null;
}
