using System.Globalization;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Modules.Payments.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Payments.Application;

public sealed class PaymentService(
    IPaymentStore store, IOrganizationAuthorizationService authorization,
    IWalletLedgerStore ledger, IFinancialStore financial,
    ITransactionCoordinator transactions, ILeasedJobStore jobs,
    IOutboxStore outbox, IOperationalAlertPublisher alerts,
    PaymentConfiguration configuration, TimeProvider clock) : IPaymentService
{
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumFxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan ProviderPendingWindow = TimeSpan.FromHours(12);

    public async Task<PaymentQuote> CreateQuoteAsync(Guid accountId, Guid organizationId,
        PaymentProvider provider, UzsTiyinAmount amount, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(provider))
            throw new ArgumentOutOfRangeException(nameof(provider));
        await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        var now = clock.GetUtcNow();
        var fx = await store.FindLatestFxAsync(now, cancellationToken)
            ?? throw new InvalidOperationException("No operator-published FX snapshot is available.");
        if (fx.ObservedAt < now - MaximumFxAge)
            throw new InvalidOperationException("The operator-published FX snapshot is stale.");
        if (configuration.FeeBasisPoints is not int feeBasisPoints
            || configuration.FixedFeeTiyin is not long fixedFeeTiyin)
            throw new InvalidOperationException("Top-up fee policy is not configured.");
        var (fee, credit) = PaymentQuoteCalculator.Calculate(amount, feeBasisPoints,
            new UzsTiyinAmount(fixedFeeTiyin), fx.UzsTiyinPerUsd);
        var quote = new PaymentQuote(Guid.CreateVersion7(), organizationId, provider,
            amount, fee, fx.Id, fx.UzsTiyinPerUsd, credit, now, now + QuoteLifetime);
        await store.InsertQuoteAsync(quote, cancellationToken);
        return quote;
    }

    public async Task<PaymentCreateIntentResult> CreateIntentAsync(Guid accountId, Guid organizationId, Guid quoteId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (quoteId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > 128)
            throw new ArgumentException("A quote and idempotency key of at most 128 characters are required.");
        var quote = await store.FindQuoteAsync(quoteId, cancellationToken)
            ?? throw new KeyNotFoundException("Payment quote was not found.");
        if (quote.OrganizationId != organizationId)
            throw new KeyNotFoundException("Payment quote was not found.");
        await authorization.EnsureOwnerAsync(accountId, quote.OrganizationId, cancellationToken);
        var key = idempotencyKey.Trim();
        var existing = await store.FindByIdempotencyKeyAsync(quote.OrganizationId, key, cancellationToken);
        if (existing is not null)
        {
            if (existing.QuoteId != quote.Id)
                throw new InvalidOperationException("The idempotency key belongs to another quote.");
            return new PaymentCreateIntentResult(existing, true, BuildCheckoutUrl(existing));
        }
        if (clock.GetUtcNow() >= quote.ExpiresAt)
            throw new InvalidOperationException("The quote has expired.");
        var scope = configuration.MerchantScope(quote.Provider);
        if (quote.Provider == PaymentProvider.Payme && string.IsNullOrWhiteSpace(configuration.PaymeKey)
            || quote.Provider == PaymentProvider.Click && (string.IsNullOrWhiteSpace(configuration.ClickMerchantId)
                || string.IsNullOrWhiteSpace(configuration.ClickSecretKey)))
            throw new InvalidOperationException("The payment merchant is not fully configured.");
        var intent = new PaymentIntent(Guid.CreateVersion7(), quote.OrganizationId, quote.Provider,
            quote.Id, PaymentStatus.Pending, quote.Amount, quote.Fee, quote.FxSnapshotId,
            quote.UzsTiyinPerUsd, quote.Credit, key, scope, null, null, null,
            clock.GetUtcNow(), null, null, null, null);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (await store.TryInsertIntentAsync(intent, cancellationToken))
        {
            await jobs.ScheduleAsync("payment.reconcile", JsonSerializer.Serialize(new { intentId = intent.Id }),
                $"{intent.Id:N}:quote", quote.ExpiresAt, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PaymentCreateIntentResult(intent, false, BuildCheckoutUrl(intent));
        }
        var prior = await store.FindByIdempotencyKeyAsync(quote.OrganizationId, key, cancellationToken)
            ?? await store.FindByQuoteAsync(quote.Id, cancellationToken);
        if (prior is null || prior.QuoteId != quote.Id || prior.IdempotencyKey != key)
            throw new InvalidOperationException("The quote or idempotency key is already bound to another intent.");
        return new PaymentCreateIntentResult(prior, true, BuildCheckoutUrl(prior));
    }

    public async Task<PaymentIntent?> GetIntentAsync(Guid accountId, Guid organizationId,
        Guid intentId, CancellationToken cancellationToken = default)
    {
        await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        var intent = await store.FindIntentAsync(intentId, cancellationToken);
        return intent?.OrganizationId == organizationId ? intent : null;
    }

    public async Task<IReadOnlyList<PaymentIntent>> ListIntentsAsync(Guid accountId,
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await authorization.EnsureOwnerAsync(accountId, organizationId, cancellationToken);
        return await store.ListForOrganizationAsync(organizationId, 100, cancellationToken);
    }

    public async Task<PaymentCommandResult> CheckAsync(PaymentProvider provider, Guid intentId,
        UzsTiyinAmount amount, CancellationToken cancellationToken = default)
    {
        var intent = await store.FindIntentAsync(intentId, cancellationToken);
        if (intent is null) return new PaymentCommandResult(PaymentCommandStatus.NotFound, null);
        if (intent.Provider != provider) return new PaymentCommandResult(PaymentCommandStatus.WrongProvider, intent);
        if (intent.Amount != amount) return new PaymentCommandResult(PaymentCommandStatus.WrongAmount, intent);
        if (intent.Status == PaymentStatus.Pending && clock.GetUtcNow() >=
            (await store.FindQuoteAsync(intent.QuoteId, cancellationToken))!.ExpiresAt)
            return new PaymentCommandResult(PaymentCommandStatus.Expired, intent);
        return intent.Status is PaymentStatus.Pending or PaymentStatus.Created
            ? new PaymentCommandResult(PaymentCommandStatus.Accepted, intent)
            : new PaymentCommandResult(PaymentCommandStatus.InvalidState, intent);
    }

    public async Task<PaymentCommandResult> PrepareAsync(PaymentProvider provider, Guid intentId,
        string externalTransactionId, UzsTiyinAmount amount, long? providerCreatedTimeUnixMs,
        string externalRequestId, byte[] requestHash, CancellationToken cancellationToken = default)
    {
        ValidateCallback(provider, externalTransactionId, externalRequestId, requestHash);
        var now = clock.GetUtcNow();
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var intent = await store.LockIntentAsync(intentId, cancellationToken);
        if (intent is null) return new PaymentCommandResult(PaymentCommandStatus.NotFound, null);
        var status = intent.Provider != provider ? PaymentCommandStatus.WrongProvider
            : intent.Amount != amount ? PaymentCommandStatus.WrongAmount
            : intent.ExternalTransactionId is not null
                ? intent.ExternalTransactionId == externalTransactionId
                    ? PaymentCommandStatus.Duplicate : PaymentCommandStatus.Conflict
            : intent.Status != PaymentStatus.Pending ? PaymentCommandStatus.InvalidState
            : now >= (await store.FindQuoteAsync(intent.QuoteId, cancellationToken))!.ExpiresAt
                ? PaymentCommandStatus.Expired : PaymentCommandStatus.Accepted;
        if (status == PaymentCommandStatus.Accepted)
        {
            var prepareId = provider == PaymentProvider.Click
                ? await store.NextClickPrepareIdAsync(cancellationToken) : (int?)null;
            await store.BindAsync(intent.Id, externalTransactionId, prepareId,
                providerCreatedTimeUnixMs, now, cancellationToken);
            await jobs.ScheduleAsync("payment.reconcile", JsonSerializer.Serialize(new { intentId = intent.Id }),
                $"{intent.Id:N}:bound", now + ProviderPendingWindow,
                cancellationToken: cancellationToken);
            intent = await store.FindIntentAsync(intent.Id, cancellationToken)
                ?? throw new InvalidOperationException("Bound payment disappeared within its transaction.");
        }
        await store.AppendCallbackReceiptAsync(provider, intent.Id, externalRequestId, requestHash,
            status.ToString(), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentCommandResult(status, intent);
    }

    public async Task<PaymentCommandResult> CompleteAsync(PaymentProvider provider,
        string externalTransactionId, int? clickPrepareId, UzsTiyinAmount? amount, string externalRequestId,
        byte[] requestHash, CancellationToken cancellationToken = default)
    {
        ValidateCallback(provider, externalTransactionId, externalRequestId, requestHash);
        var scope = configuration.MerchantScope(provider);
        var prior = await store.FindByExternalAsync(provider, scope, externalTransactionId, cancellationToken);
        if (prior is null) return new PaymentCommandResult(PaymentCommandStatus.NotFound, null);
        var now = clock.GetUtcNow();
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var intent = (await store.LockIntentAsync(prior.Id, cancellationToken))!;
        var status = intent.ExternalTransactionId != externalTransactionId ? PaymentCommandStatus.Conflict
            : amount is not null && intent.Amount != amount.Value ? PaymentCommandStatus.WrongAmount
            : provider == PaymentProvider.Click && intent.ClickPrepareId != clickPrepareId
                ? PaymentCommandStatus.WrongPrepareId
            : intent.Status == PaymentStatus.Paid ? PaymentCommandStatus.Duplicate
            : intent.Status != PaymentStatus.Created ? PaymentCommandStatus.InvalidState
            : PaymentCommandStatus.Accepted;
        if (status == PaymentCommandStatus.Accepted)
        {
            if (await store.HasTopUpCreditAsync(intent.Id, cancellationToken))
                throw new InvalidOperationException("A top-up credit exists without a paid payment state.");
            var entry = new LedgerEntry(Guid.CreateVersion7(), intent.OrganizationId, LedgerEntryType.TopUp,
                new SignedUsdMicroAmount(intent.Credit.Value), "payment_intent", intent.Id,
                JsonSerializer.Serialize(new
                {
                    amountTiyin = intent.Amount.Value,
                    feeTiyin = intent.Fee.Value,
                    intent.FxSnapshotId
                }), now);
            if (await ledger.TryAppendAsync(entry, cancellationToken) != LedgerPostingStatus.Posted)
                throw new InvalidOperationException("Top-up wallet posting did not complete atomically.");
            await financial.RecoverAvailableDebtAsync(intent.OrganizationId, entry.Id, now, cancellationToken);
            await store.SetStatusAsync(intent.Id, PaymentStatus.Paid, now, null, cancellationToken);
            await outbox.EnqueueAsync("payment.intent.paid",
                JsonSerializer.Serialize(new { intentId = intent.Id, organizationId = intent.OrganizationId }),
                cancellationToken: cancellationToken);
            intent = await store.FindIntentAsync(intent.Id, cancellationToken)
                ?? throw new InvalidOperationException("Paid payment disappeared within its transaction.");
        }
        await store.AppendCallbackReceiptAsync(provider, intent.Id, externalRequestId, requestHash,
            status.ToString(), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentCommandResult(status, intent);
    }

    public async Task<PaymentCommandResult> CancelAsync(PaymentProvider provider,
        string externalTransactionId, int? clickPrepareId, UzsTiyinAmount? amount, int? reason,
        string externalRequestId, byte[] requestHash, CancellationToken cancellationToken = default)
    {
        ValidateCallback(provider, externalTransactionId, externalRequestId, requestHash);
        var scope = configuration.MerchantScope(provider);
        var prior = await store.FindByExternalAsync(provider, scope, externalTransactionId, cancellationToken);
        if (prior is null) return new PaymentCommandResult(PaymentCommandStatus.NotFound, null);
        var now = clock.GetUtcNow();
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var intent = (await store.LockIntentAsync(prior.Id, cancellationToken))!;
        var status = intent.ExternalTransactionId != externalTransactionId ? PaymentCommandStatus.Conflict
            : amount is not null && intent.Amount != amount.Value ? PaymentCommandStatus.WrongAmount
            : provider == PaymentProvider.Click && intent.ClickPrepareId != clickPrepareId
                ? PaymentCommandStatus.WrongPrepareId
            : intent.Status == PaymentStatus.Canceled ? PaymentCommandStatus.Duplicate
            : intent.Status is PaymentStatus.Created or PaymentStatus.Paid
                ? PaymentCommandStatus.Accepted : PaymentCommandStatus.InvalidState;
        if (status == PaymentCommandStatus.Accepted)
        {
            if (intent.Status == PaymentStatus.Paid)
            {
                if (!await store.HasTopUpCreditAsync(intent.Id, cancellationToken))
                    throw new InvalidOperationException("Paid payment has no top-up ledger entry.");
                var reversal = await financial.ApplyConfirmedReversalAsync(intent.OrganizationId,
                    intent.Id, intent.Credit, now, cancellationToken);
                if (reversal.Duplicate)
                    throw new InvalidOperationException("Reversal exists before the payment state was canceled.");
                await outbox.EnqueueAsync("billing.reversal.applied", JsonSerializer.Serialize(new
                {
                    organizationId = intent.OrganizationId, externalReferenceId = intent.Id,
                    debtMicroUsd = reversal.DebtCreated.Value
                }), cancellationToken: cancellationToken);
            }
            await store.SetStatusAsync(intent.Id, PaymentStatus.Canceled, now, reason, cancellationToken);
            await outbox.EnqueueAsync("payment.intent.canceled",
                JsonSerializer.Serialize(new { intentId = intent.Id, organizationId = intent.OrganizationId }),
                cancellationToken: cancellationToken);
            intent = await store.FindIntentAsync(intent.Id, cancellationToken)
                ?? throw new InvalidOperationException("Canceled payment disappeared within its transaction.");
        }
        await store.AppendCallbackReceiptAsync(provider, intent.Id, externalRequestId, requestHash,
            status.ToString(), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentCommandResult(status, intent);
    }

    public Task<PaymentIntent?> FindByExternalAsync(PaymentProvider provider,
        string externalTransactionId, CancellationToken cancellationToken = default) =>
        store.FindByExternalAsync(provider, configuration.MerchantScope(provider),
            externalTransactionId, cancellationToken);

    public Task<IReadOnlyList<PaymentStatementEntry>> GetPaymeStatementAsync(long fromUnixMs,
        long toUnixMs, CancellationToken cancellationToken = default)
    {
        if (fromUnixMs < 0 || toUnixMs < fromUnixMs || toUnixMs - fromUnixMs > 30L * 24 * 60 * 60 * 1000)
            throw new ArgumentException("Payme statement range must be at most 30 days.");
        return store.FindPaymeStatementAsync(configuration.MerchantScope(PaymentProvider.Payme),
            fromUnixMs, toUnixMs, cancellationToken);
    }

    public async Task<PaymentReconciliationResult> ReconcileAsync(Guid intentId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var intent = await store.LockIntentAsync(intentId, cancellationToken);
        if (intent is null) return new PaymentReconciliationResult(intentId, "NotFound", false);
        string? reason = null;
        if (intent.Status == PaymentStatus.Paid && !await store.HasTopUpCreditAsync(intent.Id, cancellationToken))
            reason = "PaidWithoutCredit";
        else if (intent.Status == PaymentStatus.Canceled && intent.PaidAt is not null
            && !await store.HasReversalAsync(intent.Id, cancellationToken))
            reason = "CanceledWithoutReversal";
        else if (intent.Status == PaymentStatus.Created && intent.BoundAt <= now - ProviderPendingWindow)
            reason = "ProviderStatusUnverified";
        else if (intent.Status == PaymentStatus.Pending &&
            (await store.FindQuoteAsync(intent.QuoteId, cancellationToken))!.ExpiresAt <= now)
        {
            await store.SetStatusAsync(intent.Id, PaymentStatus.Expired, now, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PaymentReconciliationResult(intentId, "Expired", false);
        }
        if (reason is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PaymentReconciliationResult(intentId, "Consistent", false);
        }
        var created = await store.EnsureReconciliationCaseAsync(intent.Id, reason, now, cancellationToken);
        if (created)
            await alerts.RaiseAsync(OperationalAlertKind.PaymentCallbackFailure,
                $"payment:{intent.Id:N}:{reason}",
                JsonSerializer.Serialize(new { intentId = intent.Id, reason }), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentReconciliationResult(intentId, reason, created);
    }

    private string? BuildCheckoutUrl(PaymentIntent intent)
    {
        if (intent.Provider == PaymentProvider.Payme && !string.IsNullOrWhiteSpace(configuration.PaymeMerchantId))
        {
            var data = $"m={configuration.PaymeMerchantId};ac.intent_id={intent.Id:D};a={intent.Amount.Value}";
            return "https://checkout.paycom.uz/" + Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        }
        if (intent.Provider == PaymentProvider.Click
            && !string.IsNullOrWhiteSpace(configuration.ClickMerchantId)
            && !string.IsNullOrWhiteSpace(configuration.ClickServiceId))
        {
            var amount = (intent.Amount.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            return "https://my.click.uz/services/pay?service_id="
                + Uri.EscapeDataString(configuration.ClickServiceId) + "&merchant_id="
                + Uri.EscapeDataString(configuration.ClickMerchantId) + "&amount="
                + Uri.EscapeDataString(amount) + "&transaction_param=" + intent.Id.ToString("D");
        }
        return null;
    }

    private static void ValidateCallback(PaymentProvider provider, string externalTransactionId,
        string externalRequestId, byte[] requestHash)
    {
        if (!Enum.IsDefined(provider) || string.IsNullOrWhiteSpace(externalTransactionId)
            || externalTransactionId.Length > 120 || string.IsNullOrWhiteSpace(externalRequestId)
            || externalRequestId.Length > 120 || requestHash.Length != 32)
            throw new ArgumentException("A bounded provider transaction and SHA-256 request hash are required.");
    }
}
