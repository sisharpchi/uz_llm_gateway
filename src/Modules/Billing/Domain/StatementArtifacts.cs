using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Billing.Domain;

public static class StatementCommandPolicy
{
    public static StatementBuildCommand Normalize(StatementBuildCommand value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.OrganizationId == Guid.Empty || value.ActorAccountId == Guid.Empty || value.IdempotencyKey == Guid.Empty
            || value.Period is null || value.ExpectedGeneration < 0 || value.ExpectedGeneration == long.MaxValue
            || value.SchemaVersion != 1 || value.PredecessorId == Guid.Empty
            || (value.ExpectedGeneration == 0) != (value.PredecessorId is null)) throw new ArgumentException("Invalid statement command.");
        var reason = value.Reason?.Trim();
        if (reason is null || reason.Length is < 8 or > 500 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid statement reason.");
        return value with { Reason = reason };
    }

    public static string Hash(StatementBuildCommand value) => StatementSnapshotCodec.Hash(StatementSnapshotCodec.Serialize(Normalize(value)));
}

public static class StatementSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new PeriodConverter() } };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static StatementSnapshot Deserialize(string value) => JsonSerializer.Deserialize<StatementSnapshot>(value, Options)
        ?? throw new StatementBuildRejectedException("SourceDigest");
    public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    public static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private sealed class PeriodConverter : JsonConverter<StatementPeriod>
    {
        public override StatementPeriod Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var from = json.RootElement.GetProperty("From").GetDateTimeOffset();
            var period = StatementPeriod.At(from);
            if (from.Offset != TimeSpan.Zero || from != period.From || json.RootElement.GetProperty("To").GetDateTimeOffset() != period.To)
                throw new JsonException("Invalid UTC statement period.");
            return period;
        }
        public override void Write(Utf8JsonWriter writer, StatementPeriod period, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteString("From", period.From); writer.WriteString("To", period.To); writer.WriteEndObject();
        }
    }
}

public sealed class StatementArtifactBuilder : IStatementArtifactBuilder
{
    public StatementArtifact Build(StatementSnapshot snapshot, Guid versionId, long revision,
        Guid? predecessorId, StatementSnapshot? predecessor, StatementBuildLimits limits)
    {
        limits.Validate();
        if (versionId == Guid.Empty || revision < 1 || (revision == 1) != (predecessorId is null)
            || (predecessorId is null) != (predecessor is null)
            || predecessor is not null && (predecessor.OrganizationId != snapshot.OrganizationId || predecessor.Period != snapshot.Period))
            throw new StatementBuildRejectedException("Generation");
        var result = StatementReconciler.Reconcile(snapshot);
        if (!result.FinanciallyReconciled) throw new StatementBuildRejectedException("Sources");
        var sources = Sources(snapshot);
        if (sources.Count > limits.MaximumSources) throw new StatementBuildRejectedException("Bounds");
        var prior = predecessor is null ? [] : Sources(predecessor);
        var priorById = prior.ToDictionary(value => (value.Kind, value.Id));
        var currentById = sources.ToDictionary(value => (value.Kind, value.Id));
        var previousClosing = predecessor is null ? result.ClosingMicroUsd : StatementReconciler.Reconcile(predecessor).ClosingMicroUsd;
        var lines = snapshot.Ledger.Where(value => snapshot.Period.Contains(value.OccurredAt))
            .OrderBy(value => value.OccurredAt).ThenBy(value => value.Id).Select((value, index) => new StatementLineData(index + 1,
                value.Id, StatementSnapshotCodec.Serialize(new { value.Id, Category = value.Type.ToString(), value.OccurredAt,
                    AmountMicroUsd = value.AmountMicroUsd.ToString(CultureInfo.InvariantCulture), value.ReferenceType, value.ReferenceId }))).ToArray();
        var snapshotJson = StatementSnapshotCodec.Serialize(snapshot);
        var periodTopUps = snapshot.Ledger.Where(entry => entry.Type == LedgerEntryType.TopUp && snapshot.Period.Contains(entry.OccurredAt))
            .Select(entry => entry.ReferenceId).ToHashSet();
        var json = Encoding.UTF8.GetBytes(StatementSnapshotCodec.Serialize(new
        {
            SchemaVersion = 1, CalculationVersion = StatementReconciliation.CalculationVersion, Id = versionId,
            result.Kind, result.Label, snapshot.OrganizationId, snapshot.Period, Revision = revision, PredecessorId = predecessorId,
            SourceAsOf = snapshot.CapturedAt, result.SourceDigest, FinancialCoverage = "ReconciledSnapshot", result.Pending,
            Totals = new { OpeningMicroUsd = Money(result.OpeningMicroUsd), MovementMicroUsd = Money(result.MovementMicroUsd),
                ClosingMicroUsd = Money(result.ClosingMicroUsd), PostPeriodMovementMicroUsd = Money(result.PostPeriodMovementMicroUsd),
                GrossUsageMicroUsd = Money(result.GrossUsageMicroUsd), RefundedUsageMicroUsd = Money(result.RefundedUsageMicroUsd), NetUsageMicroUsd = Money(result.NetUsageMicroUsd) },
            Balance = new { PostedMicroUsd = Money(snapshot.Balance.PostedMicroUsd), HeldMicroUsd = Money(snapshot.Balance.HeldMicroUsd),
                AvailableMicroUsd = Money(snapshot.Balance.AvailableMicroUsd), RecoveryDebtMicroUsd = Money(snapshot.Balance.RecoveryDebtMicroUsd) },
            Delta = new { ClosingMicroUsd = Money(checked(result.ClosingMicroUsd - previousClosing)),
                Added = currentById.Keys.Count(key => !priorById.ContainsKey(key)), Removed = priorById.Keys.Count(key => !currentById.ContainsKey(key)),
                Changed = sources.Count(value => priorById.TryGetValue((value.Kind, value.Id), out var old) && old.Sha256 != value.Sha256) },
            PaymentAnnotations = snapshot.Payments.Items.Where(payment => periodTopUps.Contains(payment.IntentId))
                .OrderBy(payment => payment.IntentId).Select(payment => new { payment.IntentId, payment.FxSnapshotId,
                    PrincipalTiyin = Money(payment.PrincipalTiyin), FeeTiyin = Money(payment.FeeTiyin),
                    FrozenTiyinPerUsd = payment.UzsTiyinPerUsd.ToString("G29", CultureInfo.InvariantCulture), CreditMicroUsd = Money(payment.CreditMicroUsd) }),
            Lines = lines.Select(value => JsonSerializer.Deserialize<JsonElement>(value.PayloadJson))
        }));
        var csv = new StringBuilder("\"Not a tax invoice / No payment due from this statement\"\r\nordinal,ledger_id,category,posted_at,amount_micro_usd\r\n");
        foreach (var line in lines)
        {
            using var data = JsonDocument.Parse(line.PayloadJson);
            csv.Append(line.Ordinal.ToString(CultureInfo.InvariantCulture)).Append(',').Append(line.LedgerId).Append(',')
                .Append(data.RootElement.GetProperty("Category").GetString()).Append(',')
                .Append(data.RootElement.GetProperty("OccurredAt").GetDateTimeOffset().ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(data.RootElement.GetProperty("AmountMicroUsd").GetString()).Append("\r\n");
        }
        var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
        var totalBytes = checked((long)json.Length + csvBytes.Length + Encoding.UTF8.GetByteCount(snapshotJson)
            + sources.Sum(value => (long)Encoding.UTF8.GetByteCount(value.PayloadJson))
            + lines.Sum(value => (long)Encoding.UTF8.GetByteCount(value.PayloadJson)));
        if (totalBytes > limits.MaximumBytes) throw new StatementBuildRejectedException("Bounds");
        return new(result, snapshotJson, sources, lines, json, csvBytes, totalBytes);
    }

    private static string Money(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<StatementSourceData> Sources(StatementSnapshot snapshot)
    {
        var sources = new List<StatementSourceData>();
        void Add<T>(string kind, Guid id, T data)
        {
            var json = StatementSnapshotCodec.Serialize(data);
            sources.Add(new(kind, id, json, StatementSnapshotCodec.Hash(json)));
        }
        foreach (var value in snapshot.Ledger) Add("ledger", value.Id, value);
        foreach (var value in snapshot.Links) Add("link." + value.Kind, value.SourceId, value);
        foreach (var value in snapshot.Payments.Items) Add("payment", value.IntentId, value);
        foreach (var value in snapshot.Holds) Add("hold", value.Id, value);
        foreach (var value in snapshot.DebtEntries) Add("debt", value.Id, value);
        foreach (var value in snapshot.Reversals) Add("reversal", value.Id, value);
        foreach (var value in snapshot.Usage) Add("usage", value.Id, value);
        Add("wallet", snapshot.OrganizationId, new { snapshot.OrganizationId, snapshot.Balance });
        Add("pending", snapshot.OrganizationId, new { snapshot.OrganizationId, snapshot.Pending });
        return sources.OrderBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray();
    }
}
