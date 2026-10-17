using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletCreditAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_adjustment",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_adjustment", x => x.id);
                    table.UniqueConstraint("AK_wallet_adjustment_id_organization_id_requested_by_account_id", x => new { x.id, x.organization_id, x.requested_by_account_id });
                    table.CheckConstraint("CK_wallet_adjustment_positive", "amount_micro_usd > 0");
                    table.CheckConstraint("CK_wallet_adjustment_text", "length(btrim(adjustment_key)) BETWEEN 8 AND 120 AND length(btrim(reason)) BETWEEN 8 AND 500");
                    table.ForeignKey(
                        name: "FK_wallet_adjustment_user_requested_by_account_id",
                        column: x => x.requested_by_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wallet_adjustment_wallet_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "billing",
                        principalTable: "wallet",
                        principalColumn: "organization_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet_adjustment_approval",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_adjustment_approval", x => x.id);
                    table.CheckConstraint("CK_wallet_adjustment_approval_reason", "length(btrim(reason)) BETWEEN 8 AND 500");
                    table.CheckConstraint("CK_wallet_adjustment_two_person", "requested_by_account_id <> approved_by_account_id");
                    table.ForeignKey(
                        name: "FK_wallet_adjustment_approval_user_approved_by_account_id",
                        column: x => x.approved_by_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wallet_adjustment_approval_wallet_adjustment_request_id_org~",
                        columns: x => new { x.request_id, x.organization_id, x.requested_by_account_id },
                        principalSchema: "billing",
                        principalTable: "wallet_adjustment",
                        principalColumns: new[] { "id", "organization_id", "requested_by_account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_organization_id_adjustment_key",
                schema: "billing",
                table: "wallet_adjustment",
                columns: new[] { "organization_id", "adjustment_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_organization_id_created_at",
                schema: "billing",
                table: "wallet_adjustment",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_requested_by_account_id",
                schema: "billing",
                table: "wallet_adjustment",
                column: "requested_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_approval_approved_by_account_id",
                schema: "billing",
                table: "wallet_adjustment_approval",
                column: "approved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_approval_request_id",
                schema: "billing",
                table: "wallet_adjustment_approval",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_adjustment_approval_request_id_organization_id_reque~",
                schema: "billing",
                table: "wallet_adjustment_approval",
                columns: new[] { "request_id", "organization_id", "requested_by_account_id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER prevent_wallet_adjustment_mutation
                BEFORE UPDATE OR DELETE ON billing.wallet_adjustment
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_financial_history_mutation();
                CREATE TRIGGER prevent_wallet_adjustment_approval_mutation
                BEFORE UPDATE OR DELETE ON billing.wallet_adjustment_approval
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_financial_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM billing.wallet_adjustment LIMIT 1)
                        OR EXISTS (SELECT 1 FROM billing.wallet_adjustment_approval LIMIT 1) THEN
                        RAISE EXCEPTION 'refuse to discard wallet adjustment history';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "wallet_adjustment_approval",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "wallet_adjustment",
                schema: "billing");
        }
    }
}
