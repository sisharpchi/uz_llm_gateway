using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringBudgetWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_budget_budget_policy_policy_id",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropIndex(
                name: "IX_reservation_budget_policy_id",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropIndex(
                name: "IX_budget_policy_api_key_id",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropIndex(
                name: "IX_budget_policy_project_id",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropPrimaryKey(
                name: "PK_budget_bucket",
                schema: "billing",
                table: "budget_bucket");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "window_start",
                schema: "billing",
                table: "reservation_budget",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "period",
                schema: "billing",
                table: "budget_policy",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Lifetime");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "window_start",
                schema: "billing",
                table: "budget_bucket",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddPrimaryKey(
                name: "PK_budget_bucket",
                schema: "billing",
                table: "budget_bucket",
                columns: new[] { "policy_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "IX_reservation_budget_policy_id_window_start",
                schema: "billing",
                table: "reservation_budget",
                columns: new[] { "policy_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_api_key_id_period",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "api_key_id", "period" },
                unique: true,
                filter: "api_key_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_project_id_period",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "project_id", "period" },
                unique: true,
                filter: "api_key_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_budget_policy_period",
                schema: "billing",
                table: "budget_policy",
                sql: "period IN ('Lifetime', 'Daily', 'Weekly', 'Monthly')");

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_budget_budget_bucket_policy_id_window_start",
                schema: "billing",
                table: "reservation_budget",
                columns: new[] { "policy_id", "window_start" },
                principalSchema: "billing",
                principalTable: "budget_bucket",
                principalColumns: new[] { "policy_id", "window_start" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back recurring financial history would merge distinct
            // windows into one bucket and lose the admission-time identity.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM billing.budget_policy WHERE period <> 'Lifetime')
                     OR EXISTS (SELECT 1 FROM billing.budget_bucket
                                WHERE window_start <> TIMESTAMPTZ '1970-01-01 00:00:00+00') THEN
                    RAISE EXCEPTION 'Recurring budget history cannot be downgraded';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_reservation_budget_budget_bucket_policy_id_window_start",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropIndex(
                name: "IX_reservation_budget_policy_id_window_start",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropIndex(
                name: "IX_budget_policy_api_key_id_period",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropIndex(
                name: "IX_budget_policy_project_id_period",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropCheckConstraint(
                name: "CK_budget_policy_period",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropPrimaryKey(
                name: "PK_budget_bucket",
                schema: "billing",
                table: "budget_bucket");

            migrationBuilder.DropColumn(
                name: "window_start",
                schema: "billing",
                table: "reservation_budget");

            migrationBuilder.DropColumn(
                name: "period",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.DropColumn(
                name: "window_start",
                schema: "billing",
                table: "budget_bucket");

            migrationBuilder.AddPrimaryKey(
                name: "PK_budget_bucket",
                schema: "billing",
                table: "budget_bucket",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_budget_policy_id",
                schema: "billing",
                table: "reservation_budget",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_api_key_id",
                schema: "billing",
                table: "budget_policy",
                column: "api_key_id",
                unique: true,
                filter: "api_key_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_project_id",
                schema: "billing",
                table: "budget_policy",
                column: "project_id",
                unique: true,
                filter: "api_key_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_reservation_budget_budget_policy_policy_id",
                schema: "billing",
                table: "reservation_budget",
                column: "policy_id",
                principalSchema: "billing",
                principalTable: "budget_policy",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
