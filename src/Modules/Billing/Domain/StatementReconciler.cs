using System.Security.Cryptography;
using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Billing.Domain;

public static class StatementReconciler
{
    public static StatementReconciliation Reconcile(StatementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.OrganizationId == Guid.Empty || snapshot.CapturedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("A tenant and UTC snapshot capture time are required.", nameof(snapshot));
        var errors = new List<string>();
        if (snapshot.Ledger.GroupBy(value => value.Id).Any(group => group.Count() != 1))
            errors.Add("duplicate_ledger_id");
        if (snapshot.Links.GroupBy(value => value.LedgerId).Any(group => group.Count() != 1))
            errors.Add("duplicate_source_link");
        if (snapshot.Payments.Items.GroupBy(value => value.IntentId).Any(group => group.Count() != 1))
            errors.Add("duplicate_payment_source");
        if (snapshot.Ledger.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.Links.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.Payments.Items.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.Holds.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.DebtEntries.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.Reversals.Any(value => value.OrganizationId != snapshot.OrganizationId)
            || snapshot.Usage.Any(value => value.OrganizationId != snapshot.OrganizationId))
            errors.Add("tenant_mismatch");
        if (snapshot.Holds.Select(value => value.Id).Distinct().Count() != snapshot.Holds.Count
            || snapshot.DebtEntries.Select(value => value.Id).Distinct().Count() != snapshot.DebtEntries.Count
            || snapshot.Reversals.Select(value => value.Id).Distinct().Count() != snapshot.Reversals.Count
            || snapshot.Usage.Select(value => value.Id).Distinct().Count() != snapshot.Usage.Count)
            errors.Add("duplicate_balance_source");
        if (snapshot.Holds.Any(value => value.Id == Guid.Empty || value.AmountMicroUsd < 0 || value.ExpiresAt <= value.CreatedAt)
            || snapshot.DebtEntries.Any(value => value.Id == Guid.Empty || value.Type switch
                { "Incurred" => value.AmountMicroUsd <= 0, "Recovered" => value.AmountMicroUsd >= 0, _ => true }))
            errors.Add("invalid_balance_source");
        if (snapshot.Reversals.Any(value => value.AmountMicroUsd <= 0 || value.RecoveredMicroUsd < 0
            || value.DebtCreatedMicroUsd < 0 || checked(value.RecoveredMicroUsd + value.DebtCreatedMicroUsd) != value.AmountMicroUsd))
            errors.Add("invalid_reversal_breakdown");
        var reversals = snapshot.Reversals.ToLookup(value => value.Id);
        var reversedIntents = snapshot.Reversals.Select(value => value.IntentId).ToHashSet();
        var incurred = snapshot.DebtEntries.Where(value => value.Type == "Incurred").ToLookup(value => value.ReferenceId);
        var recovered = snapshot.DebtEntries.Where(value => value.Type == "Recovered").ToLookup(value => value.Id);
        if (snapshot.Reversals.Any(value => Sum(incurred[value.IntentId].Select(entry => entry.AmountMicroUsd)) != value.DebtCreatedMicroUsd)
            || snapshot.DebtEntries.Any(value => value.Type == "Incurred" && !reversedIntents.Contains(value.ReferenceId)))
            errors.Add("reversal_debt_source_mismatch");
        foreach (var link in snapshot.Links)
        {
            if (link.Kind == "payment_reversal" && !reversals[link.SourceId].Any(value => value.IntentId == link.ReferenceId
                && -value.RecoveredMicroUsd == link.AmountMicroUsd && value.AmountMicroUsd == link.ExternalReversalMicroUsd
                && value.DebtCreatedMicroUsd == link.ReversalDebtCreatedMicroUsd))
                errors.Add("reversal_source_mismatch");
            if (link.Kind == "debt_recovery" && !recovered[link.SourceId].Any(value => value.ReferenceId == link.ReferenceId
                && value.AmountMicroUsd == link.AmountMicroUsd)) errors.Add("debt_recovery_source_mismatch");
        }
        var usageBySettlement = snapshot.Usage.Where(value => value.SettlementId.HasValue).ToLookup(value => value.SettlementId!.Value);
        if (snapshot.Links.Where(value => value.Kind == "settlement").Any(link => link.FeePolicyVersionId is null
            || !usageBySettlement[link.SourceId].Any(value => value.State == "Verified" && value.PriceVersionId is not null
                && value.RequestId == link.RelatedSourceId)))
            errors.Add("missing_settlement_usage_terms");

        var links = snapshot.Links.ToLookup(value => value.LedgerId);
        var payments = snapshot.Payments.Items.ToLookup(value => value.IntentId);
        if (snapshot.Reversals.Any(value => !payments[value.IntentId].Any(payment => payment.CreditMicroUsd == value.AmountMicroUsd)))
            errors.Add("missing_or_mismatched_reversal_payment");
        foreach (var entry in snapshot.Ledger)
        {
            if (entry.Id == Guid.Empty || entry.ReferenceId == Guid.Empty || entry.AmountMicroUsd == 0
                || !Enum.IsDefined(entry.Type)) errors.Add("invalid_ledger_source");
            var positive = entry.Type is LedgerEntryType.TopUp or LedgerEntryType.Refund
                or LedgerEntryType.AdjustmentCredit or LedgerEntryType.PromotionalCredit;
            if ((entry.AmountMicroUsd > 0) != positive) errors.Add("invalid_posting_direction");
            if (entry.Type == LedgerEntryType.PromotionalCredit) continue; // Ledger itself is authoritative.
            if (entry.Type == LedgerEntryType.TopUp && entry.ReferenceType == "payment_intent")
            {
                var items = payments[entry.ReferenceId].ToArray();
                if (items.Length != 1 || items[0].CreditMicroUsd != entry.AmountMicroUsd)
                    errors.Add("missing_or_mismatched_payment_link");
                continue;
            }
            var kind = (entry.Type, entry.ReferenceType) switch
            {
                (LedgerEntryType.UsageCharge, "reservation") => "settlement",
                (LedgerEntryType.Refund, "settlement_refund") => "refund",
                (LedgerEntryType.AdjustmentCredit, "wallet_adjustment") => "approved_adjustment",
                (LedgerEntryType.AdjustmentDebit, "payment_reversal") => "payment_reversal",
                (LedgerEntryType.AdjustmentDebit, "debt_recovery") => "debt_recovery",
                _ => null
            };
            var matches = links[entry.Id].ToArray();
            if (kind is null || matches.Length != 1 || matches[0].Kind != kind
                || matches[0].SourceId == Guid.Empty || matches[0].AmountMicroUsd != entry.AmountMicroUsd)
                errors.Add("missing_or_mismatched_financial_link");
            else if (matches[0].ReferenceId != entry.ReferenceId) errors.Add("financial_reference_mismatch");
            if (matches.Length == 1 && kind == "payment_reversal"
                && (matches[0].ExternalReversalMicroUsd is not { } reversed
                    || matches[0].ReversalDebtCreatedMicroUsd is not { } debtCreated || debtCreated < 0
                    || reversed <= 0 || checked(-matches[0].AmountMicroUsd + debtCreated) != reversed))
                errors.Add("invalid_reversal_breakdown");
        }
        var ledgerIds = snapshot.Ledger.Select(value => value.Id).ToHashSet();
        if (snapshot.Links.Any(link => !ledgerIds.Contains(link.LedgerId)))
            errors.Add("source_without_posting");
        foreach (var payment in snapshot.Payments.Items)
        {
            if (!payment.FrozenTermsMatch || payment.QuoteId == Guid.Empty || payment.FxSnapshotId == Guid.Empty
                || payment.PrincipalTiyin <= 0 || payment.FeeTiyin < 0 || payment.FeeTiyin >= payment.PrincipalTiyin
                || payment.UzsTiyinPerUsd <= 0 || payment.CreditMicroUsd <= 0)
            {
                errors.Add("invalid_frozen_payment_terms");
                continue;
            }
            var credit = checked((long)decimal.Floor(checked((payment.PrincipalTiyin - (decimal)payment.FeeTiyin)
                * 1_000_000m) / payment.UzsTiyinPerUsd));
            if (credit != payment.CreditMicroUsd) errors.Add("payment_credit_fx_mismatch");
        }

        var opening = Sum(snapshot.Ledger.Where(value => value.OccurredAt < snapshot.Period.From).Select(value => value.AmountMicroUsd));
        var period = snapshot.Ledger.Where(value => snapshot.Period.Contains(value.OccurredAt)).ToArray();
        var movement = Sum(period.Select(value => value.AmountMicroUsd));
        var closing = checked(opening + movement);
        var after = Sum(snapshot.Ledger.Where(value => value.OccurredAt >= snapshot.Period.To).Select(value => value.AmountMicroUsd));
        var balance = snapshot.Balance;
        if (checked(closing + after) != balance.PostedMicroUsd) errors.Add("posted_balance_mismatch");
        if (balance.PostedMicroUsd < 0 || balance.HeldMicroUsd < 0 || balance.RecoveryDebtMicroUsd < 0
            || balance.AvailableMicroUsd < 0 || balance.HeldMicroUsd > balance.PostedMicroUsd
            || checked(balance.PostedMicroUsd - balance.HeldMicroUsd) != balance.AvailableMicroUsd)
            errors.Add("invalid_wallet_balance");
        if (balance.HeldMicroUsd != balance.ActiveReservationMicroUsd) errors.Add("hold_balance_mismatch");
        if (balance.RecoveryDebtMicroUsd != balance.DebtEntriesMicroUsd) errors.Add("debt_balance_mismatch");
        if (balance.ActiveReservationMicroUsd != Sum(snapshot.Holds.Select(value => value.AmountMicroUsd))
            || balance.DebtEntriesMicroUsd != Sum(snapshot.DebtEntries.Select(value => value.AmountMicroUsd)))
            errors.Add("balance_source_mismatch");
        if (snapshot.Pending.Evidence < 0 || snapshot.Pending.Settlement < 0 || snapshot.Pending.MerchantMismatch < 0
            || snapshot.Pending.UnmatchedPayment < 0 || snapshot.Pending.UnmatchedPayment != snapshot.Payments.UnmatchedPaidCount
            || snapshot.Pending.MerchantMismatch != snapshot.Payments.OpenMismatchCount)
            errors.Add("invalid_pending_counts");
        if (snapshot.Payments.UnmatchedPaidCount > 0) errors.Add("payment_without_posting");
        var gross = checked(-Sum(period.Where(value => value.Type == LedgerEntryType.UsageCharge).Select(value => value.AmountMicroUsd)));
        var refunds = Sum(period.Where(value => value.Type == LedgerEntryType.Refund).Select(value => value.AmountMicroUsd));
        // Sorted explicit membership and values, not MAX(UUID)/timestamps or mutable request analytics.
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = StatementReconciliation.CalculationVersion, snapshot.OrganizationId, snapshot.Period,
            snapshot.CapturedAt, Ledger = snapshot.Ledger.OrderBy(value => value.Id),
            Links = snapshot.Links.OrderBy(value => value.LedgerId).ThenBy(value => value.SourceId),
            Payments = snapshot.Payments.Items.OrderBy(value => value.IntentId), snapshot.Balance, snapshot.Pending,
            Holds = snapshot.Holds.OrderBy(value => value.Id), DebtEntries = snapshot.DebtEntries.OrderBy(value => value.Id),
            Reversals = snapshot.Reversals.OrderBy(value => value.Id), Usage = snapshot.Usage.OrderBy(value => value.Id)
        })));
        return new(opening, movement, closing, after, gross, refunds, checked(gross - refunds), digest,
            errors.Distinct().Order().ToArray(), snapshot.Pending);
    }

    private static long Sum(IEnumerable<long> amounts) => checked((long)amounts.Sum(value => (decimal)value));
}
