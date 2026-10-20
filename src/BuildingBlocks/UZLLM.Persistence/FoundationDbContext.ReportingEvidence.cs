using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureReportingEvidence(ModelBuilder model)
    {
        var profile = StatementEntity<BillingReportingProfileEntity>(model, "reporting_profile");
        profile.HasKey(v => new { v.Id, v.Version });
        profile.Property(v => v.Id).HasMaxLength(64); profile.Property(v => v.Sha256).HasMaxLength(64);
        profile.ToTable("reporting_profile", "billing", t => t.HasCheckConstraint("CK_reporting_profile_bounds",
            "version > 0 AND octet_length(snapshot_json) BETWEEN 1 AND 32768 AND length(sha256) = 64"));
        var e = StatementEntity<BillingReportingEvidenceEntity>(model, "reporting_evidence");
        e.HasKey(v => v.Id); e.HasAlternateKey(v => new { v.Id, v.OrganizationId });
        e.HasAlternateKey(v => new { v.Id, v.OrganizationId, v.Kind });
        e.HasIndex(v => new { v.OrganizationId, v.Kind, v.IdempotencyKey }).IsUnique();
        e.HasIndex(v => new { v.OrganizationId, v.Kind, v.RecordedAt, v.Id });
        e.HasIndex(v => v.PredecessorId).IsUnique();
        e.HasIndex(v => new { v.IssuerKey, v.ExternalOperationReference }).IsUnique().HasFilter("is_operation_root");
        e.HasIndex(v => new { v.IssuerKey, v.DocumentNumber }).IsUnique().HasFilter("kind = 'fiscal' AND state = 'Registered'");
        e.HasIndex(v => new { v.OrganizationId, v.ContractKey, v.PolicyVersion }).IsUnique().HasFilter("kind = 'contracts' AND state = 'EvidenceOnly'");
        e.HasIndex(v => new { v.OrganizationId, v.LinkKey }).IsUnique().HasFilter("kind = 'links'");
        e.HasOne<OrganizationEntity>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(v => v.ActorId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BillingReportingProfileEntity>().WithMany().HasForeignKey(v => new { v.ProfileId, v.ProfileVersion }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BillingReportingEvidenceEntity>().WithMany().HasForeignKey(v => new { v.PredecessorId, v.OrganizationId, v.Kind })
            .HasPrincipalKey(v => new { v.Id, v.OrganizationId, v.Kind }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BillingReportingEvidenceEntity>().WithMany().HasForeignKey(v => new { v.ContractReferenceId, v.OrganizationId })
            .HasPrincipalKey(v => new { v.Id, v.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(v => new { v.StatementVersionId, v.OrganizationId })
            .HasPrincipalKey(v => new { v.Id, v.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<SliReportVersionEntity>().WithMany().HasForeignKey(v => v.SliReportVersionId).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(BillingReportingEvidenceEntity.Kind), nameof(BillingReportingEvidenceEntity.State) }) e.Property(name).HasMaxLength(32);
        foreach (var name in new[] { nameof(BillingReportingEvidenceEntity.CommandSha256), nameof(BillingReportingEvidenceEntity.PayloadSha256), nameof(BillingReportingEvidenceEntity.IssuerKey), nameof(BillingReportingEvidenceEntity.LinkKey) }) e.Property(name).HasMaxLength(64);
        foreach (var name in new[] { nameof(BillingReportingEvidenceEntity.ExternalOperationReference), nameof(BillingReportingEvidenceEntity.DocumentNumber), nameof(BillingReportingEvidenceEntity.ContractKey), nameof(BillingReportingEvidenceEntity.PolicyVersion) }) e.Property(name).HasMaxLength(120);
        e.Property(v => v.ProfileId).HasMaxLength(64); e.Property(v => v.ScopeId).HasMaxLength(64);
        e.ToTable("reporting_evidence", "billing", t =>
        {
            t.HasCheckConstraint("CK_reporting_evidence_bounds", "octet_length(payload_json) BETWEEN 1 AND 32768 AND length(payload_sha256) = 64 AND length(command_sha256) = 64");
            t.HasCheckConstraint("CK_reporting_evidence_kind", "(kind = 'fiscal' AND state IN ('Unknown','ReviewRequired','Registered','Cancelled') AND issuer_key IS NOT NULL AND external_operation_reference IS NOT NULL AND contract_reference_id IS NULL AND sli_report_version_id IS NULL AND (state <> 'Registered' OR document_number IS NOT NULL)) OR (kind = 'contracts' AND state IN ('EvidenceOnly','Revoked') AND contract_key IS NOT NULL AND policy_version IS NOT NULL AND scope_id IS NOT NULL AND effective_from < effective_to AND statement_version_id IS NULL AND sli_report_version_id IS NULL AND contract_reference_id IS NULL AND issuer_key IS NULL AND NOT is_operation_root) OR (kind = 'links' AND state = 'EvidenceOnly' AND contract_reference_id IS NOT NULL AND link_key IS NOT NULL AND (statement_version_id IS NOT NULL OR sli_report_version_id IS NOT NULL) AND predecessor_id IS NULL AND issuer_key IS NULL AND NOT is_operation_root)");
        });
    }
}
