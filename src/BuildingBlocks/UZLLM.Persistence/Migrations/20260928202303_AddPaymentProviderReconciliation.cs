using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviderReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reconciliation_case_payment_intent_id_reason",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_intent_id",
                schema: "payment",
                table: "reconciliation_case",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "external_transaction_id",
                schema: "payment",
                table: "reconciliation_case",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "merchant_scope",
                schema: "payment",
                table: "reconciliation_case",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                schema: "payment",
                table: "reconciliation_case",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "provider_observation_id",
                schema: "payment",
                table: "reconciliation_case",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_reference",
                schema: "payment",
                table: "reconciliation_case",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provider_observation",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merchant_scope = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    row_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_transaction_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_tiyin = table.Column<long>(type: "bigint", nullable: false),
                    provider_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_observation", x => x.id);
                    table.CheckConstraint("CK_payment_provider_observation_amount", "amount_tiyin > 0");
                    table.CheckConstraint("CK_payment_provider_observation_status", "status IN ('Created', 'Paid', 'Canceled', 'Reversed')");
                    table.CheckConstraint("CK_payment_provider_observation_digest", "source_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_provider_observation_payment_intent_payment_intent_id",
                        column: x => x.payment_intent_id,
                        principalSchema: "payment",
                        principalTable: "payment_intent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_observation_user_recorded_by_account_id",
                        column: x => x.recorded_by_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_provider_merchant_scope_external_transa~",
                schema: "payment",
                table: "reconciliation_case",
                columns: new[] { "provider", "merchant_scope", "external_transaction_id", "reason" },
                unique: true,
                filter: "status = 'Open' AND provider IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_payment_intent_id_reason",
                schema: "payment",
                table: "reconciliation_case",
                columns: new[] { "payment_intent_id", "reason" },
                unique: true,
                filter: "status = 'Open' AND payment_intent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_provider_observation_id",
                schema: "payment",
                table: "reconciliation_case",
                column: "provider_observation_id");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case",
                column: "resolved_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_observation_payment_intent_id",
                schema: "payment",
                table: "provider_observation",
                column: "payment_intent_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_observation_provider_merchant_scope_external_trans~",
                schema: "payment",
                table: "provider_observation",
                columns: new[] { "provider", "merchant_scope", "external_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_observation_provider_merchant_scope_source_referen~",
                schema: "payment",
                table: "provider_observation",
                columns: new[] { "provider", "merchant_scope", "source_reference", "row_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_observation_recorded_by_account_id",
                schema: "payment",
                table: "provider_observation",
                column: "recorded_by_account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_reconciliation_case_provider_observation_provider_observati~",
                schema: "payment",
                table: "reconciliation_case",
                column: "provider_observation_id",
                principalSchema: "payment",
                principalTable: "provider_observation",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reconciliation_case_user_resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case",
                column: "resolved_by_account_id",
                principalSchema: "iam",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE TRIGGER prevent_provider_observation_mutation
                BEFORE UPDATE OR DELETE ON payment.provider_observation
                FOR EACH ROW EXECUTE FUNCTION payment.prevent_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reconciliation_case_payment_intent_id_reason",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropForeignKey(
                name: "FK_reconciliation_case_provider_observation_provider_observati~",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropForeignKey(
                name: "FK_reconciliation_case_user_resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropTable(
                name: "provider_observation",
                schema: "payment");

            migrationBuilder.DropIndex(
                name: "IX_reconciliation_case_provider_merchant_scope_external_transa~",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropIndex(
                name: "IX_reconciliation_case_provider_observation_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropIndex(
                name: "IX_reconciliation_case_resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "external_transaction_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "merchant_scope",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "provider",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "provider_observation_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "resolution_reference",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.DropColumn(
                name: "resolved_by_account_id",
                schema: "payment",
                table: "reconciliation_case");

            migrationBuilder.AlterColumn<Guid>(
                name: "payment_intent_id",
                schema: "payment",
                table: "reconciliation_case",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_payment_intent_id_reason",
                schema: "payment",
                table: "reconciliation_case",
                columns: new[] { "payment_intent_id", "reason" },
                unique: true);
        }
    }
}
