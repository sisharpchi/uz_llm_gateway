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
with `{ "provider": "Payme|Click", "amountTiyin": 100000 }`, then
`POST /management/v1/organizations/{organizationId}/billing/topups` with
`{ "quoteId": "..." }` and an `Idempotency-Key` header. Both writes require the
management session and CSRF header. The latter returns an intent and provider
checkout URL; the key may be replayed only for the same quote. `GET` endpoints
for `/billing/topups`, `/billing/topups/{intentId}`, and `/billing/wallet` are
owner-scoped. Amounts are integer UZS tiyin; wallet credits are integer USD
micro-units using the quote's immutable FX snapshot.

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
`400 unsupported_parameter`. `model` may be canonical or `openai/<canonical>`.
Only the OpenAI route exists until `PROVIDER-002`; an explicit provider route is
never silently substituted. An omitted output limit uses the catalog model's
maximum. For streaming, `data: [DONE]` follows a complete upstream stream;
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

### API Keys

```text
GET    /projects/{projectId}/api-keys
POST   /projects/{projectId}/api-keys
PATCH  /api-keys/{id}
POST   /api-keys/{id}/rotate
POST   /api-keys/{id}/disable
DELETE /api-keys/{id}
```

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

### BYOK

```text
GET    /provider-keys
POST   /provider-keys
PATCH  /provider-keys/{id}
POST   /provider-keys/{id}/disable
DELETE /provider-keys/{id}
```

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
```

Successful login issues an opaque server-backed `__Host-uzllm-session` cookie
(`HttpOnly`, `Secure`, `SameSite=Strict`) and an equally strict readable CSRF
cookie. State-changing authenticated browser endpoints require the matching
`X-CSRF-Token` header. Verification and recovery tokens are one-time opaque
secrets and must never be returned in HTTP responses or logs.

Wallet responses distinguish `posted`, `reserved`, `available`, `recoveryDebt`,
and `spendingHeld`. Request detail distinguishes execution, delivery, and
financial outcomes plus attempts. Micro-unit amounts are decimal strings when
they may exceed JavaScript safe integers. Aggregate responses include `dataAsOf`.

For supported inference requests, `Idempotency-Key` is scoped to organization,
key identity, and operation for 24 hours. A repeat returns `409` with the
original request ID; mismatched request content also conflicts. The API stores
no completion body for replay. Gateway extensions belong under `uzllm`.
