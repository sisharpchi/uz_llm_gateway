using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Billing.Tests;

public sealed class StatementReconciliationTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Exact_fixture_separates_usage_reversal_debt_holds_and_frozen_tiyin()
    {
        var snapshot = Fixture();
        var result = StatementReconciler.Reconcile(snapshot);
        Assert.Empty(result.Errors);
        Assert.True(result.FinanciallyReconciled);
        Assert.Equal("AccountStatement", result.Kind);
        Assert.Equal("Not a tax invoice / No payment due from this statement", result.Label);
        Assert.Equal(1_000_000, result.OpeningMicroUsd);
        Assert.Equal(500_000, result.MovementMicroUsd);
        Assert.Equal(1_500_000, result.ClosingMicroUsd);
        Assert.Equal(3_000_000, result.GrossUsageMicroUsd);
        Assert.Equal(500_000, result.RefundedUsageMicroUsd);
        Assert.Equal(2_500_000, result.NetUsageMicroUsd);
        Assert.Equal(new StatementBalance(1_500_000, 1_500_000, 0, 2_800_000, 1_500_000, 2_800_000), snapshot.Balance);
        Assert.Equal(8_000_000, Assert.Single(snapshot.Payments.Items).CreditMicroUsd);
        Assert.Equal(10_000_000, Assert.Single(snapshot.Payments.Items).PrincipalTiyin);
        Assert.Equal(-4_500_000, Assert.Single(snapshot.Links, value => value.Kind == "payment_reversal").AmountMicroUsd);
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2025, 2, 28)]
    [InlineData(2026, 12, 31)]
    [InlineData(2027, 1, 31)]
    public void Period_is_half_open_utc_calendar_month(int year, int month, int days)
    {
        var period = new StatementPeriod(year, month);
        Assert.Equal(days, (period.To - period.From).TotalDays);
        Assert.Equal(TimeSpan.Zero, period.From.Offset);
        Assert.False(period.Contains(period.From.AddTicks(-1)));
        Assert.True(period.Contains(period.From));
        Assert.True(period.Contains(period.To.AddTicks(-1)));
        Assert.False(period.Contains(period.To));
    }

    [Fact]
    public void Period_uses_utc_instant_not_display_offset()
    {
        var local = new DateTimeOffset(2024, 3, 1, 1, 0, 0, TimeSpan.FromHours(5));
        Assert.Equal(new StatementPeriod(2024, 2), StatementPeriod.At(local));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9999, 12)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void Invalid_calendar_period_is_rejected(int year, int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatementPeriod(year, month));

    [Fact]
    public void Late_refund_uses_posting_month_with_negative_period_net_and_historical_bridge()
    {
        var snapshot = Fixture();
        var refund = snapshot.Ledger.Single(value => value.Type == LedgerEntryType.Refund);
        snapshot = snapshot with { Ledger = snapshot.Ledger.Select(value => value.Id == refund.Id
            ? value with { OccurredAt = October.AddMonths(1) } : value).ToArray() };
        var october = StatementReconciler.Reconcile(snapshot);
        Assert.Empty(october.Errors);
        Assert.Equal(1_000_000, october.ClosingMicroUsd);
        Assert.Equal(500_000, october.PostPeriodMovementMicroUsd);
        Assert.Equal(0, october.RefundedUsageMicroUsd);
        var november = StatementReconciler.Reconcile(snapshot with { Period = new(2026, 11) });
        Assert.Empty(november.Errors);
        Assert.Equal(1_000_000, november.OpeningMicroUsd);
        Assert.Equal(-500_000, november.NetUsageMicroUsd);
        Assert.Equal(1_500_000, november.ClosingMicroUsd);
        Assert.True(snapshot.Links.Single(value => value.Kind == "settlement").RequestStartedAt < October);
    }

    [Theory]
    [InlineData("duplicate", "duplicate_ledger_id")]
    [InlineData("tenant", "tenant_mismatch")]
    [InlineData("missing", "missing_or_mismatched_financial_link")]
    [InlineData("reference", "financial_reference_mismatch")]
    [InlineData("direction", "invalid_posting_direction")]
    [InlineData("posted", "posted_balance_mismatch")]
    [InlineData("held", "hold_balance_mismatch")]
    [InlineData("debt", "debt_balance_mismatch")]
    [InlineData("fx", "payment_credit_fx_mismatch")]
    [InlineData("quote", "invalid_frozen_payment_terms")]
    [InlineData("reversal", "invalid_reversal_breakdown")]
    [InlineData("orphan", "source_without_posting")]
    [InlineData("links", "duplicate_source_link")]
    [InlineData("payments", "duplicate_payment_source")]
    [InlineData("no_payment", "missing_or_mismatched_payment_link")]
    [InlineData("no_usage", "missing_settlement_usage_terms")]
    [InlineData("available", "invalid_wallet_balance")]
    [InlineData("hold_source", "balance_source_mismatch")]
    [InlineData("pending", "invalid_pending_counts")]
    [InlineData("reversal_source", "reversal_source_mismatch")]
    [InlineData("debt_source", "reversal_debt_source_mismatch")]
    public void Invalid_sources_or_balances_block_financial_reconciliation(string mutation, string error)
    {
        var snapshot = Fixture();
        snapshot = mutation switch
        {
            "duplicate" => snapshot with { Ledger = [..snapshot.Ledger, snapshot.Ledger[0]] },
            "tenant" => snapshot with { Links = snapshot.Links.Select(value => value with { OrganizationId = Guid.NewGuid() }).ToArray() },
            "missing" => snapshot with { Links = [] },
            "reference" => snapshot with { Links = snapshot.Links.Select(value => value with { ReferenceId = Guid.NewGuid() }).ToArray() },
            "direction" => snapshot with { Ledger = snapshot.Ledger.Select(value => value.Type == LedgerEntryType.UsageCharge
                ? value with { AmountMicroUsd = 3_000_000 } : value).ToArray() },
            "posted" => snapshot with { Balance = snapshot.Balance with { PostedMicroUsd = 2_000_000 } },
            "held" => snapshot with { Balance = snapshot.Balance with { ActiveReservationMicroUsd = 0 } },
            "debt" => snapshot with { Balance = snapshot.Balance with { DebtEntriesMicroUsd = 0 } },
            "fx" => snapshot with { Payments = new([snapshot.Payments.Items[0] with { UzsTiyinPerUsd = 1_250_001m }], 0) },
            "quote" => snapshot with { Payments = new([snapshot.Payments.Items[0] with { FrozenTermsMatch = false }], 0) },
            "reversal" => snapshot with { Links = snapshot.Links.Select(value => value.Kind == "payment_reversal"
                ? value with { ReversalDebtCreatedMicroUsd = 0 } : value).ToArray() },
            "orphan" => snapshot with { Links = [..snapshot.Links, snapshot.Links[0] with { LedgerId = Guid.NewGuid() }] },
            "links" => snapshot with { Links = [..snapshot.Links, snapshot.Links[0]] },
            "payments" => snapshot with { Payments = new([..snapshot.Payments.Items, snapshot.Payments.Items[0]], 0) },
            "no_payment" => snapshot with { Payments = new([], 0) },
            "no_usage" => snapshot with { Usage = [] },
            "available" => snapshot with { Balance = snapshot.Balance with { AvailableMicroUsd = 1 } },
            "hold_source" => snapshot with { Holds = [snapshot.Holds[0] with { AmountMicroUsd = 1 }] },
            "pending" => snapshot with { Pending = new(-1, 0, 0) },
            "reversal_source" => snapshot with { Reversals = [] },
            "debt_source" => snapshot with { DebtEntries = [] },
            _ => throw new InvalidOperationException()
        };
        var result = StatementReconciler.Reconcile(snapshot);
        Assert.False(result.FinanciallyReconciled);
        Assert.Contains(error, result.Errors);
    }

    [Fact]
    public void Pending_is_disclosed_without_zero_charge_or_fake_external_finality()
    {
        var snapshot = Fixture() with { Pending = new(2, 3, 1) };
        snapshot = snapshot with { Payments = snapshot.Payments with { OpenMismatchCount = 1 } };
        var result = StatementReconciler.Reconcile(snapshot);
        Assert.True(result.FinanciallyReconciled);
        Assert.Equal(new StatementPending(2, 3, 1), result.Pending);
        Assert.Equal(3_000_000, result.GrossUsageMicroUsd);
    }

    [Fact]
    public void Frozen_payment_fee_uses_floor_and_never_adds_tiyin_to_wallet()
    {
        var snapshot = Fixture();
        var payment = snapshot.Payments.Items[0] with { FeeTiyin = 1, CreditMicroUsd = 7_999_999 };
        snapshot = snapshot with { Ledger = [snapshot.Ledger[1] with { AmountMicroUsd = 7_999_999 }],
            Links = [], Payments = new([payment], 0), Balance = new(7_999_999, 0, 7_999_999, 0, 0, 0),
            Holds = [], DebtEntries = [], Reversals = [], Usage = [] };
        var result = StatementReconciler.Reconcile(snapshot);
        Assert.Empty(result.Errors);
        Assert.Equal(7_999_999, result.ClosingMicroUsd);
        Assert.Equal(0, result.GrossUsageMicroUsd);
    }

    [Fact]
    public void Digest_freezes_exact_membership_terms_and_balances_independent_of_input_order()
    {
        var snapshot = Fixture();
        var original = StatementReconciler.Reconcile(snapshot);
        Assert.Equal(original.SourceDigest, StatementReconciler.Reconcile(snapshot with
            { Ledger = snapshot.Ledger.Reverse().ToArray(), Links = snapshot.Links.Reverse().ToArray() }).SourceDigest);
        Assert.NotEqual(original.SourceDigest, StatementReconciler.Reconcile(snapshot with
            { Links = snapshot.Links.Select(value => value with { FeePolicyVersionId = Guid.NewGuid() }).ToArray() }).SourceDigest);
        Assert.Equal(64, original.SourceDigest.Length);
    }

    [Fact]
    public void Empty_snapshot_reconciles_but_overflow_never_wraps_or_clamps()
    {
        var snapshot = Fixture() with { Ledger = [], Links = [], Payments = new([], 0), Balance = new(0, 0, 0, 0, 0, 0),
            Holds = [], DebtEntries = [], Reversals = [], Usage = [] };
        Assert.True(StatementReconciler.Reconcile(snapshot).FinanciallyReconciled);
        snapshot = snapshot with { Ledger = [new(Guid.NewGuid(), snapshot.OrganizationId, LedgerEntryType.PromotionalCredit,
            long.MaxValue, "promotion", Guid.NewGuid(), October), new(Guid.NewGuid(), snapshot.OrganizationId,
            LedgerEntryType.PromotionalCredit, 1, "promotion", Guid.NewGuid(), October)] };
        Assert.Throws<OverflowException>(() => StatementReconciler.Reconcile(snapshot));
    }

    private static StatementSnapshot Fixture()
    {
        var org = Guid.NewGuid();
        StatementLedgerSource Entry(LedgerEntryType type, long amount, string reference, DateTimeOffset? at = null) =>
            new(Guid.NewGuid(), org, type, amount, reference, Guid.NewGuid(), at ?? October.AddDays(1));
        var ledger = new[] { Entry(LedgerEntryType.PromotionalCredit, 1_000_000, "promotion", October.AddDays(-1)),
            Entry(LedgerEntryType.TopUp, 8_000_000, "payment_intent"), Entry(LedgerEntryType.UsageCharge, -3_000_000, "reservation"),
            Entry(LedgerEntryType.AdjustmentDebit, -4_500_000, "payment_reversal"), Entry(LedgerEntryType.Refund, 500_000, "settlement_refund"),
            Entry(LedgerEntryType.AdjustmentCredit, 200_000, "wallet_adjustment"), Entry(LedgerEntryType.AdjustmentDebit, -700_000, "debt_recovery") };
        ledger[3] = ledger[3] with { ReferenceId = ledger[1].ReferenceId };
        var links = ledger.Skip(2).Select(value => new StatementSourceLink(value.Id, org, value.ReferenceType switch
            { "reservation" => "settlement", "settlement_refund" => "refund", "wallet_adjustment" => "approved_adjustment", _ => value.ReferenceType },
            Guid.NewGuid(), value.ReferenceId, value.AmountMicroUsd, null, October.AddDays(-2), Guid.NewGuid(), null,
            value.ReferenceType == "payment_reversal" ? 8_000_000 : null,
            value.ReferenceType == "payment_reversal" ? 3_500_000 : null)).ToArray();
        var requestId = Guid.NewGuid();
        links[0] = links[0] with { RelatedSourceId = requestId };
        var recoveryId = Guid.NewGuid();
        links[4] = links[4] with { SourceId = recoveryId };
        return new(org, new(2026, 10), October.AddDays(15), ledger, links,
            new([new(ledger[1].ReferenceId, org, Guid.NewGuid(), Guid.NewGuid(), 10_000_000, 0, 1_250_000m, 8_000_000, true)], 0),
            new(1_500_000, 1_500_000, 0, 2_800_000, 1_500_000, 2_800_000), new(0, 0, 0),
            [new(Guid.NewGuid(), org, Guid.NewGuid(), 1_500_000, October, October.AddDays(1))],
            [new(Guid.NewGuid(), org, "Incurred", ledger[3].ReferenceId, 3_500_000, October),
                new(recoveryId, org, "Recovered", ledger[6].ReferenceId, -700_000, October)],
            [new(links[1].SourceId, org, ledger[3].ReferenceId, 8_000_000, 4_500_000, 3_500_000, October)],
            [new(Guid.NewGuid(), org, requestId, Guid.NewGuid(), links[0].SourceId, "Verified", Guid.NewGuid(),
                3_000_000, 0, 0, null, October)]);
    }
}
