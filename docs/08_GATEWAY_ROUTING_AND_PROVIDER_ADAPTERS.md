# Gateway Routing and Provider Adapters

## 1. Goals

Routing should optimize among:
- correctness;
- availability;
- price;
- latency;
- throughput;
- policy;
- provider capacity.

No routing strategy may bypass:
- model/provider permissions;
- privacy restriction;
- budget;
- provider health;
- credential eligibility.

---

## 2. Candidate generation

For requested canonical model:

```text
Model
 |
 +--> ProviderModel A / Credential 1
 +--> ProviderModel B / Credential 2
 +--> ProviderModel C / Managed
```

Filter candidates by:

1. provider/model enabled;
2. key IAM/allowlist;
3. project policy;
4. BYOK/managed mode;
5. provider credential status;
6. model capability support;
7. context/output constraints;
8. health/circuit state;
9. provider quota/capacity;
10. privacy/data policy.

Only then score candidates.

---

## 3. MVP routing [P0]

### Explicit route
If client requests a provider-prefixed model and policy allows it, use that endpoint without silent substitution unless fallback was explicitly allowed.

### Default route
Use a deterministic provider priority list with health-aware fallback.

This is easier to debug than premature “AI routing”.

---

## 4. Advanced routing [P1]

### Price

```text
score = estimated_customer_cost
```

Choose cheapest healthy endpoint.

### Latency
Use rolling recent TTFT, preferably percentile/EMA rather than one last request.

### Throughput
Use rolling output tokens/sec.

### Auto
Possible weighted score:

```text
score =
  w_price      * normalized_price
+ w_error      * normalized_error_rate
+ w_latency    * normalized_ttft
+ w_throughput * inverse_normalized_throughput
```

Lower score wins.

Weights are configuration, not hard-coded domain law.

---

## 5. Sticky routing

For long conversations/provider prompt caching, switching providers every turn can destroy cache benefits.

Future routing context may accept:
- session ID;
- cache affinity key;
- previous provider hint.

Use stickiness while endpoint is healthy and policy allows.

---

## 6. Failover policy

### Provider failover

Within same canonical model.

Trigger examples:
- connect timeout;
- selected 429;
- 502/503/504;
- unhealthy circuit.

### Model fallback

P1 request may contain:

```json
{
  "model": "primary-model",
  "fallback_models": [
    "fallback-model-a",
    "fallback-model-b"
  ]
}
```

or an OpenRouter-compatible `models`-style extension later.

### Do not infinite retry

Set:
- maximum attempts;
- total deadline;
- per-attempt timeout.

---

## 7. Provider adapter responsibilities

Each adapter handles:
- authentication header;
- base URL;
- request translation;
- parameter mapping;
- streaming parser;
- usage parsing;
- provider error normalization;
- cancellation;
- provider request ID extraction.

It must not:
- debit wallet;
- decide organization permissions;
- directly send alerts.

---

## 8. Normalized provider error

Example internal type:

```text
ProviderError
  Category:
    Authentication
    RateLimited
    InvalidRequest
    ContextExceeded
    ContentRejected
    Capacity
    Timeout
    Upstream5xx
    Unknown

  IsFallbackEligible
  IsRetryable
  ProviderStatusCode
  ProviderRequestId
  SafeMessage
```

---

## 9. Streaming

### Requirements

- use `ResponseHeadersRead`;
- avoid buffering entire upstream response;
- propagate cancellation when client disconnects;
- parse usage chunk if provider supports it;
- count/estimate usage safely when provider does not return usage;
- flush downstream incrementally.

### Billing caution

Client cancellation does not necessarily mean provider did not charge.

Final usage source must support:
- provider authoritative;
- gateway estimated;
- later reconciliation.

---

## 10. Provider health

Track per endpoint:
- rolling success rate;
- 429 rate;
- 5xx rate;
- TTFT;
- total latency;
- throughput;
- last failure;
- circuit state.

States:
- Healthy
- Degraded
- Open/Unavailable
- Unknown

Health state should be shared across gateway instances via Redis or another distributed mechanism.

---

## 11. Provider credential pool

A provider may have multiple platform credentials.

Routing can select credential based on:
- status;
- remaining quota;
- concurrency;
- region;
- model access.

Never use a credential merely because it exists.

---

## 12. Cache

### Provider prompt cache
Forward provider cache semantics and usage metadata.

### Gateway response cache [P1]
Key must include normalized:
- model;
- messages/input;
- temperature;
- tools;
- response format;
- relevant routing/policy factors.

Avoid serving cached data across organizations unless security/privacy design explicitly guarantees safe isolation.

Default safer key scope:

```text
organization + project + normalized request
```

---

## 13. Compatibility philosophy

Support the common OpenAI contract first.

Provider-specific features can be exposed through:
- normalized extension fields;
- metadata;
- provider passthrough only when deliberately supported.

Do not claim 100% compatibility for parameters that are silently ignored.

---

## 14. Execution certainty and stream termination

The Gateway owns a total attempt budget, deadline, and downstream-delivery
state. It may fail over only after `NotDispatched` or verified
`RejectedBeforeExecution` transient failures. It must not retry an `Unknown`
attempt or any attempt after output was delivered.

P0 failover changes only to an equivalent mapping for the same canonical model.
Cross-model fallback is P1 and must be explicit under the `uzllm` extension
object. Pinned provider routes do not silently substitute a provider.

Stream adapters use incremental parsing and expose text, tool deltas, usage,
finish, and safe-error events. If an error occurs after HTTP headers/output,
emit a safe terminal SSE error when the response shape permits and close; do not
fabricate success or transparently replay the stream. Client cancellation stops
upstream transport but does not cancel evidence/financial cleanup.
