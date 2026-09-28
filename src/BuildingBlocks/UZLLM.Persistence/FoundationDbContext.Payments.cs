using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentQuoteEntity>(entity =>
        {
            entity.ToTable("fx_quote", "payment", table =>
            {
                table.HasCheckConstraint("CK_payment_quote_amount", "amount_tiyin > 0 AND fee_tiyin >= 0 AND fee_tiyin < amount_tiyin AND credit_micro_usd > 0");
                table.HasCheckConstraint("CK_payment_quote_expiry", "expires_at > created_at");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(value => value.AmountTiyin).HasColumnName("amount_tiyin");
            entity.Property(value => value.FeeTiyin).HasColumnName("fee_tiyin");
            entity.Property(value => value.FxSnapshotId).HasColumnName("fx_snapshot_id");
            entity.Property(value => value.UzsTiyinPerUsd).HasColumnName("uzs_tiyin_per_usd").HasPrecision(20, 8);
            entity.Property(value => value.CreditMicroUsd).HasColumnName("credit_micro_usd");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.ExpiresAt).HasColumnName("expires_at");
            entity.HasIndex(value => new { value.Id, value.OrganizationId }).IsUnique();
            entity.HasIndex(value => new { value.OrganizationId, value.CreatedAt });
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingFxRateSnapshotEntity>().WithMany().HasForeignKey(value => value.FxSnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentIntentEntity>(entity =>
        {
            entity.ToTable("payment_intent", "payment", table =>
            {
                table.HasCheckConstraint("CK_payment_intent_amount", "amount_tiyin > 0 AND fee_tiyin >= 0 AND fee_tiyin < amount_tiyin AND credit_micro_usd > 0");
                table.HasCheckConstraint("CK_payment_intent_status", "status IN ('Pending', 'Created', 'Paid', 'Canceled', 'Expired')");
                table.HasCheckConstraint("CK_payment_intent_binding", "(external_transaction_id IS NULL AND bound_at IS NULL AND click_prepare_id IS NULL) OR (external_transaction_id IS NOT NULL AND bound_at IS NOT NULL)");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.QuoteId).HasColumnName("quote_id");
            entity.Property(value => value.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.AmountTiyin).HasColumnName("amount_tiyin");
            entity.Property(value => value.FeeTiyin).HasColumnName("fee_tiyin");
            entity.Property(value => value.FxSnapshotId).HasColumnName("fx_snapshot_id");
            entity.Property(value => value.UzsTiyinPerUsd).HasColumnName("uzs_tiyin_per_usd").HasPrecision(20, 8);
            entity.Property(value => value.CreditMicroUsd).HasColumnName("credit_micro_usd");
            entity.Property(value => value.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(value => value.MerchantScope).HasColumnName("merchant_scope").HasMaxLength(120);
            entity.Property(value => value.ExternalTransactionId).HasColumnName("external_transaction_id").HasMaxLength(120);
            entity.Property(value => value.ClickPrepareId).HasColumnName("click_prepare_id");
            entity.Property(value => value.ProviderCreatedTimeUnixMs).HasColumnName("provider_created_time_unix_ms");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.BoundAt).HasColumnName("bound_at");
            entity.Property(value => value.PaidAt).HasColumnName("paid_at");
            entity.Property(value => value.CanceledAt).HasColumnName("canceled_at");
            entity.Property(value => value.CancelReason).HasColumnName("cancel_reason");
            entity.HasIndex(value => value.QuoteId).IsUnique();
            entity.HasIndex(value => new { value.OrganizationId, value.IdempotencyKey }).IsUnique();
            entity.HasIndex(value => new { value.Provider, value.MerchantScope, value.ExternalTransactionId })
                .IsUnique().HasFilter("external_transaction_id IS NOT NULL");
            entity.HasIndex(value => value.ClickPrepareId).IsUnique().HasFilter("click_prepare_id IS NOT NULL");
            entity.HasIndex(value => new { value.OrganizationId, value.CreatedAt });
            entity.HasIndex(value => new { value.Status, value.BoundAt });
            entity.HasOne<PaymentQuoteEntity>().WithMany().HasForeignKey(value => new { value.QuoteId, value.OrganizationId })
                .HasPrincipalKey(value => new { value.Id, value.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingFxRateSnapshotEntity>().WithMany().HasForeignKey(value => value.FxSnapshotId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentCallbackReceiptEntity>(entity =>
        {
            entity.ToTable("callback_log", "payment", table => table.HasCheckConstraint(
                "CK_payment_callback_hash", "octet_length(request_hash) = 32"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.IntentId).HasColumnName("payment_intent_id");
            entity.Property(value => value.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(value => value.ExternalRequestId).HasColumnName("external_request_id").HasMaxLength(120);
            entity.Property(value => value.RequestHash).HasColumnName("request_hash");
            entity.Property(value => value.ResponseCode).HasColumnName("response_code").HasMaxLength(50);
            entity.Property(value => value.ReceivedAt).HasColumnName("received_at");
            entity.HasIndex(value => new { value.Provider, value.ReceivedAt });
            entity.HasOne<PaymentIntentEntity>().WithMany().HasForeignKey(value => value.IntentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentReconciliationCaseEntity>(entity =>
        {
            entity.ToTable("reconciliation_case", "payment", table => table.HasCheckConstraint(
                "CK_payment_reconciliation_status", "status IN ('Open', 'Resolved')"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.IntentId).HasColumnName("payment_intent_id");
            entity.Property(value => value.ObservationId).HasColumnName("provider_observation_id");
            entity.Property(value => value.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(value => value.MerchantScope).HasColumnName("merchant_scope").HasMaxLength(150);
            entity.Property(value => value.ExternalTransactionId).HasColumnName("external_transaction_id").HasMaxLength(120);
            entity.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(100);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(value => value.ResolvedByAccountId).HasColumnName("resolved_by_account_id");
            entity.Property(value => value.ResolutionReference).HasColumnName("resolution_reference").HasMaxLength(200);
            entity.HasIndex(value => new { value.IntentId, value.Reason }).IsUnique()
                .HasFilter("status = 'Open' AND payment_intent_id IS NOT NULL");
            entity.HasIndex(value => new { value.Provider, value.MerchantScope,
                value.ExternalTransactionId, value.Reason }).IsUnique()
                .HasFilter("status = 'Open' AND provider IS NOT NULL");
            entity.HasOne<PaymentIntentEntity>().WithMany().HasForeignKey(value => value.IntentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentProviderObservationEntity>().WithMany().HasForeignKey(value => value.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.ResolvedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentProviderObservationEntity>(entity =>
        {
            entity.ToTable("provider_observation", "payment", table =>
            {
                table.HasCheckConstraint("CK_payment_provider_observation_amount", "amount_tiyin > 0");
                table.HasCheckConstraint("CK_payment_provider_observation_status",
                    "status IN ('Created', 'Paid', 'Canceled', 'Reversed')");
                table.HasCheckConstraint("CK_payment_provider_observation_digest",
                    "source_sha256 ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.IntentId).HasColumnName("payment_intent_id");
            entity.Property(value => value.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(value => value.MerchantScope).HasColumnName("merchant_scope").HasMaxLength(150);
            entity.Property(value => value.SourceReference).HasColumnName("source_reference").HasMaxLength(120);
            entity.Property(value => value.SourceSha256).HasColumnName("source_sha256").HasMaxLength(64);
            entity.Property(value => value.RowReference).HasColumnName("row_reference").HasMaxLength(120);
            entity.Property(value => value.ExternalTransactionId).HasColumnName("external_transaction_id").HasMaxLength(120);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.AmountTiyin).HasColumnName("amount_tiyin");
            entity.Property(value => value.ProviderObservedAt).HasColumnName("provider_observed_at");
            entity.Property(value => value.RecordedAt).HasColumnName("recorded_at");
            entity.Property(value => value.RecordedByAccountId).HasColumnName("recorded_by_account_id");
            entity.HasIndex(value => new { value.Provider, value.MerchantScope,
                value.SourceReference, value.RowReference }).IsUnique();
            entity.HasIndex(value => new { value.Provider, value.MerchantScope, value.ExternalTransactionId });
            entity.HasOne<PaymentIntentEntity>().WithMany().HasForeignKey(value => value.IntentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityAccountEntity>().WithMany().HasForeignKey(value => value.RecordedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
