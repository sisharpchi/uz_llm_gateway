using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyRoutePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "allowed_models",
                schema: "gateway",
                table: "api_key",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "allowed_providers",
                schema: "gateway",
                table: "api_key",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "denied_models",
                schema: "gateway",
                table: "api_key",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "denied_providers",
                schema: "gateway",
                table: "api_key",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM gateway.api_key WHERE allowed_models IS NOT NULL
                        OR allowed_providers IS NOT NULL OR cardinality(denied_models) > 0
                        OR cardinality(denied_providers) > 0) THEN
                        RAISE EXCEPTION 'API key permissions cannot be downgraded while restrictions exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "allowed_models",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "allowed_providers",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "denied_models",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "denied_providers",
                schema: "gateway",
                table: "api_key");
        }
    }
}
