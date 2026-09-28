using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace UZLLM.Persistence;

public sealed record OutboxMessage(
    Guid Id,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt,
    int AttemptCount,
    int MaxAttempts);

public sealed record LeasedJob(
    Guid Id,
    string JobType,
    string Payload,
    string DeduplicationKey,
    int AttemptCount,
    int MaxAttempts);

public sealed record DeadLetteredWork(Guid Id, string Kind, string WorkType,
    int AttemptCount, int MaxAttempts, DateTimeOffset DeadLetteredAt, string? LastError);

public interface IOperationalWorkMonitor
{
    Task<IReadOnlyList<DeadLetteredWork>> ListDeadLettersAsync(int limit = 50,
        CancellationToken cancellationToken = default);
}

public interface IOutboxStore
{
    Task<Guid> EnqueueAsync(
        string eventType,
        string payload,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> ClaimAvailableAsync(
        string workerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(Guid eventId, string workerId, int attemptCount,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(Guid eventId, string workerId, int attemptCount,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        Guid eventId,
        string workerId,
        int attemptCount,
        TimeSpan retryDelay,
        string failureKind,
        CancellationToken cancellationToken = default);
}

public interface IConsumerInboxStore
{
    Task<bool> HasProcessedAsync(string consumer, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a completed consumer event. Database consumers should write this in the same
    /// transaction as their durable side effect; external consumers also need an idempotency key.
    /// </summary>
    Task<bool> TryRecordProcessedAsync(
        string consumer,
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public interface ILeasedJobStore
{
    Task<Guid> ScheduleAsync(
        string jobType,
        string payload,
        string deduplicationKey,
        DateTimeOffset availableAt,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeasedJob>> ClaimAvailableAsync(
        string workerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(Guid jobId, string workerId, int attemptCount,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    Task<bool> MarkCompletedAsync(Guid jobId, string workerId, int attemptCount,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        Guid jobId,
        string workerId,
        int attemptCount,
        TimeSpan retryDelay,
        string failureKind,
        CancellationToken cancellationToken = default);
}

public interface IOutboxHandler
{
    string EventType { get; }

    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public interface ILeasedJobHandler
{
    string JobType { get; }

    Task HandleAsync(LeasedJob job, CancellationToken cancellationToken);
}

public enum OperationalAlertKind
{
    InvalidWalletCondition,
    SettlementFailure,
    PaymentCallbackFailure,
    ProviderOutage,
    ElevatedServerErrors,
    OutboxBacklog,
    FinancialExposure,
    RecoveryDebt,
    LateExternalSpend
}

public sealed record OperationalAlert(
    Guid Id,
    OperationalAlertKind Kind,
    string DeduplicationKey,
    string Details,
    DateTimeOffset OccurredAt);

public interface IOperationalAlertPublisher
{
    Task<OperationalAlert> RaiseAsync(
        OperationalAlertKind kind,
        string deduplicationKey,
        string details,
        CancellationToken cancellationToken = default);
}

public sealed record OperationalAlertDelivery(Guid Id, string Kind, string Severity,
    DateTimeOffset OccurredAt, Guid? NotificationEventId, DateTimeOffset? NotifiedAt,
    DateTimeOffset? ResolvedAt, string DeliveryStatus);

public interface IOperationalAlertDeliveryStore
{
    Task<OperationalAlertDelivery?> FindAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task<bool> MarkNotifiedAsync(Guid alertId, DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationalAlertDelivery>> ListAsync(int limit = 50,
        CancellationToken cancellationToken = default);
}

internal sealed class PostgreSqlOperationalAlertPublisher(FoundationDbContext dbContext, TimeProvider timeProvider) : IOperationalAlertPublisher
{
    public async Task<OperationalAlert> RaiseAsync(
        OperationalAlertKind kind,
        string deduplicationKey,
        string details,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        ValidateJson(details);

        var alert = new OperationalAlert(
            Guid.CreateVersion7(),
            kind,
            deduplicationKey,
            details,
            timeProvider.GetUtcNow());
        var notificationEventId = Guid.CreateVersion7();
        dbContext.OperationalAlerts.Add(new OperationalAlertEntity
        {
            Id = alert.Id,
            Kind = alert.Kind.ToString(),
            Severity = "critical",
            DeduplicationKey = alert.DeduplicationKey,
            Details = alert.Details,
            OccurredAt = alert.OccurredAt,
            NotificationEventId = notificationEventId
        });
        dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = notificationEventId,
            EventType = "ops.alert.raised",
            Payload = JsonSerializer.Serialize(new { alert.Id, alert.Kind, alert.DeduplicationKey }),
            OccurredAt = alert.OccurredAt,
            AvailableAt = alert.OccurredAt,
            MaxAttempts = 10
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return alert;
    }

    private static void ValidateJson(string details)
    {
        try
        {
            using var document = JsonDocument.Parse(details);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Alert details must be valid JSON.", nameof(details), exception);
        }
    }
}

internal sealed class PostgreSqlOperationalAlertDeliveryStore(FoundationDbContext dbContext) : IOperationalAlertDeliveryStore
{
    public async Task<OperationalAlertDelivery?> FindAsync(Guid alertId,
        CancellationToken cancellationToken = default)
    {
        var alert = await dbContext.OperationalAlerts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == alertId, cancellationToken);
        if (alert is null) return null;
        var deadLettered = alert.NotificationEventId is not null &&
            await dbContext.OutboxMessages.AsNoTracking().AnyAsync(value =>
                value.Id == alert.NotificationEventId && value.DeadLetteredAt != null, cancellationToken);
        return ToDelivery(alert, deadLettered);
    }

    public async Task<bool> MarkNotifiedAsync(Guid alertId, DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.OperationalAlerts
            .Where(value => value.Id == alertId && value.NotifiedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.NotifiedAt, notifiedAt),
                cancellationToken) == 1;

    public async Task<IReadOnlyList<OperationalAlertDelivery>> ListAsync(int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var alerts = await dbContext.OperationalAlerts.AsNoTracking()
            .OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.Id)
            .Take(limit).ToListAsync(cancellationToken);
        var eventIds = alerts.Where(value => value.NotificationEventId.HasValue)
            .Select(value => value.NotificationEventId!.Value).ToArray();
        var deadLettered = await dbContext.OutboxMessages.AsNoTracking()
            .Where(value => eventIds.Contains(value.Id) && value.DeadLetteredAt != null)
            .Select(value => value.Id).ToListAsync(cancellationToken);
        var deadLetterIds = deadLettered.ToHashSet();
        return alerts.Select(alert => ToDelivery(alert,
            alert.NotificationEventId is Guid id && deadLetterIds.Contains(id))).ToArray();
    }

    private static OperationalAlertDelivery ToDelivery(OperationalAlertEntity alert, bool deadLettered) =>
        new(alert.Id, alert.Kind, alert.Severity, alert.OccurredAt,
            alert.NotificationEventId, alert.NotifiedAt, alert.ResolvedAt,
            alert.NotifiedAt is not null ? "Delivered" : deadLettered ? "DeadLettered" :
            alert.NotificationEventId is null ? "Unlinked" : "Pending");
}

internal sealed class PostgreSqlOutboxStore(FoundationDbContext dbContext, TimeProvider timeProvider) : IOutboxStore
{
    public async Task<Guid> EnqueueAsync(
        string eventType,
        string payload,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateNewWork(eventType, payload, maxAttempts);

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        dbContext.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = id,
            EventType = eventType,
            Payload = payload,
            OccurredAt = now,
            AvailableAt = now,
            MaxAttempts = maxAttempts
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimAvailableAsync(
        string workerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ValidateClaim(workerId, maxCount, leaseDuration);
        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = now.Add(leaseDuration);
        await DeadLetterExpiredExhaustedAsync("outbox", "processed_at", now, cancellationToken);
        const string sql = """
            WITH candidates AS (
                SELECT id
                FROM ops.outbox
                WHERE processed_at IS NULL
                  AND dead_lettered_at IS NULL
                  AND attempt_count < max_attempts
                  AND available_at <= @now
                  AND (lease_expires_at IS NULL OR lease_expires_at <= @now)
                ORDER BY occurred_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT @max_count
            )
            UPDATE ops.outbox AS message
            SET lease_owner = @worker_id,
                lease_expires_at = @lease_expires_at,
                attempt_count = message.attempt_count + 1
            FROM candidates
            WHERE message.id = candidates.id
            RETURNING message.id, message.event_type, message.payload, message.occurred_at,
                      message.attempt_count, message.max_attempts;
            """;

        return await ExecuteQueryAsync(
            sql,
            [
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("max_count", maxCount),
                new NpgsqlParameter("worker_id", workerId),
                new NpgsqlParameter("lease_expires_at", leaseExpiresAt)
            ],
            reader => new OutboxMessage(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetInt32(4),
                reader.GetInt32(5)),
            cancellationToken);
    }

    public Task<bool> RenewLeaseAsync(Guid eventId, string workerId, int attemptCount,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
        RenewLeaseCoreAsync("outbox", "processed_at", eventId, workerId, attemptCount, leaseDuration, cancellationToken);

    public Task<bool> MarkProcessedAsync(Guid eventId, string workerId, int attemptCount,
        CancellationToken cancellationToken = default) =>
        ExecuteLeaseCompletionAsync("outbox", eventId, workerId, attemptCount, null, null, cancellationToken);

    public Task<bool> MarkFailedAsync(
        Guid eventId,
        string workerId,
        int attemptCount,
        TimeSpan retryDelay,
        string failureKind,
        CancellationToken cancellationToken = default) =>
        ExecuteLeaseCompletionAsync("outbox", eventId, workerId, attemptCount, retryDelay, failureKind, cancellationToken);

    private async Task<bool> ExecuteLeaseCompletionAsync(
        string table,
        Guid id,
        string workerId,
        int attemptCount,
        TimeSpan? retryDelay,
        string? failureKind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = timeProvider.GetUtcNow();
        var sql = retryDelay is null
            ? $"UPDATE ops.{table} SET processed_at = @now, lease_owner = NULL, lease_expires_at = NULL WHERE id = @id AND lease_owner = @worker_id AND attempt_count = @attempt_count AND lease_expires_at > @now;"
            : $"UPDATE ops.{table} SET lease_owner = NULL, lease_expires_at = NULL, available_at = @next_attempt_at, dead_lettered_at = CASE WHEN attempt_count >= max_attempts THEN @now ELSE NULL END, last_error = @last_error WHERE id = @id AND lease_owner = @worker_id AND attempt_count = @attempt_count AND lease_expires_at > @now;";

        var parameters = new List<NpgsqlParameter>
        {
            new("id", id),
            new("worker_id", workerId),
            new("attempt_count", attemptCount),
            new("now", now)
        };
        if (retryDelay is not null)
        {
            parameters.Add(new NpgsqlParameter("next_attempt_at", now.Add(retryDelay.Value)));
            parameters.Add(new NpgsqlParameter("last_error", SanitizeFailureKind(failureKind)));
        }

        return await ExecuteNonQueryAsync(sql, parameters, cancellationToken) == 1;
    }

    internal async Task<bool> RenewLeaseCoreAsync(string table, string terminalColumn, Guid id,
        string workerId, int attemptCount, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ValidateClaim(workerId, 1, leaseDuration);
        var now = timeProvider.GetUtcNow();
        return await ExecuteNonQueryAsync($"""
            UPDATE ops.{table} SET lease_expires_at = @expires_at
            WHERE id = @id AND {terminalColumn} IS NULL AND dead_lettered_at IS NULL
              AND lease_owner = @worker_id AND attempt_count = @attempt_count
              AND lease_expires_at > @now;
            """, [new("id", id), new("worker_id", workerId), new("attempt_count", attemptCount),
                new("now", now), new("expires_at", now.Add(leaseDuration))], cancellationToken) == 1;
    }

    internal Task DeadLetterExpiredExhaustedAsync(string table, string terminalColumn,
        DateTimeOffset now, CancellationToken cancellationToken) => ExecuteNonQueryAsync($"""
            UPDATE ops.{table}
            SET dead_lettered_at = @now, lease_owner = NULL, lease_expires_at = NULL,
                last_error = 'LeaseExpired'
            WHERE {terminalColumn} IS NULL AND dead_lettered_at IS NULL
              AND attempt_count >= max_attempts
              AND (lease_expires_at IS NULL OR lease_expires_at <= @now);
            """, [new("now", now)], cancellationToken);

    private async Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            var values = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(map((NpgsqlDataReader)reader));
            }

            return values;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> ExecuteNonQueryAsync(
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    internal static void ValidateNewWork(string workType, string payload, int maxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
    }

    internal static void ValidateClaim(string workerId, int maxCount, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "A lease duration must be positive.");
        }
    }

    internal static string SanitizeFailureKind(string? failureKind)
    {
        if (string.IsNullOrWhiteSpace(failureKind)
            || failureKind.Length > 200
            || failureKind.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            return "UnknownFailure";
        }

        return failureKind;
    }
}

internal sealed class PostgreSqlConsumerInboxStore(FoundationDbContext dbContext, TimeProvider timeProvider) : IConsumerInboxStore
{
    public Task<bool> HasProcessedAsync(string consumer, Guid eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        return dbContext.ConsumerInboxEntries.AsNoTracking()
            .AnyAsync(value => value.Consumer == consumer && value.EventId == eventId, cancellationToken);
    }

    public async Task<bool> TryRecordProcessedAsync(string consumer, Guid eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        dbContext.ConsumerInboxEntries.Add(new ConsumerInboxEntryEntity
        {
            Id = Guid.CreateVersion7(),
            Consumer = consumer,
            EventId = eventId,
            ProcessedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}

internal sealed class PostgreSqlLeasedJobStore(FoundationDbContext dbContext, TimeProvider timeProvider) : ILeasedJobStore
{
    public async Task<Guid> ScheduleAsync(
        string jobType,
        string payload,
        string deduplicationKey,
        DateTimeOffset availableAt,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default)
    {
        PostgreSqlOutboxStore.ValidateNewWork(jobType, payload, maxAttempts);
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection) await connection.OpenAsync(cancellationToken);
        try
        {
            var id = Guid.CreateVersion7();
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
                insert.CommandText = """
                    INSERT INTO ops.job
                        (id, job_type, payload, deduplication_key, available_at, attempt_count, max_attempts)
                    VALUES (@id, @job_type, CAST(@payload AS jsonb), @deduplication_key, @available_at, 0, @max_attempts)
                    ON CONFLICT (job_type, deduplication_key) DO NOTHING
                    RETURNING id;
                    """;
                insert.Parameters.Add(new NpgsqlParameter("id", id));
                insert.Parameters.Add(new NpgsqlParameter("job_type", jobType));
                insert.Parameters.Add(new NpgsqlParameter("payload", payload));
                insert.Parameters.Add(new NpgsqlParameter("deduplication_key", deduplicationKey));
                insert.Parameters.Add(new NpgsqlParameter("available_at", availableAt));
                insert.Parameters.Add(new NpgsqlParameter("max_attempts", maxAttempts));
                if (await insert.ExecuteScalarAsync(cancellationToken) is Guid inserted) return inserted;
            }
            await using var lookup = connection.CreateCommand();
            lookup.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            lookup.CommandText = "SELECT id FROM ops.job WHERE job_type = @job_type AND deduplication_key = @deduplication_key;";
            lookup.Parameters.Add(new NpgsqlParameter("job_type", jobType));
            lookup.Parameters.Add(new NpgsqlParameter("deduplication_key", deduplicationKey));
            return await lookup.ExecuteScalarAsync(cancellationToken) is Guid existing
                ? existing : throw new InvalidOperationException("A deduplicated job was not found.");
        }
        finally
        {
            if (shouldCloseConnection) await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyList<LeasedJob>> ClaimAvailableAsync(
        string workerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        PostgreSqlOutboxStore.ValidateClaim(workerId, maxCount, leaseDuration);
        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = now.Add(leaseDuration);
        await dbContext.Database.ExecuteSqlRawAsync("""
            UPDATE ops.job SET dead_lettered_at = {0}, lease_owner = NULL, lease_expires_at = NULL,
                last_error = 'LeaseExpired'
            WHERE completed_at IS NULL AND dead_lettered_at IS NULL AND attempt_count >= max_attempts
              AND (lease_expires_at IS NULL OR lease_expires_at <= {0});
            """, [now], cancellationToken);
        const string sql = """
            WITH candidates AS (
                SELECT id
                FROM ops.job
                WHERE completed_at IS NULL
                  AND dead_lettered_at IS NULL
                  AND attempt_count < max_attempts
                  AND available_at <= @now
                  AND (lease_expires_at IS NULL OR lease_expires_at <= @now)
                ORDER BY available_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT @max_count
            )
            UPDATE ops.job AS job
            SET lease_owner = @worker_id,
                lease_expires_at = @lease_expires_at,
                attempt_count = job.attempt_count + 1
            FROM candidates
            WHERE job.id = candidates.id
            RETURNING job.id, job.job_type, job.payload, job.deduplication_key,
                      job.attempt_count, job.max_attempts;
            """;

        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in new[]
            {
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("max_count", maxCount),
                new NpgsqlParameter("worker_id", workerId),
                new NpgsqlParameter("lease_expires_at", leaseExpiresAt)
            })
            {
                command.Parameters.Add(parameter);
            }
            var jobs = new List<LeasedJob>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add(new LeasedJob(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }

            return jobs;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<bool> RenewLeaseAsync(Guid jobId, string workerId, int attemptCount,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        PostgreSqlOutboxStore.ValidateClaim(workerId, 1, leaseDuration);
        var now = timeProvider.GetUtcNow();
        return await dbContext.Database.ExecuteSqlRawAsync("""
            UPDATE ops.job SET lease_expires_at = {0}
            WHERE id = {1} AND completed_at IS NULL AND dead_lettered_at IS NULL
              AND lease_owner = {2} AND attempt_count = {3} AND lease_expires_at > {4};
            """, [now.Add(leaseDuration), jobId, workerId, attemptCount, now], cancellationToken) == 1;
    }

    public Task<bool> MarkCompletedAsync(Guid jobId, string workerId, int attemptCount,
        CancellationToken cancellationToken = default) =>
        CompleteLeaseAsync(jobId, workerId, attemptCount, null, null, cancellationToken);

    public Task<bool> MarkFailedAsync(
        Guid jobId,
        string workerId,
        int attemptCount,
        TimeSpan retryDelay,
        string failureKind,
        CancellationToken cancellationToken = default) =>
        CompleteLeaseAsync(jobId, workerId, attemptCount, retryDelay, failureKind, cancellationToken);

    private async Task<bool> CompleteLeaseAsync(
        Guid jobId,
        string workerId,
        int attemptCount,
        TimeSpan? retryDelay,
        string? failureKind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = timeProvider.GetUtcNow();
        var sql = retryDelay is null
            ? "UPDATE ops.job SET completed_at = @now, lease_owner = NULL, lease_expires_at = NULL WHERE id = @id AND lease_owner = @worker_id AND attempt_count = @attempt_count AND lease_expires_at > @now;"
            : "UPDATE ops.job SET lease_owner = NULL, lease_expires_at = NULL, available_at = @next_attempt_at, dead_lettered_at = CASE WHEN attempt_count >= max_attempts THEN @now ELSE NULL END, last_error = @last_error WHERE id = @id AND lease_owner = @worker_id AND attempt_count = @attempt_count AND lease_expires_at > @now;";
        var parameters = new List<NpgsqlParameter>
        {
            new("id", jobId),
            new("worker_id", workerId),
            new("attempt_count", attemptCount),
            new("now", now)
        };
        if (retryDelay is not null)
        {
            parameters.Add(new NpgsqlParameter("next_attempt_at", now.Add(retryDelay.Value)));
            parameters.Add(new NpgsqlParameter("last_error", PostgreSqlOutboxStore.SanitizeFailureKind(failureKind)));
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}

internal sealed class PostgreSqlOperationalWorkMonitor(FoundationDbContext dbContext) : IOperationalWorkMonitor
{
    public async Task<IReadOnlyList<DeadLetteredWork>> ListDeadLettersAsync(int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var outbox = await dbContext.OutboxMessages.AsNoTracking()
            .Where(message => message.DeadLetteredAt != null)
            .OrderByDescending(message => message.DeadLetteredAt)
            .Take(limit)
            .Select(message => new DeadLetteredWork(message.Id, "Outbox", message.EventType,
                message.AttemptCount, message.MaxAttempts, message.DeadLetteredAt!.Value, message.LastError))
            .ToListAsync(cancellationToken);
        var jobs = await dbContext.LeasedJobs.AsNoTracking()
            .Where(job => job.DeadLetteredAt != null)
            .OrderByDescending(job => job.DeadLetteredAt)
            .Take(limit)
            .Select(job => new DeadLetteredWork(job.Id, "Job", job.JobType,
                job.AttemptCount, job.MaxAttempts, job.DeadLetteredAt!.Value, job.LastError))
            .ToListAsync(cancellationToken);
        return outbox.Concat(jobs).OrderByDescending(item => item.DeadLetteredAt)
            .ThenBy(item => item.Id).Take(limit).ToArray();
    }
}
