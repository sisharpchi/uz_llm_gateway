using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreExecutionRejectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_usage_request_execution",
                schema: "usage",
                table: "request");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usage_attempt_execution",
                schema: "usage",
                table: "attempt");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usage_request_execution",
                schema: "usage",
                table: "request",
                sql: "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usage_attempt_execution",
                schema: "usage",
                table: "attempt",
                sql: "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_usage_request_execution",
                schema: "usage",
                table: "request");

            migrationBuilder.DropCheckConstraint(
                name: "CK_usage_attempt_execution",
                schema: "usage",
                table: "attempt");

            // The older schema cannot represent this terminal state during rollback.
            migrationBuilder.Sql("UPDATE usage.request SET execution_state = 'Failed' WHERE execution_state = 'RejectedBeforeExecution';");
            migrationBuilder.Sql("UPDATE usage.attempt SET execution_state = 'Failed' WHERE execution_state = 'RejectedBeforeExecution';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usage_request_execution",
                schema: "usage",
                table: "request",
                sql: "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_usage_attempt_execution",
                schema: "usage",
                table: "attempt",
                sql: "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown')");
        }
    }
}
