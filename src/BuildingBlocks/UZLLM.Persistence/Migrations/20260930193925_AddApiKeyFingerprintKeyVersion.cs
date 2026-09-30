using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyFingerprintKeyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fingerprint_key_version",
                schema: "gateway",
                table: "api_key",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_api_key_fingerprint_key_version",
                schema: "gateway",
                table: "api_key",
                sql: "fingerprint_key_version >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM gateway.api_key WHERE fingerprint_key_version > 1) THEN
                    RAISE EXCEPTION 'API keys using rotated fingerprint keys cannot be downgraded';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_api_key_fingerprint_key_version",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "fingerprint_key_version",
                schema: "gateway",
                table: "api_key");
        }
    }
}
