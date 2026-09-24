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
            entity.Property(value => value.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(value => new { value.ProviderId, value.CredentialType, value.Status });
            entity.HasOne<CatalogProviderEntity>().WithMany().HasForeignKey(value => value.ProviderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OrganizationEntity>().WithMany().HasForeignKey(value => value.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
