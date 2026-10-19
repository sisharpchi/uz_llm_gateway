using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

/// <summary>Bounded, metadata-free projection. Caller owns the snapshot and its deadline/lifecycle.</summary>
public sealed class PostgreSqlStatementSourceReader(FoundationDbContext db,
    IStatementPaymentEvidenceReader payments) : IStatementSourceReader
{
    public const int MaximumSources = 100_000;

    public async Task<StatementSnapshot?> ReadAsync(Guid organizationId, StatementPeriod period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (organizationId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(organizationId));
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead)
            throw new InvalidOperationException("Statement sources require the caller's Repeatable Read snapshot.");
        // Capture time is metadata, not a commit watermark. Every subsequent query uses this same transaction.
        var capturedAt = await db.Database.SqlQuery<DateTimeOffset>($"SELECT transaction_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        var wallet = await db.Set<BillingWalletEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId, cancellationToken);
        if (wallet is null) return null;
        var ledger = await BoundedAsync(db.Set<BillingLedgerEntryEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId).OrderBy(value => value.OccurredAt).ThenBy(value => value.Id)
            .Select(value => new StatementLedgerSource(value.Id, value.OrganizationId, Enum.Parse<LedgerEntryType>(value.Type),
                value.AmountMicroUsd, value.ReferenceType, value.ReferenceId, value.OccurredAt)), cancellationToken);

        var sources = new List<StatementSourceLink>();
        sources.AddRange(await BoundedAsync(
            from settlement in db.Set<BillingSettlementEntity>().AsNoTracking()
            join reservation in db.Set<BillingReservationEntity>().AsNoTracking() on settlement.ReservationId equals reservation.Id
            join request in db.Set<UsageRequestEntity>().AsNoTracking() on settlement.RequestId equals request.Id
            where settlement.OrganizationId == organizationId && reservation.OrganizationId == organizationId
                && request.OrganizationId == organizationId && settlement.ChargedMicroUsd > 0
            select new StatementSourceLink(Guid.Empty, organizationId, "settlement", settlement.Id, reservation.Id,
                -settlement.ChargedMicroUsd, request.Id, request.StartedAt,
                reservation.FeePolicyVersionId, reservation.ByokFeePolicyVersionId), cancellationToken));
        sources.AddRange(await BoundedAsync(
            from refund in db.Set<BillingSettlementRefundEntity>().AsNoTracking()
            join settlement in db.Set<BillingSettlementEntity>().AsNoTracking() on refund.SettlementId equals settlement.Id
            join request in db.Set<UsageRequestEntity>().AsNoTracking() on settlement.RequestId equals request.Id
            where refund.OrganizationId == organizationId && settlement.OrganizationId == organizationId
                && request.OrganizationId == organizationId
            select new StatementSourceLink(Guid.Empty, organizationId, "refund", refund.Id, refund.Id,
                refund.AmountMicroUsd, refund.SettlementId, request.StartedAt), cancellationToken));
        sources.AddRange(await BoundedAsync(
            from adjustment in db.Set<BillingWalletAdjustmentEntity>().AsNoTracking()
            join approval in db.Set<BillingWalletAdjustmentApprovalEntity>().AsNoTracking() on adjustment.Id equals approval.RequestId
            where adjustment.OrganizationId == organizationId && approval.OrganizationId == organizationId
            select new StatementSourceLink(Guid.Empty, organizationId, "approved_adjustment", adjustment.Id, adjustment.Id,
                adjustment.AmountMicroUsd, approval.Id), cancellationToken));
        var reversals = await BoundedAsync(db.Set<BillingReversalEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId)
            .Select(value => new StatementReversalSource(value.Id, organizationId, value.ExternalReferenceId,
                value.AmountMicroUsd, value.RecoveredMicroUsd, value.DebtCreatedMicroUsd, value.CreatedAt)), cancellationToken);
        sources.AddRange(reversals.Where(value => value.RecoveredMicroUsd > 0)
            .Select(value => new StatementSourceLink(Guid.Empty, organizationId, "payment_reversal", value.Id,
                value.IntentId, -value.RecoveredMicroUsd, null, null, null, null,
                value.AmountMicroUsd, value.DebtCreatedMicroUsd)));
        var debtEntries = await BoundedAsync(db.Set<BillingDebtEntryEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId)
            .Select(value => new StatementDebtSource(value.Id, organizationId, value.Type, value.ReferenceId,
                value.AmountMicroUsd, value.CreatedAt)), cancellationToken);
        sources.AddRange(debtEntries.Where(value => value.Type == "Recovered").Select(value =>
            new StatementSourceLink(Guid.Empty, organizationId, "debt_recovery", value.Id, value.ReferenceId, value.AmountMicroUsd)));
        if (sources.Count > MaximumSources) throw new InvalidOperationException("Statement source limit exceeded; no truncation is permitted.");
        var byReference = ledger.ToLookup(value => (value.ReferenceType, value.ReferenceId));
        var links = sources.SelectMany(source =>
        {
            var referenceType = source.Kind switch
            {
                "settlement" => "reservation", "refund" => "settlement_refund",
                "approved_adjustment" => "wallet_adjustment", _ => source.Kind
            };
            var entries = byReference[(referenceType, source.ReferenceId)].ToArray();
            // Missing mandatory posting is retained as an unmatched source, never silently discarded.
            return entries.Length == 0 ? [source] : entries.Select(entry => source with { LedgerId = entry.Id });
        }).ToArray();
        var paymentSources = await payments.ReadAsync(organizationId,
            ledger.Where(value => value.Type == LedgerEntryType.TopUp && value.ReferenceType == "payment_intent")
                .Select(value => value.ReferenceId).Distinct().ToArray(), cancellationToken);
        var activeHolds = await BoundedAsync(db.Set<BillingReservationEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.Status == "Reserved")
            .Select(value => new StatementHoldSource(value.Id, organizationId, value.RequestId,
                value.AmountMicroUsd, value.CreatedAt, value.ExpiresAt)), cancellationToken);
        var debt = await db.Set<BillingRecoveryDebtEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId).Select(value => (long?)value.OutstandingMicroUsd)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var unknown = await db.Set<UsageEvidenceEntity>().AsNoTracking()
            .CountAsync(value => value.Request.OrganizationId == organizationId
                && value.State == nameof(EvidenceState.Unknown), cancellationToken);
        var pending = await db.Set<UsageRequestEntity>().AsNoTracking()
            .CountAsync(value => value.OrganizationId == organizationId
                && value.FinancialState == nameof(FinancialState.PendingSettlement), cancellationToken);
        var usage = await BoundedAsync(
            from evidence in db.Set<UsageEvidenceEntity>().AsNoTracking()
            where evidence.Request.OrganizationId == organizationId
            join membership in db.Set<BillingSettlementEvidenceEntity>().AsNoTracking() on evidence.Id equals membership.EvidenceId into memberships
            from membership in memberships.DefaultIfEmpty()
            select new StatementUsageSource(evidence.Id, organizationId, evidence.RequestId, evidence.AttemptId,
                membership == null ? null : membership.SettlementId, evidence.State, evidence.PriceVersionId,
                evidence.InputTokens, evidence.OutputTokens, evidence.CachedInputTokens, evidence.ReasoningTokens,
                evidence.CapturedAt), cancellationToken);
        return new(organizationId, period, capturedAt, ledger, links, paymentSources,
            new(wallet.PostedBalanceMicroUsd, wallet.ReservedBalanceMicroUsd,
                checked(wallet.PostedBalanceMicroUsd - wallet.ReservedBalanceMicroUsd), debt,
                checked((long)activeHolds.Sum(value => (decimal)value.AmountMicroUsd)), checked((long)debtEntries.Sum(value => (decimal)value.AmountMicroUsd))),
            new(unknown, pending, paymentSources.OpenMismatchCount, paymentSources.UnmatchedPaidCount), activeHolds, debtEntries, reversals, usage);
    }

    private static async Task<List<T>> BoundedAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        var rows = await query.Take(MaximumSources + 1).ToListAsync(cancellationToken);
        if (rows.Count > MaximumSources) throw new InvalidOperationException("Statement source limit exceeded; no truncation is permitted.");
        return rows;
    }
}
