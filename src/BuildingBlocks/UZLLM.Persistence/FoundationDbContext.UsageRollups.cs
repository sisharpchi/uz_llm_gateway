using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureUsageRollups(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsageRollupQueueEntity>(entity =>
        {
            entity.ToTable("rollup_queue", "usage");
            entity.HasKey(value => value.RequestId);
            entity.Property(value => value.RequestId).HasColumnName("request_id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Hour).HasColumnName("hour");
            entity.Property(value => value.EnqueuedAt).HasColumnName("enqueued_at");
            entity.HasOne<UsageRequestEntity>().WithMany().HasForeignKey(value => value.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.EnqueuedAt, value.RequestId });
            entity.HasIndex(value => new { value.OrganizationId, value.Hour });
        });
        modelBuilder.Entity<UsageRollupBucketEntity>(entity =>
        {
            entity.ToTable("rollup_bucket", "usage");
            entity.HasKey(value => new { value.OrganizationId, value.Hour });
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Hour).HasColumnName("hour");
            entity.Property(value => value.DataAsOf).HasColumnName("data_as_of");
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<UsageHourlyRollupEntity>(entity =>
        {
            entity.ToTable("hourly_rollup", "usage", table => table.HasCheckConstraint("CK_rollup_counts",
                "request_count > 0 AND completed_count >= 0 AND error_count >= 0 AND pending_count >= 0 " +
                "AND input_tokens >= 0 AND output_tokens >= 0 AND charged_micro_usd >= 0 " +
                "AND refunded_micro_usd >= 0 AND refunded_micro_usd <= charged_micro_usd"));
            entity.HasKey(value => new { value.OrganizationId, value.Hour, value.ProjectId,
                value.ApiKeyId, value.ModelId, value.ProviderId, value.ExecutionState, value.IsStream });
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Hour).HasColumnName("hour");
            entity.Property(value => value.ProjectId).HasColumnName("project_id");
            entity.Property(value => value.ApiKeyId).HasColumnName("api_key_id");
            entity.Property(value => value.ModelId).HasColumnName("model_id");
            entity.Property(value => value.ProviderId).HasColumnName("provider_id");
            entity.Property(value => value.ExecutionState).HasColumnName("execution_state").HasMaxLength(30);
            entity.Property(value => value.IsStream).HasColumnName("is_stream");
            entity.Property(value => value.RequestCount).HasColumnName("request_count");
            entity.Property(value => value.CompletedCount).HasColumnName("completed_count");
            entity.Property(value => value.ErrorCount).HasColumnName("error_count");
            entity.Property(value => value.PendingCount).HasColumnName("pending_count");
            entity.Property(value => value.InputTokens).HasColumnName("input_tokens");
            entity.Property(value => value.OutputTokens).HasColumnName("output_tokens");
            entity.Property(value => value.ChargedMicroUsd).HasColumnName("charged_micro_usd");
            entity.Property(value => value.RefundedMicroUsd).HasColumnName("refunded_micro_usd");
            entity.HasOne<UsageRollupBucketEntity>().WithMany()
                .HasForeignKey(value => new { value.OrganizationId, value.Hour }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
