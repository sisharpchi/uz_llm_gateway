using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Identity.Contracts;

namespace UZLLM.Management.Api.ReportingEvidence;

/// <summary>Operator-only tenant/kind-scoped immutable evidence page; no global source export.</summary>
public sealed record ReportingEvidencePage(IReadOnlyList<ReportingEvidenceResponse> Items, string? NextCursor);
public interface IReportingEvidenceApiService
{
    Task EnsureOperatorAsync(string session, CancellationToken ct);
    Task<ReportingEvidenceResponse> FiscalAsync(string session, Guid org, RegisterFiscalEvidenceRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse> ContractAsync(string session, Guid org, RegisterContractReferenceRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse> LinkAsync(string session, Guid org, LinkContractReportRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse?> FindAsync(string session, Guid org, string kind, Guid id, CancellationToken ct);
    Task<ReportingEvidencePage> ListAsync(string session, Guid org, string kind, int? limit, string? cursor, CancellationToken ct);
}

public sealed class ReportingEvidenceApiService(IIdentityService identity, IReportingEvidenceStore store,
    IDataProtectionProvider protection) : IReportingEvidenceApiService
{
    public async Task EnsureOperatorAsync(string session, CancellationToken ct) => _ = await ActorAsync(session, ct);
    public async Task<ReportingEvidenceResponse> FiscalAsync(string session, Guid org, RegisterFiscalEvidenceRequest request, CancellationToken ct)
    { var actor = await ActorAsync(session, ct); return await store.RegisterFiscalAsync(org, actor, request, ct); }
    public async Task<ReportingEvidenceResponse> ContractAsync(string session, Guid org, RegisterContractReferenceRequest request, CancellationToken ct)
    { var actor = await ActorAsync(session, ct); return await store.RegisterContractAsync(org, actor, request, ct); }
    public async Task<ReportingEvidenceResponse> LinkAsync(string session, Guid org, LinkContractReportRequest request, CancellationToken ct)
    { var actor = await ActorAsync(session, ct); return await store.LinkAsync(org, actor, request, ct); }
    public async Task<ReportingEvidenceResponse?> FindAsync(string session, Guid org, string kind, Guid id, CancellationToken ct)
    { await ActorAsync(session, ct); return await store.FindAsync(org, kind, id, ct); }
    public async Task<ReportingEvidencePage> ListAsync(string session, Guid org, string kind, int? limit, string? cursor, CancellationToken ct)
    {
        await ActorAsync(session, ct); ReportingEvidencePolicy.Required(org); ReportingEvidencePolicy.Kind(kind);
        var count = limit ?? 20; if (count is < 1 or > 100) throw new ArgumentException("Invalid page size.");
        var protector = protection.CreateProtector($"reporting-evidence:v1:{org:N}:{kind}:{count}");
        Position? position = null;
        if (cursor is not null)
        {
            if (cursor.Length > 2048) throw new ArgumentException("Invalid cursor.");
            try { position = JsonSerializer.Deserialize<Position>(protector.Unprotect(cursor)) ?? throw new JsonException(); }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
            { throw new ArgumentException("Invalid cursor."); }
        }
        var values = await store.ListAsync(org, kind, position?.Time ?? DateTimeOffset.MaxValue,
            position?.Id ?? Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), count + 1, ct);
        var items = values.Take(count).ToArray();
        return new(items, values.Count > count ? protector.Protect(JsonSerializer.Serialize(new Position(items[^1].RecordedAt, items[^1].Id))) : null);
    }
    private async Task<Guid> ActorAsync(string session, CancellationToken ct)
    {
        var actor = await identity.AuthenticateSessionAsync(session, ct) ?? throw new ReportingEvidenceSessionException();
        if (!actor.IsOperator || !await identity.HasRecentOperatorReauthenticationAsync(session, ct)) throw new UnauthorizedAccessException();
        return actor.AccountId;
    }
    private sealed record Position(DateTimeOffset Time, Guid Id);
}
