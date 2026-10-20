using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureSli(ModelBuilder modelBuilder)
    {
        var manifest = SliEntity<SliManifestEntity>(modelBuilder, "sli_manifest");
        manifest.HasKey(value => value.Id);
        manifest.HasAlternateKey(value => new { value.Id, value.Producer, value.Epoch });
        manifest.HasIndex(value => new { value.Producer, value.Epoch, value.BatchNumber }).IsUnique();
        manifest.HasIndex(value => new { value.Producer, value.Epoch, value.FirstSequence, value.LastSequence });
        manifest.HasIndex(value => new { value.Source, value.Region, value.IntervalStart });
        manifest.HasIndex(value => new { value.CoverageWarnings, value.ReceivedAt });
        manifest.Property(value => value.Producer).HasMaxLength(64);
        manifest.Property(value => value.Region).HasMaxLength(64);
        manifest.Property(value => value.Source).HasMaxLength(16);
        manifest.Property(value => value.Sha256).HasMaxLength(64);
        manifest.Property(value => value.TerminalCoverage).HasMaxLength(16);
        manifest.ToTable("sli_manifest", "ops", table => table.HasCheckConstraint("CK_sli_manifest_bounds",
            "batch_number > 0 AND first_sequence > 0 AND last_sequence >= first_sequence - 1 AND observation_count BETWEEN 0 AND 1024 AND dropped >= 0 AND last_sequence::numeric - first_sequence + 1 = observation_count::numeric + dropped AND interval_end >= interval_start AND interval_end - interval_start <= interval '1 hour' AND sha256 ~ '^[a-f0-9]{64}$' AND source IN ('Gateway','Edge','Probe') AND terminal_coverage IN ('Partial','Unknown','NotApplicable')"));

        var observation = SliEntity<SliObservationEntity>(modelBuilder, "sli_observation");
        observation.HasKey(value => new { value.Producer, value.Epoch, value.Sequence });
        observation.HasIndex(value => new { value.Producer, value.Epoch, value.EventKey }).IsUnique();
        observation.HasIndex(value => new { value.ExecutionId, value.At });
        observation.HasIndex(value => new { value.Operation, value.StartedAt });
        observation.HasIndex(value => value.At);
        observation.Property(value => value.Producer).HasMaxLength(64);
        observation.Property(value => value.EventKey).HasMaxLength(100);
        observation.Property(value => value.Operation).HasMaxLength(16);
        observation.Property(value => value.PayloadJson).HasColumnType("jsonb");
        observation.HasOne<SliManifestEntity>().WithMany()
            .HasForeignKey(value => new { value.ManifestId, value.Producer, value.Epoch })
            .HasPrincipalKey(value => new { value.Id, value.Producer, value.Epoch }).OnDelete(DeleteBehavior.Restrict);
        observation.ToTable("sli_observation", "ops", table => table.HasCheckConstraint("CK_sli_observation_bounds",
            "sequence > 0 AND at >= started_at AND octet_length(payload_json::text) <= 2048"));

        var receipt = SliEntity<SliImportReceiptEntity>(modelBuilder, "sli_import_receipt");
        receipt.HasKey(value => value.Id);
        receipt.HasIndex(value => new { value.Producer, value.Epoch, value.BatchNumber, value.ReceivedAt });
        receipt.Property(value => value.Producer).HasMaxLength(64);
        receipt.Property(value => value.Sha256).HasMaxLength(64);
        receipt.Property(value => value.Status).HasMaxLength(16);
        receipt.HasOne<SliManifestEntity>().WithMany().HasForeignKey(value => value.ManifestId).OnDelete(DeleteBehavior.Restrict);
        receipt.ToTable("sli_import_receipt", "ops", table => table.HasCheckConstraint("CK_sli_receipt_status",
            "status IN ('Accepted','Duplicate','Conflict','Expired') AND observation_count BETWEEN 0 AND 1024 AND sha256 ~ '^[a-f0-9]{64}$'"));
    }

    private static EntityTypeBuilder<T> SliEntity<T>(ModelBuilder builder, string table) where T : class
    {
        var entity = builder.Entity<T>();
        entity.ToTable(table, "ops");
        foreach (var property in typeof(T).GetProperties())
            entity.Property(property.Name).HasColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
        return entity;
    }
}
