using System.Text;
using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Billing.Tests;

public sealed class StatementPublicationTests
{
    private static StatementSnapshot Snapshot()
    {
        var org = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        return new(org, new(2026, 9), at.AddDays(20),
            [new(Guid.NewGuid(), org, LedgerEntryType.PromotionalCredit, 123, "promotion", Guid.NewGuid(), at)], [],
            new([], 0), new(123, 0, 123, 0, 0, 0), new(0, 0, 0), [], [], [], []);
    }

    [Fact]
    public void Snapshot_round_trip_preserves_period_sources_and_digest()
    {
        var original = Snapshot();
        var encoded = StatementSnapshotCodec.Serialize(original);
        var restored = StatementSnapshotCodec.Deserialize(encoded);
        Assert.Equal(original.Period, restored.Period);
        Assert.Equal(original.Ledger, restored.Ledger);
        Assert.Equal(StatementReconciler.Reconcile(original).SourceDigest, StatementReconciler.Reconcile(restored).SourceDigest);
        Assert.Empty(StatementReconciler.Reconcile(restored).Errors);
        Assert.Equal(encoded, StatementSnapshotCodec.Serialize(restored));
    }

    [Fact]
    public void Artifact_is_deterministic_allowlisted_and_money_is_a_string()
    {
        var snapshot = Snapshot(); var id = Guid.NewGuid();
        var builder = new StatementArtifactBuilder();
        var first = builder.Build(snapshot, id, 1, null, null, new());
        var second = builder.Build(snapshot, id, 1, null, null, new());
        Assert.Equal(first.Json, second.Json); Assert.Equal(first.Csv, second.Csv);
        using var json = JsonDocument.Parse(first.Json);
        Assert.Equal("123", json.RootElement.GetProperty("Totals").GetProperty("ClosingMicroUsd").GetString());
        Assert.Equal("Not a tax invoice / No payment due from this statement", json.RootElement.GetProperty("Label").GetString());
        Assert.DoesNotContain("ProviderCost", Encoding.UTF8.GetString(first.Json));
        Assert.DoesNotContain("Reason", Encoding.UTF8.GetString(first.Json));
        Assert.Equal(3, first.Sources.Count); Assert.Single(first.Lines);
        Assert.All(first.Sources, source => Assert.Equal(StatementSnapshotCodec.Hash(source.PayloadJson), source.Sha256));
        var total = first.Json.Length + first.Csv.Length + Encoding.UTF8.GetByteCount(first.SnapshotJson)
            + first.Sources.Sum(value => Encoding.UTF8.GetByteCount(value.PayloadJson))
            + first.Lines.Sum(value => Encoding.UTF8.GetByteCount(value.PayloadJson));
        Assert.Equal(total, first.TotalBytes);
        Assert.Contains(",PromotionalCredit,2026-09-02T00:00:00.0000000+00:00,123\r\n", Encoding.UTF8.GetString(first.Csv));
    }

    [Fact]
    public void Revision_records_explicit_delta_and_never_changes_old_artifact()
    {
        var old = Snapshot(); var id = Guid.NewGuid(); var builder = new StatementArtifactBuilder();
        var original = builder.Build(old, id, 1, null, null, new());
        var hash = StatementSnapshotCodec.Hash(original.Json);
        var updated = old with { Ledger = [.. old.Ledger, old.Ledger[0] with { Id = Guid.NewGuid(), ReferenceId = Guid.NewGuid(), AmountMicroUsd = 7 }],
            Balance = new(130, 0, 130, 0, 0, 0) };
        var revision = builder.Build(updated, Guid.NewGuid(), 2, id, old, new());
        using var json = JsonDocument.Parse(revision.Json);
        Assert.Equal(id, json.RootElement.GetProperty("PredecessorId").GetGuid());
        Assert.Equal("7", json.RootElement.GetProperty("Delta").GetProperty("ClosingMicroUsd").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("Delta").GetProperty("Added").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("Delta").GetProperty("Removed").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("Delta").GetProperty("Changed").GetInt32()); // wallet
        Assert.Equal(hash, StatementSnapshotCodec.Hash(original.Json));
    }

    [Fact]
    public void Pending_is_disclosed_but_imbalance_and_missing_links_block()
    {
        var snapshot = Snapshot() with { Pending = new(2, 3, 4), Payments = new([], 4) };
        var builder = new StatementArtifactBuilder();
        using var json = JsonDocument.Parse(builder.Build(snapshot, Guid.NewGuid(), 1, null, null, new()).Json);
        Assert.Equal(2, json.RootElement.GetProperty("Pending").GetProperty("Evidence").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("Pending").GetProperty("Settlement").GetInt32());
        Assert.Equal(4, json.RootElement.GetProperty("Pending").GetProperty("MerchantMismatch").GetInt32());
        Assert.Equal("Sources", Assert.Throws<StatementBuildRejectedException>(() => builder.Build(snapshot with
            { Balance = snapshot.Balance with { PostedMicroUsd = 124 } }, Guid.NewGuid(), 1, null, null, new())).Code);
        Assert.Equal("Sources", Assert.Throws<StatementBuildRejectedException>(() => builder.Build(snapshot with
            { Ledger = [snapshot.Ledger[0] with { Type = LedgerEntryType.Refund, ReferenceType = "settlement_refund" }] },
            Guid.NewGuid(), 1, null, null, new())).Code);
    }

    [Fact]
    public void Combined_membership_and_byte_bounds_accept_exact_limit_reject_one_less()
    {
        var snapshot = Snapshot(); var builder = new StatementArtifactBuilder(); var id = Guid.NewGuid();
        var artifact = builder.Build(snapshot, id, 1, null, null, new());
        Assert.Equal(artifact.Json, builder.Build(snapshot, id, 1, null, null, new(3, (int)artifact.TotalBytes)).Json);
        Assert.Equal("Bounds", Assert.Throws<StatementBuildRejectedException>(() => builder.Build(snapshot, id, 1, null, null, new(2))).Code);
        Assert.Equal("Bounds", Assert.Throws<StatementBuildRejectedException>(() => builder.Build(snapshot, id, 1, null, null, new(3, (int)artifact.TotalBytes - 1))).Code);
    }

    [Fact]
    public void Command_hash_normalizes_reason_and_pins_all_terms()
    {
        var command = new StatementBuildCommand(Guid.NewGuid(), Guid.NewGuid(), new(2026, 9), Guid.NewGuid(), 0, null, "  Monthly statement  ");
        var hash = StatementCommandPolicy.Hash(command);
        Assert.Equal(hash, StatementCommandPolicy.Hash(command with { Reason = "Monthly statement" }));
        Assert.NotEqual(hash, StatementCommandPolicy.Hash(command with { Period = new(2026, 10) }));
        Assert.NotEqual(hash, StatementCommandPolicy.Hash(command with { ActorAccountId = Guid.NewGuid() }));
        Assert.NotEqual(hash, StatementCommandPolicy.Hash(command with { Reason = "Corrected statement" }));
        Assert.NotEqual(hash, StatementCommandPolicy.Hash(command with { ExpectedGeneration = 1, PredecessorId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { IdempotencyKey = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { ExpectedGeneration = 1 }));
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { Reason = "short" }));
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { Reason = "unsafe\nreason" }));
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { SchemaVersion = 2 }));
        Assert.Equal(8, StatementCommandPolicy.Normalize(command with { Reason = "12345678" }).Reason.Length);
        Assert.Equal(500, StatementCommandPolicy.Normalize(command with { Reason = new('x', 500) }).Reason.Length);
        Assert.Throws<ArgumentException>(() => StatementCommandPolicy.Normalize(command with { Reason = new('x', 501) }));
    }

    [Fact]
    public void Csv_never_renders_arbitrary_source_reference_text_as_a_formula()
    {
        var snapshot = Snapshot();
        const string formula = "=HYPERLINK(\"https://invalid.test\",\"x\")";
        snapshot = snapshot with { Ledger = [snapshot.Ledger[0] with { ReferenceType = formula }] };
        var artifact = new StatementArtifactBuilder().Build(snapshot, Guid.NewGuid(), 1, null, null, new());
        var csv = Encoding.UTF8.GetString(artifact.Csv);
        Assert.DoesNotContain("HYPERLINK", csv);
        Assert.Contains(",PromotionalCredit,", csv);
        Assert.EndsWith(",123\r\n", csv);
    }

    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(100001, 100, 100)]
    [InlineData(100, 33554433, 100)]
    [InlineData(100, 100, 30001)]
    public void Bounds_cannot_be_disabled(int sources, int bytes, int deadline) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatementBuildLimits(sources, bytes, deadline).Validate());

    [Fact]
    public void Many_payment_annotations_include_only_postings_in_the_selected_month()
    {
        var snapshot = Snapshot();
        var payments = Enumerable.Range(0, 1001).Select(_ => new StatementPaymentEvidence(Guid.NewGuid(), snapshot.OrganizationId,
            Guid.NewGuid(), Guid.NewGuid(), 1, 0, 1_000_000m, 1, true)).ToArray();
        var ledger = payments.Select((payment, index) => new StatementLedgerSource(Guid.NewGuid(), snapshot.OrganizationId,
            LedgerEntryType.TopUp, 1, "payment_intent", payment.IntentId,
            index == 1000 ? snapshot.Period.To : snapshot.Period.From)).ToArray();
        snapshot = snapshot with { Payments = new(payments, 0), Ledger = ledger, Balance = new(1001, 0, 1001, 0, 0, 0) };
        var artifact = new StatementArtifactBuilder().Build(snapshot, Guid.NewGuid(), 1, null, null, new());
        using var json = JsonDocument.Parse(artifact.Json);
        var annotations = json.RootElement.GetProperty("PaymentAnnotations").EnumerateArray().ToArray();
        Assert.Equal(1000, annotations.Length);
        Assert.Equal(1000, artifact.Lines.Count);
        Assert.Equal(2004, artifact.Sources.Count);
        Assert.DoesNotContain(annotations, value => value.GetProperty("IntentId").GetGuid() == payments[1000].IntentId);
        Assert.All(annotations, value => Assert.Equal("1", value.GetProperty("CreditMicroUsd").GetString()));
        Assert.Equal("1000", json.RootElement.GetProperty("Totals").GetProperty("ClosingMicroUsd").GetString());
        Assert.Equal("1", json.RootElement.GetProperty("Totals").GetProperty("PostPeriodMovementMicroUsd").GetString());
    }
}
