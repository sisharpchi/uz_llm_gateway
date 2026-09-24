using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "usage");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_project_organization_id_id",
                schema: "gateway",
                table: "project",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_model_price_provider_model_id_id",
                schema: "catalog",
                table: "model_price",
                columns: new[] { "provider_model_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_api_key_project_id_id",
                schema: "gateway",
                table: "api_key",
                columns: new[] { "project_id", "id" });

            migrationBuilder.CreateTable(
                name: "request",
                schema: "usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    financial_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_stream = table.Column<bool>(type: "boolean", nullable: false),
                    operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    route_strategy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request", x => x.id);
                    table.UniqueConstraint("AK_request_id_organization_id_api_key_id", x => new { x.id, x.organization_id, x.api_key_id });
                    table.CheckConstraint("CK_usage_request_delivery", "delivery_state IN ('NotStarted', 'Partial', 'Completed', 'ClientDisconnected')");
                    table.CheckConstraint("CK_usage_request_execution", "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown')");
                    table.CheckConstraint("CK_usage_request_financial", "financial_state IN ('PendingAdmission', 'Reserved', 'PendingEvidence', 'PendingSettlement', 'Settled', 'Released')");
                    table.ForeignKey(
                        name: "FK_request_api_key_project_id_api_key_id",
                        columns: x => new { x.project_id, x.api_key_id },
                        principalSchema: "gateway",
                        principalTable: "api_key",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_request_model_canonical_model_id",
                        column: x => x.canonical_model_id,
                        principalSchema: "catalog",
                        principalTable: "model",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_request_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_request_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempt",
                schema: "usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    provider_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    execution_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attempt", x => x.id);
                    table.UniqueConstraint("AK_attempt_id_request_id", x => new { x.id, x.request_id });
                    table.CheckConstraint("CK_usage_attempt_execution", "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown')");
                    table.ForeignKey(
                        name: "FK_attempt_provider_model_provider_model_id",
                        column: x => x.provider_model_id,
                        principalSchema: "catalog",
                        principalTable: "provider_model",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attempt_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_claim",
                schema: "usage",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_claim", x => new { x.organization_id, x.api_key_id, x.operation, x.key_hash });
                    table.CheckConstraint("CK_usage_idempotency_hashes", "octet_length(key_hash) = 32 AND octet_length(payload_hash) = 32");
                    table.ForeignKey(
                        name: "FK_idempotency_claim_request_request_id_organization_id_api_ke~",
                        columns: x => new { x.request_id, x.organization_id, x.api_key_id },
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumns: new[] { "id", "organization_id", "api_key_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence",
                schema: "usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    cached_input_tokens = table.Column<int>(type: "integer", nullable: true),
                    reasoning_tokens = table.Column<int>(type: "integer", nullable: true),
                    price_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reconcile_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence", x => x.id);
                    table.CheckConstraint("CK_usage_evidence_source", "source IN ('Provider', 'Estimated', 'Reconciled', 'Unknown')");
                    table.CheckConstraint("CK_usage_evidence_state", "(state = 'Unknown' AND source = 'Unknown' AND input_tokens IS NULL AND output_tokens IS NULL AND price_version_id IS NULL AND reconcile_after IS NOT NULL) OR (state = 'Verified' AND source IN ('Provider', 'Estimated', 'Reconciled') AND input_tokens >= 0 AND output_tokens >= 0 AND cached_input_tokens >= 0 AND cached_input_tokens <= input_tokens AND (reasoning_tokens IS NULL OR (reasoning_tokens >= 0 AND reasoning_tokens <= output_tokens)) AND price_version_id IS NOT NULL AND reconcile_after IS NULL)");
                    table.ForeignKey(
                        name: "FK_evidence_attempt_attempt_id_request_id",
                        columns: x => new { x.attempt_id, x.request_id },
                        principalSchema: "usage",
                        principalTable: "attempt",
                        principalColumns: new[] { "id", "request_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_model_price_provider_model_id_price_version_id",
                        columns: x => new { x.provider_model_id, x.price_version_id },
                        principalSchema: "catalog",
                        principalTable: "model_price",
                        principalColumns: new[] { "provider_model_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_price_provider_model_id_id",
                schema: "catalog",
                table: "model_price",
                columns: new[] { "provider_model_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_id_request_id",
                schema: "usage",
                table: "attempt",
                columns: new[] { "id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attempt_provider_model_id",
                schema: "usage",
                table: "attempt",
                column: "provider_model_id");

            migrationBuilder.CreateIndex(
                name: "IX_attempt_request_id_number",
                schema: "usage",
                table: "attempt",
                columns: new[] { "request_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_attempt_id_request_id",
                schema: "usage",
                table: "evidence",
                columns: new[] { "attempt_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_attempt_id_state",
                schema: "usage",
                table: "evidence",
                columns: new[] { "attempt_id", "state" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_provider_model_id_price_version_id",
                schema: "usage",
                table: "evidence",
                columns: new[] { "provider_model_id", "price_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_request_id_captured_at",
                schema: "usage",
                table: "evidence",
                columns: new[] { "request_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_claim_expires_at",
                schema: "usage",
                table: "idempotency_claim",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_claim_request_id_organization_id_api_key_id",
                schema: "usage",
                table: "idempotency_claim",
                columns: new[] { "request_id", "organization_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "IX_request_api_key_id_started_at",
                schema: "usage",
                table: "request",
                columns: new[] { "api_key_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_request_canonical_model_id",
                schema: "usage",
                table: "request",
                column: "canonical_model_id");

            migrationBuilder.CreateIndex(
                name: "IX_request_id_organization_id_api_key_id",
                schema: "usage",
                table: "request",
                columns: new[] { "id", "organization_id", "api_key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_request_organization_id_project_id",
                schema: "usage",
                table: "request",
                columns: new[] { "organization_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_request_organization_id_started_at",
                schema: "usage",
                table: "request",
                columns: new[] { "organization_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_request_project_id_api_key_id",
                schema: "usage",
                table: "request",
                columns: new[] { "project_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "IX_request_project_id_started_at",
                schema: "usage",
                table: "request",
                columns: new[] { "project_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.Sql("""
                ALTER TABLE usage.attempt
                ADD CONSTRAINT uq_usage_attempt_evidence_identity UNIQUE (id, request_id, provider_model_id);

                ALTER TABLE usage.evidence
                ADD CONSTRAINT fk_usage_evidence_attempt_mapping
                FOREIGN KEY (attempt_id, request_id, provider_model_id)
                REFERENCES usage.attempt (id, request_id, provider_model_id);

                CREATE FUNCTION usage.prevent_evidence_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'usage evidence is append-only';
                END;
                $$;

                CREATE TRIGGER prevent_evidence_mutation
                BEFORE UPDATE OR DELETE ON usage.evidence
                FOR EACH ROW EXECUTE FUNCTION usage.prevent_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS prevent_evidence_mutation ON usage.evidence;
                DROP FUNCTION IF EXISTS usage.prevent_evidence_mutation();
                """);
            migrationBuilder.DropTable(
                name: "evidence",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "idempotency_claim",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "attempt",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "request",
                schema: "usage");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_project_organization_id_id",
                schema: "gateway",
                table: "project");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_model_price_provider_model_id_id",
                schema: "catalog",
                table: "model_price");

            migrationBuilder.DropIndex(
                name: "IX_model_price_provider_model_id_id",
                schema: "catalog",
                table: "model_price");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_api_key_project_id_id",
                schema: "gateway",
                table: "api_key");
        }
    }
}
