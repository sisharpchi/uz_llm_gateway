using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenOperationalWorkLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_exhausted_lease",
                schema: "ops",
                table: "outbox",
                column: "lease_expires_at",
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL AND attempt_count >= max_attempts");

            migrationBuilder.CreateIndex(
                name: "ix_job_exhausted_lease",
                schema: "ops",
                table: "job",
                column: "lease_expires_at",
                filter: "completed_at IS NULL AND dead_lettered_at IS NULL AND attempt_count >= max_attempts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_exhausted_lease",
                schema: "ops",
                table: "outbox");

            migrationBuilder.DropIndex(
                name: "ix_job_exhausted_lease",
                schema: "ops",
                table: "job");
        }
    }
}
