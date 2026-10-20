using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSliObservationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sli_manifest",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: false),
                    dropped = table.Column<long>(type: "bigint", nullable: false),
                    interval_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    interval_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_synchronized = table.Column<bool>(type: "boolean", nullable: false),
                    collector_continuous = table.Column<bool>(type: "boolean", nullable: false),
                    terminal_coverage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    coverage_warnings = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_manifest", x => x.id);
                    table.UniqueConstraint("AK_sli_manifest_id_producer_epoch", x => new { x.id, x.producer, x.epoch });
                    table.CheckConstraint("CK_sli_manifest_bounds", "batch_number > 0 AND first_sequence > 0 AND last_sequence >= first_sequence - 1 AND observation_count BETWEEN 0 AND 1024 AND dropped >= 0 AND last_sequence::numeric - first_sequence + 1 = observation_count::numeric + dropped AND interval_end >= interval_start AND interval_end - interval_start <= interval '1 hour' AND sha256 ~ '^[a-f0-9]{64}$' AND source IN ('Gateway','Edge','Probe') AND terminal_coverage IN ('Partial','Unknown','NotApplicable')");
                });

            migrationBuilder.CreateTable(
                name: "sli_import_receipt",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_import_receipt", x => x.id);
                    table.CheckConstraint("CK_sli_receipt_status", "status IN ('Accepted','Duplicate','Conflict','Expired') AND observation_count BETWEEN 0 AND 1024 AND sha256 ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_sli_import_receipt_sli_manifest_manifest_id",
                        column: x => x.manifest_id,
                        principalSchema: "ops",
                        principalTable: "sli_manifest",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sli_observation",
                schema: "ops",
                columns: table => new
                {
                    producer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sli_observation", x => new { x.producer, x.epoch, x.sequence });
                    table.CheckConstraint("CK_sli_observation_bounds", "sequence > 0 AND at >= started_at AND octet_length(payload_json::text) <= 2048");
                    table.ForeignKey(
                        name: "FK_sli_observation_sli_manifest_manifest_id_producer_epoch",
                        columns: x => new { x.manifest_id, x.producer, x.epoch },
                        principalSchema: "ops",
                        principalTable: "sli_manifest",
                        principalColumns: new[] { "id", "producer", "epoch" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sli_import_receipt_manifest_id",
                schema: "ops",
                table: "sli_import_receipt",
                column: "manifest_id");

            migrationBuilder.CreateIndex(
                name: "IX_sli_import_receipt_producer_epoch_batch_number_received_at",
                schema: "ops",
                table: "sli_import_receipt",
                columns: new[] { "producer", "epoch", "batch_number", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_manifest_coverage_warnings_received_at",
                schema: "ops",
                table: "sli_manifest",
                columns: new[] { "coverage_warnings", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_manifest_producer_epoch_batch_number",
                schema: "ops",
                table: "sli_manifest",
                columns: new[] { "producer", "epoch", "batch_number" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION ops.reject_sli_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'SLI evidence is immutable'; END $$;
                CREATE TRIGGER immutable_sli_manifest BEFORE UPDATE OR DELETE ON ops.sli_manifest
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE TRIGGER immutable_sli_receipt BEFORE UPDATE OR DELETE ON ops.sli_import_receipt
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE TRIGGER immutable_sli_observation BEFORE UPDATE ON ops.sli_observation
                  FOR EACH ROW EXECUTE FUNCTION ops.reject_sli_mutation();
                CREATE FUNCTION ops.guard_sli_membership() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM ops.sli_import_receipt WHERE manifest_id = NEW.manifest_id AND status = 'Accepted')
                    OR NOT EXISTS (SELECT 1 FROM ops.sli_manifest WHERE id = NEW.manifest_id
                      AND NEW.sequence BETWEEN first_sequence AND last_sequence) THEN
                    RAISE EXCEPTION 'SLI source membership is sealed or outside sequence range';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER seal_sli_observation BEFORE INSERT ON ops.sli_observation
                  FOR EACH ROW EXECUTE FUNCTION ops.guard_sli_membership();
                CREATE UNIQUE INDEX one_sli_acceptance ON ops.sli_import_receipt(manifest_id) WHERE status = 'Accepted';
                CREATE FUNCTION ops.validate_sli_import() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW.status = 'Accepted' AND NOT EXISTS (
                    SELECT 1 FROM ops.sli_manifest m WHERE m.id = NEW.manifest_id
                      AND m.producer = NEW.producer AND m.epoch = NEW.epoch AND m.batch_number = NEW.batch_number
                      AND m.sha256 = NEW.sha256 AND m.observation_count = NEW.observation_count
                      AND m.observation_count = (SELECT count(*) FROM ops.sli_observation WHERE manifest_id = m.id)) THEN
                    RAISE EXCEPTION 'incomplete SLI import';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER validate_sli_import AFTER INSERT ON ops.sli_import_receipt
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ops.validate_sli_import();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_sli_manifest_producer_epoch_first_sequence_last_sequence",
                schema: "ops",
                table: "sli_manifest",
                columns: new[] { "producer", "epoch", "first_sequence", "last_sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_manifest_source_region_interval_start",
                schema: "ops",
                table: "sli_manifest",
                columns: new[] { "source", "region", "interval_start" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_observation_at",
                schema: "ops",
                table: "sli_observation",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "IX_sli_observation_execution_id_at",
                schema: "ops",
                table: "sli_observation",
                columns: new[] { "execution_id", "at" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_observation_manifest_id_producer_epoch",
                schema: "ops",
                table: "sli_observation",
                columns: new[] { "manifest_id", "producer", "epoch" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_observation_operation_started_at",
                schema: "ops",
                table: "sli_observation",
                columns: new[] { "operation", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sli_observation_producer_epoch_event_key",
                schema: "ops",
                table: "sli_observation",
                columns: new[] { "producer", "epoch", "event_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ops.sli_manifest) OR EXISTS (SELECT 1 FROM ops.sli_import_receipt) THEN
                    RAISE EXCEPTION 'SLI history must be preserved; downgrade refused';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "sli_import_receipt",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_observation",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "sli_manifest",
                schema: "ops");
            migrationBuilder.Sql("""
                DROP FUNCTION ops.validate_sli_import();
                DROP FUNCTION ops.guard_sli_membership();
                DROP FUNCTION ops.reject_sli_mutation();
                """);
        }
    }
}
