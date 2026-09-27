using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundAlertWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_destination_status",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.AlterColumn<string>(
                name: "key_version",
                schema: "ops",
                table: "notification_destination",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<byte[]>(
                name: "encrypted_chat_id",
                schema: "ops",
                table: "notification_destination",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<byte[]>(
                name: "encrypted_webhook_secret",
                schema: "ops",
                table: "notification_destination",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endpoint_url",
                schema: "ops",
                table: "notification_destination",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_key_version",
                schema: "ops",
                table: "notification_destination",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_destination_status",
                schema: "ops",
                table: "notification_destination",
                sql: "status IN ('Verified', 'Active', 'Disabled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_destination_type",
                schema: "ops",
                table: "notification_destination",
                sql: "(type = 'Telegram' AND encrypted_chat_id IS NOT NULL AND key_version IS NOT NULL AND endpoint_url IS NULL AND encrypted_webhook_secret IS NULL AND webhook_key_version IS NULL) OR (type = 'Webhook' AND encrypted_chat_id IS NULL AND key_version IS NULL AND endpoint_url IS NOT NULL AND encrypted_webhook_secret IS NOT NULL AND webhook_key_version IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM ops.notification_destination WHERE type = 'Webhook') THEN RAISE EXCEPTION 'Export and remove outbound webhook destinations before rolling back'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_destination_status",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_destination_type",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_destination_status",
                schema: "ops",
                table: "notification_destination",
                sql: "status IN ('Verified', 'Disabled')");

            migrationBuilder.DropColumn(
                name: "encrypted_webhook_secret",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.DropColumn(
                name: "endpoint_url",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.DropColumn(
                name: "webhook_key_version",
                schema: "ops",
                table: "notification_destination");

            migrationBuilder.AlterColumn<string>(
                name: "key_version",
                schema: "ops",
                table: "notification_destination",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "encrypted_chat_id",
                schema: "ops",
                table: "notification_destination",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
