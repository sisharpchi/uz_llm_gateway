using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorMfaHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_totp_step",
                schema: "iam",
                table: "operator_access",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mfa_enrollment_expires_at",
                schema: "iam",
                table: "operator_access",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_totp_step",
                schema: "iam",
                table: "operator_access");

            migrationBuilder.DropColumn(
                name: "mfa_enrollment_expires_at",
                schema: "iam",
                table: "operator_access");
        }
    }
}
