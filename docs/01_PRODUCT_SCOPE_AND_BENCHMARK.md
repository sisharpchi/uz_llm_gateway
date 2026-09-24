# Product Scope and Benchmark

## 1. Problem statement

Local developers often need several LLM providers but face four recurring problems:

1. payment friction with international cards;
2. separate balances and invoices across providers;
3. no unified cost and token visibility;
4. provider downtime, rate limits and model price differences.

UZLLM solves these with one gateway, one account and one usage/billing layer.

---

## 2. Primary personas

### 2.1 Solo developer

Needs:
- quick API key;
- low minimum top-up;
- simple OpenAI-compatible integration;
- per-key budget;
- request logs.

### 2.2 Startup / SaaS

Needs:
- projects/workspaces;
- separate production/staging keys;
- budgets and alerts;
- provider fallback;
- analytics;
- multiple team members.

### 2.3 AI agency / integrator

Needs:
- separate client projects;
- cost attribution;
- project keys;
- export;
- controlled provider/model access.

### 2.4 Enterprise

Needs:
- organization roles;
- audit trail;
- BYOK;
- data/privacy controls;
- custom routing;
- invoices;
- SLA and support.

---

## 3. Product boundary

### In scope

- Account and organization management
- Projects/workspaces
- API keys
- Unified OpenAI-compatible inference API
- Model catalog
- Provider adapters
- Routing and failover
- Managed credits
- BYOK after the sellable MVP
- UZS top-up
- Wallet and immutable ledger
- Usage/cost accounting
- Rate limits and budgets
- Logs and analytics
- Alerts
- Admin console

### Not in initial scope

- Training/fine-tuning infrastructure
- Full AI chat consumer product
- GPU hosting platform
- Arbitrary agent execution environment
- Marketplace for third-party prompts
- Full observability replacement for Datadog/Grafana
- Enterprise SSO in MVP

---

## 4. Benchmark matrix

| Capability | LLM Gateway | OpenRouter | UZLLM target |
|---|---|---|---|
| Unified API | Yes | Yes | P0 |
| OpenAI compatibility | Yes | Yes | P0 |
| Multiple providers | Yes | Yes | P0 |
| Provider failover | Yes | Yes | P0 |
| Model fallback | Routing/dynamic routing | Yes | P1 |
| Price-aware routing | Yes | Yes | P1 |
| Latency routing | Yes | Yes | P1 |
| Throughput routing | Yes | Yes | P1 |
| BYOK | Yes | Yes | P1 |
| API-key spend caps | Yes | Yes | P0 |
| Key TTL/rotation | Yes | Yes/management controls | P1 |
| Workspaces/projects | Yes | Yes | P0 |
| Usage analytics | Yes | Yes | P0 |
| Full activity logs | Yes | Yes | P0 |
| Response caching | Yes | Yes | P1 |
| Guardrails | Yes | Yes | P2 |
| ZDR/data policy routing | Yes/related privacy controls | Yes | P2 |
| Team roles | Yes | Yes | P1 |
| Audit logs | Yes | Yes | P1 |
| Local UZS payments | No | No | **P0 differentiator** |
| Payme/CLICK | No | No | **P0 differentiator** |
| Telegram low-balance alerts | Not core differentiator | Notifications exist | **P1 differentiator** |
| Local support | No | No | **Differentiator** |

---

## 5. Core value proposition

### Developer promise

> One API. One balance. Multiple AI models.

### Local promise

> Pay in UZS and control AI spend locally.

### Business promise

> Know exactly which project, key, model and provider consumed every unit of credit.

---

## 6. Product modes

### 6.1 Managed Credits

UZLLM owns/configures provider credentials and user pays UZLLM.

```text
User -> UZLLM wallet -> UZLLM provider account -> Provider
```

Advantages:
- easiest UX;
- local payment solves major friction;
- unified billing.

Risks:
- provider commercial/resale terms;
- working capital;
- FX exposure;
- fraud/chargeback exposure.

### 6.2 BYOK

Customer stores their provider key in UZLLM.

```text
User -> UZLLM Gateway -> user's provider account
```

Advantages:
- lower provider-account risk;
- easier enterprise adoption;
- users retain negotiated rates/credits.

Requirements:
- encrypted provider secrets;
- routing policy;
- usage analytics even when UZLLM is not paying inference cost.

### 6.3 Hybrid

Try BYOK first; fallback to managed credits if allowed.

This is the recommended long-term mode.

---

## 7. MVP product hypothesis

The MVP should prove:

1. users will top up in UZS;
2. developers accept an OpenAI-compatible base URL;
3. centralized cost visibility is valuable;
4. local payment/support justifies a platform margin;
5. OpenAI and Anthropic cover sufficient early demand while proving the adapter
   boundary; both are required for the sellable MVP.

Do not integrate dozens of providers before validating these hypotheses. Two
provider families do not imply interchangeable models: P0 failover only uses
verified mappings for the same canonical model. Cross-model fallback is P1.
