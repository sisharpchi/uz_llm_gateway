# REPORTING-003 — Authorized statement APIs

## Approval and ownership

The subsequent Continue promotes only REPORTING-003. Its REPORTING-002/DX-002/
USAGE-005 dependencies are completed; OPS-006 stays Blocked. This independent P2
increment is not paid-launch approval. Billing owns typed version reads; the
Management facade owns authorization/cursors/operator orchestration; endpoints
own HTTP. Existing schema/publication/Worker remain authoritative. No migration,
Gateway change, UI, fiscal invoice, SLI/SLA, notification, bearer or repair API.

## Customer contract

Base `/management/v1/organizations/{organizationId}/statements`:

| Method/path | Behavior |
|---|---|
| `GET /` | All immutable versions, descending UTC posting month/revision. Optional inclusive from/exclusive to must be UTC month starts; default all months. Page default 20/max 100. |
| `GET /{id}` | Allowlisted metadata for an explicit immutable version, not a moving latest alias. |
| `GET /{id}/lines` | Ascending stable ordinals; page default 50/max 100. Missing/alien version is 404, not an empty page. |
| `GET /{id}/export?format=csv` | Original stored CSV bytes; omitted format means csv; other values are 400. No source/snapshot/JSON or partial-page export. |

Owner/Admin/BillingViewer use ReadBilling. Developer/ReadOnly, including project
grants, cannot read org-wide money. Operator grant is not tenant membership.
Gateway/automation Bearer credentials do not authenticate. The facade revalidates
session/permission independently of HTTP; actor identity is not command JSON.

Metadata includes non-fiscal label/kind, organization, UTC posting period,
revision/generation/predecessor, schema/calculation version, cut-off/digest,
coverage/pending counts, totals, balance/holds/debt, delta, publication time and
line count. Integer micro-USD amounts remain strings. Generation equals that
version's frozen revision number, not the live head; stale revision commands
conflict rather than silently targeting latest. The bounded slice omits
payment/FX annex and source manifests; no current FX/pricing/analytics conversion.
ReconciledSnapshot with pending counts is not final merchant/tax reconciliation.
PostgreSQL strips Lines/PaymentAnnotations before metadata transfer; typed DTOs
provide a second allowlist. No SnapshotJson, raw source/ledger metadata,
operator reasons/actors, merchant references or internal provider cost/exposure.

Cursors use the existing Data Protection ring with statement-v1 purpose, binding
organization/month filters/page size or organization/version/page size. They
are continuations, not authorization. Tampered/foreign-scope/retired-key cursor:
400, restart paging. Management nodes share their already-required key ring.
Lists are live keyset traversals, not a multi-page database snapshot.

CSV has a fixed non-fiscal label/unit-bearing header, finite categories, UUIDs,
UTC timestamps and signed integer amounts; no user-text formulas/templates/fetch.
UTF-8 text/csv, version-UUID attachment, nosniff, Content-Length and fixed
X-UZLLM-Report-Sha256; no range processing. Shared client verifies length/digest
before returning a Blob. Disconnect may truncate delivery; retry same version.
Audit statement.export.requested records real actor/version/format/digest before
response, not complete delivery. Audit failure/cancellation prevents return.

## Operator contract and recovery

Base `/management/v1/admin/organizations/{organizationId}/reports`:

| Method/path | Request/result |
|---|---|
| `POST /statements` | `{periodStart,idempotencyKey,expectedGeneration:0,reason,schemaVersion:1}`; 202 JSON operation + Location job URL. |
| `POST /statements/{id}/revisions` | `{idempotencyKey,expectedGeneration,reason,schemaVersion:1}`; positive current generation, explicit predecessor; inherit its period. 202 same job shape. |
| `GET /jobs/{id}` | Tenant-scoped operation identity/status/version/failure and independent Worker metadata/state. |

All require current operator grant/recent MFA; mutations additionally require
session CSRF. Facade rechecks grant/MFA independently of HTTP claims. Initial
builds cannot bypass revisions. Existing organization/kind/UUID receipts replay
the same operation before checking newer head; changed terms/generation: 409.
Replay remains 202 even if Published/Failed: accepted command, not newly queued
work. Client consumes 202 JSON while preserving existing empty auth-202 replies.

REPORTING-002 owns receipt/request-audit/job and publication/audit/outbox
transactions unchanged. GET never retries/builds. Queued/Leased/AwaitingReclaim/
Completed/DeadLettered/Missing are distinct from Pending/Failed/Published.
Job metadata excludes payload, lease owner and arbitrary error text. Resolve
tenant operation before fixed type/dedup lookup. Pending plus DeadLettered after
crash is explicit; no attempt reset, receipt deletion, silent restart or repair
endpoint. Document 48's operator triage limitation remains.

## Bounds and operations

JSON commands: 8192 bytes before Minimal API binding, including chunked input;
unknown fields rejected. Linked maximum 30-second deadline/client cancellation
covers reporting I/O. Existing 100,000-source/32-MiB/five-attempt publication
bounds remain. Global Management no-store covers success/security/binding/errors.
No wallet/ledger/hold/budget/debt mutation, new secrets or startup migrations.

401 session; 403 role/MFA/CSRF; 404 missing/alien version, operation or build org;
400 invalid month/fields/cursor/limit/format; 409 terms/generation; 413 body/artifact
bound; 415 non-JSON command media; 503 unavailable/corrupt dependency; 504 server
deadline. Safe errors retain correlation, not raw exception/source/secret data.
Generated OpenAPI/examples/client describe all seven routes and headers; existing
v1/180-day breaking policy remains. [HTTP examples](../src/UZLLM.Management.Api/statements.http).
Reporting archive/retention/legal hold, production restore, merchant/provider,
fiscal/SLA approvals and document 18 launch gates remain independent prerequisites.

## Validation evidence

Environment: Windows, .NET SDK 10.0.401/VSTest/xUnit 2, Node 24.19.0,
npm 11.17.0, Docker 29.7.2; existing disposable PostgreSQL 17/Redis 8 fixtures.
No shared database, live merchant/provider or paid-admission operation.

| Requirement | Exact new test evidence |
|---|---|
| ENT-001/003 — non-fiscal label, posting month, exact frozen totals/units | `Statement_api_build_revision_export_and_paging_preserve_exact_posting_month_and_old_bytes`; `Statement_month_ranges_and_initial_revision_generation_are_not_silently_coerced`. |
| ENT-005 — stable accepted identity, explicit revision/replay/conflict | `Statement_api_build_revision_export_and_paging_preserve_exact_posting_month_and_old_bytes`; unchanged REPORTING-002 parallel/receipt tests remain in the baseline. |
| ENT-017 — tenant/ReadBilling/operator/MFA/CSRF/no-store | `Statement_api_enforces_roles_tenant_ids_operator_Mfa_Csrf_and_body_bounds`; `Statement_facade_rechecks_session_ReadBilling_and_recent_operator_Mfa_before_any_store_access`. |
| ENT-017 — cursor/filter/version and safe CSV | `Statement_cursors_bind_tenant_filters_page_size_and_version_without_exposing_internal_sources`; PG build/revision/export test; Node `statement contracts pin immutable versions session security exact money and CSV verification`. |
| ENT-018 — bounds, audit, deadline/dependency and disconnect | `Statement_export_validates_format_digest_and_audit_before_returning_version_bytes`; `Statement_http_failures_return_safe_no_artifact_errors`; `Statement_client_disconnect_propagates_cancellation_without_artifact_or_success`; PG body-bound/security test. |
| Terminal job truth, no silent repair/restart | `Statement_job_exposes_dead_letter_even_when_crash_left_operation_pending_without_restarting_work`; `Statement_job_transport_states_do_not_override_operation_state_or_requeue`. |
| Client 202 and verified downloads | Node `statement client consumes 202 jobs preserves empty auth replies and verifies CSV before returning Blob`; negative generated TypeScript fixtures. |

Final unchanged Release baseline: **1133 PASS, 0 FAIL, 0 SKIP**, sixteen Completed
TRX reports, executed = total = passed. All **27 new .NET cases** pass (24 fast
API cases, three expanded PostgreSQL/HTTP cases). Gateway contracts 384,
PostgreSQL 267, architecture 15, Billing 88 and AppHost 2 pass. PostgreSQL took
18m17s; no timeout/skip was substituted for completion. New required-field,
month/filter/keyset and both-mutation CSRF assertions are in this final run.

| Command | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS, only existing ASPIRE010. |
| `dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx --results-directory artifacts/test-results/reporting-003-final` | 1133 PASS; project serialization preserves in-test concurrency. |
| `npm ci --prefix frontend/apps` | PASS; existing advisory below, no lock/dependency change. |
| `npm run api:generate --prefix frontend/apps` / `npm run api:check --prefix frontend/apps` | PASS, regenerated and exact drift match. |
| `npm run api:test --prefix frontend/apps` | 18 PASS, including two new client/schema cases. |
| `npm run api:types --prefix frontend/apps` / `npm run build --prefix frontend/apps` | PASS, dashboard and admin. |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- auth-recovery.spec.ts usage.spec.ts --workers=1` | 13 PASS in one worker; existing mocked-network auth/export UX regression, not a new statement UI or live backend proof. |
| `dotnet ef migrations has-pending-model-changes --project src/BuildingBlocks/UZLLM.Persistence/UZLLM.Persistence.csproj --configuration Release --no-build` | PASS, synthetic design-only connection; no DB connection or migration. |
| Task documentation file links / `git diff --check` / staged whitespace/scope review | PASS before the single-task commit. |

Existing 97 Management paths and all their schemas remain semantically
unchanged; seven paths added. Focused fast API 24/24 and early PG/HTTP 3/3 runs
also passed; the final full run is the evidence for expanded assertions.
The task-only local commit closes REPORTING-003. No follow-on task is started.
The initial browser wrapper invocation consumed the workers flag and ran two
workers (13 PASS); the direct workspace command above confirms the requested
serial run (13 PASS). No unrelated npm script change was made.

Initial test compilation needed missing cryptography/Management extension
imports. One fast error test incorrectly used the tiny deadline intended only
for the timeout case; non-timeout fixtures now use the real 30-second default.
Two PG rechecks exposed test setup, not statement money mutation: creating the
second tenant after the financial fingerprint invoked the existing empty-wallet
trigger. Setup now precedes the baseline; exact financial rows are compared
before/after each reporting phase. The
earlier whole-solution run was deliberately interrupted after the final CSRF
assertion expansion; it is not passing final evidence. Final results use a
separate `artifacts/test-results/reporting-003-final` directory.

Quality review covers behavior partitions/negative side effects, not a claimed
mutation/coverage percentage. Static pairing was attempted once; Python was
unavailable (Windows Store alias), so no pairing report is claimed. Scratch
notes remain non-stageable Git metadata. No UI/browser screenshot acceptance
applies: product pages and root mockup are unchanged.

`npm ci` succeeds, but the diagnostic `npm audit --prefix frontend/apps --json`
exits 1: existing dev-only source-map-js 1.2.1 lock entries in both workspaces
have [GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q)
(high DoS). HEAD and current lock versions are identical; no audit-fix/package
upgrade is included. This needs separate dependency assessment/remediation,
not a false audit pass. The existing >500-kB dashboard chunk warning also remains.
