using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAdmissionLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "concurrency_limit",
                schema: "gateway",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requests_per_minute",
                schema: "gateway",
                table: "project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "concurrency_limit",
                schema: "gateway",
                table: "api_key",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requests_per_minute",
                schema: "gateway",
                table: "api_key",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_project_admission_limits",
                schema: "gateway",
                table: "project",
                sql: "(requests_per_minute IS NULL OR requests_per_minute BETWEEN 1 AND 1000000) AND (concurrency_limit IS NULL OR concurrency_limit BETWEEN 1 AND 10000)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_api_key_admission_limits",
                schema: "gateway",
                table: "api_key",
                sql: "(requests_per_minute IS NULL OR requests_per_minute BETWEEN 1 AND 1000000) AND (concurrency_limit IS NULL OR concurrency_limit BETWEEN 1 AND 10000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM gateway.project WHERE requests_per_minute IS NOT NULL OR concurrency_limit IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM gateway.api_key WHERE requests_per_minute IS NOT NULL OR concurrency_limit IS NOT NULL) THEN
                        RAISE EXCEPTION 'Tenant limits cannot be downgraded while overrides exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_project_admission_limits",
                schema: "gateway",
                table: "project");

            migrationBuilder.DropCheckConstraint(
                name: "CK_api_key_admission_limits",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "concurrency_limit",
                schema: "gateway",
                table: "project");

            migrationBuilder.DropColumn(
                name: "requests_per_minute",
                schema: "gateway",
                table: "project");

            migrationBuilder.DropColumn(
                name: "concurrency_limit",
                schema: "gateway",
                table: "api_key");

            migrationBuilder.DropColumn(
                name: "requests_per_minute",
                schema: "gateway",
                table: "api_key");
        }
    }
}
