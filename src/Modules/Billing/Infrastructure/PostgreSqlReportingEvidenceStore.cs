using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

/// <summary>Low-volume administrative journal; never locks financial admission or calls an issuer.</summary>
public sealed class PostgreSqlReportingEvidenceStore(FoundationDbContext db, ITransactionCoordinator transactions,
    IAuditTrail audit, ReportingEvidenceRegistry registry, IStatementReadStore statements,
    ISliReportStore sli, TimeProvider clock) : IReportingEvidenceStore
{
    public async Task<ReportingEvidenceResponse> RegisterFiscalAsync(Guid org, Guid actor, RegisterFiscalEvidenceRequest request, CancellationToken ct)
    {
        var r = ReportingEvidencePolicy.Fiscal(request);
        await using var tx = await transactions.BeginAsync(ct); await LockAsync(ct);
        var hash = Command(org, actor, r);
        if (await ReplayAsync(org, "fiscal", r.IdempotencyKey, hash, ct) is { } replay) return replay;
        var now = Now(); var p = ReportingEvidencePolicy.Resolve(registry, r.ProfileId, r.ProfileVersion, now);
        var issuer = ReportingEvidencePolicy.Digest(new { p.IssuerId, p.Jurisdiction, p.Series });
        var root = true;
        if (r.Document is { } d && (d.Kind != p.DocumentKind || d.IssuedAt < p.EffectiveFrom || d.IssuedAt >= p.EffectiveTo || d.IssuedAt > now))
            throw new ArgumentException("Document outside approved window.");
        if (r.PredecessorId is { } id)
        {
            var previous = await FindAsync(org, "fiscal", id, ct) ?? throw new KeyNotFoundException();
            var old = previous.Fiscal!;
            if (await HasSuccessorAsync(id, ct) || previous.Profile.Synthetic != p.Synthetic
                || previous.Profile.IssuerId != p.IssuerId || previous.Profile.Jurisdiction != p.Jurisdiction || previous.Profile.Series != p.Series)
                throw new ReportingEvidenceConflictException();
            if (old.Outcome is "Unknown" or "ReviewRequired")
            {
                if (r.ExternalOperationReference != old.ExternalOperationReference || r.Outcome is not ("Registered" or "ReviewRequired" or "Cancelled"))
                    throw new ReportingEvidenceConflictException();
                root = false; // Same external operation is reconciled, never reissued after a timeout.
            }
            else if (old.Outcome == "Registered")
            {
                if (r.ExternalOperationReference == old.ExternalOperationReference || r.Outcome is not ("Registered" or "Unknown" or "ReviewRequired" or "Cancelled")
                    || r.Outcome == "Registered" && r.Document!.Number == old.Document!.Number)
                    throw new ReportingEvidenceConflictException();
            }
            else throw new ReportingEvidenceConflictException();
        }
        else if (r.Outcome == "Cancelled") throw new ArgumentException("Cancellation requires its issued predecessor.");
        if (root && await Entries.AnyAsync(e => e.IssuerKey == issuer && e.ExternalOperationReference == r.ExternalOperationReference && e.IsOperationRoot, ct))
            throw new ReportingEvidenceConflictException();
        if (r.Outcome == "Registered" && await Entries.AnyAsync(e => e.IssuerKey == issuer && e.DocumentNumber == r.Document!.Number && e.State == "Registered", ct))
            throw new ReportingEvidenceConflictException();
        var statementDigest = await StatementAsync(org, r.StatementVersionId, null, ct);
        var row = New(org, actor, "fiscal", r.Outcome, r.IdempotencyKey, hash, p, now);
        row.PredecessorId = r.PredecessorId; row.StatementVersionId = r.StatementVersionId;
        row.IssuerKey = issuer; row.ExternalOperationReference = r.ExternalOperationReference;
        row.DocumentNumber = r.Document?.Number; row.IsOperationRoot = root;
        var response = new ReportingEvidenceResponse(row.Id, org, row.Kind, row.State, now, actor, row.PredecessorId,
            p, r, null, null, null, statementDigest, null, null);
        await SaveAsync(row, response, p, ct); await tx.CommitAsync(ct); return response;
    }

    public async Task<ReportingEvidenceResponse> RegisterContractAsync(Guid org, Guid actor, RegisterContractReferenceRequest request, CancellationToken ct)
    {
        var r = ReportingEvidencePolicy.Contract(request);
        await using var tx = await transactions.BeginAsync(ct); await LockAsync(ct);
        var hash = Command(org, actor, r);
        if (await ReplayAsync(org, "contracts", r.IdempotencyKey, hash, ct) is { } replay) return replay;
        var now = Now(); var p = ReportingEvidencePolicy.Resolve(registry, r.ProfileId, r.ProfileVersion, now);
        var serviceScope = p.Scopes.SingleOrDefault(s => s.Id == r.ServiceScopeId) ?? throw new ArgumentException("Unregistered scope.");
        if (r.EffectiveFrom < p.EffectiveFrom || r.EffectiveTo > p.EffectiveTo) throw new ArgumentException("Contract outside approved window.");
        if (r.SignedContractReference is not null && !p.Approvals.Any(a => a.Role == "Commercial")) throw new ReportingProfileUnavailableException();
        if (r.PredecessorId is { } id)
        {
            var previous = await FindAsync(org, "contracts", id, ct) ?? throw new KeyNotFoundException(); var old = previous.Contract!;
            if (await HasSuccessorAsync(id, ct) || old.State == "Revoked" || old.ContractId != r.ContractId
                || previous.Scope != serviceScope || previous.Profile.Synthetic != p.Synthetic || old.CustomerIdentityReference != r.CustomerIdentityReference)
                throw new ReportingEvidenceConflictException();
            if (r.State == "Revoked")
            {
                if (r with { IdempotencyKey = old.IdempotencyKey, Reason = old.Reason, State = old.State, PredecessorId = old.PredecessorId } != old)
                    throw new ReportingEvidenceConflictException();
            }
            else if (old.PolicyVersion == r.PolicyVersion) throw new ReportingEvidenceConflictException();
        }
        else if (r.State == "Revoked" || await Entries.AnyAsync(e => e.OrganizationId == org && e.Kind == "contracts" && e.ContractKey == r.ContractId, ct))
            throw new ReportingEvidenceConflictException();
        if (r.State == "EvidenceOnly" && await Entries.AnyAsync(e => e.OrganizationId == org && e.Kind == "contracts"
            && e.ScopeId == r.ServiceScopeId && e.State == "EvidenceOnly" && e.Id != r.PredecessorId
            && e.EffectiveFrom < r.EffectiveTo && e.EffectiveTo > r.EffectiveFrom && !Entries.Any(n => n.PredecessorId == e.Id), ct))
            throw new ReportingEvidenceConflictException();
        var row = New(org, actor, "contracts", r.State, r.IdempotencyKey, hash, p, now);
        row.PredecessorId = r.PredecessorId; row.ContractKey = r.ContractId; row.PolicyVersion = r.PolicyVersion;
        row.ScopeId = r.ServiceScopeId; row.EffectiveFrom = r.EffectiveFrom; row.EffectiveTo = r.EffectiveTo;
        var response = new ReportingEvidenceResponse(row.Id, org, row.Kind, row.State, now, actor, row.PredecessorId,
            p, null, r, null, serviceScope, null, null, null);
        await SaveAsync(row, response, p, ct); await tx.CommitAsync(ct); return response;
    }

    public async Task<ReportingEvidenceResponse> LinkAsync(Guid org, Guid actor, LinkContractReportRequest request, CancellationToken ct)
    {
        var r = ReportingEvidencePolicy.Link(request);
        await using var tx = await transactions.BeginAsync(ct); await LockAsync(ct);
        var hash = Command(org, actor, r);
        if (await ReplayAsync(org, "links", r.IdempotencyKey, hash, ct) is { } replay) return replay;
        var contract = await FindAsync(org, "contracts", r.ContractReferenceId, ct) ?? throw new KeyNotFoundException();
        var terms = contract.Contract!; var now = Now();
        var p = ReportingEvidencePolicy.Resolve(registry, contract.Profile.Id, contract.Profile.Version, now);
        if (terms.State != "EvidenceOnly" || await HasSuccessorAsync(contract.Id, ct)
            || r.PeriodStart < terms.EffectiveFrom || r.PeriodStart.AddMonths(1) > terms.EffectiveTo || r.PeriodStart.AddMonths(1) > now)
            throw new ReportingEvidenceConflictException();
        var statementDigest = await StatementAsync(org, r.StatementVersionId, r.PeriodStart, ct);
        SliReportArtifact? report = null;
        if (r.SliReportVersionId is { } reportId)
        {
            report = await sli.FindAsync(reportId, ct) ?? throw new KeyNotFoundException();
            var scope = contract.Scope!;
            if (report.Metadata.PeriodStart != r.PeriodStart) throw new ReportingEvidenceConflictException();
            ReportingEvidencePolicy.EnsureScope(scope, new(report.Scope.Id, report.Scope.Region, report.Scope.Operation, report.Scope.Mode));
        }
        var linkKey = ReportingEvidencePolicy.Digest(new { r.ContractReferenceId, r.PeriodStart, r.StatementVersionId, r.SliReportVersionId });
        if (await Entries.AnyAsync(e => e.OrganizationId == org && e.LinkKey == linkKey, ct)) throw new ReportingEvidenceConflictException();
        var row = New(org, actor, "links", "EvidenceOnly", r.IdempotencyKey, hash, p, now);
        row.ContractReferenceId = contract.Id; row.StatementVersionId = r.StatementVersionId; row.SliReportVersionId = r.SliReportVersionId; row.LinkKey = linkKey;
        var response = new ReportingEvidenceResponse(row.Id, org, row.Kind, row.State, now, actor, null,
            p, null, null, r, contract.Scope, statementDigest, report?.Metadata.SourceDigest, report?.Metadata.Coverage);
        await SaveAsync(row, response, p, ct); await tx.CommitAsync(ct); return response;
    }

    public async Task<ReportingEvidenceResponse?> FindAsync(Guid org, string kind, Guid id, CancellationToken ct)
    {
        ReportingEvidencePolicy.Required(org); ReportingEvidencePolicy.Required(id); ReportingEvidencePolicy.Kind(kind);
        var row = await Entries.SingleOrDefaultAsync(e => e.OrganizationId == org && e.Kind == kind && e.Id == id, ct);
        return row is null ? null : Read(row);
    }
    public async Task<IReadOnlyList<ReportingEvidenceResponse>> ListAsync(Guid org, string kind, DateTimeOffset beforeTime, Guid beforeId, int take, CancellationToken ct)
    {
        ReportingEvidencePolicy.Required(org); ReportingEvidencePolicy.Kind(kind);
        if (take is < 1 or > 101) throw new ArgumentOutOfRangeException(nameof(take));
        var rows = await Entries.Where(e => e.OrganizationId == org && e.Kind == kind
            && (e.RecordedAt < beforeTime || e.RecordedAt == beforeTime && e.Id.CompareTo(beforeId) < 0))
            .OrderByDescending(e => e.RecordedAt).ThenByDescending(e => e.Id).Take(take).ToListAsync(ct);
        return rows.Select(Read).ToArray();
    }
    private IQueryable<BillingReportingEvidenceEntity> Entries => db.Set<BillingReportingEvidenceEntity>().AsNoTracking();
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow().ToUniversalTime();
        return new(now.Ticks - now.Ticks % 10, TimeSpan.Zero); // Cursor/artifact time must equal PostgreSQL microseconds.
    }
    private Task<int> LockAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s'; SELECT pg_advisory_xact_lock(7819201001);", ct);
    private static string Command<T>(Guid org, Guid actor, T request)
    { ReportingEvidencePolicy.Required(org); ReportingEvidencePolicy.Required(actor); return ReportingEvidencePolicy.Digest(new { org, actor, request }); }
    private async Task<ReportingEvidenceResponse?> ReplayAsync(Guid org, string kind, Guid key, string hash, CancellationToken ct)
    {
        var row = await Entries.SingleOrDefaultAsync(e => e.OrganizationId == org && e.Kind == kind && e.IdempotencyKey == key, ct);
        if (row is null) return null; if (row.CommandSha256 != hash) throw new ReportingEvidenceConflictException(); return Read(row);
    }
    private Task<bool> HasSuccessorAsync(Guid id, CancellationToken ct) => Entries.AnyAsync(e => e.PredecessorId == id, ct);
    private async Task<string?> StatementAsync(Guid org, Guid? id, DateTimeOffset? month, CancellationToken ct)
    {
        if (id is null) return null;
        var value = await statements.FindAsync(org, id.Value, ct) ?? throw new KeyNotFoundException();
        if (month is { } start && value.Period.From != start) throw new ReportingEvidenceConflictException();
        return value.SourceDigest;
    }
    private static BillingReportingEvidenceEntity New(Guid org, Guid actor, string kind, string state, Guid key, string hash,
        ReportingEvidenceProfile p, DateTimeOffset now) => new()
        { Id = Guid.CreateVersion7(), OrganizationId = org, ActorId = actor, Kind = kind, State = state,
          IdempotencyKey = key, CommandSha256 = hash, ProfileId = p.Id, ProfileVersion = p.Version, RecordedAt = now };
    private async Task SaveAsync(BillingReportingEvidenceEntity row, ReportingEvidenceResponse response, ReportingEvidenceProfile p, CancellationToken ct)
    {
        var profileHash = ReportingEvidencePolicy.Digest(p);
        var profile = await db.Set<BillingReportingProfileEntity>().FindAsync([p.Id, p.Version], ct);
        if (profile is null) db.Add(new BillingReportingProfileEntity { Id = p.Id, Version = p.Version, SnapshotJson = JsonSerializer.Serialize(p, ReportingEvidencePolicy.Json), Sha256 = profileHash });
        else if (profile.Sha256 != profileHash) throw new ReportingEvidenceConflictException();
        row.PayloadJson = JsonSerializer.Serialize(response, ReportingEvidencePolicy.Json);
        if (Encoding.UTF8.GetByteCount(row.PayloadJson) > 32768) throw new ArgumentException("Evidence too large.");
        row.PayloadSha256 = BytesHash(row.PayloadJson); db.Add(row);
        // No legal identifiers, reasons, external document numbers or content in audit/ordinary telemetry.
        try
        {
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(row.OrganizationId, row.ActorId, "reporting.evidence.recorded", "reporting_evidence", row.Id,
                null, JsonSerializer.Serialize(new { row.Kind, row.State, profileId = p.Id, profileVersion = p.Version, p.Synthetic })), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new ReportingEvidenceDependencyException(exception); }
    }
    private static ReportingEvidenceResponse Read(BillingReportingEvidenceEntity row)
    {
        if (BytesHash(row.PayloadJson) != row.PayloadSha256) throw new InvalidOperationException("Evidence integrity failure.");
        var response = JsonSerializer.Deserialize<ReportingEvidenceResponse>(row.PayloadJson, ReportingEvidencePolicy.Json)
            ?? throw new InvalidOperationException("Missing evidence.");
        if (response.Id != row.Id || response.OrganizationId != row.OrganizationId || response.Kind != row.Kind || response.State != row.State
            || response.Profile.Id != row.ProfileId || response.Profile.Version != row.ProfileVersion || response.Eligibility != "EvidenceOnly")
            throw new InvalidOperationException("Evidence identity failure.");
        return response;
    }
    private static string BytesHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class ReportingEvidenceRegistration
{
    public static IServiceCollection AddUzllmReportingEvidence(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var registry = configuration.GetSection("Reporting:Evidence")
            .Get<ReportingEvidenceRegistry>(options => options.ErrorOnUnknownConfiguration = true) ?? new(false, []);
        if (registry.Profiles is null || registry.Profiles.Length > 32 || registry.Profiles.Select(p => (p.Id, p.Version)).Distinct().Count() != registry.Profiles.Length)
            throw new ArgumentException("Invalid reporting profile registry.");
        foreach (var p in registry.Profiles)
        {
            ReportingEvidencePolicy.ValidateProfile(p);
            if (p.Synthetic && environment.IsProduction()) throw new ArgumentException("Synthetic reporting profile prohibited in Production.");
        }
        services.AddSingleton(registry); services.AddScoped<IReportingEvidenceStore, PostgreSqlReportingEvidenceStore>();
        return services;
    }
}
