# MVP and Delivery Roadmap

## Phase 0 — Validation and legal/commercial checks

Before managed-credit launch:

- confirm provider commercial terms;
- decide provider account/resale model;
- choose payment merchant setup;
- determine tax/accounting treatment;
- choose FX policy.

Technical spike:
- one OpenAI-compatible request;
- streaming;
- usage extraction;
- Payme/CLICK sandbox callback;
- reservation concurrency test.

---

## Phase 1 — Foundation

Deliver:
- solution structure;
- auth;
- organization;
- project;
- PostgreSQL;
- Redis;
- observability baseline;
- CI/CD;
- Docker environments.

Exit:
- authenticated dashboard;
- create/list project.

---

## Phase 2 — First end-to-end gateway

Deliver:
- gateway API key;
- `/v1/models`;
- `/v1/chat/completions`;
- streaming;
- one provider adapter;
- request log;
- usage/token capture.

Exit:

```text
Developer app -> UZLLM -> Provider -> response
```

with traceable request.

---

## Phase 3 — Wallet and local payments

Deliver:
- wallet;
- immutable ledger;
- reservation;
- settlement;
- Payme;
- CLICK;
- payment reconciliation.

Exit:
- UZS top-up creates credit;
- managed request safely deducts real usage.

This is the financial core and requires heavy tests.

---

## Phase 4 — Multi-provider routing

Deliver:
- 2–4 provider families;
- canonical models;
- provider mappings;
- health;
- failover;
- admin enable/disable.

Exit:
- same client API can switch/fail over between providers.

---

## Phase 5 — Dashboard MVP

Deliver:
- overview;
- API keys;
- models;
- activity;
- request detail;
- analytics basic;
- billing/top-up;
- payment history.

Exit:
- customer can operate without direct DB/admin assistance.

---

## Phase 6 — Production controls

Deliver:
- key spend caps;
- RPM;
- concurrency limits;
- project budgets;
- low-balance guard;
- security hardening;
- backup/restore;
- alerting;
- load testing.

Exit:
- safe public beta.

---

## Phase 7 — BYOK + advanced routing

Deliver:
- encrypted BYOK;
- hybrid mode;
- price route;
- latency route;
- throughput route;
- model fallback;
- Telegram alerts.

---

## Phase 8 — Advanced product

Candidates:
- response caching;
- guardrails;
- ZDR/privacy routing;
- SSO;
- external management automation API;
- custom providers;
- invoices;
- enterprise SLA;
- image/audio/video.

---

## MVP must-have checklist

### Backend
- [ ] Organization/project
- [ ] API key
- [ ] OpenAI-compatible chat
- [ ] SSE streaming
- [ ] Models endpoint
- [ ] 2+ provider capability or one provider + clear adapter framework
- [ ] Usage metering
- [ ] Versioned pricing
- [ ] Wallet
- [ ] Reservation/settlement
- [ ] Payme
- [ ] CLICK
- [ ] Provider health/fallback
- [ ] Rate/concurrency limits
- [ ] Admin provider/model controls

### Frontend
- [ ] Onboarding
- [ ] Overview
- [ ] Projects
- [ ] API keys
- [ ] Models
- [ ] Activity
- [ ] Request detail
- [ ] Billing/top-up
- [ ] Payment history

### Ops
- [ ] Metrics
- [ ] Tracing
- [ ] Structured logs
- [ ] DB backup
- [ ] TLS
- [ ] Secret management
- [ ] Production alerts
- [ ] Load test

---

## Do not overbuild before MVP

Postpone unless demanded:
- Kubernetes;
- Kafka;
- dozens of microservices;
- 50 providers;
- complex AI router;
- SSO;
- custom billing plans engine;
- full prompt observability platform;
- video generation.

The fastest path to learning is a financially correct, reliable gateway with local top-up and excellent developer UX.

---

## Superseding delivery order

This section supersedes the phase order above where it conflicts.

1. **Foundation and secure ownership:** documentation, solution, persistence,
   telemetry, outbox, identity, organizations, audit, encryption, projects.
2. **Financial admission and metering:** API keys, catalog/prices, request and
   attempt evidence, atomic wallet/key holds, settlement, debt, Redis limits.
3. **First managed Gateway slice:** OpenAI, deterministic routing, chat/SSE,
   cancellation, evidence, and finalization.
4. **Local payments and onboarding:** FX quotes, Payme, CLICK, reversal,
   reconciliation, and customer top-up/key screens.
5. **Multi-provider and visibility:** Anthropic, same-model failover, activity,
   rollups, dashboard, and operator controls.
6. **Production qualification:** alerts, two-node deployment, secrets/TLS,
   HA/PITR recovery, load and failure gates, commercial configuration.
7. **BYOK and advanced routing:** team/project grants, BYOK/Hybrid, recurring
   budgets, price/latency routing, explicit model fallback, customer alerts.
8. **Advanced/enterprise:** SSO, SCIM, ZDR controls, custom endpoints,
   guardrails, and new inference operations.

No public managed-credit traffic may begin before financial admission, key caps,
rate/concurrency limits, wallet reservation/settlement, audit, and dependency
failure behavior are complete. Sellable MVP requires both OpenAI and Anthropic,
both Payme and CLICK, and the checklist in `16_IMPLEMENTATION_BACKLOG.md`.
