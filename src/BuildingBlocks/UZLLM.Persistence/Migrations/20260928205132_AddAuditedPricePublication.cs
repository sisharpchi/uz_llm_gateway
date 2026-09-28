using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditedPricePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE billing.fee_policy_version
                ADD CONSTRAINT fee_policy_version_no_overlap
                EXCLUDE USING gist
                (policy_code WITH =,
                 tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamptz), '[)') WITH &&);

                CREATE FUNCTION billing.guard_fee_policy_window()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'fee policy versions cannot be deleted';
                    END IF;
                    IF (NEW.id, NEW.policy_code, NEW.markup_basis_points,
                        NEW.fixed_fee_micro_usd, NEW.effective_from, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.policy_code, OLD.markup_basis_points,
                        OLD.fixed_fee_micro_usd, OLD.effective_from, OLD.created_at)
                       OR NEW.effective_to IS NULL
                       OR NEW.effective_to <= OLD.effective_from
                       OR NEW.effective_to <= transaction_timestamp()
                       OR (OLD.effective_to IS NOT NULL AND NEW.effective_to >= OLD.effective_to)
                    THEN
                        RAISE EXCEPTION 'fee policy values and historical windows are immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                DROP TRIGGER prevent_fee_policy_version_mutation ON billing.fee_policy_version;
                CREATE TRIGGER guard_fee_policy_window
                BEFORE UPDATE OR DELETE ON billing.fee_policy_version
                FOR EACH ROW EXECUTE FUNCTION billing.guard_fee_policy_window();
                """);
            migrationBuilder.CreateIndex(
                name: "IX_fx_rate_snapshot_observed_at",
                schema: "billing",
                table: "fx_rate_snapshot",
                column: "observed_at",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER guard_fee_policy_window ON billing.fee_policy_version;
                DROP FUNCTION billing.guard_fee_policy_window();
                CREATE TRIGGER prevent_fee_policy_version_mutation
                BEFORE UPDATE OR DELETE ON billing.fee_policy_version
                FOR EACH ROW EXECUTE FUNCTION billing.prevent_financial_history_mutation();
                ALTER TABLE billing.fee_policy_version DROP CONSTRAINT fee_policy_version_no_overlap;
                """);
            migrationBuilder.DropIndex(
                name: "IX_fx_rate_snapshot_observed_at",
                schema: "billing",
                table: "fx_rate_snapshot");
        }
    }
}
