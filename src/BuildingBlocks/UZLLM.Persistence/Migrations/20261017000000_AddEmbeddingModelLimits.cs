using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UZLLM.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("20261017000000_AddEmbeddingModelLimits")]
public sealed class AddEmbeddingModelLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_catalog_model_limits", "model", "catalog");
        migrationBuilder.AddCheckConstraint("CK_catalog_model_limits", "model",
            "context_length > 0 AND max_output_tokens <= context_length AND (max_output_tokens > 0 OR (max_output_tokens = 0 AND capabilities_json = '[\"Embeddings\"]'::jsonb))", "catalog");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately fails if referenced embedding models remain; never rewrite their metadata/history.
        migrationBuilder.DropCheckConstraint("CK_catalog_model_limits", "model", "catalog");
        migrationBuilder.AddCheckConstraint("CK_catalog_model_limits", "model",
            "context_length > 0 AND max_output_tokens > 0 AND max_output_tokens <= context_length", "catalog");
    }
}
