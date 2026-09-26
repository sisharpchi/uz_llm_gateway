using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamInvitationsAndProjectGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitation",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    invited_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation", x => x.id);
                    table.CheckConstraint("CK_org_invitation_expiry", "expires_at > created_at");
                    table.CheckConstraint("CK_org_invitation_role", "role IN ('Admin', 'Developer', 'BillingViewer', 'ReadOnly')");
                    table.ForeignKey(
                        name: "FK_invitation_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invitation_user_invited_by_account_id",
                        column: x => x.invited_by_account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_grant",
                schema: "org",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_grant", x => new { x.organization_id, x.account_id, x.project_id });
                    table.ForeignKey(
                        name: "FK_project_grant_member_organization_id_account_id",
                        columns: x => new { x.organization_id, x.account_id },
                        principalSchema: "org",
                        principalTable: "member",
                        principalColumns: new[] { "organization_id", "account_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_grant_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_org_member_role",
                schema: "org",
                table: "member",
                sql: "role IN ('Owner', 'Admin', 'Developer', 'BillingViewer', 'ReadOnly')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_org_member_status",
                schema: "org",
                table: "member",
                sql: "status IN ('Active', 'Revoked')");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_invited_by_account_id",
                schema: "org",
                table: "invitation",
                column: "invited_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_organization_id_email",
                schema: "org",
                table: "invitation",
                columns: new[] { "organization_id", "email" },
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_token_hash",
                schema: "org",
                table: "invitation",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_grant_organization_id_project_id",
                schema: "org",
                table: "project_grant",
                columns: new[] { "organization_id", "project_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation",
                schema: "org");

            migrationBuilder.DropTable(
                name: "project_grant",
                schema: "org");

            migrationBuilder.DropCheckConstraint(
                name: "CK_org_member_role",
                schema: "org",
                table: "member");

            migrationBuilder.DropCheckConstraint(
                name: "CK_org_member_status",
                schema: "org",
                table: "member");
        }
    }
}
