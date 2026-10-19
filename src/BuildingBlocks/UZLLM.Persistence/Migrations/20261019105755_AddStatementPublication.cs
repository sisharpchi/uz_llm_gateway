using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatementPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statement_attempt",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_attempt", x => x.id);
                    table.CheckConstraint("CK_statement_attempt_outcome", "attempt_number BETWEEN 1 AND 5 AND ((outcome = 'Published' AND failure_kind IS NULL) OR (outcome = 'Failed' AND failure_kind IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "statement_head",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    latest_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_head", x => x.id);
                    table.UniqueConstraint("AK_statement_head_id_organization_id", x => new { x.id, x.organization_id });
                    table.CheckConstraint("CK_statement_head_generation", "generation >= 0 AND ((generation = 0 AND latest_version_id IS NULL) OR (generation > 0 AND latest_version_id IS NOT NULL))");
                    table.CheckConstraint("CK_statement_head_period", "kind = 'AccountStatement' AND period_start = (date_trunc('month', period_start AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')");
                    table.ForeignKey(
                        name: "FK_statement_head_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_line",
                schema: "billing",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_line", x => new { x.version_id, x.ordinal });
                    table.CheckConstraint("CK_statement_line_ordinal", "ordinal BETWEEN 1 AND 100000");
                });

            migrationBuilder.CreateTable(
                name: "statement_operation",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_generation = table.Column<long>(type: "bigint", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    published_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_operation", x => x.id);
                    table.UniqueConstraint("AK_statement_operation_id_organization_id", x => new { x.id, x.organization_id });
                    table.UniqueConstraint("AK_statement_operation_id_organization_id_head_id", x => new { x.id, x.organization_id, x.head_id });
                    table.CheckConstraint("CK_statement_operation_status", "(status = 'Pending' AND published_version_id IS NULL AND failure_kind IS NULL) OR (status = 'Failed' AND published_version_id IS NULL AND failure_kind IS NOT NULL) OR (status = 'Published' AND published_version_id IS NOT NULL AND failure_kind IS NULL)");
                    table.CheckConstraint("CK_statement_operation_terms", "expected_generation >= 0 AND length(btrim(reason)) BETWEEN 8 AND 500 AND ((expected_generation = 0 AND predecessor_id IS NULL) OR (expected_generation > 0 AND predecessor_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_statement_operation_statement_head_head_id_organization_id",
                        columns: x => new { x.head_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "statement_head",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_operation_user_requested_by_account_id",
                        column: x => x.requested_by_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_receipt",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    command_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_receipt", x => x.id);
                    table.CheckConstraint("CK_statement_receipt_terms", "schema_version = 1 AND operation_kind IN ('Build','Revision') AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND command_sha256 ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_statement_receipt_statement_operation_operation_id_organiza~",
                        columns: x => new { x.operation_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "statement_operation",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_version",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    calculation_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    snapshot_captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    json_artifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    csv_artifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    json_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    csv_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_count = table.Column<int>(type: "integer", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    total_bytes = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_version", x => x.id);
                    table.UniqueConstraint("AK_statement_version_id_organization_id", x => new { x.id, x.organization_id });
                    table.UniqueConstraint("AK_statement_version_id_organization_id_head_id", x => new { x.id, x.organization_id, x.head_id });
                    table.UniqueConstraint("AK_statement_version_id_organization_id_head_id_revision", x => new { x.id, x.organization_id, x.head_id, x.revision });
                    table.CheckConstraint("CK_statement_version_bounds", "source_count BETWEEN 1 AND 100000 AND line_count BETWEEN 0 AND 100000 AND total_bytes BETWEEN 1 AND 33554432 AND octet_length(json_artifact) + octet_length(csv_artifact) + octet_length(snapshot_json) <= total_bytes");
                    table.CheckConstraint("CK_statement_version_terms", "schema_version = 1 AND revision > 0 AND ((revision = 1 AND predecessor_id IS NULL) OR (revision > 1 AND predecessor_id IS NOT NULL)) AND source_digest ~ '^[a-f0-9]{64}$' AND json_sha256 ~ '^[a-f0-9]{64}$' AND csv_sha256 ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_statement_version_statement_head_head_id_organization_id",
                        columns: x => new { x.head_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "statement_head",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_version_statement_operation_operation_id_organiza~",
                        columns: x => new { x.operation_id, x.organization_id, x.head_id },
                        principalSchema: "billing",
                        principalTable: "statement_operation",
                        principalColumns: new[] { "id", "organization_id", "head_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_version_statement_version_predecessor_id_organiza~",
                        columns: x => new { x.predecessor_id, x.organization_id, x.head_id },
                        principalSchema: "billing",
                        principalTable: "statement_version",
                        principalColumns: new[] { "id", "organization_id", "head_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_source",
                schema: "billing",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_source", x => new { x.version_id, x.kind, x.source_id });
                    table.CheckConstraint("CK_statement_source_payload", "payload_sha256 ~ '^[a-f0-9]{64}$' AND (payload_json::jsonb->>'OrganizationId') IS NOT NULL AND (payload_json::jsonb->>'OrganizationId')::uuid = organization_id");
                    table.ForeignKey(
                        name: "FK_statement_source_statement_version_version_id_organization_~",
                        columns: x => new { x.version_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "statement_version",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_statement_attempt_operation_id_attempt_number",
                schema: "billing",
                table: "statement_attempt",
                columns: new[] { "operation_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_attempt_operation_id_organization_id",
                schema: "billing",
                table: "statement_attempt",
                columns: new[] { "operation_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_head_active_operation_id_organization_id_id",
                schema: "billing",
                table: "statement_head",
                columns: new[] { "active_operation_id", "organization_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_head_latest_version_id_organization_id_id_generat~",
                schema: "billing",
                table: "statement_head",
                columns: new[] { "latest_version_id", "organization_id", "id", "generation" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_head_organization_id_period_start_kind",
                schema: "billing",
                table: "statement_head",
                columns: new[] { "organization_id", "period_start", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_line_version_id_ledger_id",
                schema: "billing",
                table: "statement_line",
                columns: new[] { "version_id", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_line_version_id_organization_id",
                schema: "billing",
                table: "statement_line",
                columns: new[] { "version_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_operation_head_id_organization_id",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "head_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_operation_organization_id_created_at",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_operation_predecessor_id_organization_id_head_id",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "predecessor_id", "organization_id", "head_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_operation_published_version_id_organization_id_he~",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "published_version_id", "organization_id", "head_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_operation_requested_by_account_id",
                schema: "billing",
                table: "statement_operation",
                column: "requested_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_receipt_operation_id",
                schema: "billing",
                table: "statement_receipt",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_receipt_operation_id_organization_id",
                schema: "billing",
                table: "statement_receipt",
                columns: new[] { "operation_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_receipt_organization_id_operation_kind_idempotenc~",
                schema: "billing",
                table: "statement_receipt",
                columns: new[] { "organization_id", "operation_kind", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_source_version_id_organization_id",
                schema: "billing",
                table: "statement_source",
                columns: new[] { "version_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_version_head_id_organization_id",
                schema: "billing",
                table: "statement_version",
                columns: new[] { "head_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_version_head_id_revision",
                schema: "billing",
                table: "statement_version",
                columns: new[] { "head_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_version_operation_id",
                schema: "billing",
                table: "statement_version",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_version_operation_id_organization_id_head_id",
                schema: "billing",
                table: "statement_version",
                columns: new[] { "operation_id", "organization_id", "head_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_version_predecessor_id_organization_id_head_id",
                schema: "billing",
                table: "statement_version",
                columns: new[] { "predecessor_id", "organization_id", "head_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_statement_attempt_statement_operation_operation_id_organiza~",
                schema: "billing",
                table: "statement_attempt",
                columns: new[] { "operation_id", "organization_id" },
                principalSchema: "billing",
                principalTable: "statement_operation",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_head_statement_operation_active_operation_id_orga~",
                schema: "billing",
                table: "statement_head",
                columns: new[] { "active_operation_id", "organization_id", "id" },
                principalSchema: "billing",
                principalTable: "statement_operation",
                principalColumns: new[] { "id", "organization_id", "head_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_head_statement_version_latest_version_id_organiza~",
                schema: "billing",
                table: "statement_head",
                columns: new[] { "latest_version_id", "organization_id", "id", "generation" },
                principalSchema: "billing",
                principalTable: "statement_version",
                principalColumns: new[] { "id", "organization_id", "head_id", "revision" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_line_statement_version_version_id_organization_id",
                schema: "billing",
                table: "statement_line",
                columns: new[] { "version_id", "organization_id" },
                principalSchema: "billing",
                principalTable: "statement_version",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_operation_statement_version_predecessor_id_organi~",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "predecessor_id", "organization_id", "head_id" },
                principalSchema: "billing",
                principalTable: "statement_version",
                principalColumns: new[] { "id", "organization_id", "head_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_operation_statement_version_published_version_id_~",
                schema: "billing",
                table: "statement_operation",
                columns: new[] { "published_version_id", "organization_id", "head_id" },
                principalSchema: "billing",
                principalTable: "statement_version",
                principalColumns: new[] { "id", "organization_id", "head_id" },
                onDelete: ReferentialAction.Restrict);
            ProtectHistory(migrationBuilder);
        }

        private static void ProtectHistory(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "statement_version", "statement_line", "statement_source", "statement_receipt", "statement_attempt" })
                migrationBuilder.Sql($"CREATE TRIGGER prevent_{table}_mutation BEFORE UPDATE OR DELETE ON billing.{table} FOR EACH ROW EXECUTE FUNCTION billing.prevent_financial_history_mutation();");
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.guard_statement_terms() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'statement history cannot be deleted'; END IF;
                  IF TG_TABLE_NAME = 'statement_head' THEN
                    IF (NEW.id, NEW.organization_id, NEW.period_start, NEW.kind) IS DISTINCT FROM
                       (OLD.id, OLD.organization_id, OLD.period_start, OLD.kind) OR NEW.generation < OLD.generation THEN
                      RAISE EXCEPTION 'statement head identity is immutable';
                    END IF;
                  ELSE
                    IF (to_jsonb(NEW) - ARRAY['status','failure_kind','published_version_id']) IS DISTINCT FROM
                       (to_jsonb(OLD) - ARRAY['status','failure_kind','published_version_id']) OR
                       (OLD.status = 'Published' AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD)) THEN
                      RAISE EXCEPTION 'statement operation terms are immutable';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER protect_statement_head BEFORE UPDATE OR DELETE ON billing.statement_head
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_statement_terms();
                CREATE TRIGGER protect_statement_operation BEFORE UPDATE OR DELETE ON billing.statement_operation
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_statement_terms();

                CREATE FUNCTION billing.guard_statement_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM billing.statement_version v
                      JOIN billing.statement_operation o ON o.id = v.operation_id
                      WHERE v.id = NEW.version_id AND o.status <> 'Published' AND o.published_version_id IS NULL) THEN
                    RAISE EXCEPTION 'published statement membership is sealed';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER seal_statement_source BEFORE INSERT ON billing.statement_source
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_statement_membership();
                CREATE TRIGGER seal_statement_line BEFORE INSERT ON billing.statement_line
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_statement_membership();

                CREATE FUNCTION billing.validate_statement_publication() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE source_bytes bigint; line_bytes bigint;
                BEGIN
                  SELECT coalesce(sum(octet_length(payload_json)),0) INTO source_bytes FROM billing.statement_source WHERE version_id = NEW.id;
                  SELECT coalesce(sum(octet_length(payload_json)),0) INTO line_bytes FROM billing.statement_line WHERE version_id = NEW.id;
                  IF NEW.source_count <> (SELECT count(*) FROM billing.statement_source WHERE version_id = NEW.id)
                     OR NEW.line_count <> (SELECT count(*) FROM billing.statement_line WHERE version_id = NEW.id)
                     OR NEW.total_bytes <> source_bytes + line_bytes + octet_length(NEW.snapshot_json) + octet_length(NEW.json_artifact) + octet_length(NEW.csv_artifact)
                     OR NOT EXISTS (SELECT 1 FROM billing.statement_operation WHERE id = NEW.operation_id AND
                       status = 'Published' AND published_version_id = NEW.id AND expected_generation + 1 = NEW.revision AND
                       predecessor_id IS NOT DISTINCT FROM NEW.predecessor_id)
                     OR NOT EXISTS (SELECT 1 FROM billing.statement_head WHERE id = NEW.head_id AND
                       latest_version_id = NEW.id AND generation = NEW.revision) THEN
                    RAISE EXCEPTION 'incomplete statement publication';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER validate_statement_publication AFTER INSERT ON billing.statement_version
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION billing.validate_statement_publication();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM billing.statement_head) THEN
                    RAISE EXCEPTION 'refuse to discard statement publication history';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_statement_head_statement_operation_active_operation_id_orga~",
                schema: "billing",
                table: "statement_head");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_version_statement_operation_operation_id_organiza~",
                schema: "billing",
                table: "statement_version");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_head_statement_version_latest_version_id_organiza~",
                schema: "billing",
                table: "statement_head");

            migrationBuilder.DropTable(
                name: "statement_attempt",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_line",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_receipt",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_source",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_operation",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_version",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "statement_head",
                schema: "billing");
            migrationBuilder.Sql("""
                DROP FUNCTION billing.guard_statement_terms();
                DROP FUNCTION billing.guard_statement_membership();
                DROP FUNCTION billing.validate_statement_publication();
                """);
        }
    }
}
