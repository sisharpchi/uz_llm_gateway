# UZLLM Gateway — System Design Pack

> Working name: **UZLLM Gateway**  
> Target stack: **ASP.NET Core 10 + PostgreSQL + Redis + React/TypeScript**  
> Product type: **Multi-provider AI API Gateway + prepaid billing + observability**

## Implementation status

The Foundation baseline provides a buildable .NET 10 solution, three deployable
hosts, bounded module assemblies, PostgreSQL/Redis access, a version-controlled
foundation-schema migration, a migration-only host, local Compose environment,
CI validation, and architecture/integration tests. No business capability,
provider integration, or public endpoint is implemented until its dedicated
backlog task is complete.

The sellable MVP requires OpenAI and Anthropic integrations plus Payme and
CLICK top-ups. It uses USD-denominated credits purchased in UZS with immutable
FX snapshots. See `16_IMPLEMENTATION_BACKLOG.md` for delivery order.

## 1. Product vision

UZLLM Gateway is an Uzbekistan-first LLM gateway that gives developers and businesses:

- one API key;
- one OpenAI-compatible API;
- access to multiple AI providers/models;
- UZS top-up via local payment systems;
- centralized usage/cost analytics;
- per-project and per-key budgets;
- provider failover and routing;
- optional BYOK (Bring Your Own Key);
- Telegram/webhook alerts.

The intended user experience is:

```text
Developer
   |
   |  UZLLM API key
   v
UZLLM Gateway
   |
   +--> OpenAI
   +--> Anthropic
   +--> Google / Gemini
   +--> DeepSeek
   +--> other providers
```

A developer should usually migrate by changing only:

```text
BASE_URL=https://api.uzllm.example/v1
API_KEY=uzllm_xxx
```

while keeping the OpenAI-style request/response format.

---

## 2. Benchmark products

The requirements in this pack were derived from the project idea plus current public capabilities of:

- LLM Gateway
- OpenRouter
- Payme Merchant API
- CLICK API

Important benchmark capabilities:

### LLM Gateway

- OpenAI-compatible unified API;
- multiple providers;
- project-specific API keys;
- key TTL, rotation, enable/disable;
- lifetime and recurring spend limits;
- model/provider/pricing IAM rules;
- BYOK provider keys;
- provider/model routing;
- price, latency and throughput-aware routing;
- provider failover;
- response caching;
- usage, token, cost, error and performance analytics;
- guardrails;
- audit logs;
- project access roles.

### OpenRouter

- standardized multi-provider API;
- unified billing;
- provider failover;
- model fallbacks;
- routing by price, throughput and latency;
- BYOK;
- workspaces;
- per-key and workspace/team spend controls;
- model/provider restrictions;
- guardrails and privacy routing;
- activity/analytics;
- zero-data-retention-aware routing.

### Local differentiation

UZLLM adds:

- UZS payments;
- Payme and CLICK top-up;
- local-language support;
- Telegram alerts;
- local business onboarding;
- optional UZS-first dashboard while provider cost remains auditable in USD;
- future local/self-hosted model providers.

---

## 3. Recommended implementation strategy

**Do not begin with microservices.**

Start with a **modular monolith plus a separately deployable high-throughput Gateway API**:

```text
Frontend
   |
   +----------------------+
   |                      |
Management API       Gateway API
   |                      |
   +----------+-----------+
              |
      Application/Domain
              |
    +---------+---------+
    |         |         |
Postgres    Redis     Workers
                         |
                         +--> provider metadata sync
                         +--> alerts
                         +--> settlement/reconciliation
```

This creates clear boundaries without paying the operational cost of many microservices too early.

---

## 4. Documents in this pack

1. `01_PRODUCT_SCOPE_AND_BENCHMARK.md`
2. `02_FUNCTIONAL_REQUIREMENTS.md`
3. `03_NON_FUNCTIONAL_REQUIREMENTS.md`
4. `04_SYSTEM_DESIGN.md`
5. `05_BACKEND_ARCHITECTURE_DOTNET.md`
6. `06_DOMAIN_AND_BUSINESS_LOGIC.md`
7. `07_DATABASE_AND_DATA_MODEL.md`
8. `08_GATEWAY_ROUTING_AND_PROVIDER_ADAPTERS.md`
9. `09_BILLING_WALLET_AND_PAYMENTS.md`
10. `10_SECURITY_OBSERVABILITY_RELIABILITY.md`
11. `11_API_CONTRACTS.md`
12. `12_FRONTEND_ARCHITECTURE.md`
13. `13_FRONTEND_PAGES_AND_FLOWS.md`
14. `14_MVP_AND_DELIVERY_ROADMAP.md`
15. `15_ARCHITECTURE_DECISIONS.md`
16. `16_IMPLEMENTATION_BACKLOG.md`

---

## 5. Priority notation

- **P0** — required for first sellable MVP.
- **P1** — required shortly after MVP / production hardening.
- **P2** — advanced or enterprise capability.

## 6. Terminology and document authority

| Term | Meaning |
|---|---|
| Organization | Tenant and billing identity. |
| Project | Workload/configuration scope; “workspace” is explanatory only. |
| Logical request | One customer inference operation. |
| Attempt | One potential upstream execution within a logical request. |
| Provider mapping | A configured endpoint serving a canonical model. |
| Provider failover | Another eligible mapping for the same canonical model. |
| Model fallback | An explicitly allowed canonical-model change. |
| Usage evidence | Persisted upstream observations supporting accounting. |
| Settlement | Atomic charge and release of unused reservation. |
| Recovery debt | Reversed credit unavailable for immediate wallet recovery. |

Accepted ADRs own architectural decisions. Functional Requirements own
capabilities and priorities; Domain and Billing own invariants; API Contracts
own wire behavior; Database Design owns persistence; NFR/Security own
operational guarantees. The roadmap and backlog sequence this work.

---

## 7. Sources used

Official/current sources reviewed for this design:

- https://docs.llmgateway.io/
- https://docs.llmgateway.io/features/routing
- https://docs.llmgateway.io/learn/api-keys
- https://docs.llmgateway.io/learn/provider-keys
- https://docs.llmgateway.io/features/caching
- https://docs.llmgateway.io/resources/rate-limits
- https://docs.llmgateway.io/features/guardrails
- https://docs.llmgateway.io/learn/dashboard
- https://openrouter.ai/docs/guides/overview/principles
- https://openrouter.ai/docs/guides/routing/model-fallbacks
- https://openrouter.ai/providers/
- https://openrouter.ai/pricing
- https://openrouter.ai/blog/announcements/introducing-workspaces/
- https://openrouter.ai/blog/announcements/guardrails/
- https://developer.help.paycom.uz/metody-merchant-api/
- https://docs.click.uz/

This document is a system-design baseline, not legal or financial advice. Provider resale/aggregation terms must be reviewed before managed-credit production launch.
