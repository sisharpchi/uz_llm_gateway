# Non-Functional Requirements

## 1. Availability

### NFR-001
Production gateway target availability: **99.9% monthly** for the UZLLM control/gateway layer, excluding upstream provider outages.

Gateway availability, provider availability, and end-to-end model success shall
be measured and reported separately.

### NFR-002
Management/dashboard failure must not unnecessarily stop already-configured gateway traffic.

### NFR-003
A single upstream provider failure must not take down a model when another eligible mapping exists.

---

## 2. Performance

### NFR-010
Gateway overhead, excluding upstream model time, should target:

- p50 < 25 ms
- p95 < 100 ms
- p99 < 250 ms

for normal authenticated routing requests under expected load.

The initial acceptance load is 10 requests/second with 200 concurrent streams
across two Gateway nodes and deterministic upstream fixtures. These targets are
initial configurable defaults until load-tested.

### NFR-011
Streaming must begin forwarding upstream data as soon as available; gateway must not wait for full response completion.

### NFR-012
Model catalog and management reads should generally be < 300 ms p95 under normal load.

### NFR-013
High-volume analytics work must not run synchronously in the inference hot path.

---

## 3. Scalability

### NFR-020
Gateway API must be horizontally scalable and stateless except for external dependencies.

### NFR-021
Rate-limit and concurrency state must work across multiple gateway instances.

### NFR-022
Initial architecture should support scaling from hundreds to tens of thousands of daily requests without redesigning the domain model.

### NFR-023
Provider connections must use pooled `HttpClient`/connection reuse.

---

## 4. Financial correctness

### NFR-030
Wallet/ledger operations require strong transactional consistency.

### NFR-031
Payment callbacks must be idempotent.

### NFR-032
A duplicate provider callback or user retry must never double-credit the wallet.

### NFR-033
A request must not be double-settled.

### NFR-034
All monetary values must use integer minor/micro units or fixed-precision decimals; never binary floating point.

### NFR-035
Historical inference cost must remain reproducible from:
- usage;
- price version;
- fee policy;
- FX snapshot when applicable.

---

## 5. Security

### NFR-040
Gateway API key plaintext must not be recoverable from the database.

Recommended:
- show once;
- store keyed HMAC fingerprint;
- optional secret prefix for lookup.

### NFR-041
BYOK/provider credentials must be encrypted at rest using application-managed or cloud KMS-backed keys.

### NFR-042
Secrets must never appear in application logs.

### NFR-043
All traffic must use TLS.

### NFR-044
Management endpoints require authorization by organization/project role.

### NFR-045
Sensitive admin actions require audit records.

### NFR-046
Rate limiting, request-size limits and abuse controls are mandatory before public launch.

---

## 6. Privacy

### NFR-050
Prompt/response payload retention should be **off by default** or explicitly configurable.

### NFR-051
Operational metadata can be retained separately from payload data.

### NFR-052
User shall be able to understand whether an upstream provider may retain or train on data when that metadata is available.

### NFR-053
Future ZDR mode shall prohibit incompatible caching/payload logging.

---

## 7. Reliability

### NFR-060
Use timeouts per provider and operation.

### NFR-061
Retries must be limited and only performed for retry-safe/eligible failures.

### NFR-062
Use circuit breakers per provider endpoint/credential.

### NFR-063
Prevent retry amplification across gateway and provider layers.

### NFR-064
Background workers require retry + dead-letter strategy.

### NFR-065
Payment reconciliation job shall detect ambiguous/incomplete payment states.

### NFR-066
New managed inference shall fail closed when PostgreSQL cannot commit financial
admission or Redis distributed admission state is unavailable. Existing reserved
work may finalize through PostgreSQL. Redis restart recovery starts closed until
the maximum outstanding lease horizon has elapsed.

---

## 8. Observability

### NFR-070
Every gateway request must have:
- RequestId
- TraceId
- OrganizationId
- ProjectId
- ApiKeyId
- selected provider/model
- outcome

without logging secrets.

Unauthenticated telemetry may omit organization, project, and key identifiers
while retaining request and trace IDs.

### NFR-071
Collect:
- request rate;
- error rate;
- latency;
- TTFT;
- throughput;
- token usage;
- spend;
- provider failure rate;
- fallback rate;
- cache hit rate;
- reservation/settlement failures.

### NFR-072
Use distributed tracing with OpenTelemetry-compatible instrumentation.

### NFR-073
Critical business alerts must exist for:
- negative/invalid wallet conditions;
- settlement failures;
- payment callback failures;
- provider outage;
- elevated 5xx;
- queue backlog.

---

## 9. Maintainability

### NFR-080
Provider-specific code must not leak throughout application/domain layers.

### NFR-081
New provider integration should mostly require a new adapter plus configuration/mappings.

### NFR-082
Business logic should be covered by automated unit/integration tests.

### NFR-083
Database migrations must be version-controlled.

### NFR-084
API contracts require explicit versioning/deprecation policy.

---

## 10. Disaster recovery

Initial production targets:

- **RPO:** <= 5 minutes for ordinary platform data; financial ledger ideally zero-loss within committed database transactions.
- **RTO:** <= 30 minutes for core gateway/control-plane recovery target.
- Automated database backups.
- Point-in-time recovery where infrastructure supports it.
- Restore procedure tested periodically.

An acknowledged financial transaction is durable only within the managed
database service’s documented failure model. Regional disasters can exceed this
guarantee; recovery objectives and provider guarantees must be recorded before
launch.

---

## 11. Compliance readiness

The MVP does not claim certification.

Architecture should make future work possible:
- auditability;
- least privilege;
- retention policy;
- data deletion workflows;
- vendor/provider data-policy metadata;
- security incident records.
