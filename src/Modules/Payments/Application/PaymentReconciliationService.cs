using System.Text.Json;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Application;

public sealed class PaymentReconciliationService(IPaymentReconciliationStore store,
    IPaymentStore payments, PaymentConfiguration configuration, IAuditTrail audit,
    IOperationalAlertPublisher alerts, IOperationalAlertDeliveryStore alertDelivery,
    ITransactionCoordinator transactions, TimeProvider clock) : IPaymentReconciliationService
{
    public async Task<ProviderObservationResult> RecordObservationAsync(Guid actorId,
        ProviderObservationInput input, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || !Enum.IsDefined(input.Provider) || !Enum.IsDefined(input.Status)
            || input.Amount.Value <= 0 || input.ProviderObservedAt.Offset != TimeSpan.Zero
            || input.ProviderObservedAt > clock.GetUtcNow().AddMinutes(5))
            throw new ArgumentException("Valid provider observation fields are required.");
        var source = Required(input.SourceReference, 120, nameof(input.SourceReference));
        var row = Required(input.RowReference, 120, nameof(input.RowReference));
        var external = Required(input.ExternalTransactionId, 120, nameof(input.ExternalTransactionId));
        var reason = Required(input.Reason, 500, nameof(input.Reason));
        if (reason.Length < 8) throw new ArgumentException("An 8–500 character reason is required.");
        var digest = Required(input.SourceSha256, 64, nameof(input.SourceSha256)).ToLowerInvariant();
        if (digest.Length != 64 || digest.Any(value => !Uri.IsHexDigit(value)))
            throw new ArgumentException("A SHA-256 source digest is required.");
        var scope = configuration.MerchantScope(input.Provider);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await store.LockExternalTransactionAsync(input.Provider, scope, external, cancellationToken);
        var prior = await store.FindBySourceAsync(input.Provider, scope, source, row, cancellationToken);
        if (prior is not null)
            return DuplicateOrConflict(prior, input, digest, external);

        var intent = await payments.FindByExternalAsync(input.Provider, scope, external, cancellationToken);
        var observation = new ProviderObservation(Guid.CreateVersion7(), intent?.Id, input.Provider,
            scope, source, digest, row, external, input.Status, input.Amount,
            input.ProviderObservedAt.AddTicks(-(input.ProviderObservedAt.Ticks % 10)),
            clock.GetUtcNow(), actorId);
        if (!await store.TryInsertObservationAsync(observation, cancellationToken))
        {
            prior = await store.FindBySourceAsync(input.Provider, scope, source, row, cancellationToken)
                ?? throw new InvalidOperationException("Observation source row raced without a visible winner.");
            return DuplicateOrConflict(prior, input, digest, external);
        }

        var reasons = new List<string>();
        if (await store.CountByExternalAsync(input.Provider, scope, external, cancellationToken) > 1)
            reasons.Add("DuplicateProviderTransaction");
        if (intent is null) reasons.Add("MissingLocalPayment");
        else
        {
            if (intent.Amount != input.Amount) reasons.Add("AmountMismatch");
            if (input.Status == ProviderObservationStatus.Paid)
            {
                if (intent.Status == PaymentStatus.Canceled) reasons.Add("PaidCanceledConflict");
                else if (intent.Status != PaymentStatus.Paid) reasons.Add("ProviderPaidLocallyUnpaid");
                else if (!await payments.HasTopUpCreditAsync(intent.Id, cancellationToken))
                    reasons.Add("PaidWithoutCredit");
            }
            else if (input.Status is ProviderObservationStatus.Canceled or ProviderObservationStatus.Reversed)
            {
                if (intent.Status != PaymentStatus.Canceled || intent.PaidAt is not null
                    && !await payments.HasReversalAsync(intent.Id, cancellationToken))
                    reasons.Add("ProviderReversalUnapplied");
            }
            else if (intent.Status == PaymentStatus.Paid)
                reasons.Add("ProviderPendingLocallyPaid");
        }

        foreach (var caseReason in reasons)
        {
            var caseId = await store.EnsureCaseAsync(intent?.Id, observation.Id,
                input.Provider, scope, external, caseReason, clock.GetUtcNow(), cancellationToken);
            if (caseId is { } id)
                await alerts.RaiseAsync(OperationalAlertKind.PaymentReconciliation,
                    id.ToString("N"), JsonSerializer.Serialize(new { caseId = id, reason = caseReason }),
                    cancellationToken);
        }
        await audit.RecordAsync(new AuditEventInput(null, actorId, "payment.observation.recorded",
            "payment_provider_observation", observation.Id, null,
            JsonSerializer.Serialize(new { provider = input.Provider.ToString(), source,
                row, digest, caseReasons = reasons, reason })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProviderObservationResult(observation.Id, false, reasons);
    }

    public Task<IReadOnlyList<PaymentReconciliationCase>> ListCasesAsync(int limit,
        CancellationToken cancellationToken = default) =>
        store.ListCasesAsync(limit is >= 1 and <= 100 ? limit
            : throw new ArgumentOutOfRangeException(nameof(limit)), cancellationToken);

    public Task<ProviderObservation?> FindObservationAsync(Guid id,
        CancellationToken cancellationToken = default) =>
        id == Guid.Empty ? throw new ArgumentException("Observation ID is required.")
            : store.FindObservationAsync(id, cancellationToken);

    public async Task<bool> ResolveCaseAsync(Guid actorId, Guid caseId, string reason,
        string resolutionReference, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || caseId == Guid.Empty)
            throw new ArgumentException("Actor and case IDs are required.");
        reason = Required(reason, 500, nameof(reason));
        resolutionReference = Required(resolutionReference, 200, nameof(resolutionReference));
        if (reason.Length < 8 || resolutionReference.Length < 8)
            throw new ArgumentException("Resolution reason and evidence reference must have at least eight characters.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        await store.LockCaseExternalTransactionAsync(caseId, cancellationToken);
        if (!await store.ResolveCaseAsync(caseId, actorId, resolutionReference,
            clock.GetUtcNow(), cancellationToken)) return false;
        await alertDelivery.ResolveAsync(OperationalAlertKind.PaymentReconciliation,
            caseId.ToString("N"), clock.GetUtcNow(), cancellationToken);
        await audit.RecordAsync(new AuditEventInput(null, actorId, "payment.reconciliation.resolved",
            "payment_reconciliation_case", caseId, null,
            JsonSerializer.Serialize(new { reason, resolutionReference })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static ProviderObservationResult DuplicateOrConflict(ProviderObservation prior,
        ProviderObservationInput input, string digest, string external)
    {
        if (prior.SourceSha256 != digest || prior.ExternalTransactionId != external
            || prior.Status != input.Status || prior.Amount != input.Amount
            || prior.ProviderObservedAt.UtcTicks / 10 != input.ProviderObservedAt.UtcTicks / 10)
            throw new InvalidOperationException("A source row cannot be rewritten with different evidence.");
        return new ProviderObservationResult(prior.Id, true, []);
    }

    private static string Required(string value, int max, string name) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"{name} is required and must be at most {max} characters.");
}
