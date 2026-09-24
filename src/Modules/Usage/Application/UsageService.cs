using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Application;

public sealed class UsageService(
    IUsageStore store, ITransactionCoordinator transactions, IOutboxStore outbox,
    TimeProvider timeProvider) : IUsageService
{
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    public async Task<ClaimResult> PrepareAsync(PrepareUsageRequest input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateId(input.OrganizationId, nameof(input.OrganizationId));
        ValidateId(input.ProjectId, nameof(input.ProjectId));
        ValidateId(input.ApiKeyId, nameof(input.ApiKeyId));
        ValidateId(input.CanonicalModelId, nameof(input.CanonicalModelId));
        if (input.PayloadHash is not { Length: 32 }) throw new ArgumentException("A SHA-256 payload fingerprint is required.", nameof(input));
        var operation = ValidateText(input.Operation, 80, nameof(input.Operation));
        var traceId = input.TraceId is null ? null : ValidateText(input.TraceId, 128, nameof(input.TraceId));
        byte[]? keyHash = null;
        if (input.IdempotencyKey is not null)
        {
            if (!Guid.TryParse(input.IdempotencyKey, out var key) || key == Guid.Empty)
                throw new ArgumentException("Idempotency-Key must be a non-empty UUID.", nameof(input));
            keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString("D")));
        }

        var now = timeProvider.GetUtcNow();
        var request = new UsageRequest(Guid.CreateVersion7(), input.OrganizationId, input.ProjectId,
            input.ApiKeyId, input.CanonicalModelId, now, null, ExecutionState.Prepared,
            DeliveryState.NotStarted, FinancialState.PendingAdmission, input.IsStream,
            operation, null, traceId, null);

        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            await store.InsertRequestAsync(request, cancellationToken);
            if (keyHash is null || await store.TryClaimAsync(input.OrganizationId, input.ApiKeyId,
                operation, keyHash, input.PayloadHash, request.Id, now.Add(IdempotencyWindow), now,
                cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return new ClaimResult(ClaimResultKind.Created, request.Id);
            }
        }

        var original = await store.FindClaimAsync(input.OrganizationId, input.ApiKeyId,
            operation, keyHash!, cancellationToken)
            ?? throw new InvalidOperationException("An idempotency conflict was observed without a durable claim.");
        return new ClaimResult(CryptographicOperations.FixedTimeEquals(original.PayloadHash, input.PayloadHash)
            ? ClaimResultKind.Duplicate : ClaimResultKind.PayloadConflict, original.RequestId);
    }

    public async Task<UsageAttempt> StartAttemptAsync(Guid requestId, Guid providerModelId, CancellationToken cancellationToken = default)
    {
        ValidateId(requestId, nameof(requestId));
        ValidateId(providerModelId, nameof(providerModelId));
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var attempt = await store.AddAttemptAsync(new UsageAttempt(Guid.CreateVersion7(), requestId, 0,
            providerModelId, timeProvider.GetUtcNow(), null, ExecutionState.Prepared, null, null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return attempt;
    }

    public async Task<bool> MarkDispatchedAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        ValidateId(attemptId, nameof(attemptId));
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var changed = await store.TryTransitionAttemptAsync(attemptId, ExecutionState.Prepared,
            ExecutionState.Dispatched, null, cancellationToken);
        if (changed) await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<UsageEvidence?> RecordUnknownAsync(Guid requestId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        ValidateId(requestId, nameof(requestId));
        var attempt = await RequireAttemptAsync(requestId, attemptId, cancellationToken);
        if (attempt.Execution is not ExecutionState.Dispatched and not ExecutionState.OutcomeUnknown)
            throw new InvalidOperationException("Only a dispatched attempt can have unknown provider usage.");
        if ((await store.ListEvidenceAsync(requestId, cancellationToken)).Any(value =>
            value.AttemptId == attemptId && value.State == EvidenceState.Verified))
            throw new InvalidOperationException("Verified usage already exists for this attempt.");
        var now = timeProvider.GetUtcNow();
        var evidence = new UsageEvidence(Guid.CreateVersion7(), requestId, attemptId,
            attempt.ProviderModelId, EvidenceState.Unknown, EvidenceSource.Unknown, null, null, null, null, null,
            attempt.ProviderRequestId, now, now.AddHours(24));
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await store.TryAppendEvidenceAsync(evidence, FinancialState.PendingEvidence, cancellationToken))
            return null;
        if (attempt.Execution == ExecutionState.Dispatched)
            await store.TryTransitionAttemptAsync(attemptId, ExecutionState.Dispatched,
                ExecutionState.OutcomeUnknown, now, cancellationToken);
        await outbox.EnqueueAsync("usage.evidence.unknown", JsonSerializer.Serialize(new { requestId, attemptId, evidenceId = evidence.Id }), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return evidence;
    }

    public async Task<UsageEvidence?> RecordVerifiedAsync(VerifiedUsageInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateId(input.RequestId, nameof(input.RequestId));
        ValidateId(input.PriceVersionId, nameof(input.PriceVersionId));
        var attempt = await RequireAttemptAsync(input.RequestId, input.AttemptId, cancellationToken);
        if (attempt.Execution is not ExecutionState.Dispatched and not ExecutionState.OutcomeUnknown
            || input.Source is EvidenceSource.Unknown || !Enum.IsDefined(input.Source)
            || input.InputTokens < 0 || input.OutputTokens < 0
            || input.CachedInputTokens < 0 || input.CachedInputTokens > input.InputTokens
            || input.ReasoningTokens < 0 || input.ReasoningTokens > input.OutputTokens)
            throw new ArgumentException("Verified usage requires a dispatched attempt and nonnegative, non-overlapping token counts.", nameof(input));

        var evidence = new UsageEvidence(Guid.CreateVersion7(), input.RequestId, input.AttemptId,
            attempt.ProviderModelId, EvidenceState.Verified, input.Source, input.InputTokens, input.OutputTokens,
            input.CachedInputTokens, input.ReasoningTokens, input.PriceVersionId,
            input.ProviderRequestId, timeProvider.GetUtcNow(), null);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await store.TryAppendEvidenceAsync(evidence, FinancialState.PendingSettlement, cancellationToken))
            return null;
        await outbox.EnqueueAsync("usage.evidence.verified", JsonSerializer.Serialize(new { requestId = input.RequestId, attemptId = input.AttemptId, evidenceId = evidence.Id }), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return evidence;
    }

    public Task<UsageRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        store.FindRequestAsync(requestId, cancellationToken);

    public Task<IReadOnlyList<UsageEvidence>> ListEvidenceAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        store.ListEvidenceAsync(requestId, cancellationToken);

    private async Task<UsageAttempt> RequireAttemptAsync(Guid requestId, Guid attemptId, CancellationToken cancellationToken)
    {
        ValidateId(attemptId, nameof(attemptId));
        var attempt = await store.FindAttemptAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.RequestId != requestId)
            throw new KeyNotFoundException("The request attempt does not exist.");
        return attempt;
    }

    private static void ValidateId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("An identifier is required.", name);
    }

    private static string ValidateText(string? value, int maximumLength, string name)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maximumLength)
            throw new ArgumentException($"A value up to {maximumLength} characters is required.", name);
        return normalized;
    }
}
