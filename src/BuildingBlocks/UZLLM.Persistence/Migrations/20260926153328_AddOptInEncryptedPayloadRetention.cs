using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptInEncryptedPayloadRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_request_id_organization_id_project_id",
                schema: "usage",
                table: "request",
                columns: new[] { "id", "organization_id", "project_id" });

            migrationBuilder.CreateTable(
                name: "payload",
                schema: "usage",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encrypted_request_payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    wrapped_request_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    request_key_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    encrypted_response_payload = table.Column<byte[]>(type: "bytea", nullable: true),
                    wrapped_response_key = table.Column<byte[]>(type: "bytea", nullable: true),
                    response_key_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payload", x => x.request_id);
                    table.CheckConstraint("CK_usage_payload_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_payload_request_request_id_organization_id_project_id",
                        columns: x => new { x.request_id, x.organization_id, x.project_id },
                        principalSchema: "usage",
                        principalTable: "request",
                        principalColumns: new[] { "id", "organization_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payload_retention_policy",
                schema: "usage",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    retention_minutes = table.Column<int>(type: "integer", nullable: false),
                    updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payload_retention_policy", x => new { x.organization_id, x.project_id });
                    table.CheckConstraint("CK_usage_payload_retention_minutes", "retention_minutes BETWEEN 60 AND 10080");
                    table.ForeignKey(
                        name: "FK_payload_retention_policy_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payload_expires_at",
                schema: "usage",
                table: "payload",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_payload_organization_id_project_id_expires_at",
                schema: "usage",
                table: "payload",
                columns: new[] { "organization_id", "project_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payload_request_id_organization_id_project_id",
                schema: "usage",
                table: "payload",
                columns: new[] { "request_id", "organization_id", "project_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM usage.payload LIMIT 1) THEN
                    RAISE EXCEPTION 'Retained payloads must be explicitly purged before downgrade';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "payload",
                schema: "usage");

            migrationBuilder.DropTable(
                name: "payload_retention_policy",
                schema: "usage");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_request_id_organization_id_project_id",
                schema: "usage",
                table: "request");

        }
    }
}
