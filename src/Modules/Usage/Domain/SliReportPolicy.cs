using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Observability;

namespace UZLLM.Modules.Usage.Domain;

public static class SliReportPolicy
{
    public const string CalculationVersion = "sli-v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static byte[] Serialize<T>(T value, int maximumBytes = 32 * 1024 * 1024, CancellationToken ct = default)
    {
        using var output = new BoundedJsonStream(maximumBytes, ct);
        JsonSerializer.Serialize(output, value, Json);
        return output.ToArray();
    }
    public static T Read<T>(ReadOnlySpan<byte> bytes)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new SliReportRejectedException("Integrity"); }
        catch (JsonException) { throw new SliReportRejectedException("Integrity"); }
    }
    public static void Validate(SliReportScope scope)
    {
        if (!ObservationProtocol.IsLabel(scope.Id) || !ObservationProtocol.IsLabel(scope.Region) || scope.Version != 1
            || scope.ProbeMaximumGapSeconds is < 1 or > 300
            || scope.IndependentProbeEvidence is not null && !ObservationProtocol.IsLabel(scope.IndependentProbeEvidence)
            || scope.Operation is not ("Chat" or "Responses" or "Embeddings") || scope.Mode is not ("All" or "Json" or "Sse")
            || scope.Producers is not { Length: >= 3 and <= 32 }
            || scope.Producers.Any(value => !ObservationProtocol.IsLabel(value.Producer) || value.Source is not ("Edge" or "Gateway" or "Probe"))
            || scope.Producers.Select(value => value.Producer).Distinct(StringComparer.Ordinal).Count() != scope.Producers.Length
            || new[] { "Edge", "Gateway", "Probe" }.Any(source => !scope.Producers.Any(value => value.Source == source)))
            throw new ArgumentException("Invalid registered SLI scope.");
    }
    public static DateTimeOffset Month(DateTimeOffset start)
    {
        if (start.Offset != TimeSpan.Zero || start.Year >= 9999 || start.Day != 1 || start.TimeOfDay != TimeSpan.Zero)
            throw new ArgumentException("Use a UTC calendar month start.");
        return start.AddMonths(1);
    }
    public static SliReportCommand Normalize(SliReportCommand command, DateTimeOffset now)
    {
        if (Month(command.PeriodStart) > now || command.ActorId == Guid.Empty || command.IdempotencyKey == Guid.Empty
            || !ObservationProtocol.IsLabel(command.ScopeId) || command.ExpectedGeneration < 0 || command.ExpectedGeneration == long.MaxValue
            || command.PredecessorId == Guid.Empty || (command.PredecessorId is null) != (command.ExpectedGeneration == 0))
            throw new ArgumentException("Invalid closed-month SLI command.");
        var reason = command.Reason?.Trim();
        if (reason is not { Length: >= 8 and <= 500 } || reason.Any(char.IsControl)) throw new ArgumentException("Invalid reason.");
        return command with { Reason = reason };
    }
    private sealed class BoundedJsonStream(int maximumBytes, CancellationToken ct) : MemoryStream
    {
        private void Check(int count)
        {
            ct.ThrowIfCancellationRequested();
            if (maximumBytes is < 1 or > 32 * 1024 * 1024 || Length + count > maximumBytes) throw new SliReportRejectedException("Bounds");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    }
}
