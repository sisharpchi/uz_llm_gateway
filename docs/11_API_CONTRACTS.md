# API Contracts

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

### Payment callbacks
Provider-specific:

```text
https://api.example.uz/payments/payme/callback
https://api.example.uz/payments/click/callback
```

### P0 top-up control plane

An authenticated organization owner uses `POST /management/v1/organizations/{organizationId}/billing/quotes`
with `{ "provider": "Payme", "amountTiyin": "100000" }`, then
`POST /management/v1/organizations/{organizationId}/billing/topups` with
`{ "quoteId": "..." }` and an `Idempotency-Key` header. Both writes require the
management session and CSRF header. The latter returns an intent and provider
checkout URL; the key may be replayed only for the same quote. `GET` endpoints
for `/billing/topups`, `/billing/topups/{intentId}`, and `/billing/wallet` are
owner-scoped. Top-up amount input, quote/intent amounts, FX rate, and wallet
USD micro-units are JSON decimal strings; the server parses them as fixed-
precision values using the quote's immutable FX snapshot. This prevents
JavaScript number precision loss in the browser.

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

---

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

## 3. Gateway extensions

Prefer namespace-like extension to reduce collision risk:

```json
{
  "model": "model-x",
  "messages": [],
  "uzllm": {
    "routing": "price",
    "allowed_providers": ["provider-a", "provider-b"],
    "fallback_models": ["model-y"],
    "max_cost_usd": 0.05
  }
}
```

P0 can omit most extensions and use project defaults.
`ROUTING-003` accepts `{"uzllm":{"routing":"price"}}` without a BYOK key.
Only `price` is currently supported; unknown routing values return
`400 unsupported_parameter`. The strategy ranks eligible same-model Managed
endpoints by a frozen catalog/customer-fee estimate; actual charges still use
verified usage and the selected price version. An explicit provider prefix
continues to constrain the eligible set. BYOK/Hybrid may include `routing=price`
without changing the explicitly requested BYOK-first order.
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
archive is a P0 terminal state transition; project settings and unarchive are
not part of the initial contract.

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

### Budgets

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

Owner/Admin only. Create takes `providerId`, `name`, and `secret`; PATCH takes
optional `name` and/or `secret` to rotate it. Read/write responses contain
`maskedKey`, status, last test result, and explicit `projectIds`, never secret or
ciphertext. All mutations require the management session and CSRF token.
Test returns `Valid`, `Invalid`, or `Unavailable` without upstream response
body; it makes a bounded GET to the provider's fixed model-list endpoint.
Only active OpenAI/Anthropic catalog providers are supported at this stage.
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
Use explicit version path or version header.

Breaking inference changes are especially costly because customer applications depend on compatibility.

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
```

Successful login issues an opaque server-backed `__Host-uzllm-session` cookie
(`HttpOnly`, `Secure`, `SameSite=Strict`) and an equally strict readable CSRF
cookie. State-changing authenticated browser endpoints require the matching
`X-CSRF-Token` header. Verification and recovery tokens are one-time opaque
secrets and must never be returned in HTTP responses or logs.

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
POST  /providers | /models | /mappings | /credentials | /prices
PATCH /providers/{id} | /models/{id} | /mappings/{id}
PATCH /credentials/{id} | /controls/{ManagedTraffic|TopUps}
```

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
and require an authenticated organization owner:

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
These reads aggregate directly from authoritative rows over the bounded window;
no gateway hot-path rollup write is required.

For supported inference requests, `Idempotency-Key` is scoped to organization,
key identity, and operation for 24 hours. A repeat returns `409` with the
original request ID; mismatched request content also conflicts. The API stores
no completion body for replay. Gateway extensions belong under `uzllm`.
