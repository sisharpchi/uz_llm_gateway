using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Statements;

/// <summary>Immutable versions, newest posting month/revision first; opaque scope-bound continuation.</summary>
public sealed record StatementPage(IReadOnlyList<StatementMetadataResponse> Items, string? NextCursor);
/// <summary>Stable ascending ordinals within one selected immutable version.</summary>
public sealed record StatementLinePage(Guid VersionId, IReadOnlyList<StatementLineResponse> Items, string? NextCursor);
/// <summary>Request one UTC calendar-month snapshot; not a financial correction.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateStatementRequest([property: JsonRequired] DateTimeOffset PeriodStart,
    [property: JsonRequired] Guid IdempotencyKey, [property: JsonRequired] long ExpectedGeneration,
    [property: JsonRequired] string Reason, int SchemaVersion = 1);
/// <summary>Explicit revision of a selected version, with current expected generation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviseStatementRequest([property: JsonRequired] Guid IdempotencyKey,
    [property: JsonRequired] long ExpectedGeneration, [property: JsonRequired] string Reason, int SchemaVersion = 1);
/// <summary>Operation identity is stable across command replay; Worker transport outcome is independent.</summary>
public sealed record StatementJobResponse(Guid Id, Guid OrganizationId, string OperationStatus, long ExpectedGeneration,
    Guid? VersionId, string? FailureKind, string JobState, JobProgress? Job);

public interface IStatementApiService
{
    Task EnsureCustomerAsync(string session, Guid organizationId, CancellationToken ct);
    Task EnsureOperatorAsync(string session, CancellationToken ct);
    Task<StatementPage> ListAsync(string session, Guid organizationId, DateTimeOffset? from, DateTimeOffset? to,
        int? limit, string? cursor, CancellationToken ct);
    Task<StatementMetadataResponse?> FindAsync(string session, Guid organizationId, Guid versionId, CancellationToken ct);
    Task<StatementLinePage?> LinesAsync(string session, Guid organizationId, Guid versionId, int? limit, string? cursor, CancellationToken ct);
    Task<StatementCsv?> ExportAsync(string session, Guid organizationId, Guid versionId, string? format, CancellationToken ct);
    Task<StatementJobResponse> CreateAsync(string session, Guid organizationId, CreateStatementRequest request, CancellationToken ct);
    Task<StatementJobResponse?> ReviseAsync(string session, Guid organizationId, Guid predecessorId, ReviseStatementRequest request, CancellationToken ct);
    Task<StatementJobResponse?> JobAsync(string session, Guid organizationId, Guid operationId, CancellationToken ct);
}

/// <summary>Authorization is revalidated here as well as HTTP; internal reporting stores are not auth facades.</summary>
public sealed class StatementApiService(IIdentityService identity, IOrganizationAuthorizationService authorization,
    IStatementReadStore reads, IStatementPublicationStore publication, IOperationalWorkMonitor work,
    IAuditTrail audit, IDataProtectionProvider protection, TimeProvider clock) : IStatementApiService
{
    public async Task EnsureCustomerAsync(string session, Guid organizationId, CancellationToken ct)
    {
        var actor = await ActorAsync(session, ct);
        await authorization.EnsurePermissionAsync(actor.AccountId, organizationId, OrganizationPermission.ReadBilling, cancellationToken: ct);
    }

    public async Task EnsureOperatorAsync(string session, CancellationToken ct)
    {
        var actor = await ActorAsync(session, ct);
        if (!actor.IsOperator || !await identity.HasRecentOperatorReauthenticationAsync(session, ct))
            throw new UnauthorizedAccessException();
    }

    public async Task<StatementPage> ListAsync(string session, Guid organizationId, DateTimeOffset? from, DateTimeOffset? to,
        int? limit, string? cursor, CancellationToken ct)
    {
        await EnsureCustomerAsync(session, organizationId, ct);
        var count = Limit(limit, 20);
        var start = from is { } first ? Month(first).From : DateTimeOffset.MinValue;
        var end = to is { } last ? Month(last).From : DateTimeOffset.MaxValue;
        if (start >= end) throw new ArgumentException("Invalid UTC month range.");
        var purpose = $"list:{organizationId:N}:{start:O}:{end:O}:{count}";
        var position = Decode<StatementPosition>(cursor, purpose);
        var values = await reads.ListAsync(organizationId, start, end, position?.Period ?? DateTimeOffset.MaxValue,
            position?.Revision ?? long.MaxValue, count + 1, ct);
        var items = values.Take(count).ToArray();
        return new(items, values.Count > count ? Encode(new StatementPosition(items[^1].Period.From, items[^1].Revision), purpose) : null);
    }

    public async Task<StatementMetadataResponse?> FindAsync(string session, Guid organizationId, Guid versionId, CancellationToken ct)
    {
        await EnsureCustomerAsync(session, organizationId, ct);
        return await reads.FindAsync(organizationId, versionId, ct);
    }

    public async Task<StatementLinePage?> LinesAsync(string session, Guid organizationId, Guid versionId, int? limit,
        string? cursor, CancellationToken ct)
    {
        await EnsureCustomerAsync(session, organizationId, ct);
        var count = Limit(limit, 50);
        var purpose = $"lines:{organizationId:N}:{versionId:N}:{count}";
        var position = Decode<LinePosition>(cursor, purpose);
        if (await reads.FindAsync(organizationId, versionId, ct) is null) return null;
        var values = await reads.LinesAsync(organizationId, versionId, position?.Ordinal ?? 0, count + 1, ct);
        var items = values.Take(count).ToArray();
        return new(versionId, items, values.Count > count ? Encode(new LinePosition(items[^1].Ordinal), purpose) : null);
    }

    public async Task<StatementCsv?> ExportAsync(string session, Guid organizationId, Guid versionId, string? format, CancellationToken ct)
    {
        await EnsureCustomerAsync(session, organizationId, ct);
        if (format is not null and not "csv") throw new ArgumentException("Only csv format is supported.");
        var value = await reads.CsvAsync(organizationId, versionId, ct);
        if (value is null) return null;
        if (value.Content.Length > 32 * 1024 * 1024) throw new StatementBuildRejectedException("Bounds");
        if (Convert.ToHexStringLower(SHA256.HashData(value.Content)) != value.Sha256)
            throw new StatementArtifactIntegrityException();
        var actor = await ActorAsync(session, ct);
        // Access/request evidence only, never a claim of complete network delivery.
        try
        {
            await audit.RecordAsync(new(organizationId, actor.AccountId, "statement.export.requested", "statement_version",
                versionId, null, JsonSerializer.Serialize(new { format = "csv", sha256 = value.Sha256 })), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new StatementReadDependencyException(exception); }
        ct.ThrowIfCancellationRequested();
        return value;
    }

    public async Task<StatementJobResponse> CreateAsync(string session, Guid organizationId, CreateStatementRequest request, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        if (request.ExpectedGeneration != 0) throw new ArgumentException("Initial build requires generation zero; use an explicit revision.");
        if (!await reads.OrganizationExistsAsync(organizationId, ct)) throw new KeyNotFoundException();
        var actor = await ActorAsync(session, ct);
        var operation = await publication.RequestAsync(new(organizationId, actor.AccountId, Month(request.PeriodStart),
            request.IdempotencyKey, request.ExpectedGeneration, null, request.Reason, request.SchemaVersion), ct);
        return await JobValueAsync(operation, ct);
    }

    public async Task<StatementJobResponse?> ReviseAsync(string session, Guid organizationId, Guid predecessorId,
        ReviseStatementRequest request, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        if (request.ExpectedGeneration < 1) throw new ArgumentException("A revision requires a positive expected generation.");
        var previous = await reads.FindAsync(organizationId, predecessorId, ct);
        if (previous is null) return null;
        var actor = await ActorAsync(session, ct);
        var operation = await publication.RequestAsync(new(organizationId, actor.AccountId, Month(previous.Period.From),
            request.IdempotencyKey, request.ExpectedGeneration, predecessorId, request.Reason, request.SchemaVersion), ct);
        return await JobValueAsync(operation, ct);
    }

    public async Task<StatementJobResponse?> JobAsync(string session, Guid organizationId, Guid operationId, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        var operation = await publication.FindOperationAsync(organizationId, operationId, ct);
        return operation is null ? null : await JobValueAsync(operation, ct);
    }

    private async Task<StatementJobResponse> JobValueAsync(StatementBuildOperation operation, CancellationToken ct)
    {
        // Resolve tenant-scoped operation first, never expose arbitrary global work IDs or payloads.
        var job = await work.FindJobAsync("billing.statement.build", operation.Id.ToString("N"), ct);
        var state = job is null ? "Missing" : job.DeadLetteredAt is not null ? "DeadLettered"
            : job.CompletedAt is not null ? "Completed" : job.LeaseExpiresAt > clock.GetUtcNow() ? "Leased"
            : job.LeaseExpiresAt is not null ? "AwaitingReclaim" : "Queued";
        return new(operation.Id, operation.OrganizationId, operation.Status, operation.ExpectedGeneration,
            operation.VersionId, operation.FailureKind, state, job);
    }

    private async Task<AuthenticatedIdentity> ActorAsync(string session, CancellationToken ct) =>
        await identity.AuthenticateSessionAsync(session, ct) ?? throw new StatementSessionRequiredException();

    private static StatementPeriod Month(DateTimeOffset start)
    {
        var value = StatementPeriod.At(start);
        if (start.Offset != TimeSpan.Zero || start != value.From) throw new ArgumentException("Use the start of a UTC calendar month.");
        return value;
    }
    private static int Limit(int? value, int fallback) => (value ?? fallback) is >= 1 and <= 100
        ? value ?? fallback : throw new ArgumentException("Limit must be between 1 and 100.");

    private string Encode<T>(T value, string purpose) => protection.CreateProtector("statement-cursor-v1", purpose).Protect(JsonSerializer.Serialize(value));
    private T? Decode<T>(string? value, string purpose) where T : class
    {
        if (value is null) return null;
        if (value.Length is < 1 or > 2048) throw new ArgumentException("Invalid statement cursor.");
        try { return JsonSerializer.Deserialize<T>(protection.CreateProtector("statement-cursor-v1", purpose).Unprotect(value))
                ?? throw new ArgumentException("Invalid statement cursor."); }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        { throw new ArgumentException("Invalid statement cursor."); }
    }

    private sealed record StatementPosition(DateTimeOffset Period, long Revision);
    private sealed record LinePosition(int Ordinal);
}

public sealed class StatementSessionRequiredException : Exception;
public sealed class StatementArtifactIntegrityException : Exception;
public sealed class StatementReadDependencyException(Exception inner) : Exception("Statement dependency unavailable.", inner);
