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
| OPS-001, OPS-002 | Completed | Health, OpenTelemetry baseline, outbox/jobs, runbooks and local drill exist. | Custom gateway/business instruments are not emitted; alert events lack consumer; Worker leases/retries need hardening. `OPS-003–007`. |
| IDENTITY-001, ORGS-001, TEAM-001, AUDIT-001 | Completed | Accounts/sessions, organizations, membership, projects, audit and invitations exist. | Abuse throttling addressed by `SECURITY-002`; MFA enrollment proof/replay, denial behavior and recovery UX still need `SECURITY-003–004`, `FRONTEND-004`. |
| BILLING-001–002, BUDGET-001, LIMITS-001 | Completed | Conditional wallet update, append-only ledger, holds, terminal settlement, debt and caps are implemented. | Recovery operations, settlement exposure alerts, operator fee/FX publishing and refund flow remain. `BILLING-003`, `ADMIN-002`, `REFUND-001`. |
| APIKEYS-001, APIKEYS-003 | Completed | Hashed show-once keys, revocation/rotation and key budgets exist. | New-key response cache policy and fingerprint-key rotation need follow-ups. `SECURITY-003`, `SECURITY-005`. |
| CATALOG-001 | Completed | Model mappings, capabilities and effective pricing exist. | Customer discovery and operator publication UX incomplete. `DX-001`, `ADMIN-002`. |
| USAGE-001–002 | Completed | Durable request/attempt/evidence and on-demand usage/activity queries exist. | No CSV export (`FR-095`), no durable near-real-time rollups (`FR-096`); separate `USAGE-005` and `USAGE-004`. |
| PROVIDER-001–002, PROVIDER-006–009 | Completed | OpenAI, Anthropic, Gemini and DeepSeek adapters, normalized errors, contract fixtures and SSE paths exist. | Live agreement/quota/usage qualification external; BYOK supports only OpenAI/Anthropic. `BYOK-003`. |
| GATEWAY-001, ROUTING-003–005 | Completed | Managed admission, chat/SSE, deterministic/advanced selection and bounded fallback exist. | Mapping-scoped health may let one tenant's BYOK failure affect others; Responses/embeddings not implemented. `ROUTING-006`, `GATEWAY-005–007`. |
| PAYMENT-001 | Completed | Payme/CLICK protocol handlers, signatures, callback idempotency and contract fixtures exist. | Deployment edge omits callback routes; reconciliation is primarily local, live merchant verification external. `LAUNCH-001`, `PAYMENT-002`. |
| FRONTEND-001, ADMIN-001 | Completed | Dashboard/admin applications, shell and mocked browser coverage exist. | Top-up unit mismatch, missing recovery/financial state/customer catalog and other flows. `FRONTEND-002–010`. |
| NOTIFY-001–003 | Completed | Email, webhook, Telegram integrations and transport fixtures exist. | `ops.alert.raised` is enqueued but has no Worker consumer; live credentials/chats external. `OPS-004`. |
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
4. `OperationalWork` enqueues `ops.alert.raised` (and
   `billing.late_external_spend.recorded`), but
   `src/UZLLM.Worker/Program.cs` has no matching consumer. Worker dispatch is
   sequential with a one-minute lease; a slow callback can outlive it, and
   the claim query does not stop at the retry ceiling. Delivery and
   dead-letter handling need evidence.
5. `RedisProviderHealthService` keys health by mapping ID. A BYOK tenant's
   credential-specific 429/failure can therefore suppress a healthy managed
   or other-tenant route. Scope health by failure domain and test isolation.
6. Management payment authorization uses `Results.Forbid()` without a
   registered authentication handler in the Management host. Verify the actual
   response and replace it with a deliberate 403 contract if required.
7. Management has internal payment/fee services but lacks a complete audited
   operator path for FX/fee publication and external reconciliation evidence.
   The current payment reconciliation checks local state; it does not prove
   provider-side transaction agreement or case closure.
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
