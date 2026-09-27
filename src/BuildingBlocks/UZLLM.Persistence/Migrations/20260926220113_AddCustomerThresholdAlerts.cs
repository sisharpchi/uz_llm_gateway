using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerThresholdAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_budget_policy_organization_id_project_id",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_budget_policy_organization_id_project_id_id",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "organization_id", "project_id", "id" });

            migrationBuilder.CreateTable(
                name: "notification_destination",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encrypted_chat_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_destination", x => x.id);
                    table.UniqueConstraint("AK_notification_destination_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("CK_notification_destination_status", "status IN ('Verified', 'Disabled')");
                    table.ForeignKey(
                        name: "FK_notification_destination_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_challenge",
                schema: "ops",
                columns: table => new
                {
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_challenge", x => x.token_hash);
                    table.ForeignKey(
                        name: "FK_telegram_link_challenge_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_telegram_link_challenge_user_account_id",
                        column: x => x.account_id,
                        principalSchema: "iam",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_rule",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    budget_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    threshold = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    armed = table.Column<bool>(type: "boolean", nullable: false),
                    episode = table.Column<long>(type: "bigint", nullable: false),
                    last_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_evaluation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rule", x => x.id);
                    table.CheckConstraint("CK_alert_rule_scope", "(type = 'LowBalance' AND project_id IS NULL AND budget_policy_id IS NULL AND threshold <= 9007199254740991) OR (type = 'BudgetWarning' AND project_id IS NOT NULL AND budget_policy_id IS NOT NULL AND threshold BETWEEN 1 AND 10000) OR (type = 'ErrorSpike' AND budget_policy_id IS NULL AND threshold BETWEEN 1 AND 10000)");
                    table.CheckConstraint("CK_alert_rule_threshold", "threshold >= 0");
                    table.CheckConstraint("CK_alert_rule_type", "type IN ('LowBalance', 'BudgetWarning', 'ErrorSpike')");
                    table.ForeignKey(
                        name: "FK_alert_rule_budget_policy_organization_id_project_id_budget_~",
                        columns: x => new { x.organization_id, x.project_id, x.budget_policy_id },
                        principalSchema: "billing",
                        principalTable: "budget_policy",
                        principalColumns: new[] { "organization_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_rule_notification_destination_organization_id_destina~",
                        columns: x => new { x.organization_id, x.destination_id },
                        principalSchema: "ops",
                        principalTable: "notification_destination",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_rule_organization_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "org",
                        principalTable: "organization",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_rule_project_organization_id_project_id",
                        columns: x => new { x.organization_id, x.project_id },
                        principalSchema: "gateway",
                        principalTable: "project",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_event",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode = table.Column<long>(type: "bigint", nullable: false),
                    observed_value = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_event", x => x.id);
                    table.CheckConstraint("CK_alert_event_status", "status IN ('Pending', 'Delivered', 'Suppressed')");
                    table.ForeignKey(
                        name: "FK_alert_event_alert_rule_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "ops",
                        principalTable: "alert_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_event_rule_id_episode",
                schema: "ops",
                table: "alert_event",
                columns: new[] { "rule_id", "episode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_rule_enabled_next_evaluation_at",
                schema: "ops",
                table: "alert_rule",
                columns: new[] { "enabled", "next_evaluation_at" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_rule_organization_id_destination_id",
                schema: "ops",
                table: "alert_rule",
                columns: new[] { "organization_id", "destination_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_rule_organization_id_id",
                schema: "ops",
                table: "alert_rule",
                columns: new[] { "organization_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_rule_organization_id_project_id_budget_policy_id",
                schema: "ops",
                table: "alert_rule",
                columns: new[] { "organization_id", "project_id", "budget_policy_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_destination_organization_id_id",
                schema: "ops",
                table: "notification_destination",
                columns: new[] { "organization_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_destination_organization_id_type",
                schema: "ops",
                table: "notification_destination",
                columns: new[] { "organization_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenge_account_id",
                schema: "ops",
                table: "telegram_link_challenge",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenge_expires_at",
                schema: "ops",
                table: "telegram_link_challenge",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenge_organization_id",
                schema: "ops",
                table: "telegram_link_challenge",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_event",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "telegram_link_challenge",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "alert_rule",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "notification_destination",
                schema: "ops");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_budget_policy_organization_id_project_id_id",
                schema: "billing",
                table: "budget_policy");

            migrationBuilder.CreateIndex(
                name: "IX_budget_policy_organization_id_project_id",
                schema: "billing",
                table: "budget_policy",
                columns: new[] { "organization_id", "project_id" });
        }
    }
}
