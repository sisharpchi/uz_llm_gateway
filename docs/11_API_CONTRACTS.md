# API Contracts

Planning only: the [approved SECURITY-007 §5/7/8](43_GUARDRAIL_PII_POLICY_SPEC.md)
defines future Owner guardrail policy/decision APIs, consent/version/replay and
input-screening errors. No such route or inference skip extension exists today;
generated artifacts remain unchanged. No per-request override, raw-content
preview, response-body replay or output-safety guarantee is proposed.

## 1. API groups

### Public inference API
Base:

```text
https://api.example.uz/v1
```

### Management API
Base:

```text
https://api.example.uz/management/v1
```

Public `POST /management/v1/auth/{register,login,verify-email,recover,reset-password}`
and team invitation create/accept routes accept at most 8 KiB. The edge may
return 429 by client IP; Management returns 429 with `Retry-After` when a
distributed per-IP or per-account attempt limit is reached, or 503 if its
Redis limiter is unavailable. `POST /auth/recover` and duplicate registration
return an indistinguishable 202 for known and unknown addresses. Auth-limit
responses use `Cache-Control: no-store`.
All `/management/v1` responses, including authentication cookies, show-once
secrets, recovery outcomes, checkout links, and denials, use
`Cache-Control: no-store`. A missing/invalid session returns `401`; an authenticated account
without the required tenant, project, or operator role returns `403`.

### Payment callbacks
Provider-specific:

```text
https://api.example.uz/payments/payme/callback
https://api.example.uz/payments/click/callback
```

### P0 top-up control plane

An authenticated member with billing-management permission uses `POST /management/v1/organizations/{organizationId}/billing/quotes`
with `{ "provider": "Payme", "amountTiyin": "100000" }`, then
`POST /management/v1/organizations/{organizationId}/billing/topups` with
`{ "quoteId": "..." }` and an `Idempotency-Key` header. Both writes require the
management session and CSRF header. The latter returns an intent and provider
checkout URL; the key may be replayed only for the same quote. `GET` endpoints
for `/billing/topups`, `/billing/topups/{intentId}`, `/billing/refunds`, and
`/billing/wallet` require
billing-read permission (including `BillingViewer`). Cross-tenant or
insufficient-role access returns `403`, not a framework authentication challenge.
Top-up amount input, quote/intent amounts, FX rate, and wallet
USD micro-units are JSON decimal strings; the server parses them as fixed-
precision values using the quote's immutable FX snapshot. This prevents
JavaScript number precision loss in the browser.
The customer form accepts whole UZS and converts exactly to tiyin before
requesting a quote (for example, `100000` UZS sends `"amountTiyin": "10000000"`).
It must display the returned tiyin amount, including fractional UZS where
present, and never expose checkout when the quote or intent amount differs.
`GET /billing/topups` returns the newest 50 tenant-scoped intents with
`hasCredit`, `hasReversal`, and `hasOpenReconciliationCase` flags. These are
wallet/ledger and open-case facts, not independent merchant confirmation;
the create-intent response has no such flags. `GET /billing/refunds` returns
the newest 50 wallet-credit refunds with settlement ID, amount and timestamp,
but no operator reason or identity. Wallet reads include decimal-string
`recoveryDebtMicroUsd` and boolean `spendingHeld` alongside posted, reserved,
available and version. Customer reads do not expose provider statement rows or
operator case-resolution details.

Payme calls `/payments/payme/callback` with its Merchant API JSON-RPC body and
`Authorization: Basic` credential (`Paycom:<merchant key>`). Implemented methods:
`CheckPerformTransaction`, `CreateTransaction`, `PerformTransaction`,
`CancelTransaction`, `CheckTransaction`, and `GetStatement`. CLICK Shop API
calls `/payments/click/callback` with `application/x-www-form-urlencoded`
Prepare (`action=0`) or Complete (`action=1`) fields; its documented MD5
`sign_string` is validated before processing. Callback routes do not use browser
sessions or CSRF. Both reject bodies over 32 KiB. Payme `GetStatement` accepts
at most a 30-day range and fails rather than truncating a response over 10,000
transactions; callers can split the range.

The deployment edge exposes only these exact HTTPS POST callback routes. CLICK
ingress defaults to 403 until verified merchant source ranges are installed;
its signature alone cannot authenticate the unsigned reversal `error` field.
Payment errors remain provider-native response bodies after proxying.

---

## 1a. API-key model/provider permissions

`PUT /management/v1/api-keys/{apiKeyId}/permissions` replaces all four lists:

```json
{
  "allowedModels": ["example-chat"],
  "deniedModels": null,
  "allowedProviders": ["openai", "anthropic"],
  "deniedProviders": ["anthropic"]
}
```

Requires a current session, `X-CSRF-Token`, and `ManageApiKeys` permission on
the key's project (Owner/Admin or explicitly project-granted Developer).
Returns `204`; missing session `401`, denied role/tenant/CSRF `403`, unknown
key `404`, invalid rules `400`. All responses are `no-store`. Key listing and
create/rotate metadata include `permissions`; secrets remain show-once.

Null/omitted allowlists mean unrestricted; `[]` denies everything in that
dimension. Null/empty denylists deny nothing. Deny wins over allow; model AND
provider must pass. `{}` resets to unrestricted. Each list accepts at most
128 catalog codes, trimmed/lowercased/deduplicated/sorted: 1–200 ASCII letters,
digits, `-`, `_`, `.`, `:`, starting with a letter/digit. Model codes are
canonical, never provider-prefixed; aliases/wildcards are unsupported. Unknown
JSON properties are rejected to prevent misspelled restrictions becoming open
policies. Syntactically valid unpublished codes can be preconfigured; they do
not create routes or authorize a disabled catalog entry.

Gateway rejects forbidden models, pinned providers, and explicit forbidden
fallbacks with `403 api_key_permission_denied` before reservation/dispatch.
Other provider candidates are filtered before ranking/hold estimation across
Managed, BYOK and Hybrid. `/v1/models` exposes only permitted models/mappings;
its price maxima cover only those mappings. Session-based catalog discovery
remains project-scoped, not scoped to an unrelated Gateway key.

PostgreSQL is read at authentication and again immediately before financial
admission. A committed tightening/revocation observed at the latter read
rejects the request, rather than rebuilding a route silently. That read is
the permission-snapshot boundary: later changes affect subsequent admission,
not an already admitted stream or its settlement. No Redis permission cache.
Concurrent full replacements are last-committed-write-wins, never fieldwise
merges; secret rotation never overwrites permissions. Mutation and audit commit
together. Price-class restrictions remain unspecified: no taxonomy/rank exists
in Catalog, and APIKEYS-004 does not invent one (see FR-027).

## 2. Chat completions [P0]

```http
POST /v1/chat/completions
Authorization: Bearer uzllm_xxx
Content-Type: application/json
```

Example:

```json
{
  "model": "openai/gpt-x",
  "messages": [
    {
      "role": "user",
      "content": "Hello"
    }
  ],
  "temperature": 0.7,
  "stream": true
}
```

Core compatibility first:
- model
- messages
- temperature
- top_p
- max_tokens / compatible output limit
- stream
- stop
- tools
- tool_choice
- response_format where provider supports it

Unsupported parameter behavior should be explicit.

The P0 Gateway accepts the listed chat fields plus `max_completion_tokens` and
`stream_options.include_usage`; it rejects unsupported top-level fields with
`400 unsupported_parameter`. `model` may be canonical, `openai/<canonical>`,
or `anthropic/<canonical>` when an active provider mapping exists. An explicit
provider route is never silently substituted. Canonical routing prefers OpenAI,
then Anthropic, and may fail over only to another mapping of that same canonical
model after a verified pre-execution transient rejection and before any output.
At most two provider attempts share one reservation and total provider deadline.
The `/v1/models` price fields are conservative per-token maxima across active
supported mappings; actual charges use the selected attempt's price version.
An omitted output limit uses the catalog model's maximum. For streaming,
`data: [DONE]` follows a complete upstream stream;
an interrupted stream emits a safe terminal error instead of replaying output.
Missing provider usage leaves financial evidence unknown and the hold pending
reconciliation rather than charging zero.

---

## 2a. Stateless Responses text subset [P1]

`POST /v1/responses` uses the Gateway Bearer key, not a Management session or
provider secret. It accepts required `model` and `input` (text or bounded text
message items), optional `instructions`, `max_output_tokens` (integer >=16),
`temperature`, `top_p`, optional boolean `stream`, false/omitted `store`, and the existing
`uzllm` routing/BYOK/Hybrid extension. [Exact bounds and capability matrix](32_RESPONSES_API.md).
Unknown fields, duplicate JSON properties, unsupported items/features and
invalid bounds return 400 before reservation/dispatch. `stream=true` selects the
[Responses SSE lifecycle](33_RESPONSES_SSE.md); no GET/DELETE/conversation operations exist.

Output is `object: response` with a `resp_` ID, `created_at`, selected `model`,
`status: completed|incomplete`, nullable `incomplete_details`, one assistant
message containing output_text/refusal parts, `store: false` and nullable usage.
Usage uses input/output/total token names plus cached/reasoning detail subsets.
Length/content-filter endings are incomplete, not fabricated completed output.
No top-level output_text SDK convenience is added. Sampling/output request fields
are echoed as supplied (nullable when omitted), not invented provider defaults.

All results/errors are `no-store`; existing error/status and correlation/routing
headers apply, including 404 for unavailable models. `Idempotency-Key` UUID claims
are scoped to organization/key/**responses** for 24 hours: repeated or changed
bodies return 409 with the original request ID, not response replay. Chat uses a
different operation identity. Managed/BYOK/Hybrid routing, permissions, budgets,
wallet and operator gates are unchanged. Unknown usage returns null and retains
the hold for reconciliation; failed finalization returns 503 finalization_pending
before headers or `response.failed` with that code after SSE starts. Named SSE
events carry zero-based increasing sequence_number, stable response/item IDs,
incremental text/refusal deltas and a terminal completed/incomplete/failed response
snapshot, never chat chunks or `[DONE]`. Completion requires a supported upstream
finish and successful bounded financial cleanup. Missing finish, transport error,
unsupported output or snapshot overflow cannot fabricate completion. The total
stream text/refusal snapshot bound is 1,048,576 UTF-16 characters. Disconnect
cancels provider I/O, not durable evidence/settlement cleanup; no fallback follows
any emitted event. No resume/replay or conversation store is implied.
Stateless Responses storage does not disable separately consented project payload
retention or promise upstream ZDR. Provider calls use existing chat adapters,
not a transparent proxy to the native OpenAI Responses endpoint.

---

## 2b. Bounded text embeddings [P1]

`POST /v1/embeddings` accepts Gateway Bearer authentication, required `model`,
`input` as a nonblank string or 1–32 such strings, and optional
`encoding_format: "float"`. Each text is bounded by 8192 UTF-8 bytes and the
selected model's context. JSON properties must be unique; unknown properties,
token-ID input, base64, `dimensions`, `stream`, `user` and cross-model fallback
return 400 before reservation/dispatch. No input truncation or model substitution.

The optional `uzllm` object accepts `provider_key_id`, `allow_managed_fallback`
and `routing: "priority"|"price"`. Existing grant, permission, wallet, budget,
operator and explicit Hybrid-consent rules apply. An inherited incompatible
chat performance strategy requires an explicit supported override; vectors from
different canonical models are not interchangeable. Only the OpenAI native
adapter currently supplies embeddings.

Response: `{ "object": "list", "model": "<canonical-code>", "data": [
{ "object": "embedding", "index": 0, "embedding": [0.25, -0.5] } ],
"usage": { "prompt_tokens": 2, "total_tokens": 2 } }`.
Vectors are float arrays with 1–4096 finite values, consistent dimensions and
one contiguous zero-based index per input, sorted into input order. Usage is
nullable when unavailable/invalid, never an inferred zero charge. No SSE.

All results/errors are `no-store`; existing error envelopes/statuses and
correlation/provider/model/billing-mode headers apply. UUID `Idempotency-Key`
claims use organization/key/**embeddings** for 24 hours, independently of chat
and Responses; repeats/conflicting bodies return 409 and the original request
ID, not a replayed vector body. A batch has one logical request/reservation,
one final charge and one fixed request fee. Persist evidence before settlement;
ambiguous execution retains the hold for recovery. Financial cleanup failure
returns 503 `finalization_pending`. [Capability, pricing and recovery details](34_EMBEDDINGS_API.md).

## 3. Gateway extensions

Planning note: the [approved GATEWAY-008 specification](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md)
specifies future operation discovery, Responses client-function JSON/SSE,
non-streaming schema output and qualified embedding controls. These are **not
current wire contracts**: the rejection/shape rules in §2a/2b and generated
`docs/api/` artifacts remain unchanged. No hosted tool, stateful response or
image/audio/video route is introduced. Regeneration belongs to each separately
approved implementation task, not this specification.

Prefer namespace-like extension to reduce collision risk:

```json
{
  "model": "model-x",
  "messages": [],
  "uzllm": {
    "routing": "price",
    "fallback_models": ["model-y"]
  }
}
```

P0 can omit most extensions and use project defaults.
`PROJECTS-002` applies a project strategy when `uzllm.routing` is absent.
`priority` explicitly restores deterministic routing; other accepted values
are `price`, `latency`, `throughput`, `auto`. This overrides only strategy,
not hard project allowlists, API-key permissions, financial or operator gates.
`max_cost`, `max_cost_usd` and request `allowed_providers` remain unsupported
and return 400; the old illustrative cost field was not an implemented feature.
`ROUTING-003` accepts `{"uzllm":{"routing":"price"}}` without a BYOK key.
`ROUTING-004` additionally accepts `latency`, `throughput`, and `auto`; unknown routing values return
`400 unsupported_parameter`. The strategy ranks eligible same-model Managed
endpoints. `price` uses a frozen catalog/customer-fee estimate; performance
strategies use a bounded recent window and revert to deterministic priority
when samples are insufficient or stale. Actual charges still use verified
usage and the selected price version. An explicit provider prefix continues
to constrain the eligible set. BYOK/Hybrid preserves the explicitly requested
BYOK-first order under every strategy.
`ROUTING-005` also accepts `uzllm.fallback_models` with one or two distinct
canonical codes, for example `{"fallback_models":["model-y","model-z"]}`.
It is opt-in, ordered, and limited to at most four total attempts: two
same-model candidates followed by one per listed fallback model. BYOK-only
requests cannot use it; Hybrid requires `allow_managed_fallback=true`. The
worst-case reservation includes every selected candidate. `X-Uzllm-Model`
and the completion/SSE model identify the model actually executed;
`X-Uzllm-Requested-Model` retains the requested canonical model. Usage
activity/detail retain requested `modelId`/`modelCode` and add
`selectedModelId`/`selectedModelCode`; attempt detail adds `modelCode`.
An inactive or unknown fallback model returns `400 fallback_model_unavailable`.

---

## 4. Models

```http
GET /v1/models
```

Normalized response includes:
- id;
- display name;
- pricing summary;
- context length;
- capabilities.

Detailed management model catalog may be richer than public OpenAI-compatible response.
The public `/v1/models` pricing summary is the conservative provider-base
maximum, **not** the customer's final managed price; use the scoped management
catalog below for markup and fixed-fee disclosure.

Customer dashboard model discovery uses a session-authenticated project read:

```http
GET /management/v1/organizations/{organizationId}/projects/{projectId}/catalog/models
```

The project must be active and readable by the signed-in organization member.
`401` means no session, `403` means no tenant/project grant, `404` means the
project is absent/archived, and `503` means the effective managed `default` fee
policy is unavailable. The response is `no-store` and never contains provider
credentials, upstream endpoint references or another tenant's settings:

```json
{
  "dataAsOf": "2026-09-30T00:00:00Z",
  "feePolicyVersionId": "<uuid>",
  "markupBasisPoints": 2500,
  "fixedFeeMicroUsdPerRequest": "100",
  "models": [{
    "id": "<published-canonical-model-code>",
    "displayName": "Example Chat",
    "contextLength": 8192,
    "maxOutputTokens": 1024,
    "capabilities": ["Text", "Tools"],
    "status": "Published",
    "providers": [{
      "mappingId": "<uuid>",
      "provider": "openai",
      "providerName": "OpenAI",
      "capabilities": ["Text", "Tools"],
      "priceVersionId": "<uuid>",
      "priceEffectiveFrom": "2026-09-29T00:00:00Z",
      "priceEffectiveTo": null,
      "inputPriceMicroUsdPerMillion": "1250000",
      "outputPriceMicroUsdPerMillion": "2500000",
      "cachedInputPriceMicroUsdPerMillion": null
    }]
  }]
}
```

Rates are decimal strings of **customer** USD micro-units per million tokens:
effective provider rate × `(10000 + markupBasisPoints) / 10000`. The separate
fixed fee is in integer USD micro-units per request. Final settlement rounds the
aggregate charge using measured usage and the selected attempt's immutable
price/fee versions; displayed rates are not a quote or a live health/quota check.
Only active, effectively priced chat mappings supported by the Gateway appear.

---

### Provider policy provenance (`CATALOG-003`)

```http
GET /management/v1/organizations/{organizationId}/projects/{projectId}/catalog/providers/{providerCode}/data-policy
GET /management/v1/admin/providers/{providerId}/data-policies?limit=50
GET /management/v1/admin/provider-data-policies/{id}
POST /management/v1/admin/providers/{providerId}/data-policies
```

The customer read uses the same active tenant/project authorization as the
catalog, but does not require fee publication. An absent publication for an
active provider returns `200` with `retention`, `training` and `residency` all
`Unknown`, empty `regions`, and null version/provenance/scope/conditions.
Anonymous/unauthorized reads return `401`/`403`; absent or inactive
project/organization/provider returns `404`. Responses are `no-store`.

Publication requires recent-MFA operator access, CSRF and an 8–500 character
`reason`. Body fields: `retention` (`Unknown`, `MayRetain`, `NoRetentionReported`),
`training` (`Unknown`, `MayTrain`, `NoTrainingReported`), `residency` (`Unknown`,
`ProviderReported`), `regions` (0–16 labels, 1–80 characters), `scope` (8–500),
`conditions` (8–2000), HTTPS `sourceUrl` (up to 2048, no credentials/query),
and past/current UTC `sourceObservedAt` after the Unix epoch. Unknown residency requires no regions;
reported residency requires at least one. Control characters are rejected.
Observation instants are normalized to PostgreSQL microsecond precision.

`201` returns `{policy, publishedByAccountId}` and an immutable-version Location;
the customer receives only `policy`, never the operator identity/reason.
Invalid inputs return `400`, unknown provider/version `404`; non-advancing
observations, including identical replay, return `409` without another audit.
History is newest-observation-first, limit 1–100. Corrections append a later
observation, including explicit `Unknown`, rather than editing old versions.
Sources are operator-supplied and never fetched/independently authenticated by
the API. Claims remain conditional observations, not ZDR certification or
residency enforcement. [Full scope and validation](35_PROVIDER_DATA_POLICIES.md).

`PRIVACY-003` separately specifies approved-design [strict privacy/processing-region fields and policy/eligibility routes](39_PRIVACY_ELIGIBILITY_SPEC.md).
They remain **not implemented wire contracts**; feature execution is not
scheduled by specification approval. Do not send
`uzllm.privacy` to the current Gateway or infer strict routing from the existing
data-policy disclosure, payload opt-out or Responses `store=false` behavior.

---

## 5. Error shape

Example:

```json
{
  "error": {
    "message": "Insufficient balance",
    "type": "billing_error",
    "code": "insufficient_balance",
    "param": null
  }
}
```

Useful status mapping:

| HTTP | Meaning |
|---|---|
| 400 | invalid request |
| 401 | missing/invalid API key |
| 402 | insufficient balance / budget policy if chosen |
| 403 | key/project/model/provider forbidden |
| 404 | unknown model/resource |
| 409 | idempotency/state conflict |
| 429 | rate/concurrency limit |
| 500 | unexpected gateway failure |
| 502 | upstream/provider failure |
| 503 | gateway dependency unavailable |
| 529 | optional overload semantics |

Choose and document a stable policy.

P0 policy: malformed or unsupported input is `400` (`413` for oversized JSON,
`415` for non-JSON media type); balance/budget denial is `402`; idempotency
replay is `409` with `X-Original-Request-Id`; rate/concurrency denial is
`429`; unavailable Redis, PostgreSQL, pricing, or provider capacity is `503`;
provider execution failure is `502` unless the provider verifies a context
limit (`400`). Errors use the envelope above, not RFC 7807 Problem Details,
to preserve OpenAI client compatibility.

---

## 6. Response metadata

Do not break OpenAI clients unnecessarily.

Useful headers:

```text
x-request-id
x-uzllm-provider
x-uzllm-model
x-uzllm-cache
```

Detailed routing/cost metadata may be opt-in.

`x-uzllm-cache` above is illustrative, not currently emitted response-cache
support. The [approved ROUTING-007 specification](40_ADVANCED_ROUTING_CACHE_SPEC.md) designs
affinity/cache hints, versioned project policy and decision metadata. They are
**not implemented wire contracts**; current unknown extension fields remain
rejected. No generated API/client artifact changes follow from this specification.

---

## 7. Management APIs

### Projects

```text
POST   /management/v1/organizations
GET    /management/v1/organizations
GET    /management/v1/organizations/{organizationId}/projects
POST   /management/v1/organizations/{organizationId}/projects
GET    /management/v1/organizations/{organizationId}/projects/{projectId}
POST   /management/v1/organizations/{organizationId}/projects/{projectId}/archive
```

Every project operation is authorized against its path organization. `POST`
archive is a terminal state transition; unarchive remains unsupported.

`GET/PUT /management/v1/organizations/{organizationId}/projects/{projectId}/settings`
reads/replaces `{routing, environment, allowedModels, allowedProviders}`.
Fields are optional/nullable; `{}` resets this settings group. `routing` is
`priority|price|latency|throughput|auto` or null. `environment` is 1–32 ASCII
letters/digits/`_`/`-`, or null, and is metadata only. Allowlists accept at most
128 canonical codes per list; null is unrestricted, empty denies all.
Codes trim/lowercase/deduplicate like key permissions. Unknown fields are 400.
No budget, cost, provider credential, managed-fallback or retention controls
are accepted. Existing project budget endpoints remain the authoritative
Lifetime/Daily/Weekly/Monthly financial controls.

Session and current ReadProjects permission are required for reads; writes
require ManageProjects (Owner/Admin), CSRF and atomic audit. Foreign tenant
returns 403, wrong path organization/missing/archived project 404, invalid input
400; responses are no-store. Whole-group updates are last-commit-wins and
preserve other JSON groups and budget/limit state. Gateway intersects project
and key restrictions before routing and rechecks hard restrictions before
financial admission. Defaults already snapshotted by an admitted request need
not be reranked; updates do not revoke active streams. See [complete semantics
and evidence](25_PROJECT_DEFAULTS.md).

### Team (P1)

```text
GET    /management/v1/organizations/{organizationId}/team/members
GET    /management/v1/organizations/{organizationId}/team/invitations
POST   /management/v1/organizations/{organizationId}/team/invitations
POST   /management/v1/team/invitations/accept
PATCH  /management/v1/organizations/{organizationId}/team/members/{accountId}/role
DELETE /management/v1/organizations/{organizationId}/team/members/{accountId}
PUT    /management/v1/organizations/{organizationId}/team/members/{accountId}/projects/{projectId}
DELETE /management/v1/organizations/{organizationId}/team/members/{accountId}/projects/{projectId}
```

Invite takes `{ "email": "member@example.uz", "role": "Developer" }` and
returns `201` with safe invitation metadata and a Location header; the token
is delivered only by protected email outbox. Acceptance takes `{ "token": "..." }`
on an authenticated, verified session for the exact invited email. A used,
expired, revoked, or mismatched token returns `404`. All mutations require the
session CSRF proof. Role names are `Owner`, `Admin`, `Developer`,
`BillingViewer`, and `ReadOnly`; invitation cannot directly grant `Owner`.
Members/invitations are visible only to Owner/Admin. Team mutations return
`403` for insufficient privilege, `404` for missing targets, and `409` for
duplicate invitation, last-owner or invalid role-transition conflicts.

FRONTEND-007 consumes these existing routes at `/organizations/:orgId/team`
and `/accept-invitation`; no wire changes. Invitation metadata never includes
the token. The current Worker email supplies a token to paste, not an acceptance
link. The dashboard does not forward tokens through sign-in URLs or persist
them. There is no invitation cancel/resend endpoint. A new invitation may
replace an expired one; a pending duplicate conflicts. Admins cannot invite,
change or revoke Owner/Admin members. Developer/ReadOnly project grants are
individual audited PUT/DELETE operations, not a bulk replacement. See
[team behavior and real-stack evidence](27_TEAM_MEMBERSHIP.md).

### API Keys

```text
GET    /projects/{projectId}/api-keys
POST   /projects/{projectId}/api-keys
PATCH  /api-keys/{id}
POST   /api-keys/{id}/rotate
POST   /api-keys/{id}/disable
DELETE /api-keys/{id}
```

`POST /management/v1/api-keys/{id}/rotate` requires the browser session and
CSRF token. A successful `200` returns the unchanged key ID, incremented
`generation`, new public prefix and replacement `secret` exactly once. No
request body is required. `403` denies another tenant, `404` means the key
does not exist, and `409` means it is inactive/expired/archived or changed
concurrently. The old secret stops authenticating at transaction commit;
already-admitted requests may finish. Rotate consumers should never persist
the full response in logs or browser storage.

### Usage

```text
GET /usage/summary
GET /usage/activity
GET /usage/requests/{requestId}
GET /usage/by-model
GET /usage/by-provider
GET /usage/by-api-key
```

### Wallet

```text
GET  /billing/wallet
GET  /billing/ledger
POST /billing/topups
GET  /billing/payments
```

### Admission limits

`GET/PUT /management/v1/projects/{projectId}/limits` and
`GET/PUT /management/v1/api-keys/{apiKeyId}/limits` expose nullable positive integer
`requestsPerMinute`/`concurrency` overrides. PUT replaces both fields; null or
omission inherits a bounded operator default, never unlimited. Results contain
`configured`, `effective`, and `ceiling`. Key and project limits both gate new
admission. Writes require current permission and CSRF and are audited atomically.
See [LIMITS-002 contract and failure semantics](24_TENANT_LIMITS.md).

### Financial budgets

```text
GET /management/v1/projects/{projectId}/budgets
PUT /management/v1/projects/{projectId}/budgets/{period}
```

`period` is `Lifetime`, `Daily`, `Weekly`, or `Monthly` (UTC). PUT takes
`limitMicroUsd` (non-negative integer) and optional `apiKeyId`; omitting the
key sets the project cap. The response includes the current half-open
`windowStart`/`windowEnd`, captured spend and active holds. All applicable
project/key policies gate admission atomically. A newly installed policy is
rejected with `409` while matching reservations remain active; invalid scope
is `404`, and cross-tenant access is `403`. Writes require the management
session and CSRF token.

### BYOK

```text
GET    /management/v1/organizations/{organizationId}/provider-keys
GET    /management/v1/organizations/{organizationId}/provider-keys/providers
GET    /management/v1/organizations/{organizationId}/provider-keys/{id}
POST   /management/v1/organizations/{organizationId}/provider-keys
PATCH  /management/v1/organizations/{organizationId}/provider-keys/{id}
POST   /management/v1/organizations/{organizationId}/provider-keys/{id}/test
PUT    /management/v1/organizations/{organizationId}/provider-keys/{id}/restrictions
POST   /management/v1/organizations/{organizationId}/provider-keys/{id}/disable
DELETE /management/v1/organizations/{organizationId}/provider-keys/{id}
PUT    /management/v1/organizations/{organizationId}/provider-keys/{id}/projects/{projectId}
DELETE /management/v1/organizations/{organizationId}/provider-keys/{id}/projects/{projectId}
```

Owner/Admin only. `GET /provider-keys/providers`
returns active supported catalog identities `[{id,code,name}]` for registration,
including providers without a priced model mapping. It requires the same current
Owner/Admin tenant permission (401 without session, 403 denied), uses no-store,
and exposes no credential, endpoint, price, quota or health state. Creation still
rechecks provider status; a listed option is not an execution eligibility promise.
Create takes `providerId`, `name`, and `secret`; PATCH takes optional `name`
and/or `secret` to rotate it. Read/write responses contain
`maskedKey`, status, last test result, and explicit `projectIds`, never secret or
ciphertext. All mutations require the management session and CSRF token.
Test returns `Valid`, `Invalid`, or `Unavailable` without upstream response
body; it makes a bounded GET to the provider's fixed model-list endpoint.
Active OpenAI/Anthropic/Google (`google`, Gemini API-key)/DeepSeek catalog
providers are supported after `BYOK-003`. Test is read-only upstream; Valid
means the model-list endpoint accepted the key, not guaranteed model access,
available inference quota or invoice accuracy. Google structured 400
`API_KEY_INVALID` returns Invalid; generic/malformed/oversized errors, 402/429,
redirects, network errors and timeouts return Unavailable. No automatic retry
or inference health reset occurs. [Fixed endpoints, bounds and evidence](28_BYOK_PROVIDER_PARITY.md).
Custom endpoint management/probe/inference shapes in the
[approved PROVIDER-010 specification](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md#5-proposed-management-and-inference-contracts)
are future wire designs, not current API/client artifacts. There is no tenant
baseUrl/auth-header field; do not send the illustrative new contracts today.
Deleting soft-tombstones and crypto-shreds the stored ciphertext; disabled/deleted credentials and
ungranted projects cannot resolve its secret. `PUT /restrictions` replaces
`allowedModels` (canonical codes; `null` means unrestricted) and optional
`spendLimitMicroUsd` (non-negative decimal string; lifetime estimated external
provider spend). The masked read response includes these fields plus
`externalSpentMicroUsd` and `externalReservedMicroUsd` as decimal strings.
Only active mappings for that credential's provider may enter the allowlist.
The cap is enforced atomically with admission and can be lowered below
spent-plus-held to block future requests, not invalidate existing holds.

For BYOK chat, the P1 `uzllm` request extension is:

```json
{"uzllm":{"provider_key_id":"<credential UUID>","allow_managed_fallback":false}}
```

The key must be active, organization-owned, explicitly granted to the API
key's project, and allowed for the canonical model. Omitting `uzllm` stays
Managed. `allow_managed_fallback: true` is explicit Hybrid opt-in; it reserves
the managed wallet ceiling up front and may use managed capacity only after a
verified pre-execution transient rejection. Authentication errors, unknown
outcomes, and partial streams do not fallback. A paused managed-traffic switch
removes that fallback but does not pause pure BYOK traffic. At most one BYOK
candidate and one managed candidate are considered for P1 Hybrid; multi-key
ordering remains advanced routing work. Unknown extension fields are rejected.
BYOK requires an active operator-published `byok` fee-policy version
(`Gateway:ByokFeePolicyCode` overrides the code); publish zero markup/fixed
fee for fee-free BYOK. Request detail exposes `externalProviderSpendMicroUsd`
and `lateExternalSpendMicroUsd` separately from `chargedMicroUsd`.

---

## 8. Pagination

Prefer cursor pagination for high-volume activity logs.

Example:

```text
GET /usage/activity?limit=50&cursor=...
```

Simple offset pagination is acceptable for low-volume management tables.

---

## 9. Idempotency header

For supported operations:

```text
Idempotency-Key: <uuid>
```

Especially useful for:
- top-up intent;
- manual financial operations;
- future management automation.

---

## 10. Versioning

### Inference
Preserve `/v1` OpenAI-compatible namespace.

### Management
Use the existing `/management/v1` path. The customer catalog read added by
`DX-001` follows the same version as other session-backed management reads.

Breaking inference changes are especially costly because customer applications depend on compatibility.
Within either `v1` namespace, existing field names, units, authentication and
error meanings remain stable; new response fields may be additive and optional.
A breaking change requires a new versioned path, a published migration guide
and at least 180 days of overlap/deprecation notice before the old path is
sunset. Emergency security retirement requires an explicit incident decision
and customer notice. `DX-002` enforces generated document/example/client drift in
CI; see [versioned artifacts and regeneration](api/README.md).
The [DX-004 draft](44_MANAGEMENT_AUTOMATION_API_SPEC.md#5-proposed-routes-dtos-errors-and-idempotency)
specifies a separate read-only machine route family and Owner browser credential
lifecycle, safe receipt identity and show-once secret recovery. The proposed
Bearer scheme must not be added to existing session routes. Scope/replay,
epoch/restore and deprecation design needs product review; it is not today's
wire contract. Existing generated artifacts stay authoritative and unchanged.

---

## 11. Scoped management contracts

Customer management resources use:

```text
/management/v1/organizations/{organizationId}/projects
/management/v1/organizations/{organizationId}/billing/wallet
/management/v1/organizations/{organizationId}/usage/activity
```

Authentication uses `/management/v1/auth/...`; operator resources use
`/management/v1/admin/...`. Every resource is checked against the path’s
organization scope. Required P0 additions are organization/profile/settings,
catalog detail, limits, payment status, usage time-series/breakdowns, request
attempt detail, and operator provider/payment/ledger/incident endpoints.

### Management identity endpoints [P0]

Future SAML/SCIM routes are **approved design, not implemented wire contracts** in
[ENTERPRISE-001's specification](38_ENTERPRISE_IDENTITY_SPEC.md#7-approved-http-design--not-implemented-openapi).
They are not part of current OpenAPI/client artifacts or the implemented routes
below. The specification separates tenant-scoped federation, Owner configuration and
dedicated SCIM Bearer credentials from existing local browser/operator auth.

```text
POST /management/v1/auth/register
POST /management/v1/auth/verify-email
POST /management/v1/auth/login
POST /management/v1/auth/logout
GET  /management/v1/auth/session
POST /management/v1/auth/recover
POST /management/v1/auth/reset-password
POST /management/v1/auth/operator/mfa/verify
POST /management/v1/auth/operator/mfa/enroll
POST /management/v1/auth/operator/mfa/confirm
```

Successful login issues an opaque server-backed `__Host-uzllm-session` cookie
(`HttpOnly`, `Secure`, `SameSite=Strict`) and an equally strict readable CSRF
cookie. State-changing authenticated browser endpoints require the matching
`X-CSRF-Token` header. Verification and recovery tokens are one-time opaque
secrets and must never be returned in HTTP responses or logs.
`register` and `recover` return the same empty `202` for eligible and unknown/
duplicate addresses. `verify-email` accepts `{ "token": "..." }`; `reset-password`
accepts `{ "token": "...", "newPassword": "..." }`. Both return `204` on
success and `400` for an invalid, expired or consumed token. Attempt limits
return `429` with `Retry-After`; unavailable limiting returns `503`. All these
responses are `no-store`. The Worker may email a dashboard link with the token
in a URL fragment; the dashboard strips it from browser history before use and
never submits it automatically on link open.

Operator MFA enrollment takes `{ "password": "..." }` and returns a show-once
`sharedSecret` and `expiresAt`. It remains pending for at most 10 minutes and
does not grant admin access. `confirm` takes `{ "code": "123456" }` from the
pending secret and returns `204` only when it atomically enables MFA and marks
the current session recently verified. `verify` uses the same body for later
reauthentication. An accepted 30-second TOTP step cannot be reused, including
from another session or node; invalid/expired/replayed codes return `401`.
Enrollment conflicts return `409`. These authenticated mutations require CSRF
proof and all responses are `no-store`.

### Operator endpoints implemented by ADMIN-001

All routes use `/management/v1/admin` and require an active operator grant.
Except `/access`, they also require recent (15-minute) MFA verification.
Mutations additionally require `X-CSRF-Token` and a mandatory `reason`
(8–500 characters).

```text
GET   /access
GET   /accounts?query=<email-fragment>
GET   /organizations?query=<name-fragment>
GET   /providers
GET   /mappings/{id}/prices
GET   /organizations/{id}/ledger?limit=50
GET   /payments?organizationId=<optional>&limit=50
GET   /audit?limit=50
GET   /controls
GET   /work/dead-letters?limit=50
GET   /work/alerts?limit=50
GET   /financial/risk?limit=50
GET   /payment-reconciliation/cases?limit=50
GET   /payment-reconciliation/observations/{id}
POST  /payment-reconciliation/observations
PATCH /payment-reconciliation/cases/{id}/resolve
GET   /fee-policies/{code}
POST  /fee-policies
GET   /fx-rates?limit=50
POST  /fx-rates
GET   /refunds?organizationId=<optional>&limit=50
GET   /refunds/{id}
POST  /refunds
POST  /providers | /models | /mappings | /credentials | /prices
PATCH /providers/{id} | /models/{id} | /mappings/{id}
PATCH /credentials/{id} | /controls/{ManagedTraffic|TopUps}
POST  /operators/{accountId}/mfa/reset
```

`mfa/reset` requires a *different* recently MFA-verified operator, CSRF proof,
and `{ "reason": "8–500 characters" }`. It clears the target's TOTP state and
revokes all target sessions in the same transaction as an audit entry. Success
returns `204`; inactive/unconfigured targets return `404`; self-reset or stale
operator proof returns `403`. Re-enrollment requires the target's password.

`work/dead-letters` is operator-only and returns at most 100 newest terminal
outbox/job records with `id`, `kind`, `workType`, `attemptCount`, `maxAttempts`,
`deadLetteredAt`, and sanitized `lastError`. It never returns work payloads or
secrets. A limit outside 1–100 returns `400`.
`work/alerts` has the same operator/recent-MFA gate and limit. It returns at
most 100 newest alert metadata records (`id`, `kind`, `severity`, `occurredAt`,
`notificationEventId`, `notifiedAt`, `resolvedAt`, `deliveryStatus`), without
the JSON details, deduplication key or message body. Delivery status is
`Pending`, `Delivered`, `DeadLettered`, or `Unlinked` for legacy records.

`financial/risk` is read-only, requires the same recent-MFA operator gate, and
returns `Cache-Control: no-store`. Its `dataAsOf` is the read time. Bounded
`pending` entries include reservation/request/organization IDs, held micro-USD
as a decimal string, expiry, state (`MissingEvidence`, `UnknownEvidence`,
`PendingSettlement`, or `Undispatched`) and optional `nextReviewAt`. `exposure`
includes original plus late platform exposure, uncollected charge and the
unresolved-usage flag. `debt` includes outstanding micro-USD and spending-hold
state. No prompt, provider secret, upstream request ID or payment credential
is returned. These are investigation lists, not a consistent accounting
snapshot across the three queries; use ledger/reconciliation evidence for
financial closure.

The payment-reconciliation routes are operator/recent-MFA only, `no-store`;
mutations require session CSRF. `POST /observations` accepts `provider`
(`Payme`/`Click`), `sourceReference`, a lowercase/uppercase 64-hex
`sourceSha256` of the external statement, `rowReference`,
`externalTransactionId`, normalized `status` (`Created`, `Paid`, `Canceled`,
`Reversed`), positive decimal-string `amountTiyin`, UTC
`providerObservedAt`, and an 8–500 character `reason`. It returns `201` and
a Location for a new immutable row or `200` for an identical replay; a
conflicting source-row replay returns `409`. This endpoint transcribes
operator-obtained evidence and cannot independently authenticate a merchant
statement. `GET /cases` returns at most 100 newest open-first local and
external mismatch cases. `PATCH /cases/{id}/resolve` takes an 8–500 character
`reason` and 8–200 character `resolutionReference`, returns `204` on first
closure or `409` if not open, and never posts money. The closure audit entry
contains actor, reason and evidence reference.

Fee and FX publication uses the same operator/recent-MFA, CSRF and `no-store`
guard. `POST /fee-policies` accepts `policyCode`, `markupBasisPoints`,
decimal-string `fixedFeeMicroUsd`, future UTC `effectiveFrom`, optional UTC
`effectiveTo`, and an 8–500 character `reason`. A new window may close only
the future portion of its predecessor; overlapping or out-of-order windows
return `409`. `GET /fee-policies/{code}` returns newest-first versions.
`POST /fx-rates` accepts `source`, decimal-string `uzsTiyinPerUsd` (up to eight
fractional digits), UTC `observedAt` within 24 hours of publication, and
`reason`; global effective time must advance. `GET /fx-rates` returns at most
100 newest-first snapshots. Publications return `201` with the new ID; no
payment or prior quote is revalued.

`POST /refunds` is operator/recent-MFA and CSRF protected. Body:
`{ "settlementId": "uuid", "refundKey": "8–120 characters",
"amountMicroUsd": "positive integer", "reason": "8–500 characters" }`.
It returns `201` with a refund resource/Location, `200` with the original
resource on an identical replay, `404` for an unknown settlement, and `409`
for a released/zero-charge settlement, changed key terms, or cumulative amount
above the original net customer charge. The immutable resource includes
actor, settlement, organization, reason, amount and `duplicate` flag. `GET`
routes are operator-only, newest-first, with a 1–100 limit. This is wallet
credit, not a cash refund or provider payment reversal.

### Discretionary wallet credits (`REFUND-002`)

Under `/management/v1/admin`:

```text
GET  /wallet-adjustments?organizationId=<optional>&limit=50
GET  /wallet-adjustments/{id}
POST /wallet-adjustments
POST /wallet-adjustments/{id}/approve
```

All four require a current operator session and recent MFA (15 minutes);
mutations additionally require session-bound CSRF. Create body:
`{ "organizationId": "uuid", "adjustmentKey": "support-ticket-12345",
"amountMicroUsd": "1000000", "reason": "Documented service credit" }`.
Amount is a positive Int64 decimal **string**, key 8–120 and reason 8–500
printable trimmed Unicode scalar characters (matching PostgreSQL length).
Unknown command fields are rejected. Create
returns `201`/Location or `200` for identical key/maker/amount/reason replay;
no wallet credit occurs yet. Changed terms/maker return `409`.

Approve body is only `{ "reason": "Independent credit approval" }`. A
different operator is required for every amount (`403` on self-approval).
Returns `200` after atomic approval/credit/debt recovery/audit, or identical
checker/reason replay with `duplicate: true`; conflicting checker/reason or
unsupported balance capacity returns `409`. Missing wallet/request returns
`404`, malformed input `400`, missing session `401`, missing permission/MFA/CSRF
`403`. Reads are newest-first, limit 1–100, and missing detail returns `404`.

Resources expose `id`, `organizationId`, `requestedByAccountId`, immutable
`adjustmentKey`, `amountMicroUsd`, `reason`, `createdAt`, `status` (`Pending` or
`Approved`), nullable `approval` (`id`, `approvedByAccountId`, `reason`,
`createdAt`), and `duplicate`. All responses are `no-store`. Pending requests
have no automatic expiry or financial effect. No edit/delete, manual debit,
cash payout, customer adjustment-history UI or new budget state is introduced.

### Other operator controls and financial reads

`PATCH` bodies are `{ "enabled": false, "reason": "incident INC-42" }`.
Prices must be future-effective; scheduling closes the current interval in the
same transaction and never rewrites its rate values. Ledger is read-only.
Payment `providerObservation` is `VerifiedCallbackSeen` or `Unverified`: it
reports local verified callback evidence, not independent merchant status.
Reconciliation cases and credit/reversal flags are shown separately. A
paused `TopUps` control rejects new quotes/intents but verified callbacks and
settlement continue; `ManagedTraffic` rejects new managed inference before
reservation/dispatch. Initial operator grant is out-of-band.

Wallet responses distinguish `posted`, `reserved`, `available`, `recoveryDebt`,
and `spendingHeld`. Request detail distinguishes execution, delivery, and
financial outcomes plus attempts. Micro-unit amounts are decimal strings when
they may exceed JavaScript safe integers. Aggregate responses include `dataAsOf`.

### Usage reads implemented by USAGE-002

All routes below are under `/management/v1/organizations/{organizationId}/usage`
and require an authenticated account with current `ReadUsage` permission.
Owner/Admin/BillingViewer may read organization-wide; Developer/ReadOnly must
specify a currently granted project for list/aggregate/export reads. Request
detail derives the project from the stored request and checks current grants.
Revoked grants are checked on each request:

```text
GET /activity?from=<UTC>&to=<UTC>&limit=50&cursor=<opaque>
GET /requests/{requestId}
GET /summary?from=<UTC>&to=<UTC>
GET /timeseries?from=<UTC>&to=<UTC>
GET /by-model?from=<UTC>&to=<UTC>
GET /by-provider?from=<UTC>&to=<UTC>
GET /by-api-key?from=<UTC>&to=<UTC>
GET /by-project?from=<UTC>&to=<UTC>
```

List and aggregate routes accept `projectId`, `apiKeyId`, `modelId`,
`providerId`, `status` (execution state), `isStream`, and `requestId` filters.
The UTC window is `[from, to)`, defaults to the last 30 days, and is capped at
90 days. Activity uses newest-first `(startedAt, requestId)` keyset pagination;
`limit` is 1–100 and `nextCursor` is opaque. A cross-tenant request detail is
`404`; unauthorized organization access is `403`. Invalid filters return `400`.

Responses contain `dataAsOf`. `summary` counts logical requests once and
includes `requestCount`, `completedCount`, `errorCount`, `pendingCount`,
`inputTokens`, `outputTokens`, `chargedMicroUsd`, `errorRatePercent`,
`topModels`, and `topProviders`. `timeseries` groups by UTC day. Breakdowns
contain up to 100 groups. Only verified usage evidence contributes token totals;
only posted settlements contribute customer charges. Activity/detail token and
charge values remain nullable when unknown or unsettled. Detail exposes safe
attempt and evidence metadata, never prompt/response bodies or credentials.
`USAGE-004` now reads complete UTC hours from durable Worker-published rollups;
partial-hour edges and request-ID filters read authoritative rows. Activity and
detail remain live. One analytics response uses a read-only database snapshot.
`dataAsOf` is the oldest materialized hourly snapshot, not response time; Unix
epoch means initial backfill has a missing bucket. Additive `aggregation` reports
`pendingBucketCount`, nullable `oldestPendingAt`, and `source` (hourly with live
edges or authoritative request). Pending metadata respects tenant/project/key/model
scope but includes every mutable status/provider/stream mode to disclose changes
out of a filter. Gross `chargedMicroUsd` keeps its meaning; `refundedMicroUsd`
and `netChargedMicroUsd` separately report wallet-credit refunds and gross minus
refunds in summary/daily/breakdown results. Neither is a wallet balance.
See [rollup publication, lag and correction rules](21_USAGE_ROLLUPS.md).

### Bounded usage CSV export (`USAGE-005`, FR-095)

```text
GET /management/v1/organizations/{organizationId}/usage/export?kind=activity&rowLimit=1000&from=<UTC>&to=<UTC>
```

Uses the same `ReadUsage` authorization, filters and UTC `[from,to)` normalization
as usage reads. `kind` is `activity` (default) or `daily`; `rowLimit` defaults to
1000 and accepts 1–5000. The complete UTF-8 BOM/quoted CSV must fit 4 MiB;
`limit`/`cursor` are rejected rather than silently exporting one page. Invalid
filters return `400`, missing session `401`, forbidden scope `403`, excess rows
or bytes `413`, and the 30-second server deadline `504`. No partial attachment
is returned on failure. Client disconnect cancels database I/O and serialization.

Success is `text/csv; charset=utf-8`, `Content-Disposition: attachment` with
`uzllm-activity.csv` or `uzllm-daily.csv`, `Cache-Control: no-store`, and
`X-Content-Type-Options: nosniff`. Headers `X-Usage-From`, `X-Usage-To`,
`X-Usage-Data-As-Of`, `X-Usage-Source`, `X-Usage-Pending-Buckets`, and
`X-Usage-Row-Count` describe even an empty, header-only export. Rows repeat
scope/freshness metadata. Activity uses the live metadata projection in one
bounded statement; daily uses the same snapshot and lag/refund/net semantics as
`/timeseries`. Unknown activity tokens/charges are blank, not fabricated zero;
amounts retain integer micro-USD text without floating-point conversion.

All fields are quoted and embedded quotes doubled. Unsafe formula prefixes
(including whitespace/control/format bypasses and full-width equivalents) gain
a leading apostrophe. This is a CSV escaping policy, **not** a universal
spreadsheet/save-reopen safety guarantee: import untrusted names as text.
Bodies, credentials and trace IDs are excluded. See [schema, examples and
validation](22_USAGE_EXPORT.md).

### Opt-in payload retention (`PRIVACY-001`)

Organization-owner-only routes; session authentication is required and the
mutation also requires the management CSRF token:

```text
GET /management/v1/organizations/{organizationId}/projects/{projectId}/payload-retention
PUT /management/v1/organizations/{organizationId}/projects/{projectId}/payload-retention
GET /management/v1/organizations/{organizationId}/projects/{projectId}/payload-retention/requests/{requestId}
```

`PUT` accepts `{ "enabled": true, "retentionMinutes": 1440 }` (60–10080
minutes). The policy response contains `enabled` and `retentionMinutes`; no
policy means disabled with a 1440-minute default. Disabling deletes existing
project payloads. The request-specific read returns `requestId`, `projectId`,
`request` (raw JSON string), nullable `response` (exact completed JSON/SSE
bytes when at most 1 MiB), and `expiresAt`, or `404` when missing/expired or
outside the tenant/project. All responses use `Cache-Control: no-store`.
Metadata APIs never return bodies.

For supported inference requests, `Idempotency-Key` is scoped to organization,
key identity, and operation for 24 hours. A repeat returns `409` with the
original request ID; mismatched request content also conflicts. The API stores
no completion body for replay. Gateway extensions belong under `uzllm`.

### Customer alerts (`NOTIFY-002`)

Under `/management/v1/organizations/{organizationId}/alerts`:

```text
GET    /rules
GET    /events
POST   /rules
PATCH  /rules/{ruleId}
GET    /destinations
POST   /telegram/link
DELETE /destinations/{destinationId}
```

Reads require billing-read permission; mutations require billing-management
permission (Owner/Admin) and `X-CSRF-Token`. `POST /rules` accepts
`{ "type": "BudgetWarning", "threshold": 8000, "projectId": "...",
"budgetPolicyId": "...", "destinationId": "..." }`. `LowBalance` uses
micro-USD and organization scope; `BudgetWarning` uses 1–10000 basis points
and a project budget policy; `ErrorSpike` uses 1–10000 basis points and an
optional project. `PATCH` accepts `{ "enabled": false }`. Responses include
scope, threshold, destination, enabled status, and last trigger, but never
Telegram chat ID. Cross-tenant IDs return `404`; unauthorized organizations
return `403`.

`FRONTEND-009` adds `GET /events`: latest 100 events in descending
`triggeredAt`, then `id` order, with billing-read authorization and no-store.
Each item contains `id`, `ruleId`, `type`, nullable `projectId`, numeric
`observedValue`, `status`, `triggeredAt`, nullable `deliveredAt`. Values use
micro-USD for LowBalance and basis points for other types. No chat ID, endpoint,
signature, transport error or payload is returned. Existing persisted states
are Pending/Delivered/Suppressed; Pending can include work needing operator
investigation and must not be presented as guaranteed queued delivery. The
customer API does not expose the operator retry/dead-letter console.

`POST /telegram/link` returns a 10-minute `deepLink` and `expiresAt` with
`Cache-Control: no-store`. Only a private-chat `/start <token>` sent to the
configured bot and delivered to `POST /integrations/telegram/webhook` can
verify ownership. Telegram's `X-Telegram-Bot-Api-Secret-Token` is mandatory;
invalid secret returns `403`, while replay/unknown updates are acknowledged
without altering a destination. The webhook has a 16 KiB body cap.

### Signed customer outbound webhooks (`NOTIFY-003`)

`POST /management/v1/organizations/{organizationId}/alerts/webhook/destination`
accepts `{ "endpointUrl": "https://alerts.example.com/uzllm" }`. Owner/Admin
and session CSRF are required. The response is `201` with `id`, `endpointUrl`,
and a 64-hex-character `signingSecret` shown once (`Cache-Control: no-store`).
Only one active webhook destination per organization is supported; creating a
second returns `409`. Disable through the existing destination DELETE endpoint
before replacing it. The destination list includes the URL/status, never the
secret. Webhooks report `Active` after URL/DNS validation, not domain-ownership
verification. Invalid or private-address URLs return `400`. Alert rules use its ID as
the existing `destinationId`.

Delivery is `POST` JSON with `X-UZLLM-Event-Id` (stable UUID),
`X-UZLLM-Timestamp` (Unix seconds), and `X-UZLLM-Signature: v1=<lowercase
hex HMAC-SHA256>`. The HMAC key is the hex-decoded signing secret; the signed
bytes are UTF-8 `timestamp + "." + eventId + "."` followed by the exact body.
The body has `id`, `type=customer.alert.triggered`, `version=1`,
`organizationId`, optional `projectId`, `ruleId`, `alertType`, `threshold`,
`observedValue`, and `triggeredAt`. Receivers should reject timestamps outside
±300 seconds, compare signatures in constant time, and persist event IDs for
at least 24 hours for replay deduplication. Any 2xx acknowledges; 429/5xx and
transport failures retry with the same event ID and a new timestamp/signature;
other statuses disable the destination. Processing is at-least-once, not exactly once.
