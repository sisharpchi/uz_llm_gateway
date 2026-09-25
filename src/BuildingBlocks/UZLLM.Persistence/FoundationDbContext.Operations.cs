using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext
{
    private static void ConfigurePlatformControls(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PlatformControlEntity>(entity =>
        {
            entity.ToTable("platform_control", "ops", table => table.HasCheckConstraint(
                "CK_platform_control_feature", "feature IN ('ManagedTraffic', 'TopUps')"));
            entity.HasKey(value => value.Feature);
            entity.Property(value => value.Feature).HasColumnName("feature").HasMaxLength(40);
            entity.Property(value => value.Enabled).HasColumnName("enabled");
            entity.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        });
}
