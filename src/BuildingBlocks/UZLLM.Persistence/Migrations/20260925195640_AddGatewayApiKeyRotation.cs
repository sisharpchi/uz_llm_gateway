using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayApiKeyRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "generation",
                schema: "gateway",
                table: "api_key",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "api_key_generation",
                schema: "gateway",
                columns: table => new
                {
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_key_generation", x => new { x.api_key_id, x.generation });
                    table.CheckConstraint("CK_api_key_generation_number", "generation >= 1");
                    table.ForeignKey(
                        name: "FK_api_key_generation_api_key_api_key_id",
                        column: x => x.api_key_id,
                        principalSchema: "gateway",
                        principalTable: "api_key",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_api_key_generation",
                schema: "gateway",
                table: "api_key",
                sql: "generation >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_api_key_generation_key_prefix",
                schema: "gateway",
                table: "api_key_generation",
                column: "key_prefix",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO gateway.api_key_generation
                    (api_key_id, generation, key_prefix, activated_at)
                SELECT id, 1, key_prefix, created_at FROM gateway.api_key;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM gateway.api_key WHERE generation > 1) THEN
                    RAISE EXCEPTION 'Rotated API key history cannot be downgraded';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "api_key_generation",
                schema: "gateway");

            migrationBuilder.DropCheckConstraint(
                name: "CK_api_key_generation",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "generation",
                schema: "gateway",
                table: "api_key");
        }
    }
}
