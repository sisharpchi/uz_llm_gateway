using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureCustomerAlerts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationDestinationEntity>(entity =>
        {
            entity.ToTable("notification_destination", "ops", table => table.HasCheckConstraint(
                "CK_notification_destination_status", "status IN ('Verified', 'Disabled')"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Type).HasColumnName("type").HasMaxLength(20);
            entity.Property(value => value.EncryptedChatId).HasColumnName("encrypted_chat_id");
            entity.Property(value => value.KeyVersion).HasColumnName("key_version").HasMaxLength(40);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.VerifiedAt).HasColumnName("verified_at");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.OrganizationId, value.Type }).IsUnique();
            entity.HasIndex(value => new { value.OrganizationId, value.Id }).IsUnique();
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TelegramLinkChallengeEntity>(entity =>
        {
            entity.ToTable("telegram_link_challenge", "ops");
            entity.HasKey(value => value.TokenHash);
            entity.Property(value => value.TokenHash).HasColumnName("token_hash");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.AccountId).HasColumnName("account_id");
            entity.Property(value => value.ExpiresAt).HasColumnName("expires_at");
            entity.Property(value => value.ConsumedAt).HasColumnName("consumed_at");
            entity.HasIndex(value => value.ExpiresAt);
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerAlertRuleEntity>(entity =>
        {
            entity.ToTable("alert_rule", "ops", table =>
            {
                table.HasCheckConstraint("CK_alert_rule_threshold", "threshold >= 0");
                table.HasCheckConstraint("CK_alert_rule_type", "type IN ('LowBalance', 'BudgetWarning', 'ErrorSpike')");
                table.HasCheckConstraint("CK_alert_rule_scope", "(type = 'LowBalance' AND project_id IS NULL AND budget_policy_id IS NULL AND threshold <= 9007199254740991) OR (type = 'BudgetWarning' AND project_id IS NOT NULL AND budget_policy_id IS NOT NULL AND threshold BETWEEN 1 AND 10000) OR (type = 'ErrorSpike' AND budget_policy_id IS NULL AND threshold BETWEEN 1 AND 10000)");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.ProjectId).HasColumnName("project_id");
            entity.Property(value => value.BudgetPolicyId).HasColumnName("budget_policy_id");
            entity.Property(value => value.DestinationId).HasColumnName("destination_id");
            entity.Property(value => value.Type).HasColumnName("type").HasMaxLength(30);
            entity.Property(value => value.Threshold).HasColumnName("threshold");
            entity.Property(value => value.Enabled).HasColumnName("enabled");
            entity.Property(value => value.Armed).HasColumnName("armed");
            entity.Property(value => value.Episode).HasColumnName("episode");
            entity.Property(value => value.LastWindowStart).HasColumnName("last_window_start");
            entity.Property(value => value.LastTriggeredAt).HasColumnName("last_triggered_at");
            entity.Property(value => value.NextEvaluationAt).HasColumnName("next_evaluation_at");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.Enabled, value.NextEvaluationAt });
            entity.HasIndex(value => new { value.OrganizationId, value.Id }).IsUnique();
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(value => new { value.OrganizationId, value.ProjectId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingBudgetPolicyEntity>().WithMany()
                .HasForeignKey(value => new { value.OrganizationId, value.ProjectId, value.BudgetPolicyId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.ProjectId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<NotificationDestinationEntity>().WithMany()
                .HasForeignKey(value => new { value.OrganizationId, value.DestinationId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerAlertEventEntity>(entity =>
        {
            entity.ToTable("alert_event", "ops", table => table.HasCheckConstraint(
                "CK_alert_event_status", "status IN ('Pending', 'Delivered', 'Suppressed')"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.RuleId).HasColumnName("rule_id");
            entity.Property(value => value.Episode).HasColumnName("episode");
            entity.Property(value => value.ObservedValue).HasColumnName("observed_value");
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.TriggeredAt).HasColumnName("triggered_at");
            entity.Property(value => value.DeliveredAt).HasColumnName("delivered_at");
            entity.HasIndex(value => new { value.RuleId, value.Episode }).IsUnique();
            entity.HasOne<CustomerAlertRuleEntity>().WithMany().HasForeignKey(value => value.RuleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
