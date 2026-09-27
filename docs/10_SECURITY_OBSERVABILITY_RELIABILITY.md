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

Gateway-key rotation has immediate, non-overlapping cutover at the committed
database transaction. Authentication queries begun after commit reject the old
secret; already-authenticated/in-flight requests may finish. Rotating an
inactive, expired or archived-project key fails. Two concurrent rotations use
the expected generation so at most one replacement is issued. Disable racing
with rotation leaves the key disabled. A replacement is returned only after
the new fingerprint, generation history and audit commit together; neither
secret is written to audit or logs. Client applications must switch credentials
immediately; planned overlap/grace periods are not supported.
If the response is lost after commit, the owner can rotate the still-active
key again from the management session; the previous plaintext cannot be
recovered.

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

`BYOK-001` binds AES-GCM authenticated data to organization, credential,
provider, and key version. Old KEK versions remain available while ciphertext
is rotated. Credential-to-project grants have composite tenant FKs; inactive,
deleted, or ungranted credentials do not resolve. Verification sends the key
only to the fixed official provider model-list endpoint, with redirects
disabled; upstream bodies are discarded. Credential mutations and safe test
outcomes are audited in the same database transaction. Managed/Hybrid
execution is not enabled by this storage/control-plane task.

The P0 platform credential store uses AES-GCM envelope encryption bound to the
credential and provider IDs. Configure `ProviderSecrets:ActiveKeyVersion` and
`ProviderSecrets:Keys:<version>` (base64-encoded 32-byte KEKs) through a secret
manager or protected environment configuration, never repository settings. Keep
old key versions available for decryption during rotation; deleting one before
rewrapping its credentials makes those credentials unusable. Gateway API keys
are separate and remain non-recoverable.

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

Operator routes require an active operator grant and a TOTP verification within
the last 15 minutes. First-time enrollment requires the operator's password and
cannot replace an existing secret through the browser. Mutations also require
session-bound CSRF proof, a reason of 8–500 characters, and an append-only audit
event in the same database transaction as the configuration change. The
operator UI never receives platform credential ciphertext or plaintext. Granting
the first operator role is an out-of-band privileged provisioning step.

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

New managed inference fails closed when distributed admission is unavailable;
there is no per-node fallback. A Redis instance change starts a recovery window
equal to the maximum configured concurrency lease. Until it elapses, new managed
requests remain closed while already reserved work may finish and settle through
PostgreSQL. A cold, empty Redis instance follows the same window.

The limiter reads Redis `INFO SERVER` `run_id` on admission, so the gateway Redis
identity must have permission for that command. Keep `Limits:MaxLeaseSeconds`
(default 900) at least as long as the maximum upstream generation lifetime and
do not lower it while old leases may still be active. Redis never owns wallet
accounting.

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

Verification and recovery token hashes live in Identity challenges. The
plaintext token and recipient are Data-Protection-encrypted before being
written to the transactional outbox; Management and Worker share the protected
key ring. The Worker sends STARTTLS email, retries transient failure, suppresses
recorded duplicate delivery, and does not send after challenge expiry. SMTP
cannot make the send/inbox-write boundary exactly once; a crash in that window
may resend the same token, which remains one-time and time-limited. Never log
outbox plaintext, protected payloads, SMTP message bodies or tokens.

Team invitations use the same protected transactional email outbox. The
database stores only a token hash and an expiry; acceptance requires a verified
account whose normalized email matches the invitation. Organization membership
and project grants are checked from PostgreSQL on each management request, not
copied into session claims. Owner/Admin can manage the team, but Admin cannot
create/change/revoke an Admin or Owner. A locked organization row serializes
membership changes and prevents demoting or revoking the last active Owner.
Owner/Admin have all project access; Developer and ReadOnly require an explicit
project grant. Developer may manage keys only on granted projects; ReadOnly
cannot mutate. BillingViewer can read billing/usage but cannot top up or alter
budgets. Team role/grant changes and acceptance are audited in the same
transaction as their database mutation.

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

`NOTIFY-002` stores Telegram private chat IDs with a dedicated versioned
`Telegram:ChatKeys:<version>` AES-GCM key, never plaintext. Management needs
`Telegram:BotUsername`, `Telegram:WebhookSecret`,
`Telegram:ActiveChatKeyVersion`, and the active chat key; Worker needs
`Telegram:BotToken` plus all chat key versions still referenced by destinations.
Provision the Telegram webhook with HTTPS and the same secret token. Exclude
`api.telegram.org` HTTP request traces: Bot API URLs contain the bot token.
Never log link tokens, webhook bodies, bot credentials, or chat IDs. Rule and
destination mutations write audit events. Customer events and their outbox
messages commit together; Worker delivery retries transient failures and
disables destinations rejected by Telegram. Telegram `sendMessage` has no
idempotency key, so a crash after upstream acceptance but before local inbox
commit can cause an at-least-once duplicate delivery. The rule episode itself
remains deduplicated. Live bot/webhook onboarding and private-chat delivery
verification are launch prerequisites outside CI.

`PRIVACY-001` adds project-scoped, owner-only opt-in payload retention (one hour
to seven days; default off). Request and bounded complete response bodies live
only in `usage.payload`, separately from request/usage/financial metadata.
Each body has its own AES-GCM data key, wrapped by a versioned
`PayloadSecrets:Keys:<version>` 32-byte base64 key; authenticated data binds
organization, project, request, purpose, and key version. Supply
`PayloadSecrets:ActiveKeyVersion` and the active key to Gateway and Management
through secret files. Keep retired keys until all encrypted rows using them
expire; never reuse provider credential keys. Disabling retention deletes that
project's stored payload rows transactionally. The Worker removes expired
rows in bounded batches every five minutes; reads reject expired rows
immediately. Only an organization owner may change policy or retrieve a
retained body, and both actions create metadata-only audit events. The normal
usage detail API remains payload-free; prompts and responses are never logged.
