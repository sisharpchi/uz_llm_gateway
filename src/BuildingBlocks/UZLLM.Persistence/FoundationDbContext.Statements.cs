using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureStatements(ModelBuilder modelBuilder)
    {
        var head = StatementEntity<BillingStatementHeadEntity>(modelBuilder, "statement_head");
        head.HasKey(value => value.Id);
        head.HasAlternateKey(value => new { value.Id, value.OrganizationId });
        head.HasIndex(value => new { value.OrganizationId, value.PeriodStart, value.Kind }).IsUnique();
        head.Property(value => value.Kind).HasMaxLength(32);
        head.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        head.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.LatestVersionId, value.OrganizationId, value.Id, value.Generation })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId, value.Revision }).OnDelete(DeleteBehavior.Restrict);
        head.HasOne<BillingStatementOperationEntity>().WithMany().HasForeignKey(value => new { value.ActiveOperationId, value.OrganizationId, value.Id })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        head.ToTable("statement_head", "billing", table =>
        {
            table.HasCheckConstraint("CK_statement_head_period", "kind = 'AccountStatement' AND period_start = (date_trunc('month', period_start AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')");
            table.HasCheckConstraint("CK_statement_head_generation", "generation >= 0 AND ((generation = 0 AND latest_version_id IS NULL) OR (generation > 0 AND latest_version_id IS NOT NULL))");
        });

        var operation = StatementEntity<BillingStatementOperationEntity>(modelBuilder, "statement_operation");
        operation.HasKey(value => value.Id);
        operation.HasAlternateKey(value => new { value.Id, value.OrganizationId });
        operation.HasAlternateKey(value => new { value.Id, value.OrganizationId, value.HeadId });
        operation.HasOne<BillingStatementHeadEntity>().WithMany().HasForeignKey(value => new { value.HeadId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.PredecessorId, value.OrganizationId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.PublishedVersionId, value.OrganizationId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        operation.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.RequestedByAccountId).OnDelete(DeleteBehavior.Restrict);
        operation.Property(value => value.Reason).HasMaxLength(500);
        operation.Property(value => value.Status).HasMaxLength(16);
        operation.Property(value => value.FailureKind).HasMaxLength(40);
        operation.HasIndex(value => new { value.OrganizationId, value.CreatedAt });
        operation.ToTable("statement_operation", "billing", table =>
        {
            table.HasCheckConstraint("CK_statement_operation_terms", "expected_generation >= 0 AND length(btrim(reason)) BETWEEN 8 AND 500 AND ((expected_generation = 0 AND predecessor_id IS NULL) OR (expected_generation > 0 AND predecessor_id IS NOT NULL))");
            table.HasCheckConstraint("CK_statement_operation_status", "(status = 'Pending' AND published_version_id IS NULL AND failure_kind IS NULL) OR (status = 'Failed' AND published_version_id IS NULL AND failure_kind IS NOT NULL) OR (status = 'Published' AND published_version_id IS NOT NULL AND failure_kind IS NULL)");
        });

        var receipt = StatementEntity<BillingStatementReceiptEntity>(modelBuilder, "statement_receipt");
        receipt.HasKey(value => value.Id);
        receipt.HasIndex(value => new { value.OrganizationId, value.OperationKind, value.IdempotencyKey }).IsUnique();
        receipt.HasIndex(value => value.OperationId).IsUnique();
        receipt.HasOne<BillingStatementOperationEntity>().WithMany().HasForeignKey(value => new { value.OperationId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        receipt.Property(value => value.CommandSha256).HasMaxLength(64);
        receipt.Property(value => value.OperationKind).HasMaxLength(16);
        receipt.ToTable("statement_receipt", "billing", table => table.HasCheckConstraint("CK_statement_receipt_terms",
            "schema_version = 1 AND operation_kind IN ('Build','Revision') AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND command_sha256 ~ '^[a-f0-9]{64}$'"));

        var version = StatementEntity<BillingStatementVersionEntity>(modelBuilder, "statement_version");
        version.HasKey(value => value.Id);
        version.HasAlternateKey(value => new { value.Id, value.OrganizationId });
        version.HasAlternateKey(value => new { value.Id, value.OrganizationId, value.HeadId });
        version.HasAlternateKey(value => new { value.Id, value.OrganizationId, value.HeadId, value.Revision });
        version.HasIndex(value => new { value.HeadId, value.Revision }).IsUnique();
        version.HasIndex(value => value.OperationId).IsUnique();
        version.HasOne<BillingStatementHeadEntity>().WithMany().HasForeignKey(value => new { value.HeadId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        version.HasOne<BillingStatementOperationEntity>().WithMany().HasForeignKey(value => new { value.OperationId, value.OrganizationId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        version.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.PredecessorId, value.OrganizationId, value.HeadId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.HeadId }).OnDelete(DeleteBehavior.Restrict);
        version.Property(value => value.CalculationVersion).HasMaxLength(64);
        foreach (var name in new[] { "SourceDigest", "JsonSha256", "CsvSha256" }) version.Property<string>(name).HasMaxLength(64);
        version.ToTable("statement_version", "billing", table =>
        {
            table.HasCheckConstraint("CK_statement_version_terms", "schema_version = 1 AND revision > 0 AND ((revision = 1 AND predecessor_id IS NULL) OR (revision > 1 AND predecessor_id IS NOT NULL)) AND source_digest ~ '^[a-f0-9]{64}$' AND json_sha256 ~ '^[a-f0-9]{64}$' AND csv_sha256 ~ '^[a-f0-9]{64}$'");
            table.HasCheckConstraint("CK_statement_version_bounds", "source_count BETWEEN 1 AND 100000 AND line_count BETWEEN 0 AND 100000 AND total_bytes BETWEEN 1 AND 33554432 AND octet_length(json_artifact) + octet_length(csv_artifact) + octet_length(snapshot_json) <= total_bytes");
        });

        var line = StatementEntity<BillingStatementLineEntity>(modelBuilder, "statement_line");
        line.HasKey(value => new { value.VersionId, value.Ordinal });
        line.HasIndex(value => new { value.VersionId, value.LedgerId }).IsUnique();
        line.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.VersionId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        line.ToTable("statement_line", "billing", table => table.HasCheckConstraint("CK_statement_line_ordinal", "ordinal BETWEEN 1 AND 100000"));

        var source = StatementEntity<BillingStatementSourceEntity>(modelBuilder, "statement_source");
        source.HasKey(value => new { value.VersionId, value.Kind, value.SourceId });
        source.Property(value => value.Kind).HasMaxLength(40);
        source.Property(value => value.PayloadSha256).HasMaxLength(64);
        source.HasOne<BillingStatementVersionEntity>().WithMany().HasForeignKey(value => new { value.VersionId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        source.ToTable("statement_source", "billing", table => table.HasCheckConstraint("CK_statement_source_payload",
            "payload_sha256 ~ '^[a-f0-9]{64}$' AND (payload_json::jsonb->>'OrganizationId') IS NOT NULL AND (payload_json::jsonb->>'OrganizationId')::uuid = organization_id"));

        var attempt = StatementEntity<BillingStatementAttemptEntity>(modelBuilder, "statement_attempt");
        attempt.HasKey(value => value.Id);
        attempt.HasIndex(value => new { value.OperationId, value.AttemptNumber }).IsUnique();
        attempt.Property(value => value.Outcome).HasMaxLength(16);
        attempt.Property(value => value.FailureKind).HasMaxLength(40);
        attempt.HasOne<BillingStatementOperationEntity>().WithMany().HasForeignKey(value => new { value.OperationId, value.OrganizationId })
            .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        attempt.ToTable("statement_attempt", "billing", table => table.HasCheckConstraint("CK_statement_attempt_outcome",
            "attempt_number BETWEEN 1 AND 5 AND ((outcome = 'Published' AND failure_kind IS NULL) OR (outcome = 'Failed' AND failure_kind IS NOT NULL))"));
    }

    private static EntityTypeBuilder<T> StatementEntity<T>(ModelBuilder modelBuilder, string table) where T : class
    {
        var entity = modelBuilder.Entity<T>();
        entity.ToTable(table, "billing");
        foreach (var property in entity.Metadata.GetProperties())
            property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
        return entity;
    }
}
