using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformProviderCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_credential",
                schema: "gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credential_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encrypted_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    wrapped_data_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    kms_key_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_credential", x => x.id);
                    table.CheckConstraint("CK_provider_credential_ciphertext", "octet_length(encrypted_secret) > 28 AND octet_length(wrapped_data_key) = 60");
                    table.CheckConstraint("CK_provider_credential_status", "status IN ('Active', 'Disabled')");
                    table.CheckConstraint("CK_provider_credential_type_scope", "(credential_type = 'Platform' AND organization_id IS NULL) OR (credential_type = 'BYOK' AND organization_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_provider_credential_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_credential_provider_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "catalog",
                        principalTable: "provider",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_organization_id",
                schema: "gateway",
                table: "provider_credential",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_credential_provider_id_credential_type_status",
                schema: "gateway",
                table: "provider_credential",
                columns: new[] { "provider_id", "credential_type", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_credential",
                schema: "gateway");
        }
    }
}
