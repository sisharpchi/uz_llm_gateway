using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.CreateTable(
                name: "fx_quote",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_tiyin = table.Column<long>(type: "bigint", nullable: false),
                    fee_tiyin = table.Column<long>(type: "bigint", nullable: false),
                    fx_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uzs_tiyin_per_usd = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    credit_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fx_quote", x => x.id);
                    table.UniqueConstraint("AK_fx_quote_id_organization_id", x => new { x.id, x.organization_id });
                    table.CheckConstraint("CK_payment_quote_amount", "amount_tiyin > 0 AND fee_tiyin >= 0 AND fee_tiyin < amount_tiyin AND credit_micro_usd > 0");
                    table.CheckConstraint("CK_payment_quote_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_fx_quote_fx_rate_snapshot_fx_snapshot_id",
                        column: x => x.fx_snapshot_id,
                        principalSchema: "billing",
                        principalTable: "fx_rate_snapshot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fx_quote_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_intent",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_tiyin = table.Column<long>(type: "bigint", nullable: false),
                    fee_tiyin = table.Column<long>(type: "bigint", nullable: false),
                    fx_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uzs_tiyin_per_usd = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    credit_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    merchant_scope = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_transaction_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    click_prepare_id = table.Column<int>(type: "integer", nullable: true),
                    provider_created_time_unix_ms = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_intent", x => x.id);
                    table.CheckConstraint("CK_payment_intent_amount", "amount_tiyin > 0 AND fee_tiyin >= 0 AND fee_tiyin < amount_tiyin AND credit_micro_usd > 0");
                    table.CheckConstraint("CK_payment_intent_binding", "(external_transaction_id IS NULL AND bound_at IS NULL AND click_prepare_id IS NULL) OR (external_transaction_id IS NOT NULL AND bound_at IS NOT NULL)");
                    table.CheckConstraint("CK_payment_intent_status", "status IN ('Pending', 'Created', 'Paid', 'Canceled', 'Expired')");
                    table.ForeignKey(
                        name: "FK_payment_intent_fx_quote_quote_id_organization_id",
                        columns: x => new { x.quote_id, x.organization_id },
                        principalSchema: "payment",
                        principalTable: "fx_quote",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_intent_fx_rate_snapshot_fx_snapshot_id",
                        column: x => x.fx_snapshot_id,
                        principalSchema: "billing",
                        principalTable: "fx_rate_snapshot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_intent_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "callback_log",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_request_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    response_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_callback_log", x => x.id);
                    table.CheckConstraint("CK_payment_callback_hash", "octet_length(request_hash) = 32");
                    table.ForeignKey(
                        name: "FK_callback_log_payment_intent_payment_intent_id",
                        column: x => x.payment_intent_id,
                        principalSchema: "payment",
                        principalTable: "payment_intent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_case",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_case", x => x.id);
                    table.CheckConstraint("CK_payment_reconciliation_status", "status IN ('Open', 'Resolved')");
                    table.ForeignKey(
                        name: "FK_reconciliation_case_payment_intent_payment_intent_id",
                        column: x => x.payment_intent_id,
                        principalSchema: "payment",
                        principalTable: "payment_intent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_callback_log_payment_intent_id",
                schema: "payment",
                table: "callback_log",
                column: "payment_intent_id");

            migrationBuilder.CreateIndex(
                name: "IX_callback_log_provider_received_at",
                schema: "payment",
                table: "callback_log",
                columns: new[] { "provider", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_fx_quote_fx_snapshot_id",
                schema: "payment",
                table: "fx_quote",
                column: "fx_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "IX_fx_quote_id_organization_id",
                schema: "payment",
                table: "fx_quote",
                columns: new[] { "id", "organization_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fx_quote_organization_id_created_at",
                schema: "payment",
                table: "fx_quote",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_click_prepare_id",
                schema: "payment",
                table: "payment_intent",
                column: "click_prepare_id",
                unique: true,
                filter: "click_prepare_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_fx_snapshot_id",
                schema: "payment",
                table: "payment_intent",
                column: "fx_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_organization_id_created_at",
                schema: "payment",
                table: "payment_intent",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_organization_id_idempotency_key",
                schema: "payment",
                table: "payment_intent",
                columns: new[] { "organization_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_provider_merchant_scope_external_transaction~",
                schema: "payment",
                table: "payment_intent",
                columns: new[] { "provider", "merchant_scope", "external_transaction_id" },
                unique: true,
                filter: "external_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_quote_id",
                schema: "payment",
                table: "payment_intent",
                column: "quote_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_quote_id_organization_id",
                schema: "payment",
                table: "payment_intent",
                columns: new[] { "quote_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_intent_status_bound_at",
                schema: "payment",
                table: "payment_intent",
                columns: new[] { "status", "bound_at" });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_case_payment_intent_id_reason",
                schema: "payment",
                table: "reconciliation_case",
                columns: new[] { "payment_intent_id", "reason" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE SEQUENCE payment.click_prepare_id_seq AS integer START WITH 1;
                GRANT USAGE, SELECT ON SEQUENCE payment.click_prepare_id_seq TO uzllm_runtime;

                CREATE FUNCTION payment.prevent_history_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'payment quote and callback history are append-only';
                END;
                $$;
                CREATE TRIGGER prevent_quote_mutation
                BEFORE UPDATE OR DELETE ON payment.fx_quote
                FOR EACH ROW EXECUTE FUNCTION payment.prevent_history_mutation();
                CREATE TRIGGER prevent_callback_log_mutation
                BEFORE UPDATE OR DELETE ON payment.callback_log
                FOR EACH ROW EXECUTE FUNCTION payment.prevent_history_mutation();

                CREATE FUNCTION payment.enforce_quote_snapshot()
                RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE quote_row payment.fx_quote%ROWTYPE;
                BEGIN
                    SELECT * INTO quote_row FROM payment.fx_quote
                    WHERE id = NEW.quote_id AND organization_id = NEW.organization_id;
                    IF NOT FOUND OR quote_row.provider <> NEW.provider
                        OR quote_row.amount_tiyin <> NEW.amount_tiyin
                        OR quote_row.fee_tiyin <> NEW.fee_tiyin
                        OR quote_row.fx_snapshot_id <> NEW.fx_snapshot_id
                        OR quote_row.uzs_tiyin_per_usd <> NEW.uzs_tiyin_per_usd
                        OR quote_row.credit_micro_usd <> NEW.credit_micro_usd THEN
                        RAISE EXCEPTION 'payment intent must preserve its immutable quote';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER enforce_quote_snapshot
                BEFORE INSERT OR UPDATE ON payment.payment_intent
                FOR EACH ROW EXECUTE FUNCTION payment.enforce_quote_snapshot();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS enforce_quote_snapshot ON payment.payment_intent;
                DROP TRIGGER IF EXISTS prevent_callback_log_mutation ON payment.callback_log;
                DROP TRIGGER IF EXISTS prevent_quote_mutation ON payment.fx_quote;
                DROP FUNCTION IF EXISTS payment.enforce_quote_snapshot();
                DROP FUNCTION IF EXISTS payment.prevent_history_mutation();
                DROP SEQUENCE IF EXISTS payment.click_prepare_id_seq;
                """);
            migrationBuilder.DropTable(
                name: "callback_log",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "reconciliation_case",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_intent",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "fx_quote",
                schema: "payment");
        }
    }
}
