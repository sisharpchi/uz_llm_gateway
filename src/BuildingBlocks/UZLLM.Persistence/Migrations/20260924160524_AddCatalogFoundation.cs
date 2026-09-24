using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UZLLM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.CreateTable(
                name: "model",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    context_length = table.Column<int>(type: "integer", nullable: false),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    capabilities_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model", x => x.id);
                    table.CheckConstraint("CK_catalog_model_limits", "context_length > 0 AND max_output_tokens > 0 AND max_output_tokens <= context_length");
                    table.CheckConstraint("CK_catalog_model_status", "status IN ('Active', 'Disabled')");
                });

            migrationBuilder.CreateTable(
                name: "provider",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider", x => x.id);
                    table.CheckConstraint("CK_catalog_provider_status", "status IN ('Active', 'Disabled')");
                });

            migrationBuilder.CreateTable(
                name: "provider_model",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    upstream_model_code = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    endpoint_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    capabilities_override_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_model", x => x.id);
                    table.CheckConstraint("CK_catalog_provider_model_status", "status IN ('Active', 'Disabled')");
                    table.ForeignKey(
                        name: "FK_provider_model_model_model_id",
                        column: x => x.model_id,
                        principalSchema: "catalog",
                        principalTable: "model",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_model_provider_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "catalog",
                        principalTable: "provider",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "model_price",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    input_price_micro_usd_per_million = table.Column<long>(type: "bigint", nullable: false),
                    output_price_micro_usd_per_million = table.Column<long>(type: "bigint", nullable: false),
                    cached_input_price_micro_usd_per_million = table.Column<long>(type: "bigint", nullable: true),
                    extra_pricing_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_price", x => x.id);
                    table.CheckConstraint("CK_catalog_model_price_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("CK_catalog_model_price_values", "input_price_micro_usd_per_million >= 0 AND output_price_micro_usd_per_million >= 0 AND (cached_input_price_micro_usd_per_million IS NULL OR cached_input_price_micro_usd_per_million >= 0)");
                    table.ForeignKey(
                        name: "FK_model_price_provider_model_provider_model_id",
                        column: x => x.provider_model_id,
                        principalSchema: "catalog",
                        principalTable: "provider_model",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_canonical_code",
                schema: "catalog",
                table: "model",
                column: "canonical_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_price_provider_model_id_effective_from",
                schema: "catalog",
                table: "model_price",
                columns: new[] { "provider_model_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_code",
                schema: "catalog",
                table: "provider",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_model_model_id",
                schema: "catalog",
                table: "provider_model",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_model_provider_id_model_id",
                schema: "catalog",
                table: "provider_model",
                columns: new[] { "provider_id", "model_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_model_provider_id_upstream_model_code",
                schema: "catalog",
                table: "provider_model",
                columns: new[] { "provider_id", "upstream_model_code" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE catalog.model_price
                ADD CONSTRAINT "EX_model_price_non_overlapping_effective_interval"
                EXCLUDE USING gist
                (
                    provider_model_id WITH =,
                    tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamptz), '[)') WITH &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_price",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "provider_model",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "model",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "provider",
                schema: "catalog");
        }
    }
}
