using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettlementWalletRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_settlement_id_organization_id",
                schema: "billing",
                table: "settlement",
                columns: new[] { "id", "organization_id" });

            migrationBuilder.CreateTable(
                name: "settlement_refund",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_refund", x => x.id);
                    table.CheckConstraint("CK_settlement_refund_positive", "amount_micro_usd > 0");
                    table.ForeignKey(
                        name: "FK_settlement_refund_settlement_settlement_id_organization_id",
                        columns: x => new { x.settlement_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "settlement",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlement_refund_user_actor_account_id",
                        column: x => x.actor_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_refund_actor_account_id",
                schema: "billing",
                table: "settlement_refund",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_refund_organization_id_refund_key",
                schema: "billing",
                table: "settlement_refund",
                columns: new[] { "organization_id", "refund_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_refund_settlement_id_created_at",
                schema: "billing",
                table: "settlement_refund",
                columns: new[] { "settlement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_refund_settlement_id_organization_id",
                schema: "billing",
                table: "settlement_refund",
                columns: new[] { "settlement_id", "organization_id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER prevent_settlement_refund_mutation
                BEFORE UPDATE OR DELETE ON billing.settlement_refund
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_financial_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM billing.settlement_refund LIMIT 1) THEN
                        RAISE EXCEPTION 'refuse to discard posted settlement refunds';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "settlement_refund",
                schema: "billing");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_settlement_id_organization_id",
                schema: "billing",
                table: "settlement");
        }
    }
}
