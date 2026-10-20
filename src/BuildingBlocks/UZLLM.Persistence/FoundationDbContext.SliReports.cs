using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureSliReports(ModelBuilder model)
    {
        var head = SliEntity<SliReportHeadEntity>(model, "sli_report_head");
        head.HasKey(value => value.Id);
        head.Property(value => value.ScopeId).HasMaxLength(64);
        head.HasIndex(value => new { value.ScopeId, value.PeriodStart }).IsUnique();
        head.ToTable("sli_report_head", "ops", table => table.HasCheckConstraint("CK_sli_report_head_generation", "generation >= 0"));
        var op = SliEntity<SliReportOperationEntity>(model, "sli_report_operation");
        op.HasKey(value => value.Id);
        op.HasAlternateKey(value => new { value.Id, value.HeadId });
        op.HasOne<SliReportHeadEntity>().WithMany().HasForeignKey(value => value.HeadId).OnDelete(DeleteBehavior.Restrict);
        op.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.ActorId).OnDelete(DeleteBehavior.Restrict);
        op.Property(value => value.ScopeJson).HasColumnType("jsonb");
        op.Property(value => value.Reason).HasMaxLength(500);
        op.Property(value => value.Status).HasMaxLength(16);
        op.Property(value => value.FailureKind).HasMaxLength(32);
        op.ToTable("sli_report_operation", "ops", table => table.HasCheckConstraint("CK_sli_report_operation_status",
            "status IN ('Pending','Failed','Published') AND expected_generation >= 0 AND (status <> 'Published' OR version_id IS NOT NULL)"));
        var receipt = SliEntity<SliReportReceiptEntity>(model, "sli_report_receipt");
        receipt.HasKey(value => value.Id);
        receipt.HasIndex(value => new { value.Kind, value.IdempotencyKey }).IsUnique();
        receipt.HasOne<SliReportOperationEntity>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        receipt.Property(value => value.Kind).HasMaxLength(16);
        receipt.Property(value => value.CommandSha256).HasMaxLength(64);
        var version = SliEntity<SliReportVersionEntity>(model, "sli_report_version");
        version.HasKey(value => value.Id);
        version.HasAlternateKey(value => new { value.Id, value.HeadId });
        version.HasIndex(value => new { value.HeadId, value.Revision }).IsUnique();
        version.HasIndex(value => value.OperationId).IsUnique();
        version.HasOne<SliReportOperationEntity>().WithMany().HasForeignKey(value => new { value.OperationId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        version.HasOne<SliReportVersionEntity>().WithMany().HasForeignKey(value => new { value.PredecessorId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        version.Property(value => value.ScopeJson).HasColumnType("jsonb");
        version.Property(value => value.MetadataJson).HasColumnType("jsonb");
        version.Property(value => value.SourceDigest).HasMaxLength(64);
        version.Property(value => value.ArtifactSha256).HasMaxLength(64);
        version.Property(value => value.PolicyVersion).HasMaxLength(32);
        version.ToTable("sli_report_version", "ops", table => table.HasCheckConstraint("CK_sli_report_version_bounds",
            "revision > 0 AND source_count BETWEEN 0 AND 100000 AND octet_length(artifact) BETWEEN 1 AND 33554432"));
        var source = SliEntity<SliReportSourceEntity>(model, "sli_report_source");
        source.HasKey(value => new { value.VersionId, value.ManifestId });
        source.HasOne<SliReportVersionEntity>().WithMany().HasForeignKey(value => value.VersionId).OnDelete(DeleteBehavior.Restrict);
        source.HasOne<SliManifestEntity>().WithMany().HasForeignKey(value => value.ManifestId).OnDelete(DeleteBehavior.Restrict);
        source.Property(value => value.SourceJson).HasColumnType("jsonb");
        var attempt = SliEntity<SliReportAttemptEntity>(model, "sli_report_attempt");
        attempt.HasKey(value => value.Id);
        attempt.HasIndex(value => new { value.OperationId, value.AttemptNumber }).IsUnique();
        attempt.HasOne<SliReportOperationEntity>().WithMany().HasForeignKey(value => value.OperationId).OnDelete(DeleteBehavior.Restrict);
        attempt.Property(value => value.Outcome).HasMaxLength(16);
        attempt.Property(value => value.FailureKind).HasMaxLength(32);
    }
}
