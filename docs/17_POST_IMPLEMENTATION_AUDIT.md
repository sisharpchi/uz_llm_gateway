# Post-Implementation Audit

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
| IDENTITY-001, ORGS-001, TEAM-001, AUDIT-001 | Completed | Accounts/sessions, organizations, membership, projects, audit and invitations exist. | Abuse throttling addressed by `SECURITY-002`; denial/cache behavior by `SECURITY-003`; MFA proof/replay and audited operator reset by `SECURITY-004`. Customer verification/recovery UX still needs `FRONTEND-004`. |
| BILLING-001–003, BUDGET-001, LIMITS-001 | Completed | Conditional wallet update, append-only ledger, holds, terminal settlement, debt and caps are implemented. `BILLING-003` adds expired-hold sweep, terminal missing-evidence recovery, deduplicated settlement-failure alerts and operator financial-risk reads. | `ADMIN-002` adds operator fee/FX publication; `REFUND-001` adds settlement-linked wallet-credit counter-entries. Cash payouts and discretionary adjustments remain separate. |
| APIKEYS-001, APIKEYS-003 | Completed | Hashed show-once keys, revocation/rotation and key budgets exist. | New-key response cache policy addressed by `SECURITY-003`; fingerprint-key rotation still needs `SECURITY-005`. |
| CATALOG-001 | Completed | Model mappings, capabilities and effective pricing exist. | Customer discovery and operator publication UX incomplete. `DX-001`, `ADMIN-002`. |
| USAGE-001–002 | Completed | Durable request/attempt/evidence and on-demand usage/activity queries exist. | No CSV export (`FR-095`), no durable near-real-time rollups (`FR-096`); separate `USAGE-005` and `USAGE-004`. |
| PROVIDER-001–002, PROVIDER-006–009 | Completed | OpenAI, Anthropic, Gemini and DeepSeek adapters, normalized errors, contract fixtures and SSE paths exist. | Live agreement/quota/usage qualification external; BYOK supports only OpenAI/Anthropic. `BYOK-003`. |
| GATEWAY-001, ROUTING-003–006 | Completed | Managed admission, chat/SSE, deterministic/advanced selection, bounded fallback and credential-isolated health with cross-node half-open probes exist. | Responses/embeddings not implemented. `GATEWAY-005–007`. |
| PAYMENT-001 | Completed | Payme/CLICK protocol handlers, signatures, callback idempotency and contract fixtures exist. | Deployment edge omits callback routes; reconciliation is primarily local, live merchant verification external. `LAUNCH-001`, `PAYMENT-002`. |
| FRONTEND-001, ADMIN-001 | Completed | Dashboard/admin applications, shell and mocked browser coverage exist. | Top-up unit mismatch, missing recovery/financial state/customer catalog and other flows. `FRONTEND-002–010`. |
| NOTIFY-001–003 | Completed | Email, webhook, Telegram integrations and transport fixtures exist. | `OPS-004` adds the missing operational alert consumer and delivery status. Live on-call mailbox/SMTP receipt verification remains external. |
| BYOK-001–002, PRIVACY-001 | Completed | Tenant-scoped encrypted keys, BYOK execution and opt-in bounded payload retention exist. | Credential/metadata lifecycle and additional provider parity need qualification. `SECURITY-006`, `PRIVACY-002`, `BYOK-003`. |

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
   diagram in `05_BACKEND_ARCHITECTURE_DOTNET.md`; preserve the working
   transaction model and add boundary guardrails incrementally.

## Requirements still open or underspecified

- `USAGE-005` is `FR-095` export: define bounded, tenant-authorized CSV export
  from the current usage read model. `FR-096` durable/near-real-time rollups
  belong to separate `USAGE-004`; current daily SQL aggregation is not that.
- `GATEWAY-005` is `FR-043` Responses API. Start with a versioned, explicitly
  supported **non-streaming** subset; streaming and embeddings (`FR-044`) are
  separate tasks. Do not imply the chat contract automatically covers them.
- `REFUND-001` is a settlement-linked **wallet-credit refund** under `FR-080`
  and `FR-124`, with immutable counter-entry and idempotency. Cash payout,
  provider reversal and discretionary adjustment are different policies; the
  latter belongs to `REFUND-002`, while cash payout is external-policy gated.
- Project/key permissions, customer limits/alerts/team/BYOK/routing UI,
  privacy metadata retention, OpenAPI/SDK examples, and enterprise features
  remain incomplete or require a fresh contract before implementation.

## Audit disposition

No historical `Completed` status is erased. The corrective, product and
qualification tasks are registered with explicit dependencies in
`16_IMPLEMENTATION_BACKLOG.md`. Do not claim launch readiness from compilation,
mocked browser tests, local callback fixtures, or a single-host drill.
