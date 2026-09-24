# Implementation Backlog

## Status and dependency rules

The repository contains the completed Foundation baseline. Every task below is
`Planned` unless listed below. Financial admission, evidence, and settlement precede managed public
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

All remaining task-register entries are `Planned` unless listed above.

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

## Later tasks

P1: `BUDGET-001`, `APIKEYS-003`, `TEAM-001`, `BYOK-001`, `BYOK-002`,
`ROUTING-003`, `ROUTING-004`, `ROUTING-005`, `NOTIFY-002`, `NOTIFY-003`,
`USAGE-005`, `PRIVACY-001`, `GATEWAY-005`, Gemini/DeepSeek adapters, and
`REFUND-001`.

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
