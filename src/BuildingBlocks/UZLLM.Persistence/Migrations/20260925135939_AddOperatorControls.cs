using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "organization_id",
                schema: "audit",
                table: "audit_event",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "platform_control",
                schema: "ops",
                columns: table => new
                {
                    feature = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_control", x => x.feature);
                    table.CheckConstraint("CK_platform_control_feature", "feature IN ('ManagedTraffic', 'TopUps')");
                });

            migrationBuilder.Sql("INSERT INTO ops.platform_control (feature, enabled, updated_at) VALUES ('ManagedTraffic', true, now()), ('TopUps', true, now());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM audit.audit_event WHERE organization_id IS NULL) THEN RAISE EXCEPTION 'Global operator audit events must be retained before rollback'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "platform_control",
                schema: "ops");

            migrationBuilder.AlterColumn<Guid>(
                name: "organization_id",
                schema: "audit",
                table: "audit_event",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
