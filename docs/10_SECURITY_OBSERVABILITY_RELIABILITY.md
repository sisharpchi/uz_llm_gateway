# Security, Observability and Reliability

## 1. Threat model highlights

High-value assets:
- gateway API keys;
- BYOK provider secrets;
- platform provider credentials;
- wallet credits;
- payment callbacks;
- prompt/response data;
- admin controls.

Primary threats:
- stolen API key draining credits;
- leaked provider key;
- fake payment callback;
- replay attack;
- cost-exhaustion DoS;
- cross-tenant data leak;
- insecure logging;
- admin privilege abuse.

---

## 2. Gateway key design

Example shape:

```text
uzllm_live_<publicPrefix>_<secret>
```

Store:
- public prefix / key ID;
- keyed HMAC fingerprint of full secret;
- status and metadata.

Authenticate by:
1. parse prefix;
2. load candidate;
3. HMAC presented secret;
4. constant-time comparison.

Avoid storing reversible gateway key plaintext.

---

## 3. BYOK security

Because provider key must be used upstream, it is reversible by the service.

Requirements:
- envelope encryption;
- KEK in KMS/secret manager;
- DEK/ciphertext in DB;
- access only from provider execution path;
- masked UI;
- audit create/update/delete;
- never return full secret after creation.

---

## 4. Authorization

Apply resource-scoped authorization.

Example:

```text
Owner
  -> all org settings/billing

Admin
  -> projects/users/config except owner-sensitive billing actions

Developer
  -> assigned projects, own keys

Billing
  -> payments/invoices/usage financial views

ReadOnly
  -> views only
```

Never trust project ID from browser without ownership/membership check.

---

## 5. Payment webhook security

- provider-specific auth/signature validation;
- strict amount/currency matching;
- replay/idempotency protection;
- external transaction uniqueness;
- no credit before verified final state;
- safe callback logging.

---

## 6. Data retention

Separate:
1. operational metadata;
2. financial records;
3. request/response payload;
4. audit logs.

Give each a distinct retention policy.

Recommended baseline:
- payload logging off unless explicitly enabled;
- request metadata retained longer;
- financial/audit retention based on legal/accounting requirements.

---

## 7. Observability stack

Recommended:
- OpenTelemetry instrumentation;
- traces: Tempo/Jaeger/Application Insights/etc.;
- metrics: Prometheus/OpenTelemetry backend;
- dashboards: Grafana or managed equivalent;
- structured logs: Loki/ELK/managed provider.

---

## 8. Key metrics

### Gateway
- RPS;
- active streams;
- p50/p95/p99 overhead;
- total request duration;
- error rate;
- 429;
- 5xx.

### Provider
- success rate;
- upstream 429;
- upstream 5xx;
- TTFT;
- tokens/sec;
- timeout count;
- fallback count;
- circuit state.

### Financial
- reservation failures;
- settlement failures;
- stale reservations;
- negative-balance invariant violations;
- payment callback errors;
- reconciliation mismatches.

### Product
- active orgs;
- active keys;
- top-up volume;
- inference spend;
- model share;
- margin.

---

## 9. Logs

Never log:
- Authorization header;
- full gateway API key;
- BYOK secret;
- payment secrets;
- raw prompt by default.

Safe context:
- request ID;
- key ID;
- org/project IDs;
- provider/model;
- status;
- duration;
- usage totals.

---

## 10. Resilience policies

### Timeout
Separate:
- connect timeout;
- response header/TTFT timeout;
- total non-stream timeout.

Streaming total duration may need different limits.

### Retry
Use exponential backoff with jitter.

Retry only eligible failures and respect a total request deadline.

### Circuit breaker
Per provider endpoint/credential where practical.

### Bulkhead/concurrency
Prevent one provider hanging all gateway connections.

---

## 11. Redis failure behavior

Decide explicitly.

Possible policy:
- if distributed rate limiter unavailable, production inference can fail closed for unknown/high-risk traffic;
- existing authenticated paid traffic may use a carefully bounded local fallback only if financial safety remains intact.

Never let Redis outage corrupt wallet accounting.

---

## 12. Database failure behavior

New managed-credit inference should normally fail closed if financial reservation cannot be guaranteed.

Returning `503` is safer than unmetered spend.

---

## 13. SLO dashboard

Track separately:

### Gateway availability
UZLLM can receive/process request.

### Provider availability
Chosen provider endpoint health.

### End-to-end success
Client request receives successful model output.

This avoids blaming the gateway for all upstream model incidents while still exposing real user experience.

---

## 14. P0 control-plane and recovery baseline

Management uses secure, server-backed browser sessions with `HttpOnly`,
`Secure`, CSRF protection, email verification, and account recovery. Operator
access is separate from organization roles, requires MFA and recent
reauthentication for sensitive actions, and is audited.

Tenant scope is enforced in commands, queries, foreign keys, cache keys, and
worker payloads. Gateway HMAC keys, session-protection material, payment
secrets, and provider-encryption keys stay outside the database and source
control. Credentials support key-version rotation; raw prompts and secrets are
never logged.

P0 payload retention is disabled. Initial configurable retention defaults are
30 days for diagnostics and 90 days for operational metadata; financial/audit
retention needs an approved legal schedule. Operational alerts are mandatory for
settlement/evidence failures, stale known-evidence reservations, payment
mismatches, debt/exposure, Redis admission failure, provider authentication
failure, and outbox backlog. Customer threshold/Telegram alerts are P1.
