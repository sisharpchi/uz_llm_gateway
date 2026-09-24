using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UZLLM.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("202609240001_InitializeSchemas")]
public sealed class InitializeSchemas : Migration
{
    private static readonly string[] SchemaNames = ["iam", "org", "gateway", "catalog", "billing", "payment", "usage", "ops", "audit"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var schemaName in SchemaNames)
        {
            migrationBuilder.EnsureSchema(schemaName);
        }

        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'uzllm_runtime') THEN
                    RAISE EXCEPTION 'The uzllm_runtime role must exist before migrations run.';
                END IF;
            END $$;
            """);

        foreach (var schemaName in SchemaNames)
        {
            migrationBuilder.Sql($"GRANT USAGE ON SCHEMA {schemaName} TO uzllm_runtime;");
            migrationBuilder.Sql($"ALTER DEFAULT PRIVILEGES IN SCHEMA {schemaName} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO uzllm_runtime;");
            migrationBuilder.Sql($"ALTER DEFAULT PRIVILEGES IN SCHEMA {schemaName} GRANT USAGE, SELECT ON SEQUENCES TO uzllm_runtime;");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var schemaName in SchemaNames.Reverse())
        {
            migrationBuilder.Sql($"DROP SCHEMA IF EXISTS {schemaName} CASCADE;");
        }
    }
}
