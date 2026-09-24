using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Application;

public sealed class FinancialService(
    IFinancialStore store, IUsageService usage, ITransactionCoordinator transactions,
    ILeasedJobStore jobs, IOutboxStore outbox, TimeProvider timeProvider) : IFinancialService
{
    private static readonly TimeSpan ReconciliationWindow = TimeSpan.FromHours(24);

    public async Task<AdmissionResult> ReserveAsync(ManagedAdmissionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Request);
        if (input.MaximumCharge.Value <= 0 || input.FeePolicyVersionId == Guid.Empty
            || input.ExpiresAt <= timeProvider.GetUtcNow())
            throw new ArgumentException("A positive bound, fee policy, and future expiry are required.", nameof(input));

        ClaimResult? claim;
        AdmissionStatus status = AdmissionStatus.Reserved;
        Reservation? reservation = null;
        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            claim = await usage.TryPrepareInTransactionAsync(input.Request, cancellationToken);
            if (claim is not null)
            {
                reservation = new Reservation(Guid.CreateVersion7(), claim.RequestId,
                    input.Request.OrganizationId, input.Request.ProjectId, input.Request.ApiKeyId,
                    input.FeePolicyVersionId, input.MaximumCharge, timeProvider.GetUtcNow(),
                    input.ExpiresAt, "Reserved");
                status = await store.TryReserveAsync(reservation, cancellationToken);
                if (status == AdmissionStatus.Reserved)
                {
                    await jobs.ScheduleAsync("billing.reconcile", JsonSerializer.Serialize(new { reservationId = reservation.Id }),
                        reservation.Id.ToString("N"), reservation.ExpiresAt, cancellationToken: cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
            }
        }

        if (claim is null)
        {
            var prior = await usage.ResolveDuplicateAsync(input.Request, cancellationToken);
            return new AdmissionResult(prior.Kind == ClaimResultKind.PayloadConflict
                ? AdmissionStatus.PayloadConflict : AdmissionStatus.Duplicate, prior.RequestId, null);
        }
        return status == AdmissionStatus.Reserved
            ? new AdmissionResult(status, claim.RequestId, reservation)
            : new AdmissionResult(status, null, null);
    }

    public Task<FinalizationResult> FinalizeAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        FinishAsync(reservationId, FinalizationMode.Normal, cancellationToken);

    public Task<FinalizationResult> ReleaseUndispatchedAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        FinishAsync(reservationId, FinalizationMode.UndispatchedOnly, cancellationToken);

    public Task<FinalizationResult> ReconcileAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        FinishAsync(reservationId, FinalizationMode.Reconciliation, cancellationToken);

    public async Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        UsdMicroAmount limit, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || projectId == Guid.Empty || apiKeyId == Guid.Empty)
            throw new ArgumentException("Valid organization, project, and optional key IDs are required.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var policy = await store.SetBudgetAsync(organizationId, projectId, apiKeyId,
            limit, timeProvider.GetUtcNow(), cancellationToken);
        if (policy is not null) await transaction.CommitAsync(cancellationToken);
        return policy;
    }

    public async Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || externalReferenceId == Guid.Empty || amount.Value <= 0)
            throw new ArgumentException("A positive confirmed reversal and valid identifiers are required.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var result = await store.ApplyConfirmedReversalAsync(organizationId, externalReferenceId,
            amount, timeProvider.GetUtcNow(), cancellationToken);
        if (!result.Duplicate)
        {
            await outbox.EnqueueAsync("billing.reversal.applied",
                JsonSerializer.Serialize(new { organizationId, externalReferenceId, debtMicroUsd = result.DebtCreated.Value }),
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }

    public Task<FinancialWalletState?> GetWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        store.FindWalletStateAsync(organizationId, cancellationToken);

    public Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        store.FindReservationIdAsync(requestId, cancellationToken);

    public async Task<bool> RecordLateExposureAsync(Guid evidenceId, CancellationToken cancellationToken = default)
    {
        if (evidenceId == Guid.Empty) throw new ArgumentException("An evidence ID is required.", nameof(evidenceId));
        var reservationId = await store.FindReservationIdByEvidenceAsync(evidenceId, cancellationToken);
        if (reservationId is null) return false;
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var context = await store.LockAndLoadAsync(reservationId.Value, cancellationToken);
        if (context?.ExistingSettlement is not { } settlement) return false;
        var priced = context.PricedEvidence.SingleOrDefault(value => value.EvidenceId == evidenceId);
        if (priced is null) return false;
        var cost = RequestCostCalculator.Calculate([priced],
            context.FeePolicy with { MarkupBasisPoints = 0, FixedFee = UsdMicroAmount.Zero },
            UsdMicroAmount.Zero).ProviderCost;
        var recorded = await store.TryRecordLateExposureAsync(settlement.Id, evidenceId, cost,
            timeProvider.GetUtcNow(), cancellationToken);
        if (recorded)
        {
            await outbox.EnqueueAsync("billing.late_exposure.recorded",
                JsonSerializer.Serialize(new { reservationId = reservationId.Value, evidenceId,
                    exposureMicroUsd = cost.Value }), cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return recorded;
    }

    private async Task<FinalizationResult> FinishAsync(Guid reservationId, FinalizationMode mode, CancellationToken cancellationToken)
    {
        if (reservationId == Guid.Empty) throw new ArgumentException("A reservation ID is required.", nameof(reservationId));
        var now = timeProvider.GetUtcNow();
        List<Guid>? missingAttemptIds = null;
        DateTimeOffset? nextReview = null;
        Guid requestId = Guid.Empty;
        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            var context = await store.LockAndLoadAsync(reservationId, cancellationToken);
            if (context is null) return new FinalizationResult(FinalizationStatus.NotFound, null);
            requestId = context.Reservation.RequestId;
            if (context.ExistingSettlement is { } existing)
                return new FinalizationResult(FinalizationStatus.AlreadyFinalized, existing);
            if (mode == FinalizationMode.Reconciliation && now < context.Reservation.ExpiresAt)
                return new FinalizationResult(FinalizationStatus.NotDue, null);

            var dispatched = context.Attempts.Where(attempt => attempt.Execution is not
                (ExecutionState.Prepared or ExecutionState.RejectedBeforeExecution)).ToArray();
            missingAttemptIds = dispatched.Where(attempt => !attempt.HasEvidence).Select(attempt => attempt.Id).ToList();
            if (missingAttemptIds.Count == 0)
            {
                var verifiedAttempts = context.Evidence.Where(evidence => evidence.State == EvidenceState.Verified)
                    .Select(evidence => evidence.AttemptId).ToHashSet();
                var unknown = context.Evidence.Where(evidence => evidence.State == EvidenceState.Unknown
                    && !verifiedAttempts.Contains(evidence.AttemptId)).ToArray();
                if (mode == FinalizationMode.UndispatchedOnly && dispatched.Length != 0)
                    return new FinalizationResult(FinalizationStatus.PendingEvidence, null);
                if (unknown.Length != 0)
                {
                    nextReview = new[] { context.Reservation.ExpiresAt.Add(ReconciliationWindow) }
                        .Concat(unknown.Select(evidence => evidence.ReconcileAfter ?? now.Add(ReconciliationWindow)))
                        .Max();
                    if (mode != FinalizationMode.Reconciliation || now < nextReview)
                        missingAttemptIds = [];
                }
                else if (dispatched.Length != 0 && context.PricedEvidence.Count == 0)
                    return new FinalizationResult(FinalizationStatus.PendingEvidence, null);

                if (nextReview is null || mode == FinalizationMode.Reconciliation && now >= nextReview)
                {
                    var cost = context.PricedEvidence.Count == 0
                        ? new ChargeBreakdown(UsdMicroAmount.Zero, UsdMicroAmount.Zero,
                            UsdMicroAmount.Zero, UsdMicroAmount.Zero, UsdMicroAmount.Zero)
                        : RequestCostCalculator.Calculate(context.PricedEvidence,
                            context.FeePolicy, context.Reservation.Amount);
                    var settled = await store.ApplyFinalizationAsync(context, cost,
                        unknown.Length != 0, now, cancellationToken);
                    await outbox.EnqueueAsync("billing.reservation.finalized",
                        JsonSerializer.Serialize(new { reservationId, settled.Id, settled.UnresolvedUsage,
                            exposureMicroUsd = settled.PlatformExposure.Value }), cancellationToken: cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new FinalizationResult(settled.Outcome == "Settled"
                        ? FinalizationStatus.Settled : FinalizationStatus.Released, settled);
                }
            }
        }

        if (mode == FinalizationMode.Reconciliation)
        {
            foreach (var attemptId in missingAttemptIds ?? [])
                await usage.RecordUnknownAsync(requestId, attemptId, cancellationToken);
            nextReview ??= now.Add(ReconciliationWindow);
            await jobs.ScheduleAsync("billing.reconcile.final", JsonSerializer.Serialize(new { reservationId }),
                $"{reservationId:N}:{nextReview.Value.UtcTicks}", nextReview.Value,
                cancellationToken: cancellationToken);
        }
        return new FinalizationResult(FinalizationStatus.PendingEvidence, null);
    }

    private enum FinalizationMode { Normal, UndispatchedOnly, Reconciliation }
}
