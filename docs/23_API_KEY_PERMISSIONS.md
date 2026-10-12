# APIKEYS-004 — API-key route permissions

## Scope and contract

Per-key canonical-model/provider allow/deny policies are configured through
`PUT /management/v1/api-keys/{apiKeyId}/permissions`. See the precise JSON,
normalization, role, error and snapshot rules in
[API Contracts §1a](11_API_CONTRACTS.md#1a-api-key-modelprovider-permissions).
`{}` resets restrictions; empty allowlists intentionally deny all. Neither
model fallback, smart routing, BYOK nor Hybrid may widen these permissions.
Management's project catalog still describes available project models; Gateway
model discovery is additionally key-filtered, including its price summary.

The ApiKeys Domain owns policy normalization/evaluation, Application owns the
authorized audited replacement, Infrastructure owns PostgreSQL persistence,
and Gateway applies the policy before routing/financial admission. Authentication
reads PostgreSQL without caching and is repeated immediately before reservation.
This final read is the permission snapshot boundary, not a promise to cancel
already admitted streams. Settlement/reconciliation never depend on a key
remaining enabled. No provider I/O takes place in the policy transaction.

FR-027's optional **maximum model price class** is not implemented: Catalog
has no class taxonomy, rank, publication policy or historical class semantics.
That needs an approved contract before a bounded follow-up task. This delivery
fulfills the register's model/provider scope, not every optional FR-027 idea.
Project defaults, configurable limits and lifecycle UI remain separate tasks.
Explicit user selection authorized this P1 task while OPS-006 remains blocked;
no production gate is passed by this local validation.

## Migration and rollout

`20261012120511_AddApiKeyRoutePermissions` adds four nullable `text[]` columns
to `gateway.api_key`. Existing and newly created keys default to unrestricted;
configure restrictions explicitly after creation. The whole policy update and
audit commit atomically. Concurrent full replacements are last-commit-wins;
rotation updates disjoint secret fields and preserves the persisted policy.

Apply through the explicit Migrator before deploying the new hosts; no runtime
host performs DDL. No new secret/config variable is required. Downgrade refuses
to remove configured restrictions (including deny-all empty allowlists); do
not reset real policies merely to force a downgrade. Migrator down operations
can stop at an intermediate schema version, so inspect migration history before
rolling binaries back. Disposable fixture cleanup is deliberately test-only.

## Acceptance evidence

| Requirement | Exact automated evidence |
|---|---|
| Denied route never reserves or dispatches | `Api_key_denied_route_never_reserves_or_dispatches`: model/provider/empty/pinned/fallback/BYOK/Hybrid denials assert 403, zero reserves, attempts and adapter calls. `Api_key_denied_stream_never_starts_sse_or_upstream_io` checks streaming too. |
| update/revoke applies at admission | `Api_key_changes_during_routing_apply_before_financial_admission` tightens/revokes between auth and reservation; both produce zero reserves/dispatch. `Permissions_round_trip_refresh_authentication_preserve_rotation_and_rollback_with_audit` proves real PostgreSQL freshness and rollback. |
| Tenant and gateway tests | `Key_permissions_and_rotation_endpoints_enforce_current_project_roles_csrf_and_show_once_secrets` uses real sessions/DB: outsider and ungranted Developer denied, granted Developer allowed, ReadOnly/BillingViewer and missing CSRF denied; malformed/misspelled policies rejected. |
| Eligible-only routing/holds/discovery | `Api_key_filters_providers_before_ranking_and_reservation`, `Api_key_excluded_expensive_provider_does_not_inflate_the_hold`, `Api_key_denied_provider_is_never_a_same_model_or_hybrid_failover`, `Model_listing_applies_key_permissions_to_models_and_price_summary`. |
| Policy boundaries | `Permissions_use_deny_precedence_intersection_and_distinguish_empty_from_null`, `Permissions_reject_invalid_codes`, `Permissions_enforce_exact_list_and_code_bounds_and_copy_input`. |
| Persistence/rotation/migration | `Concurrent_permissions_update_and_secret_rotation_preserve_the_new_policy`, `Permissions_migration_preserves_legacy_access_and_refuses_unsafe_downgrade`; real PostgreSQL plus existing fingerprint/rotation regressions. |
| Wire/client compatibility | Serializer-derived restricted-key example, OpenAPI schema test `API-key permission contract preserves null empty deny rules and authenticated replacement`, compile-only permission type guards and generated-artifact drift check. |

## Validation — 2026-10-12

Environment: Windows/PowerShell, .NET SDK 10.0.401, Docker 29.7.2;
PostgreSQL/Redis integration tests use disposable Testcontainers.
Focused Unit: 30 passed; focused PostgreSQL API-key suite: 12 passed.

| Command (repository root) | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; existing `ASPIRE010` CLI-bundle warning only |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/apikeys004-final` | PASS; 16 projects, 662 tests, 0 failed/skipped (PostgreSQL 194, Gateway 154, ApiKeys 30, Architecture 15, Aspire 2) |
| `npm run api:generate --prefix frontend/apps` | PASS; schema/client/examples regenerated |
| `npm run api:check --prefix frontend/apps` | PASS; no artifact drift |
| `npm run api:test --prefix frontend/apps` | PASS; 8 schema/contract tests |
| `npm run api:types --prefix frontend/apps` | PASS; positive/negative compile fixtures |
| `npm run build --prefix frontend/apps` | PASS; dashboard and admin |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | 36 PASS, 1 existing Aspire-URL-only test skipped (URLs unset) |
| `git diff --check` and `git diff --cached --check` | PASS |

Browser regression uses the existing mocked-API suite, not a substitute for
the real session/HTTP/PostgreSQL endpoint tests above. No UI screen was added;
lifecycle screens remain FRONTEND-006. No live provider/merchant traffic or
production database was used; no migration was applied to a shared database.

The optional static source/test pairing analyzer could not run because Python
is unavailable on PATH. Direct behavior tests and assertion review remain the
acceptance evidence; no line/branch coverage or mutation score is claimed.
