using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLatePlatformExposure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_exposure",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_cost_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_exposure", x => x.id);
                    table.CheckConstraint("CK_platform_exposure_non_negative", "provider_cost_micro_usd >= 0");
                    table.ForeignKey(
                        name: "FK_platform_exposure_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalSchema: "usage",
                        principalTable: "evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_platform_exposure_settlement_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "billing",
                        principalTable: "settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platform_exposure_evidence_id",
                schema: "billing",
                table: "platform_exposure",
                column: "evidence_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_exposure_settlement_id",
                schema: "billing",
                table: "platform_exposure",
                column: "settlement_id");

            migrationBuilder.Sql("""
                CREATE TRIGGER prevent_platform_exposure_mutation
                BEFORE UPDATE OR DELETE ON billing.platform_exposure
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_platform_exposure_mutation ON billing.platform_exposure;");
            migrationBuilder.DropTable(
                name: "platform_exposure",
                schema: "billing");
        }
    }
}
