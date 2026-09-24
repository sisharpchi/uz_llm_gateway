using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureFinancialCompletion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingReservationEntity>(entity =>
        {
            entity.ToTable("reservation", "billing", table =>
            {
                table.HasCheckConstraint("CK_reservation_amount", "amount_micro_usd > 0 AND (captured_micro_usd IS NULL OR captured_micro_usd BETWEEN 0 AND amount_micro_usd)");
                table.HasCheckConstraint("CK_reservation_state", "(status = 'Reserved' AND captured_micro_usd IS NULL AND finalized_at IS NULL) OR (status IN ('Settled', 'Released') AND captured_micro_usd IS NOT NULL AND finalized_at IS NOT NULL)");
                table.HasCheckConstraint("CK_reservation_expiry", "expires_at > created_at");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.RequestId).HasColumnName("request_id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.ProjectId).HasColumnName("project_id");
            entity.Property(value => value.ApiKeyId).HasColumnName("api_key_id");
            entity.Property(value => value.FeePolicyVersionId).HasColumnName("fee_policy_version_id");
            entity.Property(value => value.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.CapturedMicroUsd).HasColumnName("captured_micro_usd");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.ExpiresAt).HasColumnName("expires_at");
            entity.Property(value => value.FinalizedAt).HasColumnName("finalized_at");
            entity.HasIndex(value => value.RequestId).IsUnique();
            entity.HasIndex(value => new { value.Status, value.ExpiresAt });
            entity.HasOne<UsageRequestEntity>().WithMany().HasForeignKey(value => new { value.RequestId, value.OrganizationId, value.ApiKeyId })
                .HasPrincipalKey(value => new { value.Id, value.OrganizationId, value.ApiKeyId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(value => new { value.OrganizationId, value.ProjectId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingFeePolicyVersionEntity>().WithMany().HasForeignKey(value => value.FeePolicyVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingBudgetPolicyEntity>(entity =>
        {
            entity.ToTable("budget_policy", "billing", table => table.HasCheckConstraint("CK_budget_policy_limit", "limit_micro_usd >= 0"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.ProjectId).HasColumnName("project_id");
            entity.Property(value => value.ApiKeyId).HasColumnName("api_key_id");
            entity.Property(value => value.LimitMicroUsd).HasColumnName("limit_micro_usd");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(value => value.ProjectId).IsUnique().HasFilter("api_key_id IS NULL");
            entity.HasIndex(value => value.ApiKeyId).IsUnique().HasFilter("api_key_id IS NOT NULL");
            entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(value => new { value.OrganizationId, value.ProjectId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GatewayApiKeyEntity>().WithMany().HasForeignKey(value => new { value.ProjectId, value.ApiKeyId })
                .HasPrincipalKey(value => new { value.ProjectId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingBudgetBucketEntity>(entity =>
        {
            entity.ToTable("budget_bucket", "billing", table => table.HasCheckConstraint("CK_budget_bucket_non_negative", "captured_micro_usd >= 0 AND reserved_micro_usd >= 0"));
            entity.HasKey(value => value.PolicyId);
            entity.Property(value => value.PolicyId).HasColumnName("policy_id");
            entity.Property(value => value.CapturedMicroUsd).HasColumnName("captured_micro_usd");
            entity.Property(value => value.ReservedMicroUsd).HasColumnName("reserved_micro_usd");
            entity.HasOne<BillingBudgetPolicyEntity>().WithOne().HasForeignKey<BillingBudgetBucketEntity>(value => value.PolicyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingReservationBudgetEntity>(entity =>
        {
            entity.ToTable("reservation_budget", "billing", table => table.HasCheckConstraint("CK_reservation_budget_positive", "amount_micro_usd > 0"));
            entity.HasKey(value => new { value.ReservationId, value.PolicyId });
            entity.Property(value => value.ReservationId).HasColumnName("reservation_id");
            entity.Property(value => value.PolicyId).HasColumnName("policy_id");
            entity.Property(value => value.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.HasOne<BillingReservationEntity>().WithMany().HasForeignKey(value => value.ReservationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingBudgetPolicyEntity>().WithMany().HasForeignKey(value => value.PolicyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingSettlementEntity>(entity =>
        {
            entity.ToTable("settlement", "billing", table => table.HasCheckConstraint("CK_settlement_amounts",
                "provider_cost_micro_usd >= 0 AND uncapped_customer_charge_micro_usd >= 0 AND charged_micro_usd >= 0 AND uncollected_charge_micro_usd >= 0 AND platform_exposure_micro_usd >= 0 AND outcome IN ('Settled', 'Released')"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.ReservationId).HasColumnName("reservation_id");
            entity.Property(value => value.RequestId).HasColumnName("request_id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.ProviderCostMicroUsd).HasColumnName("provider_cost_micro_usd");
            entity.Property(value => value.UncappedCustomerChargeMicroUsd).HasColumnName("uncapped_customer_charge_micro_usd");
            entity.Property(value => value.ChargedMicroUsd).HasColumnName("charged_micro_usd");
            entity.Property(value => value.UncollectedChargeMicroUsd).HasColumnName("uncollected_charge_micro_usd");
            entity.Property(value => value.PlatformExposureMicroUsd).HasColumnName("platform_exposure_micro_usd");
            entity.Property(value => value.UnresolvedUsage).HasColumnName("unresolved_usage");
            entity.Property(value => value.Outcome).HasColumnName("outcome").HasMaxLength(20);
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => value.ReservationId).IsUnique();
            entity.HasIndex(value => value.RequestId).IsUnique();
            entity.HasOne<BillingReservationEntity>().WithMany().HasForeignKey(value => value.ReservationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UsageRequestEntity>().WithMany().HasForeignKey(value => value.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingSettlementEvidenceEntity>(entity =>
        {
            entity.ToTable("settlement_evidence", "billing");
            entity.HasKey(value => new { value.SettlementId, value.EvidenceId });
            entity.Property(value => value.SettlementId).HasColumnName("settlement_id");
            entity.Property(value => value.EvidenceId).HasColumnName("evidence_id");
            entity.HasIndex(value => value.EvidenceId).IsUnique();
            entity.HasOne<BillingSettlementEntity>().WithMany().HasForeignKey(value => value.SettlementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UsageEvidenceEntity>().WithMany().HasForeignKey(value => value.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingRecoveryDebtEntity>(entity =>
        {
            entity.ToTable("recovery_debt", "billing", table => table.HasCheckConstraint("CK_recovery_debt_state",
                "outstanding_micro_usd >= 0 AND (spending_held = (outstanding_micro_usd > 0))"));
            entity.HasKey(value => value.OrganizationId);
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.OutstandingMicroUsd).HasColumnName("outstanding_micro_usd");
            entity.Property(value => value.SpendingHeld).HasColumnName("spending_held");
            entity.Property(value => value.Version).HasColumnName("version");
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingDebtEntryEntity>(entity =>
        {
            entity.ToTable("debt_entry", "billing", table => table.HasCheckConstraint("CK_debt_entry_direction",
                "(type = 'Incurred' AND amount_micro_usd > 0) OR (type = 'Recovered' AND amount_micro_usd < 0)"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Type).HasColumnName("type").HasMaxLength(20);
            entity.Property(value => value.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.Property(value => value.ReferenceId).HasColumnName("reference_id");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.OrganizationId, value.Type, value.ReferenceId }).IsUnique();
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingReversalEntity>(entity =>
        {
            entity.ToTable("reversal", "billing", table => table.HasCheckConstraint("CK_reversal_amounts",
                "amount_micro_usd > 0 AND recovered_micro_usd >= 0 AND debt_created_micro_usd >= 0 AND recovered_micro_usd + debt_created_micro_usd = amount_micro_usd"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.ExternalReferenceId).HasColumnName("external_reference_id");
            entity.Property(value => value.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.Property(value => value.RecoveredMicroUsd).HasColumnName("recovered_micro_usd");
            entity.Property(value => value.DebtCreatedMicroUsd).HasColumnName("debt_created_micro_usd");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.OrganizationId, value.ExternalReferenceId }).IsUnique();
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingPlatformExposureEntity>(entity =>
        {
            entity.ToTable("platform_exposure", "billing", table => table.HasCheckConstraint(
                "CK_platform_exposure_non_negative", "provider_cost_micro_usd >= 0"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.SettlementId).HasColumnName("settlement_id");
            entity.Property(value => value.EvidenceId).HasColumnName("evidence_id");
            entity.Property(value => value.ProviderCostMicroUsd).HasColumnName("provider_cost_micro_usd");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => value.EvidenceId).IsUnique();
            entity.HasOne<BillingSettlementEntity>().WithMany().HasForeignKey(value => value.SettlementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UsageEvidenceEntity>().WithMany().HasForeignKey(value => value.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
