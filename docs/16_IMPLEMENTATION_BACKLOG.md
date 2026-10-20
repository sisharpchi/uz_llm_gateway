# Implementation Backlog

## Status and dependency rules

The historical register below records completed delivery through `NOTIFY-003`.
The **next-phase register at the end of this file** controls new work; do not
interpret an old `Completed` label as a paid-launch gate pass. Financial admission, evidence, and settlement precede managed public
traffic. A provider call never occurs inside a database transaction. P0
same-model failover precedes P1 cross-model fallback.

| Task | Status |
|---|---|
| FOUNDATION-001 | Completed |
| FOUNDATION-002 | Completed |
| FOUNDATION-003 | Completed |
| OPS-001 | Completed |
| IDENTITY-001 | Completed |
| ORGS-001 | Completed |
| AUDIT-001 | Completed |
| BILLING-001 | Completed |
| APIKEYS-001 | Completed |
| CATALOG-001 | Completed |
| USAGE-001 | Completed |
| BILLING-002 | Completed |
| LIMITS-001 | Completed |
| PROVIDER-001 | Completed |
| GATEWAY-001 | Completed |
| PAYMENT-001 | Completed |
| FRONTEND-001 | Completed |
| PROVIDER-002 | Completed |
| USAGE-002 | Completed |
| ADMIN-001 | Completed |
| OPS-002 | Completed |
| BUDGET-001 | Completed |
| APIKEYS-003 | Completed |
| NOTIFY-001 | Completed |
| TEAM-001 | Completed |
| BYOK-001 | Completed |
| BYOK-002 | Completed |
| ROUTING-003 | Completed |
| ROUTING-005 | Completed |
| PRIVACY-001 | Completed |
| PROVIDER-006 | Completed |
| PROVIDER-007 | Completed |
| PROVIDER-008 | Completed |
| PROVIDER-009 | Completed |
| ROUTING-004 | Completed |
| NOTIFY-002 | Completed |
| NOTIFY-003 | Completed |

Historical entries not marked `Completed` in the table above are `Planned`. For
`NOTIFY-001`, the earlier execution plan's prerequisite was omitted from this
register even though the deployed Identity API suppresses verification and
recovery tokens. It is restored before `TEAM-001` so invitations do not rely
on an undeliverable email flow. This is a dependency correction, not a new
product capability.

For `PAYMENT-001`, public Payme documentation and official CLICK protocol examples
are sufficient for implementation and automated contract fixtures. Live
merchant verification remains an external paid-launch prerequisite.

`FIN-001` through `FIN-004` are accepted by `BILLING-002`, where reservations,
API-key caps, and settlement exist. `BILLING-001` establishes their wallet and
ledger prerequisites without prematurely implementing reservation behavior.

`USAGE-001` persists unknown evidence and a bounded reconciliation deadline,
but cannot complete `FIN-005`'s financial release/platform-exposure outcome
without the reservation and settlement aggregate. `BILLING-002` accepts the
full `FIN-005` scenario; `USAGE-001` accepts evidence durability (`FIN-006`).
Likewise, `USAGE-001` establishes the `FR-090` request/attempt record; final
cost, latency, and routing outcome are completed by `GATEWAY-001` and exposed
by `USAGE-002` after provider execution and settlement exist.

`FR-078` is accepted by `PAYMENT-001`: a payment amount and the credit it
created cannot be recorded before the payment aggregate exists. `BILLING-001`
provides the immutable FX-rate snapshot foundation for that later transaction.

`FR-023` is accepted by `BILLING-002`: a key hard-spend gate requires the
usage, reservation, and settlement state that follows this credential task.

## P0 task register

| ID | Objective | Dependencies | Requirements | Acceptance / test scenarios |
|---|---|---|---|---|
| FOUNDATION-001 | Align this design pack and ADRs | None | All | Documentation traceability and link checks |
| FOUNDATION-002 | Create solution, module rules, local environment, CI | FOUNDATION-001 | NFR-080–084 | Build and architecture checks |
| FOUNDATION-003 | Add PostgreSQL/Redis access, transaction coordinator, migrator | FOUNDATION-002 | NFR-030–035 | Rollback and database-permission checks |
| OPS-001 | Add tracing, structured logs, health endpoints, outbox/jobs | FOUNDATION-002 | NFR-070–073 | Redaction and duplicate-worker checks |
| IDENTITY-001 | Accounts, sessions, verification, recovery, operators | FOUNDATION-003 | FR-001–004, FR-135 | Session, CSRF, MFA tests |
| ORGS-001 | Organizations, members, tenant authorization, projects | IDENTITY-001 | FR-003, FR-010–012 | SEC-001 |
| AUDIT-001 | Append-only audit trail | ORGS-001 | NFR-045 | Commit/rollback audit tests |
| BILLING-001 | Money types, wallet, immutable ledger, fee/pricing versions | ORGS-001 | FR-070–071, FR-079; ADR-019 foundation | Wallet/ledger immutability and historical fee/FX version tests |
| APIKEYS-001 | Show-once keys, auth, status, expiry | ORGS-001 | FR-020–022, FR-025 | Secret/revocation tests |
| CATALOG-001 | Models, mappings, capabilities, price history | FOUNDATION-003 | FR-030–034 | Effective-price tests |
| USAGE-001 | Logical requests, attempts, evidence, idempotency | APIKEYS-001, CATALOG-001 | FR-090 and FR-132 record foundation, FR-130–131 | IDEM-001, FIN-006; durable unknown evidence and reconciliation deadline |
| BILLING-002 | Atomic reservation, budgets, settlement, debt | BILLING-001, USAGE-001 | FR-023, FR-075–077, FR-081, FR-132–133 | FIN-001–006, PAY-003 |
| LIMITS-001 | Distributed RPM/concurrency and recovery behavior | APIKEYS-001, OPS-001 | FR-103–106, NFR-066 | OPS-001 |
| PROVIDER-001 | Normalized provider contracts and OpenAI adapter | CATALOG-001, USAGE-001 | FR-050–054 | Contract/error fixtures |
| GATEWAY-001 | Models/chat, admission, SSE, finalization | BILLING-002, LIMITS-001, PROVIDER-001 | FR-040–047, FR-090 execution completion | SSE-001–002, OPS-002 |
| PAYMENT-001 | FX quotes, intents, Payme, CLICK, reversals/reconciliation | BILLING-002, OPS-001 | FR-072–074, FR-078, FR-081 | PAY-001–003 |
| FRONTEND-001 | Customer/admin shells, onboarding, keys, billing | IDENTITY-001, PAYMENT-001 | FR-001–003, FR-020–021 | Browser tenant/secret tests |
| PROVIDER-002 | Anthropic adapter and same-model health/failover | PROVIDER-001, GATEWAY-001 | FR-062, FR-067, FR-134 | Bounded fallback fixtures |
| USAGE-002 | Activity, request detail, rollups, basic analytics | USAGE-001, BILLING-002 | FR-090 read model, FR-092–094 | Cursor, totals, isolation tests |
| ADMIN-001 | Provider/pricing/payment/ledger/incident controls | AUDIT-001, PAYMENT-001 | FR-120–126 | Operator authorization tests |
| OPS-002 | Two-node deployment, TLS, secrets, backup/restore, release gates | ADMIN-001, PROVIDER-002 | NFR-001–084 | Recovery and load drills |
| NOTIFY-001 | Deliver verification/reset email via protected transactional outbox | OPS-001, IDENTITY-001 | FR-001, NFR-045/072 | Retry, duplicate dispatch, expiry and token-redaction tests |

Frontend UI/UX note for subsequent frontend work: inspect the existing root
`frontend/` mockup before implementation. Keep its recognizable dark,
minimalist visual direction, but improve visual hierarchy, responsive behavior,
accessibility, and interaction polish beyond the mockup. The production apps
remain under `frontend/apps`; mock data must not enter the live product.

## Later tasks

| ID | Objective | Dependencies | Acceptance / test scenarios | Priority |
|---|---|---|---|---|
| TEAM-001 | Invitations, role changes and explicit project grants | ORGS-001, NOTIFY-001 | Membership changes affect the next authorization check; invite replay, permission matrix, last-owner and tenant-FK tests | P1 |
| BYOK-001 | Encrypted organization provider credentials and explicit project grants | ADMIN-001, PROVIDER-001, TEAM-001 | Masked CRUD/test/disable against fixed provider endpoints; tenant-bound encryption and cross-project denial | P1 |
| PROVIDER-006 | Gemini native non-stream adapter and billable usage normalization | PROVIDER-001, CATALOG-001 | Capability and usage fixtures; unsupported pricing dimensions fail closed | P1 |
| PROVIDER-007 | Gemini native SSE streaming normalization | PROVIDER-006, GATEWAY-001 | Prompt first chunks; normalize cumulative usage/finish; partial, malformed, timeout and disconnect are unknown and never replayed | P1 |
| PROVIDER-008 | DeepSeek native non-stream and cache-aware metering | PROVIDER-001, CATALOG-001 | OpenAI-style wire does not bypass provider-specific cache/reasoning accounting; explicit cached rate and effective tariff price required; response/error fixtures | P1 |
| PROVIDER-009 | DeepSeek streaming and reasoning normalization | PROVIDER-008, GATEWAY-001 | Reasoning/cache counters normalized without duplication; partial or malformed streams retain unknown-charge semantics | P1 |
| ROUTING-004 | Recent latency/throughput windows and weighted routing | ROUTING-003, GATEWAY-001 | Only already-eligible mappings ranked; bounded samples, minimum count, stale-data fallback and hysteresis tested | P1 |
| NOTIFY-002 | Customer threshold rules and verified Telegram destinations | OPS-001, TEAM-001 | Tenant-scoped rules, private-chat ownership proof, deduplicated episodes, replay/cross-tenant and retry tests | P1 |
| NOTIFY-003 | Signed customer outbound webhooks | NOTIFY-002 | Signature, replay, endpoint validation and retry tests | P1 |

The original execution plan named `SECURITY-001` as a BYOK prerequisite; the
platform envelope encryption and key-version foundation was delivered in
`ADMIN-001` instead. `BYOK-001` adds organization-bound authenticated data and
grant enforcement. This mapping restores the omitted dependency without
silently treating platform-only encryption as tenant-safe BYOK encryption.
FR-056 model restrictions and BYOK spend-cap enforcement were delivered in
`BYOK-002`; the `BYOK-001` UI/API alone did not claim they were active. Live testing with
customer-owned provider secrets is an external launch prerequisite, not a CI
fixture.

Historical P1 ideas (delivered items and unscoped names at that time):
`BUDGET-001`, `APIKEYS-003`, `TEAM-001`, `BYOK-001`, `BYOK-002`,
`ROUTING-003`, `ROUTING-004`, `ROUTING-005`, `NOTIFY-002`, `NOTIFY-003`,
`USAGE-005`, `PRIVACY-001`, `GATEWAY-005`, Gemini/DeepSeek adapters, and
`REFUND-001`.

`PRIVACY-001` uses project-owner opt-in with a separate payload key ring,
tenant-bound authenticated encryption, a one-hour to seven-day lifetime,
bounded response capture, immediate opt-out deletion, and Worker expiry
deletion. Request metadata and financial/audit records remain independent.

The earlier execution plan names `CATALOG-002` for `PROVIDER-006`; the current
`CATALOG-001` register explicitly includes mappings, capabilities, and
effective price history, so that prerequisite is satisfied here. Gemini
streaming remains the separate later `PROVIDER-007` task.
The earlier execution plan calls the SSE prerequisite `GATEWAY-003`; the
current `GATEWAY-001` task explicitly delivered SSE transport, cancellation,
and evidence/finalization, so no separate gateway prerequisite remains.
The earlier `CATALOG-002` pricing prerequisite for `PROVIDER-008` is included
in the current `CATALOG-001` mapping/price-history task. DeepSeek peak/off-peak
tariffs must be represented by published effective price windows; the adapter
does not infer live prices from an HTTP response.
The original plan's `GATEWAY-003` prerequisite for `PROVIDER-009` maps to the
completed `GATEWAY-001` SSE transport task.
The original `ROUTING-004` dependency on `USAGE-004` assumed its hourly usage
rollup would provide route-performance observations. The current `USAGE-002`
read model is daily/on-demand and is not misrepresented as that hourly task.
`ROUTING-004` instead owns short-lived advisory Redis performance samples from
the existing Gateway attempt lifecycle; durable usage and financial records
remain independent. Missing/stale Redis samples restore deterministic routing.
The earlier `NOTIFY-002` plan named `OPS-003` as an operational-alert/outbox
dependency. Those capabilities were delivered in the current `OPS-001`, while
`TEAM-001` supplies current Owner/Admin membership checks. Telegram live bot,
HTTPS webhook registration, and real private-chat acceptance remain external
launch prerequisites; the public Bot API protocol and retries are covered by
local fixtures.

P2: SSO/SCIM, ZDR routing, custom endpoints, guardrails, management automation,
and additional modalities.

## Acceptance scenarios

| ID | Required result |
|---|---|
| FIN-001 | $1 permits at most 33 concurrent $0.03 holds before release. |
| FIN-002 | A key cap rejects even when wallet funds suffice. |
| FIN-003 | Concurrent settle/release reaches one terminal financial result. |
| FIN-004 | Charge cannot exceed reservation; excess is platform exposure. |
| FIN-005 | Missing usage is reconciled; expiry does not declare zero usage. |
| FIN-006 | Evidence survives a settlement failure. |
| PAY-001 | Three identical callbacks create one credit. |
| PAY-002 | A second external transaction cannot credit an intent. |
| PAY-003 | Reversal preserves reservations and creates debt for unrecovered credit. |
| IDEM-001 | Duplicate key produces no second reservation or dispatch. |
| SSE-001 | Disconnect stops transport, not financial cleanup. |
| SSE-002 | Partial output never triggers automatic fallback. |
| SEC-001 | Cross-tenant identifiers fail authorization and FK checks. |
| OPS-001 | PostgreSQL/Redis failure prevents unprotected managed admission. |
| OPS-002 | Recovery distinguishes unknown evidence from known pending settlement. |

## Traceability and launch prerequisites

Functional Requirements are mapped to the task register by module: Identity
(`FR-001–005`, `FR-135`), Projects (`FR-010–013`), ApiKeys (`FR-020–028`),
Catalog (`FR-030–034`), Gateway/Providers/Routing (`FR-040–068`, `FR-134`),
Billing/Payments (`FR-070–081`, `FR-133`), Usage (`FR-090–096`, `FR-130–132`),
Limits (`FR-100–106`), Admin (`FR-120–126`), and NFRs (`FOUNDATION`/`OPS`).

Before paid launch, record hosting region and data residency, actual HA/PITR
guarantees, provider agreements and quotas, Payme/CLICK merchant credentials and
sandbox fixtures, FX source, fees/tax/fiscal rules, retention schedule, and
incident ownership. These are external inputs, not implementation-completion
claims.

## Next-phase register (post-implementation audit, 2026-09-27)

This register supersedes the unscoped P1/P2 name list above for **new** work;
the historical table is preserved. Follow [audit](17_POST_IMPLEMENTATION_AUDIT.md),
[launch gates](18_PRODUCTION_READINESS.md), and
[roadmap](19_NEXT_PHASE_ROADMAP.md). Each row specifies objective, priority,
dependencies, requirements/ADRs, likely ownership, and observable acceptance
with required tests. `Complete` means the acceptance evidence is present,
relevant real-stack tests pass or an external blocker is explicitly recorded,
documentation matches behavior, and the single-task commit exists. Do not
mark a task complete on a stub or on mocked tests alone. `In Progress` and
`Blocked` are also valid status values. Existing historical IDs remain intact.

### P0 — Required before paid production

| ID / status | Objective and likely modules/files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| DOCS-002 · Completed | Persist audit, gates, roadmap and new task register; `docs/`, `README.md`, `AGENTS.md`. | None; all docs/ADRs. | Historical statuses preserved; every new ID/dependency/link resolves; stale implementation descriptions corrected. Static link/ID and `git diff --check` pass. |
| LAUNCH-001 · Completed | Expose only Payme/CLICK callback routes through deployment edge with TLS, body/time bounds and documented source controls; `deploy/nginx`, payment endpoint/deploy tests. | DOCS-002; FR-073–074, NFR-043/046, ADR-018. | Both signed callbacks reach Management; invalid signatures/oversize denied; CLICK is default-deny until a verified trusted source policy is supplied because its signed fields exclude reversal `error`. Duplicate delivery remains idempotent. Real edge integration fixtures; evidence in `deploy/LAUNCH-001-VALIDATION.md`. |
| FRONTEND-002 · Completed | Correct UZS-to-tiyin top-up input/display and validation; dashboard billing/client. | DOCS-002; FR-072/078, NFR-034. | 100,000 UZS requests 10,000,000 tiyin exactly; decimals/overflow rejected; quote and paid amount agree. Unit and browser tests. |
| SECURITY-002 · Completed | Throttle public registration/login/recovery/invite attempts and bound auth bodies without tenant leakage; Identity, Management, Redis/edge. | DOCS-002; NFR-040/046, ADR-025. | Per-IP and account limits, safe Redis failure mode and uniform recovery response; adversarial integration tests. |
| SECURITY-003 · Completed | Make denials explicit 401/403 and apply `no-store` to all secret-bearing responses; Management endpoints, API key/Identity contracts. | DOCS-002; FR-020–022, NFR-040/044. | Forbidden payment/admin/key access never throws 500; create/rotate/recovery responses cannot be cached; tenant/role regression tests. |
| SECURITY-004 · Completed | Require proof before enabling TOTP, reject replayed time steps, audit enrollment/reset; Identity/Admin. | SECURITY-002, SECURITY-003; NFR-045/046, ADR-025. | Enroll→confirm→active flow, replay and concurrent reuse denied, recovery audited; clock-bound unit and database integration tests. |
| OPS-003 · Completed | Harden leased Worker retry lifecycle, expiry, ceiling and dead-letter visibility; `OperationalWork`, Worker dispatch. | DOCS-002; NFR-060/064, ADR-017. | Slow handler cannot be concurrently executed without safe reclaim; crash/retry reaches bounded terminal state; duplicate side effects prevented. PostgreSQL Worker integration/fault tests. |
| OPS-004 · Completed | Deliver persisted operational alerts and late-spend events to an owned consumer/escalation path; Worker, Notifications, Audit. | OPS-003; NFR-073, ADR-017. | `ops.alert.raised` and `billing.late_external_spend.recorded` are consumed or visibly dead-lettered; replay does not duplicate notification. Outbox/consumer tests. |
| OPS-005 · Completed | Emit gateway and financial custom metrics/traces with bounded labels and redaction; Gateway, Billing, Observability. | DOCS-002; NFR-001/070–073. | Request/token/cost/TTFT/active-stream/unknown-settlement instruments observed for success, SSE cancel and error; no secrets or prompt text. Instrumentation tests and OTLP smoke. |
| ROUTING-006 · Completed | Separate provider health by mapping vs tenant credential failure; Routing, Redis health, Gateway attempts. | DOCS-002; FR-067/105, NFR-062. | One BYOK key's 401/429 never poisons managed/other tenant; provider-global outage does; bounded half-open probe. Isolation and fallback tests. |
| BILLING-003 · Completed | Recover stranded holds, unknown evidence, settlement failures and platform exposure with operator-visible outcomes; Billing, Usage, Worker. | OPS-003, OPS-004, OPS-005; FR-132–133, FIN-003–006, ADR-020–022. | No expiry assumes zero cost; each case settles/releases/escalates once; debt and exposure reconcile. Crash/concurrency/PostgreSQL tests. |
| PAYMENT-002 · Completed | Record external provider reconciliation evidence, mismatch cases and audited resolution; Payments, Worker, Admin. | OPS-003, OPS-004, SECURITY-004; FR-125, NFR-065, ADR-018. | Duplicate/missing/amount-mismatch/reversal evidence creates one actionable case; operator closure is auditable and cannot double-credit. Payme/CLICK fixtures and database tests. |
| ADMIN-002 · Completed | Add audited, versioned FX and platform-fee publication with effective windows; Management/Admin, Catalog/Billing. | SECURITY-004; FR-078–079/122, ADR-009/019. | Role-restricted publish, no overlapping effective versions, historical payments retain snapshots. Admin API and PostgreSQL tests. |
| REFUND-001 · Completed | Refund a prior settled charge to wallet by immutable counter-entry; Billing, Admin, Audit. **No cash payout.** | BILLING-003, SECURITY-004; FR-080/124, ADR-006/021. | Same refund key once; partial sums never exceed original net charge; concurrent refunds, debt/holds and reversal remain consistent; actor/reason linked. FIN/PAY-style PostgreSQL concurrency tests. |
| FRONTEND-003 · Completed | Show top-up/reconciliation/refund/debt state and actionable explanations; dashboard/admin. | PAYMENT-002, REFUND-001; FR-070/094/123/125. | Real API states distinguish pending/credited/reversed/disputed; tenant and operator views differ correctly. Contract and browser tests. |
| DX-001 · Completed | Provide customer model catalog, prices/capabilities and working quickstart; Management catalog read, dashboard, docs. | ADMIN-002; FR-030–031/042, NFR-084. | Tenant-safe catalog matches published effective prices and live chat request example; API/browser example tests. |
| FRONTEND-004 · Completed | Complete verification/recovery UX against real Identity API; dashboard/auth. | SECURITY-002, SECURITY-003; FR-001–002/135. | Expired/replayed link, success and throttled states clear without account enumeration. Browser and API-contract tests. |
| FRONTEND-005 · Completed | Add P0 activity filters and request detail with explicit unknown/partial state; dashboard/Usage API. | DOCS-002; FR-028/090–094. | Date/project/key/status filtering paginates and enforces tenant scope; no raw payload leak. Query and browser tests. |
| SECURITY-005 · Completed | Support gateway API-key fingerprint key rotation without plaintext/revocation regression; ApiKeys, config, migrator if needed. | SECURITY-003; NFR-040/042, ADR-008. | Old keys authenticate through staged rotation, revoked keys stay denied, new keys use active version; unit and PostgreSQL migration tests. |
| SECURITY-006 · Completed | Qualify/implement rotation of provider, BYOK and session signing/encryption secrets; Providers, Identity, secret config. | SECURITY-004; NFR-041–043, ADR-025. | Versioned rotate/rollback with old ciphertext readability, no cross-tenant decrypt, bounded cutover and audit; integration and operational drill. |
| PRIVACY-002 · Completed | Define and enforce retention of request metadata, audit and operational records separately from opt-in payloads; Usage, Audit, Worker, docs. | DOCS-002; NFR-050–052, ADR-011. | Legal schedule is configured, payload opt-out remains immediate, financial evidence not purged early; expiry/backup tests. Policy approval external. |
| LAUNCH-002 · Completed | Run real-stack end-to-end acceptance in CI/staging, including edge, database, Worker, payment callbacks and dashboard; tests/Integration, deploy/CI. | LAUNCH-001, FRONTEND-002–005, SECURITY-002–004, BILLING-003, PAYMENT-002, REFUND-001, DX-001; ADR-018, NFR-080–084. | Sellable vertical flow and denial/replay paths pass without mocked backend; CI artifacts identify revision and environment. |
| SECURITY-009 · Completed | Make existing retention policy mutation, expiry shortening and opt-out deletion atomic with audit; Usage Application/Contracts/Infrastructure, Management, PostgreSQL/HTTP tests. | SECURITY-007, PRIVACY-002, SECURITY-003; FR-091, NFR-045/050, ADR-011/024/025; [scope §2/6](43_GUARDRAIL_PII_POLICY_SPEC.md). | User's 2026-10-19 Continue explicitly promoted this conditional P0 correction. GRD-003/004 retention-only fault injection verifies rollback of policy/expiry/deletion and audit on exception/actual cancellation; success, Owner/Admin/CSRF/tenant/no-store and opt-out semantics pass. Final unchanged serialized Release baseline: 1035 PASS, zero FAIL/SKIP; PostgreSQL 241, retention 10, architecture 15; API schema 16, drift/types and dashboard/admin builds pass. [Implementation, exact commands and initial failures/rechecks](46_RETENTION_AUDIT_ATOMICITY.md). No guardrail consent/version/receipt feature, new endpoint, migration or launch-gate pass; OPS-006 remains independently blocked. |
| OPS-006 · Blocked | Qualify two-node Gateway load and streaming behavior with operational metrics; `tests/Load`, deploy. | OPS-003, OPS-005, ROUTING-006, LAUNCH-002; NFR-001–024/061. | ≥10 RPS for 60s and 200 simultaneous streams across distinct nodes, measured latency/TTFT/errors, zero financial over-admission. Repeatable load report. Repository evidence tooling is in `deploy/OPS-006-VALIDATION.md`; two separate staging hosts are unavailable, so no qualifying run or completion claim exists. |
| OPS-007 · Planned | Exercise Redis/PostgreSQL/provider/node/Worker faults and restore with reconciled balances; deploy, Worker, tests. | OPS-004, OPS-005, OPS-006, SECURITY-005, SECURITY-006; NFR-030–035/060–065, OPS-001–002. | Fail-closed managed admission, safe SSE cancellation, outbox recovery, backup/PITR restore with measured RPO/RTO and ledger check. Fault/restore drill report. |
| LAUNCH-003 · Planned | Assemble release manifest and enforce repository launch gate evidence; `deploy/`, docs, CI. | LAUNCH-002, OPS-007, PRIVACY-002 (transitively all preceding P0); ADR-018, NFR-080–084. | Pin artifacts/schema/config, link functional/financial/security/reliability evidence, record external gate owners and blockers; no paid activation on missing gate. Manifest validation test. |

### P1 — Product completion after safe launch

| ID / status | Objective and likely modules/files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| DEVEX-001 · Completed | Add a local Aspire AppHost for the actual Gateway API, Management API, Worker, explicit Migrator, dashboard and admin Vite workspaces, PostgreSQL, Redis, Mailpit and deterministic provider fixture; `src/UZLLM.AppHost`, solution, frontend Vite proxy config, developer docs/tests. The root `frontend/` mockup is excluded. | FOUNDATION-002, FRONTEND-001, LAUNCH-002, OPS-005; ADR-001–003, NFR-070–073/080–083. Independent of OPS-006; explicit user approval is required to select this P1 task while the P0 two-host gate is blocked. | One documented command starts the complete local stack with healthy dependency ordering and explicit environment/config mappings; Migrator runs before runtime readiness, and no host auto-migrates. Dashboard/admin browser requests reach Management; Gateway reaches the fixture; Worker can deliver to Mailpit. No real merchant/provider secrets, Aspire client dependency in runtime projects, or change to production Compose/edge. Add AppHost startup/integration smoke, frontend proxy/browser checks, clean shutdown and `dotnet`/frontend build tests. Local success is **not** two-node or paid-launch evidence. |
| ARCH-001 · Completed | Enforce incremental module contract access without changing shared financial transaction boundary; architecture tests/docs. | DOCS-002; ADR-003/010/024. | Unauthorized references fail architecture test; existing cross-module contracts and migration transaction still work. |
| DX-002 · Completed | Publish versioned OpenAPI and prevent client/contract drift; Management/Gateway, frontend client, docs. | SECURITY-003; NFR-084, ADR-004. | Generated examples and status/error schemas agree with tested endpoints; CI drift test. Versioned artifacts, typed transport and reproducible evidence: [DX-002 validation](api/VALIDATION.md). |
| USAGE-004 · Completed | Persist tenant-scoped near-real-time usage rollups with replay-safe aggregation; Usage, Worker/DB. | BILLING-003, REFUND-001, OPS-003, OPS-005; FR-096, ADR-014. | Reprocessing does not double count; `dataAsOf`/late corrections explicit. Database and Worker replay tests. [Publication, migration and validation evidence](21_USAGE_ROLLUPS.md). |
| USAGE-005 · Completed | Export bounded tenant-authorized CSV from usage read model; Usage, Management, dashboard. | DX-002; FR-095. | Filters/UTC/totals match API, spreadsheet injection escaped, size/page bound, other-tenant data absent. Contract/security tests. [Contract, limits and validation evidence](22_USAGE_EXPORT.md). |
| APIKEYS-004 · Completed | Configure/enforce per-key model/provider permissions; ApiKeys, Gateway, Management. | DX-002; FR-027. | Denied route never reserves or dispatches; update/revoke applies at admission. Tenant and gateway tests. [Contract, migration and validation evidence](23_API_KEY_PERMISSIONS.md). Explicit user selection authorized this P1 task while OPS-006 remains externally blocked. |
| LIMITS-002 · Completed | Tenant-configurable RPM/concurrency/budget controls with safe enforcement; Limits, Management/Gateway. | SECURITY-002, DX-002; FR-103–104. | Changes take effect, invalid/unbounded plans rejected, Redis loss follows documented mode. Race and API tests. [Contract, migration and validation evidence](24_TENANT_LIMITS.md). User explicitly selected this P1 task while OPS-006 remains externally blocked. |
| PROJECTS-002 · Completed | Set project routing defaults/hard model-provider allowlists and metadata-only environment; reuse existing Lifetime/Daily/Weekly/Monthly budget policies. Projects, Gateway/Management. | APIKEYS-004, DX-002; FR-013. | Explicit request overrides only strategy; tenant isolation, budget enforcement, forbidden models/providers and execution-mode safety tested. No max_cost, duplicate financial state, implicit BYOK/Hybrid/fallback. 2026-10-13 user clarification resolves the 2026-10-12 blocker; explicit P1 selection while OPS-006 remains externally blocked. [Confirmed contract and evidence](25_PROJECT_DEFAULTS.md). |
| FRONTEND-006 · Completed | Project/key/limits lifecycle screens; dashboard. | APIKEYS-004, LIMITS-002, PROJECTS-002; FR-010–013/020–028/100–104. | Show-once secrets, settings/policy/budget edits, rotation/revocation and permission errors pass browser and real TLS-edge/API/PG tests. [Scope, screenshots and validation](26_CUSTOMER_LIFECYCLE.md). Explicit user selection authorizes this P1 task while OPS-006 remains externally blocked. |
| FRONTEND-007 · Completed | Team membership/invitation/role screens; dashboard. | DX-002; FR-004–005. | Owner/Admin restrictions, last-owner protection, invitation expiry/reissue/replay, project grants/revocation and cross-tenant denial pass browser and real TLS-edge/API/PG/Worker tests. [Scope, screenshots and validation](27_TEAM_MEMBERSHIP.md). Explicit user approval selects this P1 task while OPS-006 remains externally blocked. |
| BYOK-003 · Completed | Add Gemini/DeepSeek BYOK parity with per-credential health; provider adapters, BYOK. | ROUTING-006, SECURITY-006; FR-055–056. | No provider-specific types escape adapters; tenant key, quota/health and spend cases pass contract/PostgreSQL/Redis tests, including native chat/SSE and partial-output safety. [Scope, protocol and validation](28_BYOK_PROVIDER_PARITY.md). Explicit user selection authorizes this P1 task while OPS-006 remains externally blocked. |
| FRONTEND-008 · Completed | BYOK and routing configuration UX; dashboard/Management. | BYOK-003, PROJECTS-002; FR-055–068. | Masked credential lifecycle/grants/exact external caps and accurate request-only Managed/BYOK/Hybrid preview pass 17 focused browser/security cases, real TLS-edge/API/PG/Gateway chat/SSE acceptance and the Release baseline. [Scope, screenshots and validation](29_BYOK_ROUTING_UX.md). Explicit user selection authorizes this P1 task while OPS-006 remains externally blocked. |
| FRONTEND-009 · Completed | Alerts and payload/privacy controls UX; dashboard/Management. | OPS-003, OPS-004, DX-002; FR-091/110–115. | Delivery state, threshold rules/channel controls and owner-only retention opt-in/opt-out/expiry pass 19 focused browser cases, real TLS-edge/API/PG/Gateway acceptance and Release baseline. [Scope, screenshots and validation](30_ALERTS_PRIVACY_UX.md). Explicit user selection authorizes this P1 task while OPS-006 remains externally blocked. |
| FRONTEND-010 · Completed | Complete accessible Uzbek/Russian/English localized customer flows; frontend. | FRONTEND-006–009; ADR-016; Frontend Architecture §10 (corrected the previous NFR-083 migration-versioning reference). | 870 catalog messages; keyboard, accessible names, exact financial units and canonical wire values verified across customer/auth/onboarding/invitation flows. Final browser regression 108 PASS, real TLS-edge/API/PG/Gateway/Worker acceptance 5/5, Release baseline 800 PASS; [scope, screenshots and limitations](31_LOCALIZATION_ACCESSIBILITY.md). User approval authorizes this P1 task while OPS-006 remains externally blocked; no paid-launch gate passed. |
| GATEWAY-005 · Completed | Add bounded non-streaming OpenAI-style Responses subset; Gateway, Provider contracts/adapters. | DX-002, BILLING-003; FR-043, ADR-004/010. | Explicit stateless text capability matrix, operation-scoped idempotent reservation/settlement and normalized errors. 57 Responses regressions (including four actual native adapters), PostgreSQL concurrency/reconciliation, real TLS-edge acceptance 5/5 and Release baseline 863 PASS; [scope and validation](32_RESPONSES_API.md). Explicit user selection authorizes this P1 task while OPS-006 remains externally blocked; no paid-launch gate passed. |
| GATEWAY-006 · Completed | Stream supported Responses subset with SSE cancellation/evidence; Gateway/adapters. | GATEWAY-005; FR-043, ADR-010. | No fallback after output, terminal/unknown usage and disconnect settled safely; 95 Responses regressions (38 new SSE cases, four native adapters), PostgreSQL recovery/idempotency, real TLS-edge acceptance 5/5 and final unchanged Release baseline 903 PASS. [Protocol, validation and initial Redis timeout/recheck evidence](33_RESPONSES_SSE.md). Explicit user-selected P1 while OPS-006 remains externally blocked; no launch-gate pass. |
| GATEWAY-007 · Completed | Add bounded text/float embeddings route with input-only model capability/pricing; Gateway/OpenAI adapter/Catalog. | DX-002, BILLING-003; FR-044, ADR-004/010/020/022. | Shape, usage, cost and authorization match contract; 66 embeddings regressions, PostgreSQL concurrency/recovery/full-ID migration tests, generated contract parity, real TLS-edge acceptance 5/5 and final Release baseline 973 PASS. [Scope, migration and validation](34_EMBEDDINGS_API.md). User approval selects this P1 task while OPS-006 remains externally blocked; no launch gate passed. |
| REFUND-002 · Completed | Audited discretionary wallet adjustments with approval policy; Billing, Admin, Audit. | REFUND-001, SECURITY-004; FR-080/124. | Confirmed credit-only/all-amount independent recent-MFA approval; immutable request/approval/ledger, replay/concurrency, actor/reason, audit/cancellation rollback and hold/debt/budget preservation verified. 17 new policy cases, 5 new PostgreSQL cases and extended HTTP security coverage; final Release 1028 PASS, API schema 16 PASS, SDK 33 PASS, real TLS stack 5/5. [Policy, migration and evidence](37_WALLET_ADJUSTMENTS.md). User approval supersedes the historical policy block and selects this P1 task while OPS-006 remains externally blocked; no manual debit, cash payout or launch-gate pass. |
| CATALOG-003 · Completed | Add provider data-policy/residency metadata with dated provenance; Catalog/Admin. | DX-002; NFR-052. | Explicit unknown and conditional provenance, audited append-only publication, tenant/operator/MFA/CSRF boundaries, concurrency/rollback and migration protection verified. 28 new unit cases, 4 PostgreSQL/HTTP cases and 1 wire-contract case; final Release baseline 1006 PASS, 0 FAIL/SKIP. [Scope, migration and evidence](35_PROVIDER_DATA_POLICIES.md). User approved this independent P1 task while REFUND-002 remains policy-blocked and OPS-006 externally blocked; no financial-policy approval or launch-gate pass is inferred. |
| DX-003 · Completed | Add SDK examples and optional bounded playground based on actual contracts; docs/frontend. | DX-001, DX-002, GATEWAY-005, GATEWAY-006, GATEWAY-007; NFR-084. | Six server/terminal SDK commands execute against the disposable TLS test stack, with five replay denials, exact debit/hold release and Activity. Billing/secret hygiene, 33 offline SDK cases, 15 API schema cases and 3 quickstart browser cases pass. Final unchanged Release rerun: 1006 PASS, 0 FAIL/SKIP; original health failure and rechecks retained in [scope and evidence](36_SDK_EXAMPLES.md). Optional browser playground is not introduced. User approval selects this independent P1 task while REFUND-002 remains policy-blocked and OPS-006 externally blocked; no launch gate passed. |

### P2 — Contract/specification tasks; feature build requires approval

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| ENTERPRISE-001 · Completed | Specify SSO/SCIM identities, tenant roles and deprovisioning; `docs/`. | SECURITY-004; FR-004–005/140–147, ADR-025. | User's 2026-10-18 Continue approves the [threat/identity/wire/lifecycle specification and ten-task decomposition](38_ENTERPRISE_IDENTITY_SPEC.md) delivered in 4c1e660. Approval/status closeout records static validation and revalidated prior 1028-PASS baseline evidence; no new runtime test run, SAML/SCIM feature, migration, live interoperability or launch-gate claim. Feature tasks remain unscheduled until separate execution approval/promotion. |
| PRIVACY-003 · Completed | Specify ZDR/residency eligibility and truthful provider claims; `docs/`. | CATALOG-003; NFR-050–053, ADR-011. | User's 2026-10-18 Continue approves the [evidence/eligibility profile, 14 designed scenarios and eight-task decomposition](39_PRIVACY_ELIGIBILITY_SPEC.md) delivered in c5a8d4e. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, ZDR/regional-routing feature, provider-account qualification, migration or launch-gate pass. Feature tasks remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked. |
| ROUTING-007 · Completed | Specify advanced routing/cache policy and financial impact; `docs/`. | USAGE-004; FR-013/060–068, NFR-050–053/066, ADR-011/014/015. | User's 2026-10-18 Continue approves the [decision/fee/staleness matrix, 16 designed scenarios, ten-task decomposition and benchmark plan](40_ADVANCED_ROUTING_CACHE_SPEC.md) delivered in dfd3b36. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, routing/cache feature, fee activation, migration or launch-gate pass. Feature tasks remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked. |
| PROVIDER-010 · Completed | Specify custom provider endpoint trust, auth and billing boundary; `docs/`. | SECURITY-006; FR-050–056, NFR-041–046/050–053/060–066, ADR-008/009/010/011/013/025. | User's 2026-10-18 Continue approves the [trust/auth/cost profile, 20 designed scenarios and eleven-task decomposition](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md) delivered in ea54fcf. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, custom endpoint feature, migration, fee activation or launch-gate pass. Feature tasks remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked. |
| GATEWAY-008 · Completed | Specify additional modalities/capabilities after Responses/embeddings; `docs/`. | GATEWAY-005–007; FR-032/040–045. | User's 2026-10-18 Continue approves the [wire/cost/SSE matrix, 20 designed scenarios and ten-task decomposition](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md) delivered in 352df4c. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, capability/endpoint, migration, pricing activation or launch-gate pass. Conditional FR-045 modalities retain demand/metering/security entry gates. Feature tasks remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked. |
| SECURITY-007 · Completed | Specify guardrail/PII policy and consent; `docs/`. | PRIVACY-002; NFR-050–052. | User's 2026-10-18 Continue approves the [scope/consent/retention/financial matrix, 20 designed scenarios and nine-task decomposition](43_GUARDRAIL_PII_POLICY_SPEC.md) delivered in a2ab220. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, scanner/redaction, endpoint, migration, fee or launch-gate pass. Conditional P0 SECURITY-009 remains an unfixed retention/audit correction, not promoted or executed. All follow-ons remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked. |
| DX-004 · Completed | Specify management automation/API lifecycle; `docs/`. | DX-002; NFR-084. | User's 2026-10-18 Continue approves the [read-only scope/credential/lifecycle/replay and versioning specification, 20 designed scenarios and eight-task decomposition](44_MANAGEMENT_AUTOMATION_API_SPEC.md) delivered in 0116885. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static checks; no new runtime test run, automation credential/endpoint, migration or launch-gate pass. Feature tasks remain unscheduled until separate execution approval/promotion; OPS-006 remains externally blocked and SECURITY-009's separate retention/audit correction remains unfixed/unpromoted. |
| ENTERPRISE-002 · Completed | Specify invoice/SLA reporting and fiscal dependencies; `docs/`. | REFUND-002; FR-124–126. | User's 2026-10-19 Continue approves the [non-fiscal statement/operational SLI profile, fiscal/SLA approval path, source/revision/credit rules, 20 designed cases and nine-task decomposition](45_INVOICE_SLA_REPORTING_SPEC.md) delivered in a21a231. Approval-only closeout revalidates prior 1028-PASS baseline evidence and fresh static/arithmetic checks; no new runtime test run, fiscal invoice, SLA remedy/credit, endpoint/migration or launch-gate pass. Feature tasks remain unscheduled until separate execution approval/promotion; external fiscal/commercial approvals remain separate, OPS-006 is blocked and SECURITY-009's separate retention/audit correction is unfixed/unpromoted. |

### Approved P2 implementation increments

The user's subsequent Continue separately promotes INVOICE-001 after completed
REPORTING-003/SLO-002, while OPS-006 remains externally Blocked. Only external
metadata and contract/report linkage are authorized, not fiscal issuance,
tax determination, contractual SLA activation, claim/credit or UI.

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| INVOICE-001 · Completed | Approved-profile external fiscal evidence and contract/report linkage; Billing, Persistence, Management, generated contracts/tests. | REPORTING-003, SLO-002; doc45 §3/5/7/8, FR-125, ADR-006/019/025. | ENT-001/009/010/017/020 pass strict/default-deny/synthetic profiles, exact units/unknown tax, issuer/operation/replay/correction/cancellation, tenant/scope/window/immutable report links, operator/MFA/CSRF/no-store and atomic audit/commit-loss/restore. Final serialized Release baseline: 1386 PASS, zero FAIL/SKIP, 17 Completed TRX reports; all 68 new cases pass (48 policy, 12 facade/bounds, 1 wire, 7 PG). PG 294, Gateway 427, Billing 136, architecture 15, AppHost 2. API generation/drift/schema18/types, locked frontend builds, discovery/model/whitespace checks pass. [Exact evidence, migration, configuration, intermediate failures and limitations](52_EXTERNAL_REPORTING_EVIDENCE.md). Actual finance/legal/issuer/archive/commercial approvals remain external activation prerequisites, never synthetic-fixture passes. No issuance, tax determination, claims/credit, UI or paid-launch pass. BILLING-004 remains proposed pending approval; OPS-006 remains Blocked. |

The user's 2026-10-20 Continue separately promotes SLO-002 after SLO-001.
This authorizes the bounded operational calculator/report/operator API only;
OPS-006 stays Blocked and no fiscal/SLA activation or later task is approved.

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| SLO-002 · Completed | Versioned monthly SLI classification/calculation/report and operator read API; Usage, Persistence, Worker, Management, generated contracts. | SLO-001; doc 45 §6/7/8, NFR-001/013/084, ADR-011/014/017. | ENT-012–018 pass exact ratios/thresholds, separate attempts, disputed-origin/mode U, Unknown/NoData/coverage, immutable revisions, replay/concurrency/atomicity/bounds/cancel and operator/MFA/CSRF/no-store. Final corrected Release baseline: 1318 PASS, zero FAIL/SKIP, 17 Completed TRX reports; 107 new .NET cases (76 Usage, 17 API/Worker/wire, 14 PG). PG 287, Gateway 414, Usage 92, architecture 15, AppHost 2; API generation/drift/schema18/types, both frontend builds, model drift and whitespace pass. [Exact evidence, migration, default-empty registry and capacity limitations](51_OPERATIONAL_SLI_REPORTS.md). The 100k-fact bound rejects ordinary one-second monthly capture volume; separate scaling approval/qualification is required before offering that volume. No maintenance exclusion, financial mutation, SLA or paid-launch pass. INVOICE-001 remains proposed pending approval; OPS-006 remains Blocked. |

The earlier Continue separately approved SLO-001's capture/import scope from
document 45 §6/7/9/10. That approval did not itself authorize SLO-002; its later
separate promotion is recorded above. OPS-006 remains externally Blocked, and
neither increment activates contractual SLA or passes production qualification.

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| SLO-001 · Completed | Unsampled bounded edge/Gateway metadata and authenticated collector manifest import; Observability, Gateway, Usage, Persistence, Worker, deploy. | ENTERPRISE-002, OPS-003, OPS-005; doc 45 §6/7, NFR-001/013/070–073, ADR-011/014/017. | ENT-012/013/015/016/018 at capture/import scope pass: trusted producer/epoch/sequence, pre-admission and terminal stream evidence, duplicate/conflict/gap/clock/loss disclosure, atomic bounded PG import and no payload/hot-path sink blocking. Final Release baseline: 1211 PASS, zero FAIL/SKIP, 17 Completed TRX reports; all 78 new .NET cases pass. Node collector 7, real Docker/TLS edge 17, API schema/client 18/types/drift, model drift, Compose and whitespace checks pass. [Scope, migration/default-off configuration, exact evidence and limitations](50_EXECUTION_OBSERVATIONS.md). No monthly classifier/report, contractual SLA or launch-gate pass. SLO-002 remains proposed pending separate promotion; OPS-006 remains Blocked. |

The user's 2026-10-19 approval explicitly promotes REPORTING-001 from the
approved ENTERPRISE-002 specification while OPS-006 remains externally Blocked.
That increment is complete. The subsequent Continue separately promotes
REPORTING-002 and, on the subsequent Continue, REPORTING-003. The separately
approved SLO-001 capture/import increment was promoted at that handoff;
the later SLO-002/INVOICE-001 promotions are recorded above. See
[capture scope and evidence](50_EXECUTION_OBSERVATIONS.md). Earlier rows' unpromoted notes record
their original completion boundaries.

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| REPORTING-001 · Completed | Pure monthly statement reconciliation/period/source contracts and safe caller-snapshot payment evidence port; Billing/Payments, unit/PG tests. | ENTERPRISE-002, REFUND-002, BILLING-003; [spec §3/4](45_INVOICE_SLA_REPORTING_SPEC.md), NFR-030–035, ADR-005/006/019/024. | ENT-001–004/007/008: 36 new unit cases and seven new PG cases pass exact financial/FX, posting month, delayed UUID commit/snapshot/historical bridge, tenant/pending/missing links, hold/debt/budget and read-only checks. Final serialized Release baseline: 1078 PASS, zero FAIL/SKIP; PG 248, architecture 15, sixteen Completed TRX reports. [Scope, exact commands and initial failure/recheck evidence](47_STATEMENT_SOURCE_RECONCILIATION.md). No publication, migration, endpoint, fiscal/SLA entitlement or launch-gate pass; OPS-006 stays Blocked and REPORTING-002 remains unpromoted. |
| REPORTING-002 · Completed | Versioned statement/head/source/receipt schema and bounded Worker snapshot/publish/revision; Billing/Persistence/Migrator/Worker/Audit. | REPORTING-001, OPS-003; [spec §4/7](45_INVOICE_SLA_REPORTING_SPEC.md), ADR-011/017/024. | User's Continue separately promotes this independent P2 increment while OPS-006 remains Blocked. ENT-004–008/018/020 pass scoped UUID replay, build/revision races, tenant FKs, immutable prior bytes/source membership, historical bridge, pending/financial fixture, bounds/failure atomicity and real quarantined restore. Final unchanged Release baseline: 1106 PASS, zero FAIL/SKIP, sixteen Completed TRX reports; 28 new cases (12 unit, 16 PG), two extended financial tests; PG 264, Billing 88, architecture 15, AppHost 2. Model drift/whitespace checks pass. [Exact commands, initial failures/corrections, linear annotation lookup and recovery/retention limitations](48_STATEMENT_PUBLICATION.md). No HTTP/UI, external notification, fiscal/SLA feature or launch-gate pass; REPORTING-003 remains unpromoted pending separate approval. |
| REPORTING-003 · Completed | Tenant-authorized statement metadata/lines/CSV and operator build/job/revision API; Management/Billing/generated client. | REPORTING-002, DX-002, USAGE-005; [spec §8](45_INVOICE_SLA_REPORTING_SPEC.md), FR-123, NFR-044/084. | User's Continue separately approves this independent P2 increment while OPS-006 stays Blocked. ENT-001/003/005/017/018 pass ReadBilling/tenant/operator/MFA/CSRF/no-store, scoped cursors, immutable verified CSV, body/deadline/audit/cancel, replay/generation and terminal-job checks. Final unchanged Release baseline: 1133 PASS, zero FAIL/SKIP, sixteen Completed TRX reports; all 27 new cases pass (24 fast, 3 PG), PG 267, architecture 15, Gateway 384, AppHost 2. API drift/schema 18/types, both frontend builds, 13 serial browser regressions, model drift and whitespace checks pass. [Scope, exact commands, fixture corrections and existing dev-dependency advisory](49_STATEMENT_API.md). Existing 97 Management paths/schemas unchanged; seven added. No migration, UI, fiscal/SLA, bearer, repair endpoint or launch-gate pass; SLO-001 and later proposals remain unpromoted pending separate approval. |

No specification-only P2 row authorizes feature implementation. Approval must turn its contract
into small tested implementation tasks. Live merchant, provider, FX/tax,
region, HA/PITR and incident ownership are **external launch prerequisites**,
not code tasks or automatic completion dependencies. See
`18_PRODUCTION_READINESS.md`.

### REFUND-002 readiness review — 2026-10-17

FR-080 permits audited **credit** adjustments; FR-124 requires compensating
entries. Neither specifies a two-person approval threshold or discretionary
debit authorization. The ledger supports both adjustment entry types, but that
is not a business-policy grant. Existing `AdjustmentDebit` postings recover
payment-reversal debt; they do not establish a manual debit workflow.

`REFUND-001` already serializes settlement-linked refunds on the wallet and
records the ledger, debt recovery and audit atomically. The operator boundary
requires recent MFA; it does not implement adjustment requests or approval.
See [refund persistence](../src/Modules/Billing/Infrastructure/PostgreSqlSettlementRefundStore.cs),
[admin service](../src/UZLLM.Management.Api/Administration/AdminService.cs) and
[admin authorization](../src/UZLLM.Management.Api/Administration/AdminEndpoints.cs).
Existing refund/concurrency and MFA tests are dependency evidence, not tests
of the missing adjustment workflow; they were inspected, not rerun for this
documentation-only readiness review.

**Decision required before coding:** confirm credit-only adjustments with
approval by a different recently MFA-verified operator for **every amount**,
or specify the intended USD threshold and any authorized debit rules. The
credit-only/all-amounts option is a recommendation, not an accepted policy.
Self-approval must be denied; credits must preserve active holds and the
existing recovery-debt behavior. No cash payout, historical budget reduction
or change to settlement refunds is proposed. After confirmation, specify the
policy in FR-080/124, Billing and API Contracts, then implement/test this task.
No source, migration, configuration or financial behavior changed in this
review. The original stop pending this decision was subsequently superseded
by user approval to continue to independent CATALOG-003; REFUND-002 remains
Blocked and its proposed financial policy is not approved.

### REFUND-002 policy confirmation — 2026-10-17

The user's subsequent **Continue**, directly answering the explicit DX-003
handoff question, confirms credit-only adjustments and approval by a different
recently MFA-verified operator for **every amount**. This supersedes the policy
block above without erasing its history. It explicitly selects this independent
P1 task while OPS-006 remains externally blocked. No discretionary debit, cash
payout, historical spend restoration or production-gate approval is inferred.
See [implementation scope and evidence](37_WALLET_ADJUSTMENTS.md).

### ENTERPRISE-001 readiness review — 2026-10-17

The user's Continue directly answers the offer to start SSO/SCIM
**specification** work. It does not approve federation endpoints, migrations,
new authentication mechanisms, or arbitrary product/security policies.
OPS-006 remains blocked by unavailable distinct staging hosts.

**Dependency evidence inspected, not rerun:** SECURITY-004 exists in
[IdentityService](../src/Modules/Identity/Application/IdentityService.cs) and
[operator MFA regression](../tests/Integration/UZLLM.Persistence.IntegrationTests/OperatorMfaIntegrationTests.cs):
enroll/confirm/replay, recent verification and audited independent reset.
ADR-025 requires the existing server-backed session and separate operator
boundary to remain intact.

**Missing product authority:** FR-004 and FR-005 define roles/invitations,
not enterprise federation/provisioning. Functional Requirements section 14
only names SSO/SAML and SCIM as P2; no protocol profile, reference IdP,
account-linking, enforcement or lifecycle contract exists. These cannot be
inferred from the earlier permission to begin writing a specification.

**Concrete repository constraints:**

- [Identity contracts](../src/Modules/Identity/Contracts/IdentityContracts.cs)
  and [persistence](../src/BuildingBlocks/UZLLM.Persistence/IdentityEntities.cs)
  implement global email/password accounts and account sessions, not
  organization-scoped federation identities or SCIM resources. The database
  design's conceptual `external_auth_ref` is not an implemented identity field.
- [TeamService.RevokeAsync](../src/Modules/Organizations/Application/TeamService.cs)
  enforces manager/last-owner checks and commits membership/audit together.
  [RevokeMemberAsync](../src/Modules/Organizations/Infrastructure/PostgreSqlTeamStore.cs)
  revokes that organization's membership and deletes its project grants;
  it does not suspend the global account or revoke gateway keys.
- [Gateway key authentication](../src/Modules/ApiKeys/Application/ApiKeyServices.cs)
  checks secret, key status/expiry and active project. Its
  [store](../src/Modules/ApiKeys/Infrastructure/PostgreSqlApiKeyStore.cs) does not
  join creator membership. `CreatedByAccountId` records creation, not an
  explicit human/service-key ownership distinction. Retaining all keys after
  deprovisioning can retain credential access; revoking all creator keys can
  interrupt organization workloads. A new policy must choose intentionally.

**Product decisions required before a binding spec:**

1. First supported protocol/profile and reference IdP: SAML 2.0 + SCIM 2.0
   is the proposed starting scope consistent with the existing SSO/SAML label;
   choose Microsoft Entra ID, Okta or another reference IdP. Confirm whether
   initial SCIM covers Users only or Groups/role synchronization too. OIDC
   must be explicitly selected/included, not silently substituted for SAML.
2. Provisioning and login enforcement: proposed initial scope is SCIM-managed
   enterprise membership, no implicit email-based account merge/JIT creation,
   and organization opt-in SSO. Define whether/when SSO becomes mandatory and
   the approved recovery path. External claims must not grant platform
   operator privileges or bypass the existing recent-MFA boundary.
3. Deprovisioned credentials: proposed conservative policy revokes tenant
   membership/grants and keys created by that member **in that organization**,
   without suspending the global account or other tenants. This can stop
   workloads; alternatively require explicit service-key ownership/transfer
   semantics before preserving such keys. Neither option is approved yet.

Existing reservations must still settle/reconcile, and financial/audit history
must not be erased by identity lifecycle changes. These are existing invariants,
not choices to weaken. Live IdP credentials/onboarding are **not** required to
write the specification; product decisions are the blocker, not live fixtures.
After confirmation, write the threat/identity/API/lifecycle contract, proposed
requirements and dependency-safe follow-on tasks, then obtain review approval
before marking ENTERPRISE-001 Completed. No other backlog task is started.

This is a documentation-only, explicitly incomplete readiness review. Static
relative-link, task-scope and whitespace checks are its validation; no .NET,
frontend, protocol or live IdP tests were run or claimed for this review.

### ENTERPRISE-001 specification handoff — 2026-10-17

The user's subsequent Continue directly answers the three-choice policy
handoff: SAML 2.0/SCIM 2.0 Users with Microsoft Entra ID first; opt-in optional
SSO with explicitly linked, SCIM-provisioned membership and no JIT/email merge;
tenant-only deprovisioning including keys created by that member in that
organization. This unblocks specification work, not feature implementation.
The readiness review above is preserved as history.

[Document 38](38_ENTERPRISE_IDENTITY_SPEC.md) proposes the remaining precise
security, lifecycle, wire, persistence and fixture decisions, plus ten small
dependency-ordered follow-on tasks. Those tasks are **Proposed, not executable**;
they do not supersede P0 staging dependencies or start another P2 task.
Keep ENTERPRISE-001 In Progress until the user reviews/approves that complete
specification; only then may its acceptance be marked Completed and follow-on
tasks be promoted by explicit approval. Validation and limitations are recorded
in document 38. No SAML/SCIM runtime implementation has been delivered.

### ENTERPRISE-001 specification approval — 2026-10-18

The user's Continue directly answers the final review request for `4c1e660`,
approving the complete specification and ten-task decomposition, including
Owner-authorized explicit linking, tenant-only sessions, ReadOnly/no-grant
initial membership without managed Owners, conservative reactivation and
terminal creator-key revocation. This satisfies the specification acceptance
criterion; ENTERPRISE-001 is Completed by this documentation-only closeout.
The earlier readiness block and review-pending handoff remain historical.

FR-140–147, ADR-025 and the future API-design notices now reflect the approved
design, not implemented capabilities. The follow-on tasks in document 38
remain Proposed/unscheduled: separate execution approval and register promotion
are required before SSO-001 or any other feature begins. No next P2 task is
started. OPS-006 and all external launch prerequisites remain unchanged.
