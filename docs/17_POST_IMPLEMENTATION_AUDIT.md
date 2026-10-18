# Post-Implementation Audit

## Gateway capability specification follow-up — 2026-10-18

`GATEWAY-008` rechecks the actual parsers, writers, native adapters, Catalog
and cost calculators in [the draft evidence matrix](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md#2-verified-repository-baseline).
Chat tool/format support is not Responses support; Responses remains text-only,
embeddings float-only, and customer catalog discovery chat-oriented. Nonempty
extra-pricing dimensions are rejected. These are bounded existing contracts,
not evidence that a previously completed task secretly implemented every native
provider capability. Proposed operation qualification, function/schema parity
and embedding controls await review/implementation; image/audio/video require
separate demand and metering evidence. No historical completion or launch gate
is changed by this specification.

## Scope and evidence

Audited 2026-09-27 at `cba535b` against `AGENTS.md`, `README.md`, the complete
`docs/` design pack, solution/projects, hosts, modules, migrations, deployment
and CI configuration, frontend, tests, and Git history. The 37 historical tasks
in [the backlog](16_IMPLEMENTATION_BACKLOG.md) remain `Completed` as delivery
history; this audit evaluates today's repository, not the intent of a commit.
`Completed` does **not** mean the paid-launch gates in
[production readiness](18_PRODUCTION_READINESS.md) have passed.

Evidence: `dotnet build UZLLM.slnx --configuration Debug --no-restore`
passed with zero warnings/errors. `dotnet test` passed 320 Unit/Contract tests;
133 Integration tests could not start because Docker Desktop was unavailable
at either engine endpoint. `frontend/apps` production build and nine mocked
Playwright scenarios passed. The 2026-09-26 local drill in
`deploy/DRILL-2026-09-26.md` is prior evidence, not a substitute for current
real-stack, multi-node, HA/PITR, or live merchant qualification.

## Completed-task verification matrix

| Historical task(s) | Documented as | Actual repository state | Gap / follow-up |
|---|---|---|---|
| FOUNDATION-001–003 | Completed | Buildable .NET 10 solution, three hosts, migrator, Compose, CI, PostgreSQL/Redis baseline. | None invalidating completion; module isolation is structural rather than independently deployable (`ARCH-001`). |
| OPS-001, OPS-002 | Completed | Health, OpenTelemetry baseline, outbox/jobs, runbooks and local drill exist. | Worker lease/retry/dead-letter visibility addressed by `OPS-003`; operator alert delivery by `OPS-004`; bounded gateway/financial instruments and OTLP smoke by `OPS-005`. Production qualification remains `OPS-006–007`. |
| IDENTITY-001, ORGS-001, TEAM-001, AUDIT-001 | Completed | Accounts/sessions, organizations, membership, projects, audit and invitations exist. | Abuse throttling addressed by `SECURITY-002`; denial/cache behavior by `SECURITY-003`; MFA proof/replay and audited operator reset by `SECURITY-004`. Verification/recovery UX is addressed by `FRONTEND-004`; live SMTP delivery remains a launch gate. |
| BILLING-001–003, BUDGET-001, LIMITS-001 | Completed | Conditional wallet update, append-only ledger, holds, terminal settlement, debt and caps are implemented. `BILLING-003` adds expired-hold sweep, terminal missing-evidence recovery, deduplicated settlement-failure alerts and operator financial-risk reads. | `ADMIN-002` adds operator fee/FX publication; `REFUND-001` adds settlement-linked wallet-credit counter-entries. Cash payouts and discretionary adjustments remain separate. |
| APIKEYS-001, APIKEYS-003 | Completed | Hashed show-once keys, revocation/rotation and key budgets exist. | New-key response cache policy addressed by `SECURITY-003`; `SECURITY-005` added staged fingerprint-key versions and migration coverage. Live secret distribution/cutover remains an operational gate. |
| CATALOG-001 | Completed | Model mappings, capabilities and effective pricing exist. | Audit finding: customer discovery and operator publication UX were incomplete. `ADMIN-002` and `DX-001` subsequently addressed publication and customer discovery; live provider availability remains a launch gate. |
| USAGE-001–002 | Completed | Durable request/attempt/evidence and usage/activity queries exist. `USAGE-004` adds durable hourly rollups with replay/late-correction handling and explicit freshness. | `USAGE-005` subsequently adds bounded tenant-authorized CSV; see [export evidence](22_USAGE_EXPORT.md). Rollups/export do not replace authoritative financial state or production qualification. |
| PROVIDER-001–002, PROVIDER-006–009 | Completed | OpenAI, Anthropic, Gemini and DeepSeek adapters, normalized errors, contract fixtures and SSE paths exist. | `BYOK-003` subsequently adds Google/DeepSeek tenant-key parity; [scope and evidence](28_BYOK_PROVIDER_PARITY.md). Live agreement/quota/usage qualification remains external. |
| GATEWAY-001, ROUTING-003–006 | Completed | Managed admission, chat/SSE, deterministic/advanced selection, bounded fallback and credential-isolated health with cross-node half-open probes exist. | Responses/embeddings not implemented. `GATEWAY-005–007`. |
| PAYMENT-001 | Completed | Payme/CLICK protocol handlers, signatures, callback idempotency and contract fixtures exist. | Deployment edge omits callback routes; reconciliation is primarily local, live merchant verification external. `LAUNCH-001`, `PAYMENT-002`. |
| FRONTEND-001, ADMIN-001 | Completed | Dashboard/admin applications, shell and mocked browser coverage exist. | Audit finding: top-up unit, recovery, financial state and customer catalog gaps. `FRONTEND-002–005` and `DX-001` addressed top-up, financial, catalog, recovery and P0 activity UX gaps. `FRONTEND-006` adds project/key/limits lifecycle controls; `FRONTEND-007` adds Team controls; `FRONTEND-008` adds BYOK/routing configuration with real-stack evidence. Remaining customer flows are `FRONTEND-009–010`. |
| NOTIFY-001–003 | Completed | Email, webhook, Telegram integrations and transport fixtures exist. | `OPS-004` adds the missing operational alert consumer and delivery status. Live on-call mailbox/SMTP receipt verification remains external. |
| BYOK-001–002, PRIVACY-001 | Completed | Tenant-scoped encrypted keys, BYOK execution and opt-in bounded payload retention exist. | `SECURITY-006` added bounded, audited platform/BYOK KEK and operator key-ring rotation; its live multi-node drill remains a launch gate. `BYOK-003` subsequently adds provider parity; `FRONTEND-008` delivers customer BYOK/routing configuration. Payload-control UX remains `FRONTEND-009`; metadata lifecycle is tracked separately in `PRIVACY-002`. |

## Concrete repository/document gaps

1. `deploy/nginx/edge.conf` does not proxy `/payments/payme/callback` or
   `/payments/click/callback` to Management, although both endpoints exist in
   `src/Modules/Payments/Infrastructure/PaymentEndpointExtensions.cs`. The
   public payment flow was unreachable through the documented deployment.
   **Follow-up:** `LAUNCH-001` added exact ingress with CLICK default-deny;
   see `../deploy/LAUNCH-001-VALIDATION.md`. Live merchant source/settlement
   evidence remains an external launch gate.
2. `frontend/apps/dashboard/src/App.tsx` labels a top-up field “Amount (UZS)”
   but submits it as `amountTiyin`; the mocked browser test in
   `frontend/apps/dashboard/tests/tenant-and-secret.spec.ts` preserves this
   erroneous unit. This is a financial UX defect, not a copy change.
   **Resolved by `FRONTEND-002`:** whole UZS converts to tiyin using integer
   arithmetic; overflow/fractions are rejected, quote and intent amounts are
   checked, and browser/unit regression tests cover the payment display.
3. `GatewayTelemetry` is declared in
   `src/BuildingBlocks/UZLLM.Observability/ObservabilityServiceCollectionExtensions.cs`
   but no Gateway caller emits its custom instruments. Automatic HTTP traces
   alone do not satisfy spend, token, TTFT or active-stream visibility.
4. `OPS-003` narrowed Worker claims to one item, renews active leases, fences
   updates and exposes terminal metadata. `OPS-004` registers consumers for
   `ops.alert.raised` and `billing.late_external_spend.recorded`, records email
   notification state and exposes operator-only alert metadata. SMTP has an
   unavoidable post-accept/pre-commit duplicate window; live mailbox routing
   and escalation acknowledgement remain external launch evidence.
5. `RedisProviderHealthService` formerly keyed all health by mapping ID.
   `ROUTING-006` now isolates BYOK/Managed credential 401/429 failures from the
   shared mapping outage circuit and bounds cross-node half-open probes.
6. `SECURITY-003` replaced Management payment `Results.Forbid()` with an explicit
   403 and tested cross-tenant/role denials through a real HTTP host. Management
   responses now use `no-store`; this historical audit finding is resolved.
7. `ADMIN-002` adds an audited, recent-MFA operator path for FX/fee publication
   and PostgreSQL exclusion of overlapping fee windows. `PAYMENT-002` adds
   operator-supplied external observation provenance, mismatch cases and
   audited closure; this still does not independently fetch or authenticate
   live merchant statements. Live Payme/CLICK agreement and finance approval
   of FX/fee policy remain external gates.
8. `AGENTS.md`, `README.md`, `00_README.md`, and several implementation-status
   lines in `15_ARCHITECTURE_DECISIONS.md` describe an earlier scaffold. The
   design-pack page flows are aspirational unless listed as implemented here.
   The shared `FoundationDbContext` differs from the module-owned context
   diagram in `05_BACKEND_ARCHITECTURE_DOTNET.md`; `ARCH-001` now documents
   that transaction model and guards compiled cross-module type/project
   references. Its exact legacy web/DI seams remain visible, not silently
   treated as general implementation access.

## Requirements still open or underspecified

- The originally missing `USAGE-005` (`FR-095`) export is subsequently delivered
  as bounded activity/daily CSV from the current usage read model, with tenant
  authorization and [contract/security/browser evidence](22_USAGE_EXPORT.md).
  `FR-096` durable/near-real-time rollups
  were subsequently delivered by `USAGE-004`; [publication and freshness semantics](21_USAGE_ROLLUPS.md) distinguish them from the earlier on-demand SQL reads.
- `GATEWAY-005` is `FR-043` Responses API. Start with a versioned, explicitly
  supported **non-streaming** subset; streaming and embeddings (`FR-044`) are
  separate tasks. Do not imply the chat contract automatically covers them.
- `REFUND-001` is a settlement-linked **wallet-credit refund** under `FR-080`
  and `FR-124`, with immutable counter-entry and idempotency. Cash payout,
  provider reversal and discretionary adjustment are different policies; the
  latter belongs to `REFUND-002`, while cash payout is external-policy gated.
- `APIKEYS-004` delivers per-key canonical-model/provider allow/deny policies,
  authoritative admission checks and tenant-scoped Management updates. FR-027's
  optional maximum model price class still has no catalog taxonomy/ranking or
  acceptance contract; it is not represented as implemented. Key lifecycle UI
  is subsequently delivered by `FRONTEND-006`; see [scope and real API evidence](26_CUSTOMER_LIFECYCLE.md).
- `PROJECTS-002` adds project routing defaults, intersected hard model/provider
  allowlists and metadata-only environment tags. Its user-confirmed spend scope
  reuses existing financial budgets, not per-request max cost or implicit execution
  modes. [Precedence, admission boundaries and evidence](25_PROJECT_DEFAULTS.md).
- `FRONTEND-007` adds invitation acceptance, role/revocation and project-grant
  screens on the existing transactional Team APIs; [scope and evidence](27_TEAM_MEMBERSHIP.md).
- `FRONTEND-008` adds masked BYOK lifecycle, exact external caps and grants,
  project routing defaults and an explicit Managed/BYOK/Hybrid request-only
  configuration preview. Minimal authorized provider-identity discovery closes
  the create UUID gap; [scope and validation](29_BYOK_ROUTING_UX.md) distinguish
  UI configuration from live admission/provider qualification.
- Customer alerts/privacy-control UI,
  privacy metadata retention, SDK examples, and enterprise features
  remain incomplete or require a fresh contract before implementation.
- `LIMITS-002` adds bounded tenant-managed project/key admission overrides,
  transactional audit, live PostgreSQL policy reads and shared Redis enforcement
  without resetting counters or leases. Existing financial budgets remain
  authoritative. [Contract and validation evidence](24_TENANT_LIMITS.md) separates
  this backend capability from the subsequently delivered FRONTEND-006 screens
  and still-blocked OPS-006 production qualification.
- `DX-002` now publishes the implemented Gateway/Management v1 OpenAPI surfaces,
  serializer-derived examples and generated client wire types with CI drift checks
  (see [API artifact guide](api/README.md)). This does not implement Responses,
  embeddings or automation lifecycle (`DX-004`), or certify live compatibility.

## Audit disposition

No historical `Completed` status is erased. The corrective, product and
qualification tasks are registered with explicit dependencies in
`16_IMPLEMENTATION_BACKLOG.md`. Do not claim launch readiness from compilation,
mocked browser tests, local callback fixtures, or a single-host drill.

## Subsequent customer UX follow-up — 2026-10-16

Preserve the audit snapshot and historical completion labels above. Customer
alerts/privacy controls were subsequently delivered by `FRONTEND-009`
([evidence](30_ALERTS_PRIVACY_UX.md)). `FRONTEND-010` completes the implemented
customer/auth/onboarding/invitation flows in Uzbek Latin, Russian and English,
with exact localized financial units, browser-local display preferences,
keyboard/dialog focus and accessible names. [Scope, screenshots and validation](31_LOCALIZATION_ACCESSIBILITY.md)
separate local repository evidence from manual assistive-technology, hosted CI
and production qualification. No server contract, financial rule or migration
changed; operator translation is outside this task. OPS-006 remains externally
blocked; no paid-launch gate is passed by this follow-up.

## Subsequent Gateway follow-up — 2026-10-16

`GATEWAY-005` adds the [bounded stateless non-streaming text Responses contract](32_RESPONSES_API.md)
through the existing native chat adapters and financial pipeline. This supersedes
the snapshot's statement that all Responses operations are missing; Responses
streaming was subsequently delivered by `GATEWAY-006`
([SSE lifecycle and evidence](33_RESPONSES_SSE.md)); embeddings and server-managed
conversations remain unsupported in that snapshot.
Operation-scoped duplicate suppression, verified/unknown evidence, cancellation,
normalized errors and generated contract parity require the linked task evidence.
No financial schema or accounting model is redesigned. OPS-006 and external
provider/merchant/HA/PITR launch gates remain separate and unpassed.

## Subsequent embeddings follow-up — 2026-10-17

`GATEWAY-007` introduces the [bounded native OpenAI text/float embeddings subset](34_EMBEDDINGS_API.md)
with input-only Catalog metadata, operation-scoped duplicate suppression and
the existing financial/evidence/recovery path. The earlier missing-embeddings
statement is historical, not a claim that native token IDs/base64/dimensions,
other embedding providers or cross-model vector substitution are implemented.
The Management/dashboard catalog remains its explicitly chat-only discovery
surface; Gateway `/v1/models` and the API guide describe embeddings. No financial
model or module boundary is replaced; the zero-output Catalog constraint change
requires the explicit Migrator. OPS-006/external launch gates remain unpassed.

## Subsequent provider-policy follow-up — 2026-10-17

`CATALOG-003` adds [dated provider data-policy provenance](35_PROVIDER_DATA_POLICIES.md)
with explicit unknown retention/training/residency, conditional source/scope
disclosure, append-only publication and atomic operator audit. Authorized
customer reads omit the publisher identity. This is metadata, not provider
verification, ZDR certification or residency routing; `PRIVACY-003` remains
separate. No live provider-policy facts are seeded. `REFUND-002` remains blocked
on its financial-policy decision; user approval selected this independent
task without resolving that decision or the OPS-006 staging requirement.

## Subsequent developer-experience follow-up — 2026-10-17

`DX-003` adds [bounded official Node SDK examples](36_SDK_EXAMPLES.md) for models,
chat JSON/SSE, Responses JSON/SSE and text/float embeddings. Six shipped CLI
commands run through the disposable real TLS stack; five replay denials, exact
wallet debit, hold release and Activity are verified. Offline SDK/schema/error/
cancellation cases disclose billing ambiguity and keep secrets out of browser
persistence and error logging. The optional browser Playground remains
unimplemented; no API, financial model, migration or deployed UI changes.

The final unchanged Release rerun passes 1006 tests with zero failures/skips.
The initial run's existing 400 ms provider-health test failure is retained beside
its passing isolated/full unchanged rechecks; no health fix or proven root cause
is claimed. Hosted CI, two-host staging and live provider/merchant qualification
remain separate. REFUND-002 still requires financial-policy confirmation and
OPS-006 still requires distinct staging hosts; no paid-launch gate is passed.

## Subsequent discretionary-credit follow-up — 2026-10-17

The user's Continue directly answering the DX-003 policy handoff confirms
credit-only adjustments and a different recently MFA-verified checker for
every amount. This supersedes the earlier REFUND-002 policy block without
erasing that historical review or changing the P0 staging gate.

`REFUND-002` adds immutable credit requests and single independent approvals,
organization/key replay protection, wallet-first serialization and atomic
ledger/debt/audit effects. Pending requests post no money. The maker and checker
cannot match; approval cannot override terms. Audit failure before commit and
cancelled/uncommitted operations roll back; unknown commit responses must use
the same operation identity for recovery. Active holds and captured spend
remain intact. No discretionary debit, cash payout, historical FX change or
new frontend screen is introduced.

Verified evidence includes 17 new policy cases, 5 new PostgreSQL financial
cases, the extended real HTTP operator/MFA/CSRF boundary and serializer/schema/
TypeScript fixtures. The final Release baseline passes **1028 tests across 16
assemblies**, with zero failures/skips (PostgreSQL 234/234). Targeted fixture
mistakes and OpenAPI inline-nullability failures were corrected, not accepted
or hidden. Fresh disposable TLS-stack regression also passes 5/5 scenarios;
all 24 implementation/contract file hashes remain unchanged through final
validation. See [scope, migration and dated validation](37_WALLET_ADJUSTMENTS.md).
Current production readiness still depends on distinct staging hosts,
fault/restore qualification and live business/merchant/provider evidence;
this follow-up does not pass those gates.
