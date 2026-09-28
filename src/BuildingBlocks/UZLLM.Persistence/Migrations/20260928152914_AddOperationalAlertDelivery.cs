using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAlertDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "notification_event_id",
                schema: "ops",
                table: "operational_alert",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "notified_at",
                schema: "ops",
                table: "operational_alert",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_alert_notification_event_id",
                schema: "ops",
                table: "operational_alert",
                column: "notification_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operational_alert_notification_event_id",
                schema: "ops",
                table: "operational_alert");

            migrationBuilder.DropColumn(
                name: "notification_event_id",
                schema: "ops",
                table: "operational_alert");

            migrationBuilder.DropColumn(
                name: "notified_at",
                schema: "ops",
                table: "operational_alert");
        }
    }
}
