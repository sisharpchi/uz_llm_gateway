# Architecture Decisions

This file records accepted, rejected, and superseded ADRs.

## Status convention

ADRs 001–025 are **Accepted** architecture decisions and have implementation
status **Not implemented** unless a backlog task says otherwise. “Accepted”
means selected for implementation; it does not claim a feature exists or passed
tests. ADR-001–017 are implementation defaults except the product choices
explicitly recorded below. Each decision is implemented through
`16_IMPLEMENTATION_BACKLOG.md` and governed by the normative documents named
in `00_README.md`.

---

## ADR-001 — Use ASP.NET Core 10

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Foundation-002 baseline implemented

### Decision
Use .NET 10 LTS for backend services.

### Why
- high-performance HTTP;
- strong async/streaming support;
- mature PostgreSQL/Redis ecosystem;
- good team alignment.

---

## ADR-002 — React + TypeScript as default frontend

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

### Decision
Use React + TypeScript for dashboard/admin.

### Notes
Vue 3 is an equally valid alternative if the team is stronger in Vue. The backend/system design does not depend on React.

---

## ADR-003 — Modular monolith before microservices

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Foundation-002 structural baseline implemented

### Decision
Keep business modules in one codebase/process family while preserving module boundaries.

Separately deploy:
- Gateway API
- Management API
- Worker

### Reason
Lower operational complexity while product rules are evolving.

---

## ADR-004 — OpenAI-compatible public API

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

### Decision
Use OpenAI-compatible endpoints as the initial developer contract.

### Reason
Lowest migration friction and proven gateway pattern.

---

## ADR-005 — PostgreSQL is financial source of truth

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Foundation-003 persistence baseline implemented

Redis is never authoritative for:
- wallet;
- ledger;
- payment state;
- settlement.

---

## ADR-006 — Immutable ledger

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** BILLING-001 foundation implemented

Do not correct financial history by editing rows.

Use compensating entries.

---

## ADR-007 — Reserve before managed inference

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Concurrency makes post-charge-only accounting unsafe.

Use cost reservation then settlement/release.

---

## ADR-008 — Store gateway keys as non-reversible fingerprints

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Implemented by `APIKEYS-001`

Gateway key full secret is shown once. The gateway stores a unique public
prefix and keyed HMAC fingerprint of the complete secret; authentication uses
constant-time fingerprint comparison. Key status and expiry are enforced, and
no recoverable key material is persisted.

BYOK/provider credentials are different and must be encrypted because upstream execution needs plaintext.

---

## ADR-009 — Version model pricing

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Catalog-001 foundation implemented

Never overwrite old price without history. Provider-mapping price versions use
half-open effective intervals and PostgreSQL rejects overlapping intervals.

Every usage record must be tied to the price used for billing.

---

## ADR-010 — Keep provider adapters isolated

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

No provider SDK types in Domain/Application modules.

This prevents vendor coupling.

---

## ADR-011 — Separate payload retention from request metadata

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Default product should work with request metadata even when prompt/response body retention is disabled.

---

## ADR-012 — Local payments are product-core, not plugin-only

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Payme/CLICK integration belongs in the core MVP because local top-up is a primary differentiator.

---

## ADR-013 — Managed + BYOK + Hybrid business modes

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Architecture must support all three even if MVP launches with a subset.

This protects the product from provider-commercial constraints and supports enterprise customers.

---

## ADR-014 — Eventual consistency for analytics

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Financial state is strongly consistent. Analytics rollups may lag briefly.

---

## ADR-015 — Routing starts simple

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

MVP:
- explicit provider/model;
- deterministic healthy fallback.

P1:
- price/latency/throughput weighted routing.

Reason:
routing sophistication must not delay reliable billing and developer UX.

---

## ADR-016 — English internal identifiers, localized UI

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** Not implemented

Code/database/API identifiers use stable English terms.

UI supports Uzbek/Russian/English through i18n.

---

## ADR-017 — Use Outbox for post-commit events

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** OPS-001 infrastructure baseline implemented

Examples:
- payment completed;
- wallet topped up;
- low balance threshold reached;
- audit/notification events.

This prevents lost side effects after committed transactions.

---

## ADR-018 — Sellable MVP provider/payment set

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** Not implemented

The sellable MVP requires OpenAI, Anthropic, Payme, and CLICK. Provider families
do not make models interchangeable; P0 failover remains same-model only.

## ADR-019 — USD-credit wallet and immutable FX snapshots

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** BILLING-001 wallet and FX-history foundation implemented; payment-linked snapshots remain in PAYMENT-001

Customers pay UZS and receive USD-denominated credits from a frozen quote and
payment FX snapshot. Historical values are never recomputed.

## ADR-020 — Verified-usage billing

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** BILLING-002 implements bounded reconciliation, settlement, and immutable late platform-exposure records

Persist evidence before settlement. Reconcile unknown outcomes for a bounded
window, charge verified usage, and absorb unresolved provider cost.

## ADR-021 — Recovery debt after payment reversal

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** BILLING-002 implements available-credit recovery, debt, and managed-spend hold

Recover available credit, preserve active reservations, create recovery debt for
the remainder, and block new managed spending.

## ADR-022 — Duplicate suppression without response replay

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** Not implemented

Retain inference idempotency metadata for 24 hours and return the prior request
ID on a conflict; do not retain completion bodies for replay.

## ADR-023 — Managed data services and two app nodes

**Status:** Accepted  
**Basis:** User-confirmed choice  
**Implementation status:** Not implemented

Use two application nodes, managed PostgreSQL with synchronous HA/PITR, and
managed Redis. Actual provider guarantees are launch prerequisites.

## ADR-024 — Evidence before settlement and shared financial transactions

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** BILLING-002 implements atomic reservation/finalization on the shared PostgreSQL transaction coordinator

Usage evidence is durable before financial finalization. Module contexts may
share a PostgreSQL transaction for atomic financial effects.

## ADR-025 — Secure browser sessions and operator boundary

**Status:** Accepted  
**Basis:** Implementation default  
**Implementation status:** IDENTITY-001 baseline implemented; immutable audit records complete in AUDIT-001

Use secure server-backed management sessions and separately authorized,
audited MFA-protected operator access.
