using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Billing.Domain;

public static partial class ReportingEvidencePolicy
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static string Reference(string? value, int max = 120)
    {
        if (value is null || value.Length > max || !ReferencePattern().IsMatch(value))
            throw new ArgumentException("Invalid controlled reference.");
        return value;
    }
    public static string Reason(string? value)
    {
        if (value is null || value.Trim().Length is < 8 or > 500 || value.Any(char.IsControl))
            throw new ArgumentException("Invalid reason.");
        return value.Trim();
    }
    public static void Required(Guid value) { if (value == Guid.Empty) throw new ArgumentException("Identity required."); }
    public static void Hash(string? value)
    {
        if (value is null || !ShaPattern().IsMatch(value)) throw new ArgumentException("Invalid digest.");
    }
    public static string Digest<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    public static void Utc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Year < 2000 || value.Year > 9998 || value.Ticks % 10 != 0)
            throw new ArgumentException("Invalid UTC microsecond date.");
    }
    public static string Kind(string kind) => kind is "fiscal" or "contracts" or "links" ? kind : throw new ArgumentException("Invalid evidence kind.");
    public static void EnsureScope(ReportingServiceScope expected, ReportingServiceScope actual)
    {
        if (expected != actual) throw new ReportingEvidenceConflictException();
    }

    public static void ValidateProfile(ReportingEvidenceProfile p)
    {
        Reference(p.Id, 64); if (p.Version < 1) throw new ArgumentException("Invalid profile version.");
        Reference(p.IssuerId); Reference(p.Jurisdiction); Reference(p.Series); Reference(p.Currency, 12); Reference(p.DocumentKind);
        if (p.UnitScale is < 0 or > 6) throw new ArgumentException("Invalid unit scale.");
        Reference(p.IssuerIdentityReference); Reference(p.TaxPolicyReference); Reference(p.CorrectionPolicyReference); Reference(p.ArchivePolicyReference);
        Utc(p.EffectiveFrom); Utc(p.EffectiveTo);
        if (p.EffectiveFrom >= p.EffectiveTo) throw new ArgumentException("Invalid profile window.");
        if (p.RevokedAt is { } revoked) { Utc(revoked); if (revoked < p.EffectiveFrom) throw new ArgumentException("Invalid revocation."); }
        if (p.SupersedesProfileReference is not null) Reference(p.SupersedesProfileReference);
        if (p.Approvals is null || p.Approvals.Length is < 4 or > 8 || p.Approvals.Any(a => a is null)
            || p.Approvals.Select(a => a.Role).Distinct(StringComparer.Ordinal).Count() != p.Approvals.Length)
            throw new ArgumentException("Missing approval responsibilities.");
        foreach (var role in new[] { "Finance", "Legal", "Security", "Operations" })
            if (!p.Approvals.Any(a => a.Role == role)) throw new ArgumentException("Missing approval responsibility.");
        foreach (var a in p.Approvals)
        { Reference(a.Role); Reference(a.Reviewer); Reference(a.Reference); Utc(a.ApprovedAt); if (a.ApprovedAt > p.EffectiveFrom) throw new ArgumentException("Approval after effective date."); }
        if (p.Scopes is null || p.Scopes.Length is < 1 or > 32 || p.Scopes.Any(s => s is null)
            || p.Scopes.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != p.Scopes.Length)
            throw new ArgumentException("Invalid finite scopes.");
        foreach (var s in p.Scopes)
        { Reference(s.Id, 64); Reference(s.Region, 64); if (s.Operation is not ("Chat" or "Responses" or "Embeddings") || s.Mode is not ("All" or "Json" or "Sse")) throw new ArgumentException("Invalid scope."); }
    }

    public static ReportingEvidenceProfile Resolve(ReportingEvidenceRegistry registry, string id, int version, DateTimeOffset now)
    {
        Reference(id, 64); if (version < 1) throw new ArgumentException("Invalid profile version.");
        var p = registry.Profiles.SingleOrDefault(p => p.Id == id && p.Version == version) ?? throw new ReportingProfileUnavailableException();
        ValidateProfile(p);
        if ((!p.Synthetic && !registry.AllowLiveRegistration) || now < p.EffectiveFrom || now >= p.EffectiveTo
            || p.RevokedAt is { } revoked && now >= revoked) throw new ReportingProfileUnavailableException();
        // Freeze mutable configuration arrays; request DTOs cannot add approvals or change profiles.
        return JsonSerializer.Deserialize<ReportingEvidenceProfile>(JsonSerializer.Serialize(p, Json), Json)!;
    }

    public static RegisterFiscalEvidenceRequest Fiscal(RegisterFiscalEvidenceRequest r)
    {
        Required(r.IdempotencyKey); Reference(r.ProfileId, 64); Reference(r.ExternalOperationReference); Reference(r.ExternalProcessReference);
        if (r.ProfileVersion < 1 || r.Outcome is not ("Registered" or "Unknown" or "ReviewRequired" or "Cancelled")) throw new ArgumentException("Invalid fiscal observation.");
        if (r.PredecessorId is { } prior) Required(prior);
        if (r.StatementVersionId is { } statement) Required(statement);
        if (r.Outcome == "Registered" && r.Document is null || r.Outcome is "Unknown" or "Cancelled" && r.Document is not null
            || r.Outcome != "Registered" && r.StatementVersionId is not null) throw new ArgumentException("Fiscal state cannot publish a link.");
        if (r.Document is { } d)
        {
            Reference(d.Number); Utc(d.IssuedAt); Reference(d.Kind); Reference(d.CustomerIdentityReference);
            Hash(d.ContentSha256); Reference(d.ArchiveReference); Reference(d.VerificationReference);
            if (d.Amounts is null) throw new ArgumentException("Amounts required.");
            var total = Units(d.Amounts.TotalUnits); if (total <= 0) throw new ArgumentException("Positive total required.");
            if ((d.Amounts.NetUnits is null) != (d.Amounts.TaxUnits is null)) throw new ArgumentException("Incomplete breakdown.");
            if (d.Amounts.NetUnits is { } net && d.Amounts.TaxUnits is { } tax && Units(net) + Units(tax) != total)
                throw new ArgumentException("Breakdown mismatch.");
        }
        return r with { Reason = Reason(r.Reason) };
    }

    public static RegisterContractReferenceRequest Contract(RegisterContractReferenceRequest r)
    {
        Required(r.IdempotencyKey); Reference(r.ProfileId, 64); Reference(r.ContractId); Reference(r.PolicyVersion); Reference(r.ServiceScopeId, 64); Reference(r.CustomerIdentityReference);
        Utc(r.EffectiveFrom); Utc(r.EffectiveTo);
        if (r.ProfileVersion < 1 || r.EffectiveFrom >= r.EffectiveTo || r.State is not ("EvidenceOnly" or "Revoked")) throw new ArgumentException("Invalid contract.");
        if (r.PredecessorId is { } prior) Required(prior);
        if ((r.SignedContractReference is null) != (r.ContentSha256 is null)) throw new ArgumentException("Incomplete signed evidence.");
        if (r.SignedContractReference is not null) { Reference(r.SignedContractReference); Hash(r.ContentSha256); }
        return r with { Reason = Reason(r.Reason) };
    }

    public static LinkContractReportRequest Link(LinkContractReportRequest r)
    {
        Required(r.IdempotencyKey); Required(r.ContractReferenceId); Utc(r.PeriodStart);
        if (r.PeriodStart.Day != 1 || r.PeriodStart.TimeOfDay != TimeSpan.Zero || r.StatementVersionId is null && r.SliReportVersionId is null)
            throw new ArgumentException("Select a UTC month and immutable report.");
        if (r.StatementVersionId is { } statement) Required(statement); if (r.SliReportVersionId is { } sli) Required(sli);
        return r with { Reason = Reason(r.Reason) };
    }
    private static BigInteger Units(string value)
    {
        if (value is null || value.Length is < 1 or > 38 || value.Length > 1 && value[0] == '0' || value.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Invalid exact units.");
        return BigInteger.Parse(value, CultureInfo.InvariantCulture);
    }
    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant)] private static partial Regex ReferencePattern();
    [GeneratedRegex("\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)] private static partial Regex ShaPattern();
}
