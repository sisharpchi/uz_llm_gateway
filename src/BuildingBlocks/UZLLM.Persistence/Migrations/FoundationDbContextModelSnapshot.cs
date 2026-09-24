using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace UZLLM.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
public sealed class FoundationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.3");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
#pragma warning restore 612, 618
    }
}
