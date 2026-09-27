# Next-Phase Roadmap

The application is a working modular monolith with separate Gateway,
Management and Worker hosts. Keep PostgreSQL authoritative for financial
state, Redis advisory, provider translation inside adapters, and no provider
network call inside a transaction. The audit found corrective work, not a
reason to split services or replace the wallet model. Use
[16](16_IMPLEMENTATION_BACKLOG.md) for the **executable** register and
[18](18_PRODUCTION_READINESS.md) for the independent launch gates.

## Execution rules

One dependency-safe task at a time, in register order, unless a documented
dependency correction is required. A task is complete only after its stated
acceptance criteria and tests pass, docs/status are updated, and its own commit
is made. Do not begin the next task without user approval. An external
commercial prerequisite may block launch but must not be disguised as a
passing software test. P2 items below are contract/specification work and
require explicit product approval before feature implementation.

## P0 — Safe paid-launch path

| Phase | Ordered work | Why now / exit criterion |
|---|---|---|
| 0. Audited baseline | `DOCS-002` | Freeze truthful baseline and executable dependencies. Exit: audit, gates and register linked and validated. |
| 1. Immediate correctness/security | `LAUNCH-001`, `FRONTEND-002`, `SECURITY-002–004`, `OPS-003–005`, `ROUTING-006` | Fix unreachable callbacks, UZS unit error, abuse/MFA/denial and shared routing/telemetry failure domains before broader traffic. Exit: targeted adversarial and regression suites pass. |
| 2. Financial operations | `BILLING-003`, `PAYMENT-002`, `ADMIN-002`, `REFUND-001`, `FRONTEND-003` | Recovery, reconciliation and audited operator actions must precede paid financial UX. Exit: concurrent/replayed payments and refunds leave reconciled ledger and visible cases. |
| 3. Customer vertical | `DX-001`, `FRONTEND-004–005`, `SECURITY-005–006`, `PRIVACY-002` | Make onboarding, account recovery, activity and secret/retention operations usable and safe. Exit: real backend end-to-end flow, not only mocked browser data. |
| 4. Production qualification | `LAUNCH-002`, `OPS-006–007`, `LAUNCH-003` | Exercise real-stack CI, multi-node load/fault behavior and attach evidence. Exit: repository gates have dated evidence; external gates remain separately signed off. |

Independent P0 tasks may be developed in parallel by a team, but the default
single-task execution cycle follows the register order. `LAUNCH-002` waits for
the P0 functional/security/financial prerequisites; `OPS-006–007` wait for
telemetry and real-stack testing. Neither load tests nor deployment rehearsal
can prove live payment/provider agreements.

## P1 — Product completion after safe launch

| Stream | Dependency-safe order | Exit criterion |
|---|---|---|
| Architecture and developer experience | `ARCH-001`, `DX-002` | Module access and public contract drift are enforced without redesigning the shared financial transaction boundary. |
| Usage and controls | `USAGE-004`, `USAGE-005`, `APIKEYS-004`, `LIMITS-002`, `PROJECTS-002` | Durable rollups, tenant-safe CSV, and configurable project/key/provider controls have explicit contracts. |
| Customer UI | `FRONTEND-006–010` in register dependency order | Customer can manage projects, team, BYOK/routing, alerts/privacy, and accessible localized flows using real APIs. |
| Provider and API breadth | `BYOK-003`, `GATEWAY-005–007`, `CATALOG-003` | Additional BYOK parity, explicitly bounded Responses/embeddings compatibility, and data-policy metadata have adapter/contract fixtures. |
| Financial/admin and examples | `REFUND-002`, `DX-003` | Discretionary adjustments are audited and user-facing examples agree with the versioned contracts. |

`USAGE-004` is aggregation (`FR-096`); `USAGE-005` is export (`FR-095`).
`GATEWAY-005` begins with non-streaming Responses (`FR-043`); SSE and embeddings
are separate increments. `REFUND-001` is a wallet credit for a prior settled
charge; cash payout is not silently implied.

## P2 — Advanced/enterprise, specification first

`ENTERPRISE-001` (SSO/SCIM), `PRIVACY-003` (ZDR/residency), `ROUTING-007`
(advanced routing/cache), `PROVIDER-010` (custom endpoints), `GATEWAY-008`
(additional modalities), `SECURITY-007` (guardrails/PII), `DX-004` (management
automation), and `ENTERPRISE-002` (invoice/SLA) first produce an approved
contract, risk model, pricing and acceptance scenarios. Do not implement these
features simply because their names appear in a roadmap. Their exit is an
approved, decomposed follow-on backlog, not production activation.

## Dependency spine

```text
DOCS-002
  ├─ LAUNCH-001 / FRONTEND-002 / SECURITY-002–004 / OPS-003–005 / ROUTING-006
  │    └─ BILLING-003 → PAYMENT-002 / REFUND-001 → FRONTEND-003
  ├─ DX-001 / FRONTEND-004–005 / SECURITY-005–006 / PRIVACY-002
  └─ LAUNCH-002 (real-stack) → OPS-006 (multi-node load)
                              → OPS-007 (fault/restore)
                              → LAUNCH-003 (gate evidence)
```

The graph summarizes sequencing; the exact dependencies and acceptance tests
are in the backlog. Production activation additionally requires the external
evidence in `18_PRODUCTION_READINESS.md`.
