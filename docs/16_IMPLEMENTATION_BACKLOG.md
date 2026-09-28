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
| SECURITY-004 · Planned | Require proof before enabling TOTP, reject replayed time steps, audit enrollment/reset; Identity/Admin. | SECURITY-002, SECURITY-003; NFR-045/046, ADR-025. | Enroll→confirm→active flow, replay and concurrent reuse denied, recovery audited; clock-bound unit and database integration tests. |
| OPS-003 · Planned | Harden leased Worker retry lifecycle, expiry, ceiling and dead-letter visibility; `OperationalWork`, Worker dispatch. | DOCS-002; NFR-060/064, ADR-017. | Slow handler cannot be concurrently executed without safe reclaim; crash/retry reaches bounded terminal state; duplicate side effects prevented. PostgreSQL Worker integration/fault tests. |
| OPS-004 · Planned | Deliver persisted operational alerts and late-spend events to an owned consumer/escalation path; Worker, Notifications, Audit. | OPS-003; NFR-073, ADR-017. | `ops.alert.raised` and `billing.late_external_spend.recorded` are consumed or visibly dead-lettered; replay does not duplicate notification. Outbox/consumer tests. |
| OPS-005 · Planned | Emit gateway and financial custom metrics/traces with bounded labels and redaction; Gateway, Billing, Observability. | DOCS-002; NFR-001/070–073. | Request/token/cost/TTFT/active-stream/unknown-settlement instruments observed for success, SSE cancel and error; no secrets or prompt text. Instrumentation tests and OTLP smoke. |
| ROUTING-006 · Planned | Separate provider health by mapping vs tenant credential failure; Routing, Redis health, Gateway attempts. | DOCS-002; FR-067/105, NFR-062. | One BYOK key's 401/429 never poisons managed/other tenant; provider-global outage does; bounded half-open probe. Isolation and fallback tests. |
| BILLING-003 · Planned | Recover stranded holds, unknown evidence, settlement failures and platform exposure with operator-visible outcomes; Billing, Usage, Worker. | OPS-003, OPS-004, OPS-005; FR-132–133, FIN-003–006, ADR-020–022. | No expiry assumes zero cost; each case settles/releases/escalates once; debt and exposure reconcile. Crash/concurrency/PostgreSQL tests. |
| PAYMENT-002 · Planned | Record external provider reconciliation evidence, mismatch cases and audited resolution; Payments, Worker, Admin. | OPS-003, OPS-004, SECURITY-004; FR-125, NFR-065, ADR-018. | Duplicate/missing/amount-mismatch/reversal evidence creates one actionable case; operator closure is auditable and cannot double-credit. Payme/CLICK fixtures and database tests. |
| ADMIN-002 · Planned | Add audited, versioned FX and platform-fee publication with effective windows; Management/Admin, Catalog/Billing. | SECURITY-004; FR-078–079/122, ADR-009/019. | Role-restricted publish, no overlapping effective versions, historical payments retain snapshots. Admin API and PostgreSQL tests. |
| REFUND-001 · Planned | Refund a prior settled charge to wallet by immutable counter-entry; Billing, Admin, Audit. **No cash payout.** | BILLING-003, SECURITY-004; FR-080/124, ADR-006/021. | Same refund key once; partial sums never exceed original net charge; concurrent refunds, debt/holds and reversal remain consistent; actor/reason linked. FIN/PAY-style PostgreSQL concurrency tests. |
| FRONTEND-003 · Planned | Show top-up/reconciliation/refund/debt state and actionable explanations; dashboard/admin. | PAYMENT-002, REFUND-001; FR-070/094/123/125. | Real API states distinguish pending/credited/reversed/disputed; tenant and operator views differ correctly. Contract and browser tests. |
| DX-001 · Planned | Provide customer model catalog, prices/capabilities and working quickstart; Management catalog read, dashboard, docs. | ADMIN-002; FR-030–031/042, NFR-084. | Tenant-safe catalog matches published effective prices and live chat request example; API/browser example tests. |
| FRONTEND-004 · Planned | Complete verification/recovery UX against real Identity API; dashboard/auth. | SECURITY-002, SECURITY-003; FR-001–002/135. | Expired/replayed link, success and throttled states clear without account enumeration. Browser and API-contract tests. |
| FRONTEND-005 · Planned | Add P0 activity filters and request detail with explicit unknown/partial state; dashboard/Usage API. | DOCS-002; FR-028/090–094. | Date/project/key/status filtering paginates and enforces tenant scope; no raw payload leak. Query and browser tests. |
| SECURITY-005 · Planned | Support gateway API-key fingerprint key rotation without plaintext/revocation regression; ApiKeys, config, migrator if needed. | SECURITY-003; NFR-040/042, ADR-008. | Old keys authenticate through staged rotation, revoked keys stay denied, new keys use active version; unit and PostgreSQL migration tests. |
| SECURITY-006 · Planned | Qualify/implement rotation of provider, BYOK and session signing/encryption secrets; Providers, Identity, secret config. | SECURITY-004; NFR-041–043, ADR-025. | Versioned rotate/rollback with old ciphertext readability, no cross-tenant decrypt, bounded cutover and audit; integration and operational drill. |
| PRIVACY-002 · Planned | Define and enforce retention of request metadata, audit and operational records separately from opt-in payloads; Usage, Audit, Worker, docs. | DOCS-002; NFR-050–052, ADR-011. | Legal schedule is configured, payload opt-out remains immediate, financial evidence not purged early; expiry/backup tests. Policy approval external. |
| LAUNCH-002 · Planned | Run real-stack end-to-end acceptance in CI/staging, including edge, database, Worker, payment callbacks and dashboard; tests/Integration, deploy/CI. | LAUNCH-001, FRONTEND-002–005, SECURITY-002–004, BILLING-003, PAYMENT-002, REFUND-001, DX-001; ADR-018, NFR-080–084. | Sellable vertical flow and denial/replay paths pass without mocked backend; CI artifacts identify revision and environment. |
| OPS-006 · Planned | Qualify two-node Gateway load and streaming behavior with operational metrics; `tests/Load`, deploy. | OPS-003, OPS-005, ROUTING-006, LAUNCH-002; NFR-001–024/061. | ≥10 RPS for 60s and 200 simultaneous streams across distinct nodes, measured latency/TTFT/errors, zero financial over-admission. Repeatable load report. |
| OPS-007 · Planned | Exercise Redis/PostgreSQL/provider/node/Worker faults and restore with reconciled balances; deploy, Worker, tests. | OPS-004, OPS-005, OPS-006, SECURITY-005, SECURITY-006; NFR-030–035/060–065, OPS-001–002. | Fail-closed managed admission, safe SSE cancellation, outbox recovery, backup/PITR restore with measured RPO/RTO and ledger check. Fault/restore drill report. |
| LAUNCH-003 · Planned | Assemble release manifest and enforce repository launch gate evidence; `deploy/`, docs, CI. | LAUNCH-002, OPS-007, PRIVACY-002 (transitively all preceding P0); ADR-018, NFR-080–084. | Pin artifacts/schema/config, link functional/financial/security/reliability evidence, record external gate owners and blockers; no paid activation on missing gate. Manifest validation test. |

### P1 — Product completion after safe launch

| ID / status | Objective and likely modules/files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| ARCH-001 · Planned | Enforce incremental module contract access without changing shared financial transaction boundary; architecture tests/docs. | DOCS-002; ADR-003/010/024. | Unauthorized references fail architecture test; existing cross-module contracts and migration transaction still work. |
| DX-002 · Planned | Publish versioned OpenAPI and prevent client/contract drift; Management/Gateway, frontend client, docs. | SECURITY-003; NFR-084, ADR-004. | Generated examples and status/error schemas agree with tested endpoints; CI drift test. |
| USAGE-004 · Planned | Persist tenant-scoped near-real-time usage rollups with replay-safe aggregation; Usage, Worker/DB. | BILLING-003, REFUND-001, OPS-003, OPS-005; FR-096, ADR-014. | Reprocessing does not double count; `dataAsOf`/late corrections explicit. Database and Worker replay tests. |
| USAGE-005 · Planned | Export bounded tenant-authorized CSV from usage read model; Usage, Management, dashboard. | DX-002; FR-095. | Filters/UTC/totals match API, spreadsheet injection escaped, size/page bound, other-tenant data absent. Contract/security tests. |
| APIKEYS-004 · Planned | Configure/enforce per-key model/provider permissions; ApiKeys, Gateway, Management. | DX-002; FR-027. | Denied route never reserves or dispatches; update/revoke applies at admission. Tenant and gateway tests. |
| LIMITS-002 · Planned | Tenant-configurable RPM/concurrency/budget controls with safe enforcement; Limits, Management/Gateway. | SECURITY-002, DX-002; FR-103–104. | Changes take effect, invalid/unbounded plans rejected, Redis loss follows documented mode. Race and API tests. |
| PROJECTS-002 · Planned | Set project defaults for route, spend and environment; Projects, Gateway/Management. | APIKEYS-004, DX-002; FR-013. | Explicit request precedence and tenant isolation tested; no bypass of hard caps. |
| FRONTEND-006 · Planned | Project/key/limits lifecycle screens; dashboard. | APIKEYS-004, LIMITS-002, PROJECTS-002; FR-010–013/020–028/100–104. | Show-once secrets, edits, revocation and permission errors work against API; browser tests. |
| FRONTEND-007 · Planned | Team membership/invitation/role screens; dashboard. | DX-002; FR-004–005. | Owner/admin restrictions, expired invite and cross-tenant denial tested in browser. |
| BYOK-003 · Planned | Add Gemini/DeepSeek BYOK parity with per-credential health; provider adapters, BYOK. | ROUTING-006, SECURITY-006; FR-055–056. | No provider-specific types escape adapters; tenant key, quota and spend cases have contract tests. |
| FRONTEND-008 · Planned | BYOK and routing configuration UX; dashboard/Management. | BYOK-003, PROJECTS-002; FR-055–068. | Secret reveal prohibited, mode/order preview accurate, failures explained; browser/security tests. |
| FRONTEND-009 · Planned | Alerts and payload/privacy controls UX; dashboard/Management. | OPS-003, OPS-004, DX-002; FR-091/110–115. | Delivery state/opt-out/expiry visible; role and retention browser tests. |
| FRONTEND-010 · Planned | Complete accessible Uzbek/Russian/English localized customer flows; frontend. | FRONTEND-006–009; ADR-016, NFR-083. | Keyboard, screen-reader labels and localized financial units verified across supported screens. |
| GATEWAY-005 · Planned | Add bounded non-streaming OpenAI-style Responses subset; Gateway, Provider contracts/adapters. | DX-002, BILLING-003; FR-043, ADR-004/010. | Explicit capability matrix, idempotent reservation/settlement, normalized errors; contract and billing tests. |
| GATEWAY-006 · Planned | Stream supported Responses subset with SSE cancellation/evidence; Gateway/adapters. | GATEWAY-005; FR-043, ADR-010. | No fallback after output, terminal/unknown usage and disconnect settled safely; SSE fixtures. |
| GATEWAY-007 · Planned | Add embeddings route with model capability/pricing; Gateway/adapters/Catalog. | DX-002, BILLING-003; FR-044. | Shape, usage, cost and authorization match contract; provider fixture and financial tests. |
| REFUND-002 · Planned | Audited discretionary wallet adjustments with approval policy; Billing, Admin, Audit. | REFUND-001, SECURITY-004; FR-080/124. | Actor/reason/two-person threshold policy, immutable ledger, replay/concurrency tests; no cash payout implied. |
| CATALOG-003 · Planned | Add provider data-policy/residency metadata with dated provenance; Catalog/Admin. | DX-002; NFR-052. | Unknown policy is explicit, not marketed as ZDR; publication/audit and API tests. |
| DX-003 · Planned | Add SDK examples and optional bounded playground based on actual contracts; docs/frontend. | DX-001, DX-002, GATEWAY-005, GATEWAY-006, GATEWAY-007; NFR-084. | Examples execute against test stack, disclose billing and secret hygiene; browser/contract tests. |

### P2 — Contract/specification tasks; feature build requires approval

| ID / status | Objective and likely files | Dependencies; requirements | Acceptance and required tests |
|---|---|---|---|
| ENTERPRISE-001 · Planned | Specify SSO/SCIM identities, tenant roles and deprovisioning; `docs/`. | SECURITY-004; FR-004–005, ADR-025; new product requirement needed. | Approved threat/contract spec and decomposed implementation tasks; no untested auth endpoint. |
| PRIVACY-003 · Planned | Specify ZDR/residency eligibility and truthful provider claims; `docs/`. | CATALOG-003; NFR-050–052. | Policy evidence matrix and denial cases reviewed; no ZDR promise without provider evidence. |
| ROUTING-007 · Planned | Specify advanced routing/cache policy and financial impact; `docs/`. | USAGE-004; FR-060–068. | Decision matrix, staleness/fallback/fee cases and benchmark plan approved. |
| PROVIDER-010 · Planned | Specify custom provider endpoint trust, auth and billing boundary; `docs/`. | SECURITY-006; FR-050–056. | SSRF/credential/cost threat model and contract fixtures designed. |
| GATEWAY-008 · Planned | Specify additional modalities/capabilities after Responses/embeddings; `docs/`. | GATEWAY-005–007; FR-040–044. | Wire/cost/SSE compatibility matrix and bounded tasks approved. |
| SECURITY-007 · Planned | Specify guardrail/PII policy and consent; `docs/`. | PRIVACY-002; NFR-050–052. | False-positive, retention, bypass and audit scenarios documented. |
| DX-004 · Planned | Specify management automation/API lifecycle; `docs/`. | DX-002; NFR-084. | Scope/permission/idempotency contract and versioning strategy approved. |
| ENTERPRISE-002 · Planned | Specify invoice/SLA reporting and fiscal dependencies; `docs/`. | REFUND-002; FR-124–126. | Finance/legal approval path, source-of-truth and correction rules documented. |

No P2 row authorizes feature implementation. Approval must turn its contract
into small tested implementation tasks. Live merchant, provider, FX/tax,
region, HA/PITR and incident ownership are **external launch prerequisites**,
not code tasks or automatic completion dependencies. See
`18_PRODUCTION_READINESS.md`.
