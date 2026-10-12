# Tenant admission limits — LIMITS-002

## Scope

FR-103/104, FR-023/024/100–102; ADR-001/003/012/018. This user-selected P1
task does not close externally blocked OPS-006 staging qualification.
ApiKeys owns admission contracts/policy; Projects owns project identity;
Billing owns money and recurring budgets. No new module, financial model,
Redis financial state, provider quota UI or project defaults. Screens belong
to FRONTEND-006 after PROJECTS-002.

## Management contract

```text
GET/PUT /management/v1/projects/{projectId}/limits
GET/PUT /management/v1/api-keys/{apiKeyId}/limits
```

PUT replaces the entire override, e.g. `{"requestsPerMinute":30,"concurrency":2}`.
Omitted/null fields inherit the operator ceiling; `{}` restores defaults,
**never unlimited**. Unknown fields, zero, negative, fractional and
above-ceiling values return 400. Absolute bounds: 1–1,000,000 RPM and
1–10,000 concurrent requests, not commercial plan entitlements. Operators can
configure much lower ceilings. GET/PUT return `configured`, `effective` and
`ceiling` objects. Values are per-scope: admission must satisfy **both** key
and project limits.

Session required (401); reads use ReadProjects; project writes ManageProjects
(Owner/Admin); key writes ManageApiKeys (Owner/Admin or explicitly granted
Developer). ReadOnly/BillingViewer cannot mutate. Current membership/grants
are checked on each command. Foreign tenant returns 403; missing resource 404;
PUT requires CSRF. Responses are no-store. Successful writes and
`project.limits_changed`/`api_key.limits_changed` audit entries share one
PostgreSQL transaction; audit failure rolls back. Concurrent PUTs atomically
replace the whole policy, last committed writer wins; no field-merge or
optimistic-edit promise.

Existing financial budget routes remain separate:
`GET /management/v1/projects/{projectId}/budgets` and
`PUT /management/v1/projects/{projectId}/budgets/{Lifetime|Daily|Weekly|Monthly}`.
Their non-negative Int64 `limitMicroUsd` is exact money; zero is a valid
deny-new-spend cap, not unlimited. ManageBilling and CSRF apply. No arbitrary
commercial dollar maximum or counter-reset operation is introduced.
Wallet-first row locking serializes changes with admission. Installing a new
policy with matching active holds returns 409. Lowering an existing cap below
captured+reserved spend preserves holds and blocks new admission; release and
settlement retain original admission buckets. Budget and RPM/concurrency writes
are independent transactions, not one atomic settings API.

## Gateway and failure semantics

`TenantAdmissionLimiter` reads one no-tracking PostgreSQL joined projection per
new admission, checking org/project/key scope and active key/project, then passes
the bounded policy to existing atomic Redis Lua admission. No policy cache,
versioned Redis namespace or rolling-minute counter/lease reset is introduced.
Scope-keyed state is shared across nodes. Gateway ceilings clamp stored values
again even if Management configuration differs.

A committed change applies to policy snapshots acquired afterward. An admission
already holding a pre-change snapshot may finish under it; editing limits does
not retroactively revoke/cancel in-flight work. No PostgreSQL transaction spans
Redis or provider I/O. Lowering concurrency never evicts existing streams; new
leases wait until occupancy permits admission.

Missing/unavailable PostgreSQL policy fails closed (Gateway 503) before Redis
admission, reservation or dispatch. Redis outage also fails closed, including
BYOK; there is no per-node fallback. Cold/restarted Redis waits the full
`Limits:MaxLeaseSeconds` recovery horizon. Release delegates directly to Redis
without reading PostgreSQL; already reserved work retains existing financial
finalization/recovery. Rate/concurrency denials remain 429 with Retry-After.

EF may wrap a transient Npgsql read error in InvalidOperationException. The
limiter recognizes only direct database/timeouts or that wrapper with a database/
timeout inner exception; unrelated programming errors and cancellation propagate.

## Configuration and migration

Use matching startup ceilings on Management and every Gateway:

| Setting | Default |
| --- | ---: |
| `Gateway:Limits:ApiKeyRpm` | 60 |
| `Gateway:Limits:ProjectRpm` | 600 |
| `Gateway:Limits:ApiKeyConcurrency` | 8 |
| `Gateway:Limits:ProjectConcurrency` | 64 |

These are not a tenant-editable plan catalog. GET describes Management ceilings;
Gateway ceilings remain authoritative. Operator changes need coordinated host
restart. Tenants cannot change lease/recovery settings; do not shorten the
recovery horizon while old leases may remain active.

Migration `20261012130409_AddTenantAdmissionLimits` adds nullable
`requests_per_minute`/`concurrency_limit` to `gateway.project` and `gateway.api_key`
with bounded CHECK constraints. Existing rows inherit defaults. Apply explicitly
with Migrator before new hosts run. Upgrade every Gateway instance before
deploying Management that exposes limit writes: old Gateways ignore overrides.
Do not roll Gateway binaries back while overrides remain; first withdraw the
new control-plane surface and either keep admission closed or perform authorized
policy resets. A schema downgrade guard cannot protect a binary-only rollback.
Down refuses to discard non-null overrides;
authorized reset is required first. This never flushes Redis/shortens leases.
Fixture-only cleanup enables disposable database reset, not production cleanup.
No shared/local development database was migrated for validation.

## Acceptance evidence

| Requirement | Exact tests |
| --- | --- |
| Changes take effect | `Fresh_policy_is_applied_each_admission_and_cancellation_is_not_swallowed`; `Two_nodes_enforce_live_PG_limits_without_resetting_RPM_or_existing_leases` |
| Invalid/unbounded plans rejected | `Invalid_or_unbounded_limits_are_rejected`; `Configuration_above_absolute_bounds_is_rejected_and_lower_gateway_caps_always_win`; `Limit_HTTP_controls_enforce_current_roles_csrf_tenant_bounds_and_reset` |
| Redis loss follows documented mode | two-node test above; existing `Redis_epoch_change_closes_new_admission_for_full_lease_horizon` |
| Race and API tests | `Concurrent_policy_replacement_is_atomic_and_failed_audit_rolls_back_changes`; HTTP test above; two-node test admits exactly 3 of 50 concurrent requests |
| Financial controls preserve holds | extended `Every_project_and_key_window_is_a_hard_gate_and_releases_with_the_reservation`; existing `Concurrent_admission_holds_every_applicable_window_without_overspend` (50 requests); `New_daily_policy_includes_existing_captured_spend_and_rejects_active_holds`; `Budget_endpoints_enforce_owner_scope_csrf_and_key_project` |
| Migration compatibility | `Migration_preserves_existing_defaults_enforces_bounds_and_refuses_lossy_downgrade` |
| Dependency failure/release | `Missing_or_unavailable_policy_fails_closed_but_release_does_not_read_Postgres`; two-node test uses disconnected Redis and unreachable loopback PostgreSQL |
| Wire/client agreement | serializer-derived GET/PUT examples; `tenant admission limits have bounded nullable numeric controls and protected wire operations`; generated client and negative TypeScript fixtures |

Historical API-key migration fixtures now seed only project columns present at
their target version and read only the guarded permission column after a failed
downgrade. This preserves their migration targets, unsafe-downgrade assertions
and original data-retention checks; no migration test is skipped.

## Validation — 2026-10-12

Commands run from the repository root on Windows with Docker-backed disposable
PostgreSQL 17 and Redis 8:

| Command/check | Result |
| --- | --- |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; existing ASPIRE010 warning, zero errors |
| `dotnet test UZLLM.slnx --configuration Release --no-build` | PASS: 684 tests in 16 projects, zero failed/skipped; includes all 198 persistence integration tests |
| Focused `TenantLimitTests` / `TenantLimitIntegrationTests` | PASS: 18 unit cases / 4 real infrastructure+HTTP tests |
| Focused `ApiKeyPersistenceIntegrationTests` after fixture correction | PASS: 11, zero skipped |
| `npm run api:generate --prefix frontend/apps` | PASS; reviewed generated OpenAPI, examples and client |
| `npm run api:check --prefix frontend/apps` | PASS; reproducible artifacts |
| `npm run api:test --prefix frontend/apps` | PASS: 9 schema/example tests |
| `npm run api:types --prefix frontend/apps` | PASS; positive and negative typed call fixtures |
| `npm run build --prefix frontend/apps` | PASS: dashboard and admin |
| `npm run test:e2e --prefix frontend/apps` | PASS: 36; one existing Aspire-URL-specific test skipped |
| `git diff --check` / `git diff --cached --check` | PASS |

Browser regression routes are mocked and do not prove the new backend controls;
the actual HTTP/PG/Redis tests above provide their acceptance evidence. No UI was
changed, so no new visual/screenshot evidence is claimed.

Synthetic identities, disposable PostgreSQL 17/Redis 8 on one Docker host prove
correctness, not OPS-006 separate-host load/SLO qualification or live provider/
payment behavior. No coverage/mutation-score claim. Optional static pairing
analyzer unavailable because Python is not installed.
