# Functional Requirements

## 1. Identity and organizations

### FR-001 — Registration [P0]
The system shall allow a user to create an account using email/password or another approved authentication method.

### FR-002 — Authentication [P0]
The system shall issue authenticated web sessions/tokens for management APIs.

Current Management authentication is browser session/CSRF under ADR-025, not
Gateway-key bearer access. The P2 [approved DX-004 specification](44_MANAGEMENT_AUTOMATION_API_SPEC.md)
defines a separate expiring Owner-sponsored, project/scope-bound read-only
credential design; no machine billing/key/policy writes or operator rights.
The user's 2026-10-18 Continue approves draft `0116885`, not feature execution
or task promotion. Existing authentication is unchanged.

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

### FR-011 — Project isolatfon [P0]
API keys, budgets, usage and routing configuration shall be attributable to a project.

### FR-012 — Project status [P0]
Project shall support Active / Archived.

### FR-013 — Project defaults [P1]
Project may define:
- default routing strategy;
- allowed providers;
- allowed models;
- an environment tag (metadata only).

`PROJECTS-002` decision confirmed 2026-10-13: explicit request routing overrides
only the strategy, never project allowlists, key permissions, wallet, budget or
operator limits. Null strategy inherits deterministic priority; null allowlists
are unrestricted and empty allowlists deny all. Spend controls reuse existing
Lifetime/Daily/Weekly/Monthly project budget policies, not a new cost ceiling or
duplicate financial state. Managed remains the default; BYOK and Hybrid require
explicit per-request selection/consent. Environment never changes safety or
billing. Cache policy remains future routing/cache work; payload retention uses
its separate opt-in API, not these defaults. See [project settings](25_PROJECT_DEFAULTS.md).

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

Flags describe capabilities, not universal support across operations/adapters.
The [approved GATEWAY-008 specification](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md) defines future
versioned operation-feature qualification/discovery; it is not implemented.

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

`GATEWAY-005` defines the stateless non-streaming text subset at `/v1/responses`,
translated through existing native chat adapters with the same authorization,
limits, reservation/evidence/settlement and explicit execution-mode controls.
No stored conversation, tools, modalities or Responses-specific reasoning controls
are implied. `GATEWAY-006` adds [Responses SSE](33_RESPONSES_SSE.md) for the same
subset with incremental text/refusal, terminal evidence, no replay after output,
and cancellation-independent financial cleanup. [Input capability matrix](32_RESPONSES_API.md).

Client function JSON/SSE and bounded non-streaming schema output are proposed
in the [approved GATEWAY-008 specification](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md), requiring
separate execution approval and implementation tasks. Current tools/schema rejection
and stateless execution remain unchanged; no hosted tool execution is implied.

### FR-044 — Embeddings [P1]
Add normalized embedding API.

`GATEWAY-007` defines the bounded OpenAI-native text/float subset at
`POST /v1/embeddings`: a string or 1–32 nonblank strings, each at most 8192
UTF-8 bytes and within the published model context. Input-only model/pricing,
operation-scoped idempotency and the existing authorization/financial/explicit
Managed/BYOK/Hybrid controls apply. Token IDs, base64, custom dimensions,
streaming, end-user passthrough and cross-model fallback are explicitly rejected.
Only priority/price routing is supported; chat performance samples are not reused.
See [exact contract and validation](34_EMBEDDINGS_API.md).

Qualified dimensions/base64 are proposed, not enabled, in the
[approved GATEWAY-008 specification](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md). They preserve
input-token billing, one fee per batch and the prohibition on cross-model fallback.

### FR-045 — Other modalities [P2]
Image/audio/video endpoints may be added based on demand.

[GATEWAY-008 §9](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md#9-deferred-modalities-entry-gates-not-hidden-backlog)
records demand, protocol, metering, exposure and security entry gates. No such
endpoint is scheduled merely by this conditional requirement; current token-only
pricing does not support per-image/audio/video charges.

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
BYOK-003 extends the existing capability to active `google` (Gemini) and
`deepseek` catalog providers alongside OpenAI/Anthropic. Custom endpoints,
service-account/OAuth credentials and additional capabilities are not implied.
FRONTEND-008 adds masked organization credential lifecycle and explicit project
grants. Creation never changes project execution defaults; no secret reveal.

### FR-056 — Provider-key restrictions [P1]
BYOK key may be limited to selected models and optional max spend.
The current cap bounds catalog-estimated lifetime external spend through atomic
holds; it is not a guarantee about the supplier's actual invoice. Existing
project grants, platform-fee budgets and explicit Hybrid consent remain mandatory.

P2 `PROVIDER-010` records the approved [custom endpoint trust/auth/accounting profile](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md).
The user's Continue approves draft `ea54fcf`, not feature execution.
The bounded design is operator-approved, tenant-private public
HTTPS/Bearer text BYOK, separately qualified JSON/SSE, with no Managed/Hybrid
fallback or official-provider identity reuse. Current FR-050–056 implementations
remain fixed-provider integrations; this design does not add a URL field or
enable custom serving. External spend remains an estimate, not invoice proof.

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
FRONTEND-008 provides a request-only configuration/conditional sequence preview,
not a live endpoint decision or guaranteed price/health outcome. Project defaults
and explicit strategy overrides never bypass hard authorization/financial policy.

`ROUTING-007` records approved [bounded advanced routing and response-cache semantics](40_ADVANCED_ROUTING_CACHE_SPEC.md).
The user's Continue approves draft `dfd3b36`, not feature execution.
Existing price/performance/auto/fallback behavior is not missing
work to repeat. Project weights, metadata affinity and gateway response-cache
fees/retention remain approved design, not implemented or enabled by FR-060–068.

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
REFUND-002 discretionary adjustments are credit-only USD micro-unit credits.
Every amount requires a second, different recently MFA-verified operator;
request creation does not post money. Maker, checker, immutable terms and both
reasons are audited. No discretionary debit or cash payout is authorized.

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

FRONTEND-009 exposes the existing PRIVACY-001 project policy to current
organization Owners only: explicit 60–10080 minute opt-in, confirmed opt-out
deletion, audited request-specific read and visible expiry. Ordinary Activity
remains payload-free. This setting is not upstream ZDR or metadata/ledger deletion.

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

FRONTEND-009 exposes LowBalance, BudgetWarning and ErrorSpike rules, verified
Telegram/validated signed-webhook destinations, pause/disable controls and the
latest 100 customer delivery events. Reads require billing-read; mutations
require Owner/Admin and CSRF. Pending is not a delivery guarantee; Delivered
means transport acknowledgement, not human receipt. Provider-outage alerts
remain operator-only. Live bot/receiver qualification is separate from repository
acceptance. See [scope and validation](30_ALERTS_PRIVACY_UX.md).

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
Discretionary credits use an immutable request plus one immutable approval,
with organization-scoped request-key idempotency and single approval posting.
Active holds are preserved; credits follow existing available-credit debt
recovery rules. Historical usage/spend buckets and FX snapshots are not
rewritten. Settlement-linked refunds remain separate.

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

`ENTERPRISE-001` approves FR-140–147 as P2 design requirements for opt-in
SAML/SCIM enterprise identity in [the accepted specification](38_ENTERPRISE_IDENTITY_SPEC.md#3-approved-requirements-and-adr-025-extension).
The user confirmed SAML 2.0/SCIM Users with Entra first, optional SSO/no JIT or
email merge, and tenant-only deprovisioning including member-created tenant
Gateway keys. The user's 2026-10-18 Continue approves the complete specification
and task decomposition, not feature scheduling or implementation. No SAML/SCIM
endpoint or enterprise capability is implemented.
Existing FR-001–005 and ADR-025 remain authoritative for current behavior.

### P2

Guardrails/PII design is defined in the [approved SECURITY-007 specification](43_GUARDRAIL_PII_POLICY_SPEC.md):
Owner-consented, disabled-default local input syntax screening, bounded Observe/
Block and separately implemented input Redact. It is not universal PII/injection
detection, output filtering, ZDR or legal certification. Unsupported guarded
shapes fail closed; current runtime behavior is unchanged. Feature execution
requires separately approved/promoted tasks and privacy/financial safeguards.

- SSO/SAML
- SCIM
- Guardrails
- PII redaction
- ZDR-aware routing
- regional routing
- custom provider endpoints
- routing rules UI
- A/B routing
- headless management automation (not the already implemented browser Management API)
- webhooks for usage events
- invoicing
- contractual SLA

The [ENTERPRISE-002 draft](45_INVOICE_SLA_REPORTING_SPEC.md) bounds initial
reporting to non-fiscal prepaid account statements and operational SLIs.
FR-124–126 do not define tax, revenue recognition or a compensation tariff.
Fiscal evidence and contractual claims require separately approved legal/
commercial profiles; existing refund/independent-credit invariants remain.
Product review is pending; no invoice issuer or SLA remedy is implemented.

`PRIVACY-003` specifies the approved bounded ZDR/provider-processing-region
eligibility profile in [the specification](39_PRIVACY_ELIGIBILITY_SPEC.md).
The user's 2026-10-18 Continue approves the profile and eight-task decomposition
delivered in `c5a8d4e`, not feature scheduling, an implemented capability or a
whole-platform residency promise. Existing provider-policy disclosure and
project payload controls remain distinct; no strict privacy routing is enabled
by these P2 bullets.
