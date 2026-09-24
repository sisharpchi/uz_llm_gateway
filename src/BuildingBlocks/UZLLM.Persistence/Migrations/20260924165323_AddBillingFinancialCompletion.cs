using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingFinancialCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_policy",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    limit_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_policy", x => x.id);
                    table.CheckConstraint("CK_budget_policy_limit", "limit_micro_usd >= 0");
                    table.ForeignKey(
                        name: "FK_budget_policy_api_key_project_id_api_key_id",
                        columns: x => new { x.project_id, x.api_key_id },
                        principalSchema: "gateway",
                        principalTable: "api_key",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_budget_policy_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debt_entry",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_entry", x => x.id);
                    table.CheckConstraint("CK_debt_entry_direction", "(type = 'Incurred' AND amount_micro_usd > 0) OR (type = 'Recovered' AND amount_micro_usd < 0)");
                    table.ForeignKey(
                        name: "FK_debt_entry_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recovery_debt",
                schema: "billing",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outstanding_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    spending_held = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_debt", x => x.organization_id);
                    table.CheckConstraint("CK_recovery_debt_state", "outstanding_micro_usd >= 0 AND (spending_held = (outstanding_micro_usd > 0))");
                    table.ForeignKey(
                        name: "FK_recovery_debt_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_policy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    captured_micro_usd = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation", x => x.id);
                    table.CheckConstraint("CK_reservation_amount", "amount_micro_usd > 0 AND (captured_micro_usd IS NULL OR captured_micro_usd BETWEEN 0 AND amount_micro_usd)");
                    table.CheckConstraint("CK_reservation_expiry", "expires_at > created_at");
                    table.CheckConstraint("CK_reservation_state", "(status = 'Reserved' AND captured_micro_usd IS NULL AND finalized_at IS NULL) OR (status IN ('Settled', 'Released') AND captured_micro_usd IS NOT NULL AND finalized_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_reservation_fee_policy_version_fee_policy_version_id",
                        column: x => x.fee_policy_version_id,
                        principalSchema: "billing",
                        principalTable: "fee_policy_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_request_request_id_organization_id_api_key_id",
                        columns: x => new { x.request_id, x.organization_id, x.api_key_id },
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumns: new[] { "id", "organization_id", "api_key_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reversal",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    recovered_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    debt_created_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reversal", x => x.id);
                    table.CheckConstraint("CK_reversal_amounts", "amount_micro_usd > 0 AND recovered_micro_usd >= 0 AND debt_created_micro_usd >= 0 AND recovered_micro_usd + debt_created_micro_usd = amount_micro_usd");
                    table.ForeignKey(
                        name: "FK_reversal_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_bucket",
                schema: "billing",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reserved_micro_usd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_bucket", x => x.policy_id);
                    table.CheckConstraint("CK_budget_bucket_non_negative", "captured_micro_usd >= 0 AND reserved_micro_usd >= 0");
                    table.ForeignKey(
                        name: "FK_budget_bucket_budget_policy_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "billing",
                        principalTable: "budget_policy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_budget",
                schema: "billing",
                columns: table => new
                {
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_budget", x => new { x.reservation_id, x.policy_id });
                    table.CheckConstraint("CK_reservation_budget_positive", "amount_micro_usd > 0");
                    table.ForeignKey(
                        name: "FK_reservation_budget_budget_policy_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "billing",
                        principalTable: "budget_policy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reservation_budget_reservation_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "billing",
                        principalTable: "reservation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_cost_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    uncapped_customer_charge_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    charged_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    uncollected_charge_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    platform_exposure_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    unresolved_usage = table.Column<bool>(type: "boolean", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement", x => x.id);
                    table.CheckConstraint("CK_settlement_amounts", "provider_cost_micro_usd >= 0 AND uncapped_customer_charge_micro_usd >= 0 AND charged_micro_usd >= 0 AND uncollected_charge_micro_usd >= 0 AND platform_exposure_micro_usd >= 0 AND outcome IN ('Settled', 'Released')");
                    table.ForeignKey(
                        name: "FK_settlement_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlement_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlement_reservation_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "billing",
                        principalTable: "reservation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_evidence",
                schema: "billing",
                columns: table => new
                {
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_evidence", x => new { x.settlement_id, x.evidence_id });
                    table.ForeignKey(
                        name: "FK_settlement_evidence_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalSchema: "usage",
                        principalTable: "evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlement_evidence_settlement_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "billing",
                        principalTable: "settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_api_key_id",
                schema: "billing",
                table: "budget_policy",
                column: "api_key_id",
                unique: true,
                filter: "api_key_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_organization_id_project_id",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_project_id",
                schema: "billing",
                table: "budget_policy",
                column: "project_id",
                unique: true,
                filter: "api_key_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_project_id_api_key_id",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "project_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_entry_organization_id_type_reference_id",
                schema: "billing",
                table: "debt_entry",
                columns: new[] { "organization_id", "type", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_fee_policy_version_id",
                schema: "billing",
                table: "reservation",
                column: "fee_policy_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_organization_id_project_id",
                schema: "billing",
                table: "reservation",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_request_id",
                schema: "billing",
                table: "reservation",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_request_id_organization_id_api_key_id",
                schema: "billing",
                table: "reservation",
                columns: new[] { "request_id", "organization_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_status_expires_at",
                schema: "billing",
                table: "reservation",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_budget_policy_id",
                schema: "billing",
                table: "reservation_budget",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_reversal_organization_id_external_reference_id",
                schema: "billing",
                table: "reversal",
                columns: new[] { "organization_id", "external_reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_organization_id",
                schema: "billing",
                table: "settlement",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_request_id",
                schema: "billing",
                table: "settlement",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_reservation_id",
                schema: "billing",
                table: "settlement",
                column: "reservation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_evidence_evidence_id",
                schema: "billing",
                table: "settlement_evidence",
                column: "evidence_id",
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION billing.prevent_completion_history_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'billing completion history is append-only';
                END;
                $$;

                CREATE TRIGGER prevent_settlement_mutation BEFORE UPDATE OR DELETE ON billing.settlement
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                CREATE TRIGGER prevent_settlement_evidence_mutation BEFORE UPDATE OR DELETE ON billing.settlement_evidence
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                CREATE TRIGGER prevent_debt_entry_mutation BEFORE UPDATE OR DELETE ON billing.debt_entry
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                CREATE TRIGGER prevent_reversal_mutation BEFORE UPDATE OR DELETE ON billing.reversal
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_completion_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS prevent_settlement_mutation ON billing.settlement;
                DROP TRIGGER IF EXISTS prevent_settlement_evidence_mutation ON billing.settlement_evidence;
                DROP TRIGGER IF EXISTS prevent_debt_entry_mutation ON billing.debt_entry;
                DROP TRIGGER IF EXISTS prevent_reversal_mutation ON billing.reversal;
                DROP FUNCTION IF EXISTS billing.prevent_completion_history_mutation();
                """);
            migrationBuilder.DropTable(
                name: "budget_bucket",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "debt_entry",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "recovery_debt",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "reservation_budget",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "reversal",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "settlement_evidence",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "budget_policy",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "settlement",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "reservation",
                schema: "billing");
        }
    }
}
