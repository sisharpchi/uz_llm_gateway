using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddByokCredentialGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_provider_credential_organization_id",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "gateway",
                table: "provider_credential",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_test_status",
                schema: "gateway",
                table: "provider_credential",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_tested_at",
                schema: "gateway",
                table: "provider_credential",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "masked_key",
                schema: "gateway",
                table: "provider_credential",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "gateway",
                table: "provider_credential",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "gateway",
                table: "provider_credential",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provider_credential_project_grant",
                schema: "gateway",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_credential_project_grant", x => new { x.organization_id, x.credential_id, x.project_id });
                    table.ForeignKey(
                        name: "FK_provider_credential_project_grant_project_organization_id_p~",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_credential_project_grant_provider_credential_crede~",
                        column: x => x.credential_id,
                        principalSchema: "gateway",
                        principalTable: "provider_credential",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_organization_id_created_at",
                schema: "gateway",
                table: "provider_credential",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_organization_id_id",
                schema: "gateway",
                table: "provider_credential",
                columns: new[] { "organization_id", "id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_provider_credential_byok_fields",
                schema: "gateway",
                table: "provider_credential",
                sql: "(credential_type = 'Platform' AND name IS NULL AND masked_key IS NULL) OR (credential_type = 'BYOK' AND name IS NOT NULL AND masked_key IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_provider_credential_test_status",
                schema: "gateway",
                table: "provider_credential",
                sql: "last_test_status IS NULL OR last_test_status IN ('Valid', 'Invalid', 'Unavailable')");

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_project_grant_credential_id",
                schema: "gateway",
                table: "provider_credential_project_grant",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_project_grant_organization_id_project_id",
                schema: "gateway",
                table: "provider_credential_project_grant",
                columns: new[] { "organization_id", "project_id" });

            // EF's alternate principal key would incorrectly make the nullable
            // organization_id of existing platform credentials NOT NULL.
            // PostgreSQL can reference the unique index without that model change.
            migrationBuilder.Sql("""
                ALTER TABLE gateway.provider_credential_project_grant
                ADD CONSTRAINT fk_byok_grant_tenant_credential
                FOREIGN KEY (organization_id, credential_id)
                REFERENCES gateway.provider_credential (organization_id, id)
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_credential_project_grant",
                schema: "gateway");

            migrationBuilder.DropIndex(
                name: "IX_provider_credential_organization_id_created_at",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropIndex(
                name: "IX_provider_credential_organization_id_id",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropCheckConstraint(
                name: "CK_provider_credential_byok_fields",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropCheckConstraint(
                name: "CK_provider_credential_test_status",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "last_test_status",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "last_tested_at",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "masked_key",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "gateway",
                table: "provider_credential");

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_organization_id",
                schema: "gateway",
                table: "provider_credential",
                column: "organization_id");
        }
    }
}
