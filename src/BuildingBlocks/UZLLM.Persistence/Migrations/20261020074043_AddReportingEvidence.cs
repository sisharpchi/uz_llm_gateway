using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reporting_profile",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_profile", x => new { x.id, x.version });
                    table.CheckConstraint("CK_reporting_profile_bounds", "version > 0 AND octet_length(snapshot_json) BETWEEN 1 AND 32768 AND length(sha256) = 64");
                });

            migrationBuilder.CreateTable(
                name: "reporting_evidence",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    command_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    profile_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    profile_version = table.Column<int>(type: "integer", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    statement_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sli_report_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issuer_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    external_operation_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_operation_root = table.Column<bool>(type: "boolean", nullable: false),
                    document_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contract_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    policy_version = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    scope_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    link_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_evidence", x => x.id);
                    table.UniqueConstraint("AK_reporting_evidence_id_organization_id", x => new { x.id, x.organization_id });
                    table.UniqueConstraint("AK_reporting_evidence_id_organization_id_kind", x => new { x.id, x.organization_id, x.kind });
                    table.CheckConstraint("CK_reporting_evidence_bounds", "octet_length(payload_json) BETWEEN 1 AND 32768 AND length(payload_sha256) = 64 AND length(command_sha256) = 64");
                    table.CheckConstraint("CK_reporting_evidence_kind", "(kind = 'fiscal' AND state IN ('Unknown','ReviewRequired','Registered','Cancelled') AND issuer_key IS NOT NULL AND external_operation_reference IS NOT NULL AND contract_reference_id IS NULL AND sli_report_version_id IS NULL AND (state <> 'Registered' OR document_number IS NOT NULL)) OR (kind = 'contracts' AND state IN ('EvidenceOnly','Revoked') AND contract_key IS NOT NULL AND policy_version IS NOT NULL AND scope_id IS NOT NULL AND effective_from < effective_to AND statement_version_id IS NULL AND sli_report_version_id IS NULL AND contract_reference_id IS NULL AND issuer_key IS NULL AND NOT is_operation_root) OR (kind = 'links' AND state = 'EvidenceOnly' AND contract_reference_id IS NOT NULL AND link_key IS NOT NULL AND (statement_version_id IS NOT NULL OR sli_report_version_id IS NOT NULL) AND predecessor_id IS NULL AND issuer_key IS NULL AND NOT is_operation_root)");
                    table.ForeignKey(
                        name: "FK_reporting_evidence_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_reporting_evidence_contract_reference_id~",
                        columns: x => new { x.contract_reference_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "reporting_evidence",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_reporting_evidence_predecessor_id_organi~",
                        columns: x => new { x.predecessor_id, x.organization_id, x.kind },
                        principalSchema: "billing",
                        principalTable: "reporting_evidence",
                        principalColumns: new[] { "id", "organization_id", "kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_reporting_profile_profile_id_profile_ver~",
                        columns: x => new { x.profile_id, x.profile_version },
                        principalSchema: "billing",
                        principalTable: "reporting_profile",
                        principalColumns: new[] { "id", "version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_sli_report_version_sli_report_version_id",
                        column: x => x.sli_report_version_id,
                        principalSchema: "ops",
                        principalTable: "sli_report_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_statement_version_statement_version_id_o~",
                        columns: x => new { x.statement_version_id, x.organization_id },
                        principalSchema: "billing",
                        principalTable: "statement_version",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_evidence_user_actor_id",
                        column: x => x.actor_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_actor_id",
                schema: "billing",
                table: "reporting_evidence",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_contract_reference_id_organization_id",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "contract_reference_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_issuer_key_document_number",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "issuer_key", "document_number" },
                unique: true,
                filter: "kind = 'fiscal' AND state = 'Registered'");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_issuer_key_external_operation_reference",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "issuer_key", "external_operation_reference" },
                unique: true,
                filter: "is_operation_root");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_organization_id_contract_key_policy_vers~",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "organization_id", "contract_key", "policy_version" },
                unique: true,
                filter: "kind = 'contracts' AND state = 'EvidenceOnly'");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_organization_id_kind_idempotency_key",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "organization_id", "kind", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_organization_id_kind_recorded_at_id",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "organization_id", "kind", "recorded_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_organization_id_link_key",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "organization_id", "link_key" },
                unique: true,
                filter: "kind = 'links'");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_predecessor_id",
                schema: "billing",
                table: "reporting_evidence",
                column: "predecessor_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_predecessor_id_organization_id_kind",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "predecessor_id", "organization_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_profile_id_profile_version",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "profile_id", "profile_version" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_sli_report_version_id",
                schema: "billing",
                table: "reporting_evidence",
                column: "sli_report_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_evidence_statement_version_id_organization_id",
                schema: "billing",
                table: "reporting_evidence",
                columns: new[] { "statement_version_id", "organization_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.guard_reporting_evidence_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Reporting evidence history is immutable'; END $$;
                CREATE TRIGGER immutable_reporting_profile BEFORE UPDATE OR DELETE ON billing.reporting_profile
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_reporting_evidence_history();
                CREATE TRIGGER immutable_reporting_evidence BEFORE UPDATE OR DELETE ON billing.reporting_evidence
                  FOR EACH ROW EXECUTE FUNCTION billing.guard_reporting_evidence_history();
                CREATE FUNCTION billing.validate_reporting_evidence_links() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW.kind='links' AND NOT EXISTS (SELECT 1 FROM billing.reporting_evidence c
                    WHERE c.id=NEW.contract_reference_id AND c.organization_id=NEW.organization_id
                    AND c.kind='contracts' AND c.state='EvidenceOnly' AND c.profile_id=NEW.profile_id
                    AND c.profile_version=NEW.profile_version)
                  THEN RAISE EXCEPTION 'Invalid contract evidence link'; END IF;
                  IF NEW.predecessor_id=NEW.id OR NEW.contract_reference_id=NEW.id THEN
                    RAISE EXCEPTION 'Self-referencing evidence'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER validate_reporting_evidence_links BEFORE INSERT ON billing.reporting_evidence
                  FOR EACH ROW EXECUTE FUNCTION billing.validate_reporting_evidence_links();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM billing.reporting_evidence) OR EXISTS (SELECT 1 FROM billing.reporting_profile)
                  THEN RAISE EXCEPTION 'Reporting evidence history must be preserved; downgrade refused'; END IF; END $$;
                """);
            migrationBuilder.DropTable(
                name: "reporting_evidence",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "reporting_profile",
                schema: "billing");
            migrationBuilder.Sql("DROP FUNCTION billing.validate_reporting_evidence_links(); DROP FUNCTION billing.guard_reporting_evidence_history();");
        }
    }
}
