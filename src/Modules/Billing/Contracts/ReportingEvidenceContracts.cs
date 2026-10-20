using System.Text.Json.Serialization;

namespace UZLLM.Modules.Billing.Contracts;

/// <summary>Dated external review responsibility; not an application permission or authenticity proof.</summary>
public sealed record ReportingApproval(string Role, string Reviewer, string Reference, DateTimeOffset ApprovedAt);
/// <summary>Finite deployment service scope; no customer predicate, wildcard or region assertion.</summary>
public sealed record ReportingServiceScope(string Id, string Region, string Operation, string Mode);
/// <summary>Deployment-approved version, archived with each registration; synthetic is never live approval.</summary>
public sealed record ReportingEvidenceProfile(string Id, int Version, bool Synthetic, string IssuerId,
    string Jurisdiction, string Series, string Currency, int UnitScale, string DocumentKind, string IssuerIdentityReference,
    string TaxPolicyReference, string CorrectionPolicyReference, string ArchivePolicyReference,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, ReportingApproval[] Approvals,
    ReportingServiceScope[] Scopes, DateTimeOffset? RevokedAt = null, string? SupersedesProfileReference = null);
/// <summary>Empty/default-deny deployment registry; no HTTP endpoint can approve a profile.</summary>
public sealed record ReportingEvidenceRegistry(bool AllowLiveRegistration, ReportingEvidenceProfile[] Profiles)
{
    public ReportingEvidenceRegistry() : this(false, []) { }
}
/// <summary>Exact profile-defined integer units; null tax/net means unknown, never zero or exemption.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FiscalEvidenceAmounts([property: JsonRequired] string TotalUnits, string? NetUnits, string? TaxUnits);
/// <summary>Controlled external document references only; no uploads, fetch URL or local invoice generation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FiscalDocumentReference([property: JsonRequired] string Number, [property: JsonRequired] DateTimeOffset IssuedAt,
    [property: JsonRequired] string Kind, [property: JsonRequired] string CustomerIdentityReference,
    [property: JsonRequired] FiscalEvidenceAmounts Amounts, [property: JsonRequired] string ContentSha256,
    [property: JsonRequired] string ArchiveReference, [property: JsonRequired] string VerificationReference);
/// <summary>Append an external observation, reconciliation, correction or cancellation; never issue or refund.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterFiscalEvidenceRequest([property: JsonRequired] string ProfileId, [property: JsonRequired] int ProfileVersion,
    [property: JsonRequired] Guid IdempotencyKey, [property: JsonRequired] string ExternalOperationReference,
    [property: JsonRequired] string Outcome, [property: JsonRequired] string ExternalProcessReference,
    [property: JsonRequired] string Reason, FiscalDocumentReference? Document = null, Guid? PredecessorId = null,
    Guid? StatementVersionId = null);
/// <summary>Immutable contract provenance; even signed terms do not grant credit eligibility.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterContractReferenceRequest([property: JsonRequired] string ProfileId, [property: JsonRequired] int ProfileVersion,
    [property: JsonRequired] Guid IdempotencyKey, [property: JsonRequired] string ContractId,
    [property: JsonRequired] string PolicyVersion, [property: JsonRequired] string ServiceScopeId,
    [property: JsonRequired] string CustomerIdentityReference, [property: JsonRequired] DateTimeOffset EffectiveFrom,
    [property: JsonRequired] DateTimeOffset EffectiveTo, [property: JsonRequired] string State,
    [property: JsonRequired] string Reason, string? SignedContractReference = null, string? ContentSha256 = null,
    Guid? PredecessorId = null);
/// <summary>Version-pinned evidence-only linkage; not a contractual measurement or credit review.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LinkContractReportRequest([property: JsonRequired] Guid IdempotencyKey,
    [property: JsonRequired] Guid ContractReferenceId, [property: JsonRequired] DateTimeOffset PeriodStart,
    [property: JsonRequired] string Reason, Guid? StatementVersionId = null, Guid? SliReportVersionId = null);
/// <summary>Operator-only immutable provenance. Customer statement APIs never serialize these fields.</summary>
public sealed record ReportingEvidenceResponse(Guid Id, Guid OrganizationId, string Kind, string State,
    DateTimeOffset RecordedAt, Guid ActorId, Guid? PredecessorId, ReportingEvidenceProfile Profile,
    RegisterFiscalEvidenceRequest? Fiscal, RegisterContractReferenceRequest? Contract,
    LinkContractReportRequest? Link, ReportingServiceScope? Scope, string? StatementSourceDigest,
    string? SliSourceDigest, string? SliCoverage, string Eligibility = "EvidenceOnly")
{
    public string Label => Profile.Synthetic ? "Synthetic evidence — no live fiscal or SLA authority"
        : "External evidence only — not locally issued or independently legally verified";
}
/// <summary>Internal persistence port; Management must authorize every call before invoking it.</summary>
public interface IReportingEvidenceStore
{
    Task<ReportingEvidenceResponse> RegisterFiscalAsync(Guid org, Guid actor, RegisterFiscalEvidenceRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse> RegisterContractAsync(Guid org, Guid actor, RegisterContractReferenceRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse> LinkAsync(Guid org, Guid actor, LinkContractReportRequest request, CancellationToken ct);
    Task<ReportingEvidenceResponse?> FindAsync(Guid org, string kind, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReportingEvidenceResponse>> ListAsync(Guid org, string kind, DateTimeOffset beforeTime, Guid beforeId, int take, CancellationToken ct);
}
public sealed class ReportingEvidenceConflictException : Exception;
public sealed class ReportingProfileUnavailableException : Exception;
public sealed class ReportingEvidenceSessionException : Exception;
public sealed class ReportingEvidenceDependencyException(Exception inner) : Exception("Reporting evidence unavailable.", inner);
