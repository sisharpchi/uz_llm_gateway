# Functional Requirements

## 1. Identity and organizations

### FR-001 — Registration [P0]
The system shall allow a user to create an account using email/password or another approved authentication method.

### FR-002 — Authentication [P0]
The system shall issue authenticated web sessions/tokens for management APIs.

### FR-003 — Organization [P0]
Every billable account shall belong to an organization, including a one-person organization.

### FR-004 — Organization roles [P0/P1]
P0 shall support an Owner who controls the organization and a separate operator
authorization boundary. P1 adds the full customer role set:
Support:
- Owner
- Admin
- Developer
- Billing Viewer / Finance
- Read-only

### FR-005 — Team invitations [P1]
Owner/Admin shall invite members and revoke access.

---

## 2. Projects / workspaces

### FR-010 — Project creation [P0]
Users shall create isolated projects such as:
- Production
- Staging
- Client A

### FR-011 — Project isolation [P0]
API keys, budgets, usage and routing configuration shall be attributable to a project.

### FR-012 — Project status [P0]
Project shall support Active / Archived.

### FR-013 — Project defaults [P1]
Project may define:
- default routing strategy;
- cache policy;
- allowed providers;
- allowed models;
- logging/data-retention preference.

---

## 3. Gateway API keys

### FR-020 — Create key [P0]
User shall create a project-scoped gateway API key.

### FR-021 — Show-once secret [P0]
Full secret shall be shown only on creation/rotation.

### FR-022 — Key status [P0]
Key shall be enableable/disableable without deletion.

### FR-023 — Spend limit [P0]
Key shall support a hard spend limit.

### FR-024 — Recurring limit [P1]
Key may support:
- daily;
- weekly;
- monthly.

### FR-025 — Expiration [P0]
Key expiry shall be stored and enforced when set. Advanced TTL presets are P1.

### FR-026 — Rotation [P1]
Secret may be rotated while preserving key identity/history.

### FR-027 — Model/provider permissions [P1]
Key may have allow/deny rules for:
- providers;
- models;
- maximum model price class.

### FR-028 — Key usage visibility [P0]
Dashboard shall show requests, tokens and spend by key.

---

## 4. Model catalog

### FR-030 — Model list [P0]
Expose normalized model catalog.

### FR-031 — Model metadata [P0]
At minimum:
- canonical model ID;
- provider mappings;
- input price;
- output price;
- context length;
- max output;
- capabilities;
- availability/status.

### FR-032 — Capability flags [P1]
Examples:
- text;
- vision;
- tools/function calling;
- structured output;
- reasoning;
- embeddings;
- image generation;
- audio.

### FR-033 — Price history [P0]
Pricing changes must be versioned with effective timestamps.

### FR-034 — Disable model/provider mapping [P0]
Admin must be able to disable a broken or deprecated endpoint quickly.

---

## 5. Unified inference API

### FR-040 — Chat completions [P0]
Support:

`POST /v1/chat/completions`

with OpenAI-compatible core fields.

### FR-041 — Streaming [P0]
Support `stream=true` using SSE and stream upstream chunks without buffering the whole completion.

### FR-042 — Models endpoint [P0]
Support:

`GET /v1/models`

### FR-043 — Responses API [P1]
Add OpenAI-style Responses API after chat completion stability.

### FR-044 — Embeddings [P1]
Add normalized embedding API.

### FR-045 — Other modalities [P2]
Image/audio/video endpoints may be added based on demand.

### FR-046 — Error envelope [P0]
Gateway errors shall use a predictable OpenAI-compatible error structure.

### FR-047 — Request ID [P0]
Every request shall receive a unique gateway request ID for support and tracing.

---

## 6. Provider integrations

### FR-050 — Provider adapter [P0]
Every upstream provider must be accessed through an adapter contract.

### FR-051 — Normalize request [P0]
Translate normalized gateway request into provider-specific schema when required.

### FR-052 — Normalize response [P0]
Translate provider response/stream into gateway response schema.

### FR-053 — Usage extraction [P0]
Adapter must extract:
- input tokens;
- output tokens;
- cached tokens when available;
- provider request ID.

### FR-054 — Provider credentials [P0]
Platform-managed credentials shall be stored securely.

### FR-055 — BYOK credentials [P1]
Users may register provider credentials.

### FR-056 — Provider-key restrictions [P1]
BYOK key may be limited to selected models and optional max spend.

---

## 7. Routing

### FR-060 — Explicit provider/model [P0]
Client may pin a provider/model when allowed.

Example:

`openai/gpt-x`

### FR-061 — Default model route [P0]
Canonical model ID may map to one or more eligible provider endpoints.

### FR-062 — Failover [P0]
If an upstream endpoint fails with an eligible transient failure, gateway shall attempt another eligible endpoint within policy.

### FR-063 — Price routing [P1]
Select cheapest healthy eligible provider.

### FR-064 — Latency routing [P1]
Select endpoint based on recent TTFT/latency.

### FR-065 — Throughput routing [P1]
Select endpoint based on output tokens/sec.

### FR-066 — Model fallback [P1]
Allow ordered fallback models.

### FR-067 — Provider health [P0]
Routing shall exclude open-circuit/unhealthy endpoints.

### FR-068 — Routing reason [P1]
Internal request record should store why a provider was selected.

---

## 8. Wallet and billing

### FR-070 — Wallet [P0]
Organization shall have a credit wallet.

### FR-071 — Immutable ledger [P0]
Every balance-changing event shall create immutable ledger entries.

### FR-072 — Top-up [P0]
User shall top up via supported payment provider.

### FR-073 — Payme [P0]
Implement Payme transaction lifecycle with idempotent callbacks.

### FR-074 — CLICK [P0]
Implement CLICK transaction lifecycle with idempotent callbacks.

### FR-075 — Cost reservation [P0]
Before sending a managed-credit inference request, reserve estimated maximum/allowed cost.

### FR-076 — Settlement [P0]
After completion:
- compute actual cost;
- capture actual amount;
- release unused reservation.

### FR-077 — Failed request accounting [P0]
No successful provider usage => release reservation, except when upstream contract legitimately charges partial/streamed usage.

### FR-078 — FX snapshot [P0]
Store UZS payment amount and conversion/rate snapshot used to create gateway credit.

### FR-079 — Fee policy [P0]
Support configurable platform markup/fee policy without changing provider adapters.

### FR-080 — Refund/adjustment [P1]
Admin may issue audited credit adjustments/refunds.

### FR-081 — Provider payment reversal [P0]
A confirmed provider reversal shall recover available credit, record remaining
recovery debt, preserve active reservations, and hold new managed spending.

---

## 9. Usage metering and analytics

### FR-090 — Request record [P0]
Record:
- organization;
- project;
- API key;
- model;
- provider;
- timestamps;
- status;
- token counts;
- cost;
- latency;
- routing result.

### FR-091 — Payload logging policy [P0]
Prompt/response payload storage is disabled in P0. P1 may add explicit,
encrypted, time-limited opt-in retention.

### FR-092 — Dashboard totals [P0]
Show:
- requests;
- input/output tokens;
- total cost;
- error rate;
- top models;
- top providers.

### FR-093 — Filters [P0]
Filter by:
- date;
- project;
- API key;
- model;
- provider;
- status.

### FR-094 — Request detail [P0]
Show trace-level metadata for debugging without exposing secrets.

### FR-095 — Export [P1]
CSV export of usage/activity.

### FR-096 — Near-real-time aggregation [P1]
Dashboard counters should update within a short delay.

---

## 10. Budgets, limits and abuse protection

### FR-100 — Balance gate [P0]
Reject managed-credit requests with insufficient available balance.

### FR-101 — Project budget [P1]
Hard cap per project.

### FR-102 — API key budget [P0]
Hard cap per key.

### FR-103 — RPM limit [P0]
Rate-limit requests per API key/project.

### FR-104 — Concurrency limit [P0]
Limit in-flight generation requests.

### FR-105 — Provider quota protection [P0]
Track provider quota/capacity and avoid exhausted credentials.

### FR-106 — Request constraints [P0]
Reject invalid context size, output size or unsupported features before upstream request where possible.

---

## 11. Alerts and notifications

### FR-110 — Low balance alert [P1]
Trigger at configurable thresholds.

### FR-111 — Budget warning [P1]
Notify at e.g. 50%, 80%, 100%.

### FR-112 — Error spike [P1]
Notify when error-rate threshold is exceeded.

### FR-113 — Telegram integration [P1]
User may connect a Telegram destination for alerts.

### FR-114 — Webhook [P1]
Optional signed webhook delivery.

### FR-115 — Deduplication [P1]
Alert engine must suppress repetitive alert storms.

---

## 12. Admin console

### FR-120 — User/org search [P0]
Support operators can locate accounts.

### FR-121 — Provider operations [P0]
Enable/disable provider credentials and model mappings.

### FR-122 — Pricing operations [P0]
Add future-effective prices without overwriting historical prices.

### FR-123 — Ledger inspection [P0]
Admins can inspect but not silently mutate ledger history.

### FR-124 — Manual adjustment [P1]
Audited compensating entry only.

### FR-125 — Payment reconciliation [P0]
View gateway payment state versus Payme/CLICK state.

### FR-126 — System incident controls [P0]
Temporarily disable:
- provider;
- model;
- managed-credit traffic;
- top-ups.

---

## 13. Reliability and accounting completion

### FR-130 — Inference idempotency [P0]
For supported `Idempotency-Key` requests, the system shall suppress duplicate
execution for 24 hours. Repeats return `409` and the original request ID;
response bodies are not retained for replay.

### FR-131 — Request/attempt evidence [P0]
The system shall persist a logical request, separate provider attempts, and
durable usage evidence before financial settlement.

### FR-132 — Pending financial outcomes [P0]
The system shall distinguish pending evidence from pending settlement. Unknown
usage is reconciled for a bounded period; verified usage is charged and
unresolved cost is absorbed by the platform.

### FR-133 — Reversal recovery [P0]
The system shall apply the recovery-debt and spending-hold behavior in FR-081.

### FR-134 — Required provider families [P0]
The sellable MVP shall integrate OpenAI and Anthropic through independent
adapter implementations.

### FR-135 — Verified account access [P0]
The system shall provide email verification and secure account-recovery flows
before enabling paid gateway use.

## 14. Future enterprise functions

### P2
- SSO/SAML
- SCIM
- Guardrails
- PII redaction
- ZDR-aware routing
- regional routing
- custom provider endpoints
- routing rules UI
- A/B routing
- management API
- webhooks for usage events
- invoicing
- contractual SLA
