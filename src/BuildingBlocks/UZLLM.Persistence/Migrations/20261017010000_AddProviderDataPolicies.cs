using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UZLLM.Persistence.Migrations;

[DbContext(typeof(FoundationDbContext))]
[Migration("20261017010000_AddProviderDataPolicies")]
public sealed class AddProviderDataPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE catalog.provider_data_policy (
          id uuid NOT NULL CONSTRAINT "PK_provider_data_policy" PRIMARY KEY,
          provider_id uuid NOT NULL CONSTRAINT "FK_provider_data_policy_provider_provider_id" REFERENCES catalog.provider(id) ON DELETE RESTRICT,
          retention varchar(30) NOT NULL,
          training varchar(30) NOT NULL,
          residency varchar(30) NOT NULL,
          regions text[] NOT NULL,
          scope varchar(500) NOT NULL,
          conditions varchar(2000) NOT NULL,
          source_url varchar(2048) NOT NULL,
          source_observed_at timestamptz NOT NULL,
          published_at timestamptz NOT NULL,
          published_by_account_id uuid NOT NULL CONSTRAINT "FK_provider_data_policy_user_published_by_account_id" REFERENCES iam."user"(id) ON DELETE RESTRICT,
          CONSTRAINT "CK_provider_data_policy_retention" CHECK (retention IN ('Unknown', 'MayRetain', 'NoRetentionReported')),
          CONSTRAINT "CK_provider_data_policy_training" CHECK (training IN ('Unknown', 'MayTrain', 'NoTrainingReported')),
          CONSTRAINT "CK_provider_data_policy_residency" CHECK (cardinality(regions) <= 16 AND ((residency = 'Unknown' AND cardinality(regions) = 0) OR (residency = 'ProviderReported' AND cardinality(regions) > 0))),
          CONSTRAINT "CK_provider_data_policy_dates" CHECK (source_observed_at > '1970-01-01'::timestamptz AND source_observed_at <= published_at)
        );
        CREATE UNIQUE INDEX "IX_provider_data_policy_provider_id_source_observed_at"
          ON catalog.provider_data_policy (provider_id, source_observed_at);
        CREATE INDEX "IX_provider_data_policy_published_by_account_id"
          ON catalog.provider_data_policy (published_by_account_id);
        CREATE FUNCTION catalog.prevent_data_policy_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Provider policy evidence is append-only'; END; $$;
        CREATE TRIGGER prevent_data_policy_mutation BEFORE UPDATE OR DELETE ON catalog.provider_data_policy
          FOR EACH ROW EXECUTE FUNCTION catalog.prevent_data_policy_mutation();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM catalog.provider_data_policy LIMIT 1) THEN
            RAISE EXCEPTION 'refuse to discard provider data-policy evidence';
          END IF;
        END $$;
        DROP TABLE catalog.provider_data_policy;
        DROP FUNCTION catalog.prevent_data_policy_mutation();
        """);
}
