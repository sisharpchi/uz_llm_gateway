using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.SliReports;

/// <summary>Build a closed UTC month for a deployment-registered scope; no exclusion overrides.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSliReportRequest([property: JsonRequired] string ScopeId,
    [property: JsonRequired] DateTimeOffset PeriodStart, [property: JsonRequired] Guid IdempotencyKey,
    [property: JsonRequired] long ExpectedGeneration, [property: JsonRequired] string Reason);
/// <summary>Explicit successor of an immutable report; the original remains readable.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviseSliReportRequest([property: JsonRequired] Guid IdempotencyKey,
    [property: JsonRequired] long ExpectedGeneration, [property: JsonRequired] string Reason);
/// <summary>Operator-only month/revision keyset page.</summary>
public sealed record SliReportPage(IReadOnlyList<SliReportMetadata> Items, string? NextCursor);
/// <summary>Version-bound immutable source membership, not raw payloads.</summary>
public sealed record SliSourcePage(Guid VersionId, IReadOnlyList<SliReportSource> Items, string? NextCursor);
/// <summary>Version-bound classified execution evidence with no customer content.</summary>
public sealed record SliExecutionPage(Guid VersionId, IReadOnlyList<SliClassifiedExecution> Items, string? NextCursor);
/// <summary>Summary and separate provider-attempt facts; no contractual compliance or financial zero.</summary>
public sealed record SliReportResponse(SliReportMetadata Metadata, SliReportScope Scope, IReadOnlyList<SliAttemptCounts> Attempts);
/// <summary>Stable command operation and independent leased-job state, including dead letters.</summary>
public sealed record SliReportJobResponse(SliReportOperation Operation, string JobState, JobProgress? Job);

public interface ISliReportApiService
{
    Task EnsureOperatorAsync(string session, CancellationToken ct);
    Task<SliReportPage> ListAsync(string session, string scopeId, int? limit, string? cursor, CancellationToken ct);
    Task<SliReportResponse?> FindAsync(string session, Guid id, CancellationToken ct);
    Task<SliSourcePage?> SourcesAsync(string session, Guid id, int? limit, string? cursor, CancellationToken ct);
    Task<SliExecutionPage?> ExecutionsAsync(string session, Guid id, int? limit, string? cursor, CancellationToken ct);
    Task<SliReportJobResponse> CreateAsync(string session, CreateSliReportRequest request, CancellationToken ct);
    Task<SliReportJobResponse?> ReviseAsync(string session, Guid id, ReviseSliReportRequest request, CancellationToken ct);
    Task<SliReportJobResponse?> JobAsync(string session, Guid id, CancellationToken ct);
}

public sealed class SliReportApiService(IIdentityService identity, ISliReportStore reports, SliReportProfiles profiles,
    IOperationalWorkMonitor work, IDataProtectionProvider protection, TimeProvider clock) : ISliReportApiService
{
    public async Task EnsureOperatorAsync(string session, CancellationToken ct) => _ = await ActorAsync(session, ct);
    public async Task<SliReportPage> ListAsync(string session, string scopeId, int? limit, string? cursor, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        if (!profiles.Scopes.ContainsKey(scopeId)) throw new ArgumentException("Unknown registered scope.");
        var count = Limit(limit, 20); var purpose = $"list:{scopeId}:{count}";
        var position = Decode<ListPosition>(cursor, purpose);
        var rows = await reports.ListAsync(scopeId, position?.Month ?? DateTimeOffset.MaxValue, position?.Revision ?? long.MaxValue, count + 1, ct);
        var items = rows.Take(count).ToArray();
        return new(items, rows.Count > count ? Encode(new ListPosition(items[^1].PeriodStart, items[^1].Revision), purpose) : null);
    }
    public async Task<SliReportResponse?> FindAsync(string session, Guid id, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        var value = await reports.FindAsync(id, ct);
        return value is null ? null : new(value.Metadata, value.Scope, value.Attempts);
    }
    public async Task<SliSourcePage?> SourcesAsync(string session, Guid id, int? limit, string? cursor, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        var count = Limit(limit, 50); var purpose = $"sources:{id:N}:{count}";
        var position = Decode<RowPosition>(cursor, purpose);
        var artifact = await reports.FindAsync(id, ct); if (artifact is null) return null;
        var offset = position?.Offset ?? 0;
        var items = artifact.Sources.Skip(offset).Take(count).ToArray();
        return new(id, items, artifact.Sources.Length > offset + items.Length ? Encode(new RowPosition(offset + items.Length), purpose) : null);
    }
    public async Task<SliExecutionPage?> ExecutionsAsync(string session, Guid id, int? limit, string? cursor, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        var count = Limit(limit, 50); var purpose = $"executions:{id:N}:{count}";
        var position = Decode<RowPosition>(cursor, purpose);
        var artifact = await reports.FindAsync(id, ct); if (artifact is null) return null;
        var offset = position?.Offset ?? 0;
        var items = artifact.Executions.Skip(offset).Take(count).ToArray();
        return new(id, items, artifact.Executions.Length > offset + items.Length ? Encode(new RowPosition(offset + items.Length), purpose) : null);
    }
    public async Task<SliReportJobResponse> CreateAsync(string session, CreateSliReportRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(session, ct);
        if (request.ExpectedGeneration != 0) throw new ArgumentException("Use an explicit revision.");
        return await JobValueAsync(await reports.RequestAsync(new(actor.AccountId, request.ScopeId, request.PeriodStart,
            request.IdempotencyKey, request.ExpectedGeneration, null, request.Reason), ct), ct);
    }
    public async Task<SliReportJobResponse?> ReviseAsync(string session, Guid id, ReviseSliReportRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(session, ct);
        if (request.ExpectedGeneration < 1) throw new ArgumentException("Revision requires a positive generation.");
        var previous = await reports.FindAsync(id, ct); if (previous is null) return null;
        return await JobValueAsync(await reports.RequestAsync(new(actor.AccountId, previous.Metadata.ScopeId, previous.Metadata.PeriodStart,
            request.IdempotencyKey, request.ExpectedGeneration, id, request.Reason), ct), ct);
    }
    public async Task<SliReportJobResponse?> JobAsync(string session, Guid id, CancellationToken ct)
    {
        await EnsureOperatorAsync(session, ct);
        var operation = await reports.OperationAsync(id, ct);
        return operation is null ? null : await JobValueAsync(operation, ct);
    }
    private async Task<SliReportJobResponse> JobValueAsync(SliReportOperation operation, CancellationToken ct)
    {
        var job = await work.FindJobAsync("usage.sli.build", operation.Id.ToString("N"), ct);
        var state = job is null ? "Missing" : job.DeadLetteredAt is not null ? "DeadLettered"
            : job.CompletedAt is not null ? "Completed" : job.LeaseExpiresAt > clock.GetUtcNow() ? "Leased"
            : job.LeaseExpiresAt is not null ? "AwaitingReclaim" : "Queued";
        return new(operation, state, job);
    }
    private async Task<AuthenticatedIdentity> ActorAsync(string session, CancellationToken ct)
    {
        var actor = await identity.AuthenticateSessionAsync(session, ct) ?? throw new SliReportSessionRequiredException();
        if (!actor.IsOperator || !await identity.HasRecentOperatorReauthenticationAsync(session, ct)) throw new UnauthorizedAccessException();
        return actor;
    }
    private static int Limit(int? value, int fallback) => (value ?? fallback) is >= 1 and <= 100 ? value ?? fallback : throw new ArgumentException("Invalid page limit.");
    private string Encode<T>(T value, string purpose) => protection.CreateProtector("sli-report-cursor-v1", purpose).Protect(JsonSerializer.Serialize(value));
    private T? Decode<T>(string? value, string purpose) where T : class
    {
        if (value is null) return null;
        if (value.Length is < 1 or > 2048) throw new ArgumentException("Invalid SLI cursor.");
        try { return JsonSerializer.Deserialize<T>(protection.CreateProtector("sli-report-cursor-v1", purpose).Unprotect(value)) ?? throw new ArgumentException("Invalid SLI cursor."); }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException) { throw new ArgumentException("Invalid SLI cursor."); }
    }
    private sealed record ListPosition(DateTimeOffset Month, long Revision);
    private sealed record RowPosition(int Offset);
}
public sealed class SliReportSessionRequiredException : Exception;
