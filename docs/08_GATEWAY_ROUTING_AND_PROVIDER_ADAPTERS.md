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

`ROUTING-003` accepts `uzllm.routing=price` for the requested canonical model.
It first applies provider/model, capability, credential, quota, and circuit
eligibility, then scores Managed candidates using the selected catalog price
version, estimated input tokens, requested output ceiling, and the active
customer fee policy. Ties retain deterministic provider priority and mapping
ID order. One request considers at most two same-model attempts under one
worst-case wallet hold; if the price version changes before an attempt,
execution fails closed rather than silently repricing. Explicit BYOK remains
primary for Hybrid, with price ranking applying only to eligible Managed
fallback candidates. Explicit cross-model fallback is `ROUTING-005`.

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

`ROUTING-004` accepts `uzllm.routing=latency`, `throughput`, or `auto` for
already-authorized, priced, credentialed, quota-available, healthy mappings.
Gateway writes bounded, mapping- and stream-mode-scoped attempt measurements to a five-minute
Redis window (default 256 samples). Latency uses median time to first visible
token; throughput uses median verified output tokens per second. `auto` blends
normalized customer price, recent error rate, latency and throughput using
configurable weights. All candidates need at least ten complete recent
samples; sparse/stale/unavailable performance data restores deterministic
provider priority rather than bypassing eligibility. A ten-percent hysteresis
margin keeps the deterministic primary when a measured gain is small. The
Redis samples are advisory, not usage evidence or a financial source of truth.
Only Managed attempts feed this global ranking window; tenant-owned BYOK
credential performance cannot influence another customer's route.
BYOK stays primary under explicit Hybrid routing, and reservation still holds
the worst eligible customer charge. The sampled window intentionally differs
from customer-facing usage analytics, which read durable PostgreSQL facts.

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
  "uzllm": {"fallback_models": ["fallback-model-a", "fallback-model-b"]}
}
```

or an OpenRouter-compatible `models`-style extension later.

`ROUTING-005` accepts one or two distinct active canonical fallback model
codes. The gateway tries up to two eligible primary-model mappings first,
then at most one eligible mapping per fallback model in client-supplied order;
the total is bounded to four attempts under one deadline and one reservation.
Each model must satisfy request capabilities, context/output limits, credential
eligibility, quota, and health. The hold is the maximum charge across all
selected mappings, using each model's own catalog price and output limit.
Provider calls remain outside the transaction. A price-version change before
dispatch fails closed. Only verified pre-execution transient rejection permits
the next model; unknown outcomes and partial streams never do. BYOK-only
cross-model fallback is rejected; Hybrid may use explicitly opted-in Managed
fallback. A provider-prefixed primary model can change only because the
fallback list explicitly authorizes it. Response headers and usage detail show
the selected model separately from the originally requested model.

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

## 15. P0 implementation boundary

The gateway orders eligible mappings OpenAI, then Anthropic, then Google (then
by mapping ID), skips open Redis-backed provider-model circuits, and reserves the maximum
estimated charge across at most two eligible mappings. It records each attempt
under one logical request. A verified 429 or overload rejection may try the
next mapping only before downstream output; 5xx, timeout, transport ambiguity,
and mid-stream errors remain unknown financial outcomes and are not retried.
The Redis circuit opens after three transient failures in one minute for 30
seconds; an unavailable health dependency fails new managed admission closed.
`ROUTING-006` scopes circuits to both the provider-model mapping and the
credential (BYOK keys also include organization). Authentication rejection
opens only that credential circuit; upstream 429/quota failures count only
against that credential. Capacity, timeout and upstream 5xx failures count
against the shared mapping circuit, so an endpoint outage excludes Managed and
BYOK alike. On expiry, Redis admits one cross-node half-open probe before
dispatch; neutral/client-disconnect outcomes release the probe without claiming
recovery, and stale probe results cannot close a newer probe. Redis failure at
either eligibility or dispatch fails closed and releases an undispatched hold.
Detailed latency/throughput scoring remains later P1 work; explicit bounded
cross-model fallback is delivered by `ROUTING-005`.

`BYOK-002` selects one explicitly requested, project-granted BYOK credential
before an optional managed same-model candidate. Managed fallback requires
`uzllm.allow_managed_fallback=true` and a full managed wallet reservation.
An unavailable BYOK credential, disallowed canonical model, or exceeded
lifetime external-spend cap fails admission; the gateway never silently
changes a BYOK-only request to Managed. Provider authentication rejection,
unknown execution, and partial SSE output are terminal for this request.

Anthropic translation uses its [Messages API](https://platform.claude.com/docs/en/api/messages/create),
[stream event protocol](https://platform.claude.com/docs/en/build-with-claude/streaming),
and [error codes](https://platform.claude.com/docs/en/api/errors). Its
`cache_read_input_tokens` are included in total normalized input and marked as
cached; unexpected cache-creation usage is left unknown rather than priced as
ordinary input until Catalog supports that distinct upstream price dimension.
An Anthropic refusal is a successful, billable provider response, not a
failover trigger. Live model access, pricing, and platform credentials remain
deployment/operator prerequisites.

`PROVIDER-006` adds Google's native
[GenerateContent API](https://ai.google.dev/api/generate-content) at the HTTPS
`/v1beta/models/{model}:generateContent` endpoint, rather than the newer
Interactions API. This bounded adapter accepts text dialogue and native JSON
output only. It rejects tools and vision before financial reservation. Catalog must mark each Google
mapping with its actual capabilities and provide a current price version.
`promptTokenCount` is normalized input, `cachedContentTokenCount` is its cached
subset, and `candidatesTokenCount + thoughtsTokenCount` is billable output;
thinking tokens are also reported separately. Missing or inconsistent usage,
nonzero tool-use prompt tokens, and unsupported response shapes remain unknown
financial evidence, never zero-cost success. Catalog prices for this adapter
must be flat per-token input/output with an optional cached-input rate;
nonempty extra pricing dimensions (including long-context tiers) fail closed
at reservation. Operators must verify live model eligibility and the published
[Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing) before enabling
Managed traffic. Google BYOK verification remains separate work.

`PROVIDER-007` adds native
[streamGenerateContent SSE](https://ai.google.dev/api/generate-content) for the
same text/JSON request subset. Each streamed `GenerateContentResponse` is a
delta, not a replacement for the entire completion. The adapter forwards text
as received, but emits terminal finish and one cumulative usage event only
after a complete stream with final usage. Missing terminal usage, malformed or
oversized frames, changing response IDs, regressions in cumulative token
counts, timeout, and in-band error are unknown outcomes; partial output is
never replayed. Client cancellation closes upstream I/O while Gateway cleanup
persists usage evidence and finalizes the reservation independently. The
provider-specific SSE reader is bounded per line/frame; no database transaction
spans streaming.

`PROVIDER-008` adds a separate DeepSeek
[Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)
adapter. The wire format resembles OpenAI but uses `max_tokens`, explicit
non-thinking mode for predictable sampling, and provider-specific
`prompt_cache_hit_tokens` / `prompt_cache_miss_tokens`; the two must sum to
`prompt_tokens`. Reasoning tokens, if reported, are a subset of output tokens
and never added again. Missing or inconsistent counters remain unknown
financial evidence. The bounded adapter supports text and JSON object mode;
tools, vision, JSON Schema, and DeepSeek BYOK are not advertised. It requests
non-thinking mode; because DeepSeek ignores `top_p` in that mode, non-default
`top_p` requests are rejected before dispatch rather than silently altered.

`PROVIDER-009` enables DeepSeek Chat Completions SSE with
`stream_options.include_usage=true`. It forwards text deltas immediately while
keeping provider `reasoning_content` private. The final cache-hit, cache-miss,
output, and reasoning counters are validated as one authoritative cumulative
usage snapshot; reasoning is never added to billable output a second time.
The adapter emits finish and usage only after a valid `[DONE]` with terminal
usage. Missing/malformed usage, changed response IDs, duplicate final usage,
mid-stream errors, timeout, and disconnect leave execution unknown and never
trigger replay. Client cancellation stops upstream I/O; Gateway cleanup remains
independent. The SSE reader has bounded lines/frames and does not buffer the
whole completion. A future explicit thinking-mode request contract is separate
from normalizing any reasoning tokens returned by the provider.

DeepSeek's [published prices](https://api-docs.deepseek.com/quick_start/pricing/)
include distinct cache-hit, cache-miss, output, and recurring peak/off-peak
rates. Every active DeepSeek mapping requires an explicit cached-input rate;
otherwise Gateway refuses reservation. Operators must publish separate,
non-overlapping effective Catalog price versions for each tariff window and
verify current model access before enabling Managed traffic. The adapter
never guesses a live rate from token usage. If supplier prices change outside
the published windows, disable the mapping and reconcile provider invoices;
the existing cost model cannot assert exact external spend without a matching
price snapshot.
