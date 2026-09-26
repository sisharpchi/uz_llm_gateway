using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigureProviderCredentials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderCredentialEntity>(entity =>
        {
            entity.ToTable("provider_credential", "gateway", table =>
            {
                table.HasCheckConstraint("CK_provider_credential_type_scope",
                    "(credential_type = 'Platform' AND organization_id IS NULL) OR (credential_type = 'BYOK' AND organization_id IS NOT NULL)");
                table.HasCheckConstraint("CK_provider_credential_status", "status IN ('Active', 'Disabled')");
                table.HasCheckConstraint("CK_provider_credential_ciphertext",
                    "octet_length(encrypted_secret) > 28 AND octet_length(wrapped_data_key) = 60");
                table.HasCheckConstraint("CK_provider_credential_byok_fields",
                    "(credential_type = 'Platform' AND name IS NULL AND masked_key IS NULL) OR (credential_type = 'BYOK' AND name IS NOT NULL AND masked_key IS NOT NULL)");
                table.HasCheckConstraint("CK_provider_credential_test_status",
                    "last_test_status IS NULL OR last_test_status IN ('Valid', 'Invalid', 'Unavailable')");
                table.HasCheckConstraint("CK_provider_credential_spend",
                    "external_spent_micro_usd >= 0 AND external_reserved_micro_usd >= 0 AND (spend_limit_micro_usd IS NULL OR spend_limit_micro_usd >= 0)");
                table.HasCheckConstraint("CK_provider_credential_restrictions_scope",
                    "credential_type = 'BYOK' OR (allowed_models_json IS NULL AND spend_limit_micro_usd IS NULL AND external_spent_micro_usd = 0 AND external_reserved_micro_usd = 0)");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.ProviderId).HasColumnName("provider_id");
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.CredentialType).HasColumnName("credential_type").HasMaxLength(20);
            entity.Property(value => value.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(value => value.EncryptedSecret).HasColumnName("encrypted_secret");
            entity.Property(value => value.WrappedDataKey).HasColumnName("wrapped_data_key");
            entity.Property(value => value.KeyVersion).HasColumnName("kms_key_version").HasMaxLength(100);
            entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(value => value.MaskedKey).HasColumnName("masked_key").HasMaxLength(20);
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.Property(value => value.UpdatedAt).HasColumnName("updated_at");
            entity.Property(value => value.DeletedAt).HasColumnName("deleted_at");
            entity.Property(value => value.LastTestedAt).HasColumnName("last_tested_at");
            entity.Property(value => value.LastTestStatus).HasColumnName("last_test_status").HasMaxLength(20);
            entity.Property(value => value.AllowedModelsJson).HasColumnName("allowed_models_json").HasColumnType("jsonb");
            entity.Property(value => value.SpendLimitMicroUsd).HasColumnName("spend_limit_micro_usd");
            entity.Property(value => value.ExternalSpentMicroUsd).HasColumnName("external_spent_micro_usd");
            entity.Property(value => value.ExternalReservedMicroUsd).HasColumnName("external_reserved_micro_usd");
            entity.HasIndex(value => new { value.ProviderId, value.CredentialType, value.Status });
            entity.HasIndex(value => new { value.OrganizationId, value.Id }).IsUnique();
            entity.HasIndex(value => new { value.OrganizationId, value.CreatedAt });
            entity.HasOne<CatalogProviderEntity>().WithMany().HasForeignKey(value => value.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderCredentialProjectGrantEntity>(entity =>
        {
            entity.ToTable("provider_credential_project_grant", "gateway");
            entity.HasKey(value => new { value.OrganizationId, value.CredentialId, value.ProjectId });
            entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
            entity.Property(value => value.CredentialId).HasColumnName("credential_id");
            entity.Property(value => value.ProjectId).HasColumnName("project_id");
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasOne<ProviderCredentialEntity>().WithMany()
                .HasForeignKey(value => value.CredentialId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectEntity>().WithMany()
                .HasForeignKey(value => new { value.OrganizationId, value.ProjectId })
                .HasPrincipalKey(value => new { value.OrganizationId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.OrganizationId, value.ProjectId });
        });
    }
}
