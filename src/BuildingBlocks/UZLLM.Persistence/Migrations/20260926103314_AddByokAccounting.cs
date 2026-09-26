using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddByokAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_settlement_amounts",
                schema: "billing",
                table: "settlement");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservation_budget_positive",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservation_amount",
                schema: "billing",
                table: "reservation");

            migrationBuilder.AddColumn<long>(
                name: "external_provider_spend_micro_usd",
                schema: "billing",
                table: "settlement",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "allow_managed_fallback",
                schema: "billing",
                table: "reservation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "byok_credential_id",
                schema: "billing",
                table: "reservation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "maximum_external_spend_micro_usd",
                schema: "billing",
                table: "reservation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "allowed_models_json",
                schema: "gateway",
                table: "provider_credential",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "external_reserved_micro_usd",
                schema: "gateway",
                table: "provider_credential",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "external_spent_micro_usd",
                schema: "gateway",
                table: "provider_credential",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "spend_limit_micro_usd",
                schema: "gateway",
                table: "provider_credential",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "credential_id",
                schema: "usage",
                table: "attempt",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_spend_adjustment",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_cost_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_spend_adjustment", x => x.id);
                    table.CheckConstraint("CK_external_spend_adjustment_non_negative", "provider_cost_micro_usd >= 0");
                    table.ForeignKey(
                        name: "FK_external_spend_adjustment_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalSchema: "usage",
                        principalTable: "evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_spend_adjustment_provider_credential_credential_id",
                        column: x => x.credential_id,
                        principalSchema: "gateway",
                        principalTable: "provider_credential",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_spend_adjustment_settlement_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "billing",
                        principalTable: "settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_settlement_amounts",
                schema: "billing",
                table: "settlement",
                sql: "provider_cost_micro_usd >= 0 AND uncapped_customer_charge_micro_usd >= 0 AND charged_micro_usd >= 0 AND uncollected_charge_micro_usd >= 0 AND platform_exposure_micro_usd >= 0 AND external_provider_spend_micro_usd >= 0 AND outcome IN ('Settled', 'Released')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservation_budget_positive",
                schema: "billing",
                table: "reservation_budget",
                sql: "amount_micro_usd >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_byok_credential_id",
                schema: "billing",
                table: "reservation",
                column: "byok_credential_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation",
                column: "byok_fee_policy_version_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservation_amount",
                schema: "billing",
                table: "reservation",
                sql: "amount_micro_usd >= 0 AND (captured_micro_usd IS NULL OR captured_micro_usd BETWEEN 0 AND amount_micro_usd)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservation_byok",
                schema: "billing",
                table: "reservation",
                sql: "maximum_external_spend_micro_usd >= 0 AND ((byok_credential_id IS NULL AND byok_fee_policy_version_id IS NULL AND maximum_external_spend_micro_usd = 0 AND NOT allow_managed_fallback) OR (byok_credential_id IS NOT NULL AND byok_fee_policy_version_id IS NOT NULL AND maximum_external_spend_micro_usd > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_provider_credential_restrictions_scope",
                schema: "gateway",
                table: "provider_credential",
                sql: "credential_type = 'BYOK' OR (allowed_models_json IS NULL AND spend_limit_micro_usd IS NULL AND external_spent_micro_usd = 0 AND external_reserved_micro_usd = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_provider_credential_spend",
                schema: "gateway",
                table: "provider_credential",
                sql: "external_spent_micro_usd >= 0 AND external_reserved_micro_usd >= 0 AND (spend_limit_micro_usd IS NULL OR spend_limit_micro_usd >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_attempt_credential_id",
                schema: "usage",
                table: "attempt",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_spend_adjustment_credential_id",
                schema: "billing",
                table: "external_spend_adjustment",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_spend_adjustment_evidence_id",
                schema: "billing",
                table: "external_spend_adjustment",
                column: "evidence_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_spend_adjustment_settlement_id",
                schema: "billing",
                table: "external_spend_adjustment",
                column: "settlement_id");

            migrationBuilder.AddForeignKey(
                name: "FK_attempt_provider_credential_credential_id",
                schema: "usage",
                table: "attempt",
                column: "credential_id",
                principalSchema: "gateway",
                principalTable: "provider_credential",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_fee_policy_version_byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation",
                column: "byok_fee_policy_version_id",
                principalSchema: "billing",
                principalTable: "fee_policy_version",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_provider_credential_byok_credential_id",
                schema: "billing",
                table: "reservation",
                column: "byok_credential_id",
                principalSchema: "gateway",
                principalTable: "provider_credential",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE billing.reservation
                ADD CONSTRAINT fk_byok_reservation_tenant_credential
                FOREIGN KEY (organization_id, byok_credential_id)
                REFERENCES gateway.provider_credential (organization_id, id)
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER prevent_external_spend_adjustment_mutation
                BEFORE UPDATE OR DELETE ON billing.external_spend_adjustment
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM billing.reservation WHERE byok_credential_id IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM gateway.provider_credential
                         WHERE allowed_models_json IS NOT NULL OR spend_limit_micro_usd IS NOT NULL
                            OR external_spent_micro_usd <> 0 OR external_reserved_micro_usd <> 0) THEN
                    RAISE EXCEPTION 'Cannot downgrade populated BYOK accounting without reconciliation and export';
                  END IF;
                END $$;
                """);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_external_spend_adjustment_mutation ON billing.external_spend_adjustment;");
            migrationBuilder.Sql("ALTER TABLE billing.reservation DROP CONSTRAINT IF EXISTS fk_byok_reservation_tenant_credential;");

            migrationBuilder.DropForeignKey(
                name: "FK_attempt_provider_credential_credential_id",
                schema: "usage",
                table: "attempt");

            migrationBuilder.DropForeignKey(
                name: "FK_reservation_fee_policy_version_byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropForeignKey(
                name: "FK_reservation_provider_credential_byok_credential_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropTable(
                name: "external_spend_adjustment",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_settlement_amounts",
                schema: "billing",
                table: "settlement");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservation_budget_positive",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropIndex(
                name: "IX_reservation_byok_credential_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropIndex(
                name: "IX_reservation_byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservation_amount",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reservation_byok",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_provider_credential_restrictions_scope",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropCheckConstraint(
                name: "CK_provider_credential_spend",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropIndex(
                name: "IX_attempt_credential_id",
                schema: "usage",
                table: "attempt");

            migrationBuilder.DropColumn(
                name: "external_provider_spend_micro_usd",
                schema: "billing",
                table: "settlement");

            migrationBuilder.DropColumn(
                name: "allow_managed_fallback",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "byok_credential_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "byok_fee_policy_version_id",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "maximum_external_spend_micro_usd",
                schema: "billing",
                table: "reservation");

            migrationBuilder.DropColumn(
                name: "allowed_models_json",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "external_reserved_micro_usd",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "external_spent_micro_usd",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "spend_limit_micro_usd",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "credential_id",
                schema: "usage",
                table: "attempt");

            migrationBuilder.AddCheckConstraint(
                name: "CK_settlement_amounts",
                schema: "billing",
                table: "settlement",
                sql: "provider_cost_micro_usd >= 0 AND uncapped_customer_charge_micro_usd >= 0 AND charged_micro_usd >= 0 AND uncollected_charge_micro_usd >= 0 AND platform_exposure_micro_usd >= 0 AND outcome IN ('Settled', 'Released')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservation_budget_positive",
                schema: "billing",
                table: "reservation_budget",
                sql: "amount_micro_usd > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reservation_amount",
                schema: "billing",
                table: "reservation",
                sql: "amount_micro_usd > 0 AND (captured_micro_usd IS NULL OR captured_micro_usd BETWEEN 0 AND amount_micro_usd)");
        }
    }
}
