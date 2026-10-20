using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSliMonthlyReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sli_report_head",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    latest_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_head", x => x.id);
                    table.CheckConstraint("CK_sli_report_head_generation", "generation >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sli_report_operation",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_generation = table.Column<long>(type: "bigint", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_operation", x => x.id);
                    table.UniqueConstraint("AK_sli_report_operation_id_head_id", x => new { x.id, x.head_id });
                    table.CheckConstraint("CK_sli_report_operation_status", "status IN ('Pending','Failed','Published') AND expected_generation >= 0 AND (status <> 'Published' OR version_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_sli_report_operation_sli_report_head_head_id",
                        column: x => x.head_id,
                        principalSchema: "ops",
                        principalTable: "sli_report_head",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sli_report_operation_user_actor_id",
                        column: x => x.actor_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sli_report_attempt",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_attempt", x => x.id);
                    table.ForeignKey(
                        name: "FK_sli_report_attempt_sli_report_operation_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "ops",
                        principalTable: "sli_report_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sli_report_receipt",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    command_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_receipt", x => x.id);
                    table.ForeignKey(
                        name: "FK_sli_report_receipt_sli_report_operation_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "ops",
                        principalTable: "sli_report_operation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sli_report_version",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    head_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_count = table.Column<int>(type: "integer", nullable: false),
                    artifact = table.Column<byte[]>(type: "bytea", nullable: false),
                    artifact_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_version", x => x.id);
                    table.UniqueConstraint("AK_sli_report_version_id_head_id", x => new { x.id, x.head_id });
                    table.CheckConstraint("CK_sli_report_version_bounds", "revision > 0 AND source_count BETWEEN 0 AND 100000 AND octet_length(artifact) BETWEEN 1 AND 33554432");
                    table.ForeignKey(
                        name: "FK_sli_report_version_sli_report_operation_operation_id_head_id",
                        columns: x => new { x.operation_id, x.head_id },
                        principalSchema: "ops",
                        principalTable: "sli_report_operation",
                        principalColumns: new[] { "id", "head_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sli_report_version_sli_report_version_predecessor_id_head_id",
                        columns: x => new { x.predecessor_id, x.head_id },
                        principalSchema: "ops",
                        principalTable: "sli_report_version",
                        principalColumns: new[] { "id", "head_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sli_report_source",
                schema: "ops",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_report_source", x => new { x.version_id, x.manifest_id });
                    table.ForeignKey(
                        name: "FK_sli_report_source_sli_manifest_manifest_id",
                        column: x => x.manifest_id,
                        principalSchema: "ops",
                        principalTable: "sli_manifest",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sli_report_source_sli_report_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "ops",
                        principalTable: "sli_report_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_attempt_operation_id_attempt_number",
                schema: "ops",
                table: "sli_report_attempt",
                columns: new[] { "operation_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_head_scope_id_period_start",
                schema: "ops",
                table: "sli_report_head",
                columns: new[] { "scope_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_operation_actor_id",
                schema: "ops",
                table: "sli_report_operation",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_operation_head_id",
                schema: "ops",
                table: "sli_report_operation",
                column: "head_id");

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_receipt_kind_idempotency_key",
                schema: "ops",
                table: "sli_report_receipt",
                columns: new[] { "kind", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_receipt_operation_id",
                schema: "ops",
                table: "sli_report_receipt",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_source_manifest_id",
                schema: "ops",
                table: "sli_report_source",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_version_head_id_revision",
                schema: "ops",
                table: "sli_report_version",
                columns: new[] { "head_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_version_operation_id",
                schema: "ops",
                table: "sli_report_version",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_version_operation_id_head_id",
                schema: "ops",
                table: "sli_report_version",
                columns: new[] { "operation_id", "head_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_report_version_predecessor_id_head_id",
                schema: "ops",
                table: "sli_report_version",
                columns: new[] { "predecessor_id", "head_id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_sli_report_version BEFORE UPDATE OR DELETE ON ops.sli_report_version
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE TRIGGER immutable_sli_report_source BEFORE UPDATE OR DELETE ON ops.sli_report_source
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE TRIGGER immutable_sli_report_receipt BEFORE UPDATE OR DELETE ON ops.sli_report_receipt
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE TRIGGER immutable_sli_report_attempt BEFORE UPDATE OR DELETE ON ops.sli_report_attempt
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE FUNCTION ops.guard_sli_report_head() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'SLI head history is immutable'; END IF;
                  IF (NEW.id,NEW.scope_id,NEW.period_start) IS DISTINCT FROM (OLD.id,OLD.scope_id,OLD.period_start)
                    OR NEW.generation < OLD.generation OR NEW.generation > OLD.generation+1
                    OR NEW.generation > 0 AND NOT EXISTS (SELECT 1 FROM ops.sli_report_version v
                      WHERE v.id=NEW.latest_version_id AND v.head_id=NEW.id AND v.revision=NEW.generation)
                    OR NEW.active_operation_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ops.sli_report_operation o
                      WHERE o.id=NEW.active_operation_id AND o.head_id=NEW.id)
                  THEN RAISE EXCEPTION 'Invalid SLI head transition'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER guard_sli_report_head BEFORE UPDATE OR DELETE ON ops.sli_report_head
                  FOR EACH ROW EXECUTE FUNCTION ops.guard_sli_report_head();
                CREATE FUNCTION ops.guard_sli_report_operation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'SLI operation history is immutable'; END IF;
                  IF OLD.status = 'Published' OR
                    (NEW.id,NEW.head_id,NEW.expected_generation,NEW.predecessor_id,NEW.actor_id,NEW.reason,NEW.scope_json,NEW.created_at)
                    IS DISTINCT FROM
                    (OLD.id,OLD.head_id,OLD.expected_generation,OLD.predecessor_id,OLD.actor_id,OLD.reason,OLD.scope_json,OLD.created_at)
                  THEN RAISE EXCEPTION 'SLI operation terms are immutable'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER guard_sli_report_operation BEFORE UPDATE OR DELETE ON ops.sli_report_operation
                  FOR EACH ROW EXECUTE FUNCTION ops.guard_sli_report_operation();
                CREATE FUNCTION ops.guard_sli_report_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM ops.sli_report_version v JOIN ops.sli_report_operation o ON o.id=v.operation_id
                    WHERE v.id=NEW.version_id AND o.status='Published') OR NOT EXISTS
                    (SELECT 1 FROM ops.sli_manifest m WHERE m.id=NEW.manifest_id AND m.sha256=NEW.source_json->>'sha256')
                  THEN RAISE EXCEPTION 'SLI report source is sealed or invalid'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER seal_sli_report_source BEFORE INSERT ON ops.sli_report_source
                  FOR EACH ROW EXECUTE FUNCTION ops.guard_sli_report_membership();
                CREATE FUNCTION ops.validate_sli_report_publication() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW.status='Published' AND NOT EXISTS (
                    SELECT 1 FROM ops.sli_report_version v JOIN ops.sli_report_head h ON h.id=v.head_id
                    WHERE v.id=NEW.version_id AND v.operation_id=NEW.id AND v.head_id=NEW.head_id
                      AND v.revision=NEW.expected_generation+1 AND v.predecessor_id IS NOT DISTINCT FROM NEW.predecessor_id
                      AND h.latest_version_id=v.id AND h.generation=v.revision AND h.active_operation_id=NEW.id
                      AND v.source_count=(SELECT count(*) FROM ops.sli_report_source s WHERE s.version_id=v.id))
                  THEN RAISE EXCEPTION 'Incomplete SLI publication'; END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER validate_sli_report_publication AFTER UPDATE ON ops.sli_report_operation
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ops.validate_sli_report_publication();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM ops.sli_report_operation) OR EXISTS (SELECT 1 FROM ops.sli_report_version)
                  THEN RAISE EXCEPTION 'SLI report history must be preserved; downgrade refused'; END IF; END $$;
                """);
            migrationBuilder.DropTable(
                name: "sli_report_attempt",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_report_receipt",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_report_source",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_report_version",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_report_operation",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_report_head",
                schema: "ops");
            migrationBuilder.Sql("""
                DROP FUNCTION ops.validate_sli_report_publication();
                DROP FUNCTION ops.guard_sli_report_membership();
                DROP FUNCTION ops.guard_sli_report_operation();
                DROP FUNCTION ops.guard_sli_report_head();
                """);
        }
    }
}
