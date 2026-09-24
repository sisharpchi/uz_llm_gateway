using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "fee_policy_version",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    markup_basis_points = table.Column<int>(type: "integer", nullable: false),
                    fixed_fee_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_policy_version", x => x.id);
                    table.CheckConstraint("CK_fee_policy_version_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("CK_fee_policy_version_values", "markup_basis_points BETWEEN 0 AND 100000 AND fixed_fee_micro_usd >= 0");
                });

            migrationBuilder.CreateTable(
                name: "fx_rate_snapshot",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    uzs_tiyin_per_usd = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fx_rate_snapshot", x => x.id);
                    table.CheckConstraint("CK_fx_rate_snapshot_positive", "uzs_tiyin_per_usd > 0");
                });

            migrationBuilder.CreateTable(
                name: "ledger_entry",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entry", x => x.id);
                    table.CheckConstraint("CK_ledger_entry_direction", "(amount_micro_usd > 0 AND type IN ('TopUp', 'Refund', 'AdjustmentCredit', 'PromotionalCredit')) OR (amount_micro_usd < 0 AND type IN ('UsageCharge', 'AdjustmentDebit'))");
                    table.ForeignKey(
                        name: "FK_ledger_entry_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet",
                schema: "billing",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_balance_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    reserved_balance_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet", x => x.organization_id);
                    table.CheckConstraint("CK_wallet_non_negative", "posted_balance_micro_usd >= reserved_balance_micro_usd AND reserved_balance_micro_usd >= 0");
                    table.ForeignKey(
                        name: "FK_wallet_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_policy_version_policy_code_effective_from",
                schema: "billing",
                table: "fee_policy_version",
                columns: new[] { "policy_code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fx_rate_snapshot_source_observed_at",
                schema: "billing",
                table: "fx_rate_snapshot",
                columns: new[] { "source", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_organization_id_occurred_at",
                schema: "billing",
                table: "ledger_entry",
                columns: new[] { "organization_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_organization_id_type_reference_type_reference_~",
                schema: "billing",
                table: "ledger_entry",
                columns: new[] { "organization_id", "type", "reference_type", "reference_id" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO billing.wallet (organization_id, posted_balance_micro_usd, reserved_balance_micro_usd, version)
                SELECT id, 0, 0, 0
                FROM org.organization
                ON CONFLICT (organization_id) DO NOTHING;

                CREATE FUNCTION billing.create_wallet_for_organization()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    INSERT INTO billing.wallet (organization_id, posted_balance_micro_usd, reserved_balance_micro_usd, version)
                    VALUES (NEW.id, 0, 0, 0);
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER create_wallet_for_organization
                AFTER INSERT ON org.organization
                FOR EACH ROW
                EXECUTE FUNCTION billing.create_wallet_for_organization();

                CREATE FUNCTION billing.prevent_financial_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'billing financial history is append-only';
                END;
                $$;

                CREATE TRIGGER prevent_ledger_entry_mutation
                BEFORE UPDATE OR DELETE ON billing.ledger_entry
                FOR EACH ROW
                EXECUTE FUNCTION billing.prevent_financial_history_mutation();

                CREATE TRIGGER prevent_fee_policy_version_mutation
                BEFORE UPDATE OR DELETE ON billing.fee_policy_version
                FOR EACH ROW
                EXECUTE FUNCTION billing.prevent_financial_history_mutation();

                CREATE TRIGGER prevent_fx_rate_snapshot_mutation
                BEFORE UPDATE OR DELETE ON billing.fx_rate_snapshot
                FOR EACH ROW
                EXECUTE FUNCTION billing.prevent_financial_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS create_wallet_for_organization ON org.organization;
                DROP TRIGGER IF EXISTS prevent_ledger_entry_mutation ON billing.ledger_entry;
                DROP TRIGGER IF EXISTS prevent_fee_policy_version_mutation ON billing.fee_policy_version;
                DROP TRIGGER IF EXISTS prevent_fx_rate_snapshot_mutation ON billing.fx_rate_snapshot;
                DROP FUNCTION IF EXISTS billing.create_wallet_for_organization();
                DROP FUNCTION IF EXISTS billing.prevent_financial_history_mutation();
                """);

            migrationBuilder.DropTable(
                name: "fee_policy_version",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fx_rate_snapshot",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "ledger_entry",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "wallet",
                schema: "billing");
        }
    }
}
