using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppendOnlyAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_event_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_event_user_account_id",
                        column: x => x.account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_account_id_occurred_at",
                schema: "audit",
                table: "audit_event",
                columns: new[] { "account_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_organization_id_occurred_at",
                schema: "audit",
                table: "audit_event",
                columns: new[] { "organization_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.Sql("""
                CREATE FUNCTION audit.prevent_audit_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.audit_event is append-only';
                END;
                $$;

                CREATE TRIGGER prevent_audit_event_mutation
                BEFORE UPDATE OR DELETE ON audit.audit_event
                FOR EACH ROW
                EXECUTE FUNCTION audit.prevent_audit_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "audit");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.prevent_audit_event_mutation();");
        }
    }
}
