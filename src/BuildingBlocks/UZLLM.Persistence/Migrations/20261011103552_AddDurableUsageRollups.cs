using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableUsageRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rollup_bucket",
                schema: "usage",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rollup_bucket", x => new { x.organization_id, x.hour });
                    table.ForeignKey(
                        name: "FK_rollup_bucket_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rollup_queue",
                schema: "usage",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rollup_queue", x => x.request_id);
                    table.ForeignKey(
                        name: "FK_rollup_queue_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hourly_rollup",
                schema: "usage",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_stream = table.Column<bool>(type: "boolean", nullable: false),
                    request_count = table.Column<long>(type: "bigint", nullable: false),
                    completed_count = table.Column<long>(type: "bigint", nullable: false),
                    error_count = table.Column<long>(type: "bigint", nullable: false),
                    pending_count = table.Column<long>(type: "bigint", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    charged_micro_usd = table.Column<long>(type: "bigint", nullable: false),
                    refunded_micro_usd = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hourly_rollup", x => new { x.organization_id, x.hour, x.project_id, x.api_key_id, x.model_id, x.provider_id, x.execution_state, x.is_stream });
                    table.CheckConstraint("CK_rollup_counts", "request_count > 0 AND completed_count >= 0 AND error_count >= 0 AND pending_count >= 0 AND input_tokens >= 0 AND output_tokens >= 0 AND charged_micro_usd >= 0 AND refunded_micro_usd >= 0 AND refunded_micro_usd <= charged_micro_usd");
                    table.ForeignKey(
                        name: "FK_hourly_rollup_rollup_bucket_organization_id_hour",
                        columns: x => new { x.organization_id, x.hour },
                        principalSchema: "usage",
                        principalTable: "rollup_bucket",
                        principalColumns: new[] { "organization_id", "hour" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rollup_queue_enqueued_at_request_id",
                schema: "usage",
                table: "rollup_queue",
                columns: new[] { "enqueued_at", "request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rollup_queue_organization_id_hour",
                schema: "usage",
                table: "rollup_queue",
                columns: new[] { "organization_id", "hour" });

            migrationBuilder.Sql("""
                CREATE VIEW usage.rollup_source AS
                SELECT r.id AS request_id, r.organization_id, r.project_id, r.api_key_id,
                    r.canonical_model_id AS model_id,
                    coalesce(route.provider_id, '00000000-0000-0000-0000-000000000000'::uuid) AS provider_id,
                    r.started_at, r.execution_state, r.is_stream,
                    (r.completed_at IS NOT NULL)::int::bigint AS completed_count,
                    (coalesce(r.http_status >= 400, false) OR r.execution_state IN
                        ('Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution'))::int::bigint AS error_count,
                    (r.financial_state NOT IN ('Settled', 'Released'))::int::bigint AS pending_count,
                    coalesce(e.input_tokens, 0)::bigint AS input_tokens,
                    coalesce(e.output_tokens, 0)::bigint AS output_tokens,
                    coalesce(s.charged_micro_usd, 0)::bigint AS charged_micro_usd,
                    coalesce(refund.amount, 0)::bigint AS refunded_micro_usd
                FROM usage.request r
                LEFT JOIN LATERAL (
                    SELECT pm.provider_id FROM usage.attempt a
                    JOIN catalog.provider_model pm ON pm.id = a.provider_model_id
                    WHERE a.request_id = r.id ORDER BY a.number DESC LIMIT 1
                ) route ON true
                LEFT JOIN LATERAL (
                    SELECT sum(input_tokens) AS input_tokens, sum(output_tokens) AS output_tokens
                    FROM usage.evidence WHERE request_id = r.id AND state = 'Verified'
                ) e ON true
                LEFT JOIN billing.settlement s ON s.request_id = r.id
                LEFT JOIN LATERAL (
                    SELECT sum(amount_micro_usd) AS amount FROM billing.settlement_refund
                    WHERE settlement_id = s.id
                ) refund ON true;

                CREATE FUNCTION usage.invalidate_rollup() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE request_identifier uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'request' THEN
                        request_identifier := NEW.id;
                    ELSIF TG_TABLE_NAME = 'settlement_refund' THEN
                        SELECT request_id INTO request_identifier FROM billing.settlement WHERE id = NEW.settlement_id;
                    ELSE
                        request_identifier := NEW.request_id;
                    END IF;
                    INSERT INTO usage.rollup_queue (request_id, organization_id, hour, enqueued_at)
                    SELECT id, organization_id, date_trunc('hour', started_at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                        clock_timestamp() FROM usage.request WHERE id = request_identifier
                    ON CONFLICT (request_id) DO UPDATE SET enqueued_at = usage.rollup_queue.enqueued_at;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER invalidate_request_rollup AFTER INSERT OR UPDATE OF
                    completed_at, execution_state, financial_state, http_status, is_stream ON usage.request
                    FOR EACH ROW EXECUTE FUNCTION usage.invalidate_rollup();
                CREATE TRIGGER invalidate_attempt_rollup AFTER INSERT OR UPDATE OF provider_model_id, number ON usage.attempt
                    FOR EACH ROW EXECUTE FUNCTION usage.invalidate_rollup();
                CREATE TRIGGER invalidate_evidence_rollup AFTER INSERT ON usage.evidence
                    FOR EACH ROW EXECUTE FUNCTION usage.invalidate_rollup();
                CREATE TRIGGER invalidate_settlement_rollup AFTER INSERT ON billing.settlement
                    FOR EACH ROW EXECUTE FUNCTION usage.invalidate_rollup();
                CREATE TRIGGER invalidate_refund_rollup AFTER INSERT ON billing.settlement_refund
                    FOR EACH ROW EXECUTE FUNCTION usage.invalidate_rollup();

                INSERT INTO usage.rollup_queue (request_id, organization_id, hour, enqueued_at)
                SELECT id, organization_id, date_trunc('hour', started_at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                    clock_timestamp() FROM usage.request;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER invalidate_request_rollup ON usage.request;
                DROP TRIGGER invalidate_attempt_rollup ON usage.attempt;
                DROP TRIGGER invalidate_evidence_rollup ON usage.evidence;
                DROP TRIGGER invalidate_settlement_rollup ON billing.settlement;
                DROP TRIGGER invalidate_refund_rollup ON billing.settlement_refund;
                DROP FUNCTION usage.invalidate_rollup();
                DROP VIEW usage.rollup_source;
                """);
            migrationBuilder.DropTable(
                name: "hourly_rollup",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "rollup_queue",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "rollup_bucket",
                schema: "usage");
        }
    }
}
