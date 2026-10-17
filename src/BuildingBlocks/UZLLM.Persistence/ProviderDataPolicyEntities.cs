using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed class CatalogProviderDataPolicyEntity
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string Retention { get; set; } = null!;
    public string Training { get; set; } = null!;
    public string Residency { get; set; } = null!;
    public string[] Regions { get; set; } = [];
    public string Scope { get; set; } = null!;
    public string Conditions { get; set; } = null!;
    public string SourceUrl { get; set; } = null!;
    public DateTimeOffset SourceObservedAt { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid PublishedByAccountId { get; set; }
}

public sealed partial class FoundationDbContext
{
    internal static void ConfigureProviderDataPolicies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogProviderDataPolicyEntity>(entity =>
        {
            entity.ToTable("provider_data_policy", "catalog", table =>
            {
                table.HasCheckConstraint("CK_provider_data_policy_retention", "retention IN ('Unknown', 'MayRetain', 'NoRetentionReported')");
                table.HasCheckConstraint("CK_provider_data_policy_training", "training IN ('Unknown', 'MayTrain', 'NoTrainingReported')");
                table.HasCheckConstraint("CK_provider_data_policy_residency", "cardinality(regions) <= 16 AND ((residency = 'Unknown' AND cardinality(regions) = 0) OR (residency = 'ProviderReported' AND cardinality(regions) > 0))");
                table.HasCheckConstraint("CK_provider_data_policy_dates", "source_observed_at > '1970-01-01'::timestamptz AND source_observed_at <= published_at");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.ProviderId).HasColumnName("provider_id");
            entity.Property(value => value.Retention).HasColumnName("retention").HasMaxLength(30);
            entity.Property(value => value.Training).HasColumnName("training").HasMaxLength(30);
            entity.Property(value => value.Residency).HasColumnName("residency").HasMaxLength(30);
            entity.Property(value => value.Regions).HasColumnName("regions");
            entity.Property(value => value.Scope).HasColumnName("scope").HasMaxLength(500);
            entity.Property(value => value.Conditions).HasColumnName("conditions").HasMaxLength(2000);
            entity.Property(value => value.SourceUrl).HasColumnName("source_url").HasMaxLength(2048);
            entity.Property(value => value.SourceObservedAt).HasColumnName("source_observed_at");
            entity.Property(value => value.PublishedAt).HasColumnName("published_at");
            entity.Property(value => value.PublishedByAccountId).HasColumnName("published_by_account_id");
            entity.HasIndex(value => new { value.ProviderId, value.SourceObservedAt }).IsUnique();
            entity.HasIndex(value => value.PublishedByAccountId);
            entity.HasOne<CatalogProviderEntity>().WithMany().HasForeignKey(value => value.ProviderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.PublishedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
