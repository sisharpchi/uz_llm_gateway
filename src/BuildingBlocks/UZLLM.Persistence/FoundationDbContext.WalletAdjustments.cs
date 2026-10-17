using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureWalletAdjustments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingWalletAdjustmentEntity>(entity =>
        {
            entity.ToTable("wallet_adjustment", "billing", table =>
            {
                table.HasCheckConstraint("CK_wallet_adjustment_positive", "amount_micro_usd > 0");
                table.HasCheckConstraint("CK_wallet_adjustment_text", "length(btrim(adjustment_key)) BETWEEN 8 AND 120 AND length(btrim(reason)) BETWEEN 8 AND 500");
            });
            entity.HasKey(value => value.Id);
            entity.HasAlternateKey(value => new { value.Id, value.OrganizationId, value.RequestedByAccountId });
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.RequestedByAccountId).HasColumnName("requested_by_account_id");
            entity.Property(value => value.AdjustmentKey).HasColumnName("adjustment_key").HasMaxLength(120);
            entity.Property(value => value.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.OrganizationId, value.AdjustmentKey }).IsUnique();
            entity.HasIndex(value => new { value.OrganizationId, value.CreatedAt });
            entity.HasIndex(value => value.RequestedByAccountId);
            entity.HasOne<BillingWalletEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.RequestedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BillingWalletAdjustmentApprovalEntity>(entity =>
        {
            entity.ToTable("wallet_adjustment_approval", "billing", table =>
            {
                table.HasCheckConstraint("CK_wallet_adjustment_two_person", "requested_by_account_id <> approved_by_account_id");
                table.HasCheckConstraint("CK_wallet_adjustment_approval_reason", "length(btrim(reason)) BETWEEN 8 AND 500");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.RequestId).HasColumnName("request_id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.RequestedByAccountId).HasColumnName("requested_by_account_id");
            entity.Property(value => value.ApprovedByAccountId).HasColumnName("approved_by_account_id");
            entity.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => value.RequestId).IsUnique();
            entity.HasIndex(value => value.ApprovedByAccountId);
            entity.HasOne<BillingWalletAdjustmentEntity>().WithMany()
                .HasForeignKey(value => new { value.RequestId, value.OrganizationId, value.RequestedByAccountId })
                .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.RequestedByAccountId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.ApprovedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
