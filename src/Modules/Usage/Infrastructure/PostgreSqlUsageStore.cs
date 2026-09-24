using Microsoft.EntityFrameworkCore;
using Npgsql;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public sealed class PostgreSqlUsageStore(FoundationDbContext dbContext) : IUsageStore
{
    public async Task InsertRequestAsync(UsageRequest request, CancellationToken cancellationToken = default)
    {
        dbContext.Set<UsageRequestEntity>().Add(new UsageRequestEntity
        {
            Id = request.Id,
            OrganizationId = request.OrganizationId,
            ProjectId = request.ProjectId,
            ApiKeyId = request.ApiKeyId,
            CanonicalModelId = request.CanonicalModelId,
            StartedAt = request.StartedAt,
            CompletedAt = request.CompletedAt,
            ExecutionState = request.Execution.ToString(),
            DeliveryState = request.Delivery.ToString(),
            FinancialState = request.Financial.ToString(),
            IsStream = request.IsStream,
            Operation = request.Operation,
            RouteStrategy = request.RouteStrategy,
            TraceId = request.TraceId,
            HttpStatus = request.HttpStatus
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(Guid organizationId, Guid apiKeyId, string operation,
        byte[] keyHash, byte[] payloadHash, Guid requestId, DateTimeOffset expiresAt,
        DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO usage.idempotency_claim
                (organization_id, api_key_id, operation, key_hash, payload_hash, request_id, expires_at)
            VALUES ({organizationId}, {apiKeyId}, {operation}, {keyHash}, {payloadHash}, {requestId}, {expiresAt})
            ON CONFLICT (organization_id, api_key_id, operation, key_hash)
            DO UPDATE SET payload_hash = EXCLUDED.payload_hash,
                          request_id = EXCLUDED.request_id,
                          expires_at = EXCLUDED.expires_at
            WHERE usage.idempotency_claim.expires_at <= {now};
            """, cancellationToken) == 1;

    public async Task<(Guid RequestId, byte[] PayloadHash)?> FindClaimAsync(Guid organizationId,
        Guid apiKeyId, string operation, byte[] keyHash, CancellationToken cancellationToken = default)
    {
        var claim = await dbContext.Set<UsageIdempotencyClaimEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.ApiKeyId == apiKeyId && value.Operation == operation
                && value.KeyHash == keyHash, cancellationToken);
        return claim is null ? null : (claim.RequestId, claim.PayloadHash);
    }

    public async Task<(Guid RequestId, byte[] PayloadHash)?> FindLiveClaimAsync(Guid organizationId,
        Guid apiKeyId, string operation, byte[] keyHash, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var claim = await dbContext.Set<UsageIdempotencyClaimEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && value.ApiKeyId == apiKeyId
                && value.Operation == operation && value.KeyHash == keyHash && value.ExpiresAt > now)
            .Select(value => new { value.RequestId, value.PayloadHash })
            .SingleOrDefaultAsync(cancellationToken);
        return claim is null ? null : (claim.RequestId, claim.PayloadHash);
    }

    public async Task<UsageRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<UsageRequestEntity>().AsNoTracking()
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken)) is { } request
            ? ToContract(request) : null;

    public async Task<UsageAttempt?> FindAttemptAsync(Guid attemptId, CancellationToken cancellationToken = default) =>
        (await dbContext.Set<UsageAttemptEntity>().AsNoTracking()
            .SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken)) is { } attempt
            ? ToContract(attempt) : null;

    public async Task<UsageAttempt> AddAttemptAsync(UsageAttempt attempt, CancellationToken cancellationToken = default)
    {
        // The caller holds a transaction. Lock the logical request while assigning
        // its next attempt number so competing gateway nodes cannot reuse it.
        var locked = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE usage.request SET execution_state = execution_state WHERE id = {attempt.RequestId};",
            cancellationToken);
        if (locked != 1) throw new KeyNotFoundException("The usage request does not exist.");
        var financial = await dbContext.Set<UsageRequestEntity>().AsNoTracking()
            .Where(value => value.Id == attempt.RequestId).Select(value => value.FinancialState)
            .SingleAsync(cancellationToken);
        if (financial is "Settled" or "Released")
            throw new InvalidOperationException("A finalized request cannot start another provider attempt.");
        var lastNumber = await dbContext.Set<UsageAttemptEntity>().AsNoTracking()
            .Where(value => value.RequestId == attempt.RequestId)
            .MaxAsync(value => (int?)value.Number, cancellationToken) ?? 0;
        var numbered = attempt with { Number = checked(lastNumber + 1) };
        dbContext.Set<UsageAttemptEntity>().Add(new UsageAttemptEntity
        {
            Id = numbered.Id,
            RequestId = numbered.RequestId,
            Number = numbered.Number,
            ProviderModelId = numbered.ProviderModelId,
            StartedAt = numbered.StartedAt,
            CompletedAt = numbered.CompletedAt,
            ExecutionState = numbered.Execution.ToString(),
            ProviderRequestId = numbered.ProviderRequestId,
            ErrorCategory = numbered.ErrorCategory
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return numbered;
    }

    public async Task<bool> TryTransitionAttemptAsync(Guid attemptId, ExecutionState expected,
        ExecutionState next, DateTimeOffset? completedAt, CancellationToken cancellationToken = default)
    {
        if (next == ExecutionState.Dispatched)
        {
            var requestIdForLock = await dbContext.Set<UsageAttemptEntity>().AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => (Guid?)attempt.RequestId)
                .SingleOrDefaultAsync(cancellationToken);
            if (requestIdForLock is null) return false;
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE usage.request SET execution_state = execution_state WHERE id = {requestIdForLock.Value};",
                cancellationToken);
            var financial = await dbContext.Set<UsageRequestEntity>().AsNoTracking()
                .Where(value => value.Id == requestIdForLock.Value).Select(value => value.FinancialState)
                .SingleAsync(cancellationToken);
            if (financial is "Settled" or "Released") return false;
        }
        var updated = await dbContext.Set<UsageAttemptEntity>()
            .Where(attempt => attempt.Id == attemptId && attempt.ExecutionState == expected.ToString())
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(attempt => attempt.ExecutionState, next.ToString())
                .SetProperty(attempt => attempt.CompletedAt, completedAt), cancellationToken);
        if (updated != 1) return false;
        if (next is ExecutionState.Dispatched or ExecutionState.OutcomeUnknown)
        {
            var requestId = await dbContext.Set<UsageAttemptEntity>().AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.RequestId)
                .SingleAsync(cancellationToken);
            await dbContext.Set<UsageRequestEntity>().Where(request => request.Id == requestId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.ExecutionState,
                    next.ToString()), cancellationToken);
        }
        return true;
    }

    public async Task<bool> TryAppendEvidenceAsync(UsageEvidence evidence,
        FinancialState nextFinancialState, CancellationToken cancellationToken = default)
    {
        // Finalization uses the same logical-request lock before reading evidence.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE usage.request SET financial_state = financial_state WHERE id = {evidence.RequestId};",
            cancellationToken);
        // Serialize evidence decisions for one provider attempt. Without this lock,
        // a late unknown outcome can regress an already verified request.
        var locked = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE usage.attempt SET execution_state = execution_state WHERE id = {evidence.AttemptId} AND request_id = {evidence.RequestId};",
            cancellationToken);
        if (locked != 1) throw new KeyNotFoundException("The usage attempt does not exist.");
        if (evidence.State == EvidenceState.Unknown && await dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
            .AnyAsync(value => value.AttemptId == evidence.AttemptId
                && value.State == EvidenceState.Verified.ToString(), cancellationToken))
            return false;

        dbContext.Set<UsageEvidenceEntity>().Add(new UsageEvidenceEntity
        {
            Id = evidence.Id,
            RequestId = evidence.RequestId,
            AttemptId = evidence.AttemptId,
            ProviderModelId = evidence.ProviderModelId,
            State = evidence.State.ToString(),
            Source = evidence.Source.ToString(),
            InputTokens = evidence.InputTokens,
            OutputTokens = evidence.OutputTokens,
            CachedInputTokens = evidence.CachedInputTokens,
            ReasoningTokens = evidence.ReasoningTokens,
            PriceVersionId = evidence.PriceVersionId,
            ProviderRequestId = evidence.ProviderRequestId,
            CapturedAt = evidence.CapturedAt,
            ReconcileAfter = evidence.ReconcileAfter
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }

        var hasUnresolvedUnknown = await dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
            .AnyAsync(unknown => unknown.RequestId == evidence.RequestId
                && unknown.State == EvidenceState.Unknown.ToString()
                && !dbContext.Set<UsageEvidenceEntity>().Any(verified => verified.AttemptId == unknown.AttemptId
                    && verified.State == EvidenceState.Verified.ToString()), cancellationToken);
        var financial = hasUnresolvedUnknown ? FinancialState.PendingEvidence : nextFinancialState;
        await dbContext.Set<UsageRequestEntity>().Where(request => request.Id == evidence.RequestId
                && request.FinancialState != FinancialState.Settled.ToString()
                && request.FinancialState != FinancialState.Released.ToString())
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.FinancialState,
                financial.ToString()), cancellationToken);
        return true;
    }

    public async Task<bool> TryFinishAttemptAsync(Guid attemptId, ExecutionState next,
        string? providerRequestId, string? errorCategory, DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<UsageAttemptEntity>()
            .Where(attempt => attempt.Id == attemptId
                && (attempt.ExecutionState == ExecutionState.Dispatched.ToString()
                    || attempt.ExecutionState == ExecutionState.OutcomeUnknown.ToString()))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(attempt => attempt.ExecutionState, next.ToString())
                .SetProperty(attempt => attempt.ProviderRequestId, providerRequestId)
                .SetProperty(attempt => attempt.ErrorCategory, errorCategory)
                .SetProperty(attempt => attempt.CompletedAt, completedAt), cancellationToken) == 1;

    public async Task<bool> TryFinishRequestAsync(Guid requestId, ExecutionState execution,
        DeliveryState delivery, int httpStatus, string routeStrategy, DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<UsageRequestEntity>()
            .Where(request => request.Id == requestId && request.CompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.ExecutionState, execution.ToString())
                .SetProperty(request => request.DeliveryState, delivery.ToString())
                .SetProperty(request => request.HttpStatus, httpStatus)
                .SetProperty(request => request.RouteStrategy, routeStrategy)
                .SetProperty(request => request.CompletedAt, completedAt), cancellationToken) == 1;

    public async Task<IReadOnlyList<UsageEvidence>> ListEvidenceAsync(Guid requestId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<UsageEvidenceEntity>().AsNoTracking()
            .Where(evidence => evidence.RequestId == requestId)
            .OrderBy(evidence => evidence.CapturedAt).ThenBy(evidence => evidence.Id)
            .Select(evidence => new UsageEvidence(evidence.Id, evidence.RequestId, evidence.AttemptId, evidence.ProviderModelId,
                Enum.Parse<EvidenceState>(evidence.State), Enum.Parse<EvidenceSource>(evidence.Source),
                evidence.InputTokens, evidence.OutputTokens, evidence.CachedInputTokens,
                evidence.ReasoningTokens, evidence.PriceVersionId, evidence.ProviderRequestId,
                evidence.CapturedAt, evidence.ReconcileAfter))
            .ToListAsync(cancellationToken);

    private static UsageRequest ToContract(UsageRequestEntity request) => new(
        request.Id, request.OrganizationId, request.ProjectId, request.ApiKeyId,
        request.CanonicalModelId, request.StartedAt, request.CompletedAt,
        Enum.Parse<ExecutionState>(request.ExecutionState),
        Enum.Parse<DeliveryState>(request.DeliveryState),
        Enum.Parse<FinancialState>(request.FinancialState), request.IsStream,
        request.Operation, request.RouteStrategy, request.TraceId, request.HttpStatus);

    private static UsageAttempt ToContract(UsageAttemptEntity attempt) => new(
        attempt.Id, attempt.RequestId, attempt.Number, attempt.ProviderModelId,
        attempt.StartedAt, attempt.CompletedAt, Enum.Parse<ExecutionState>(attempt.ExecutionState),
        attempt.ProviderRequestId, attempt.ErrorCategory);
}
