# Advanced Routing and Response Cache — Specification

## 1. Status, authority and scope

**ROUTING-007 · P2 · Draft for approval · 2026-10-18.** The user's Continue
selects this specification after PRIVACY-003. This is not feature execution or
approval of new charging/retention behavior. Decision matrices, fee semantics,
failure cases, benchmark plan and follow-on decomposition await review;
the task remains In Progress. Nothing here enables paid traffic or changes
OPS-006/007/LAUNCH-003.

FR-060–068 own routing capabilities; FR-013 names future project cache policy.
[Gateway §12](08_GATEWAY_ROUTING_AND_PROVIDER_ADAPTERS.md#12-cache) supplies the
response-cache intent; it is not an implemented charging contract. Add FR-013,
NFR-050–053/066 and ADR-011/014/015 to this task's references to make that scope
traceable, without changing other tasks or their priorities. Keep the existing
[financial invariants](06_DOMAIN_AND_BUSINESS_LOGIC.md#12-corrected-ownership-and-state-model),
[billing transactions](09_BILLING_WALLET_AND_PAYMENTS.md#15-normative-transaction-and-recovery-rules),
[project precedence](25_PROJECT_DEFAULTS.md), and approved-design
[strict privacy constraints](39_PRIVACY_ELIGIBILITY_SPEC.md). The latter are
not implemented yet; this spec neither implements nor weakens them.

Proposed first scope: structured routing reasons, versioned project `auto`
weights, operation-isolated performance samples, advisory metadata-only
affinity, and a separately gated, bounded Managed text response cache.
Keep the modular monolith, shared PostgreSQL financial context and three hosts.
No router microservice, ML predictor, broker, event sourcing or new budget model.

Defer: arbitrary routing DSL, traffic-split/A/B or exploration, best-of-N/hedged
parallel upstream calls, multi-key BYOK pooling, automatic execution-mode
changes, cross-tenant sharing, semantic caching, cached SSE/Responses/embeddings,
and new per-request cost ceilings. Features are unscheduled until explicit
execution approval/promotion. Current [API artifacts](api/README.md) remain
the implemented wire authority; proposed fields below must not be sent today.

## 2. Current repository and gaps

Inspected on clean `main` at `ae52e2e`:

| Existing evidence | Actual behavior | Consequence |
|---|---|---|
| [Price ranker](../src/Modules/Routing/Domain/PriceRouteRanker.cs), [performance ranker](../src/Modules/Routing/Domain/PerformanceRouteRanker.cs) | `price`, `latency`, `throughput`, `auto` already exist. `auto` weights price/error/latency/throughput; it is not probabilistic traffic allocation. Price ties prefer OpenAI, then ordinal provider code, then mapping ID. Performance ties use input index and primary hysteresis. | Do not reimplement these algorithms as new work. Preserve their distinct tie rules, including DeepSeek/Google equal-price ordering. |
| [Redis measurements](../src/Modules/Routing/Infrastructure/RedisProviderPerformanceService.cs), [configuration](../src/Modules/Routing/Infrastructure/RoutingServiceCollectionExtensions.cs) | Defaults: five-minute window, minimum ten successful samples, maximum 256 observations, 10% hysteresis; weights .25/.20/.35/.20. Mapping + stream-mode scope, no logical-operation dimension. BYOK excluded by Gateway. | Weights are deployment config, not project controls. Operation isolation and durable decision provenance are additive work. Redis measurements remain advisory. |
| [Gateway](../src/UZLLM.Gateway.Api/Inference/InferenceGateway.cs), [parser](../src/UZLLM.Gateway.Api/Inference/ChatRequestParser.cs) | Candidate eligibility, project/key controls, explicit BYOK-first Hybrid, worst-case hold, serial bounded fallback and price recheck. Only existing enumerated `uzllm` fields accepted. `RouteStrategy` is a compact finalization string, not frozen scores/window/policy evidence. | No affinity/cache request contract or detailed decision snapshot exists. A configuration preview is not a live route decision. |
| [Financial service](../src/Modules/Billing/Application/FinancialService.cs), [store](../src/Modules/Billing/Infrastructure/PostgreSqlFinancialStore.cs) | Undispatched finalization can create a unique zero-charge Released settlement and release wallet/budget holds; UsageCharge ledger entry is written only for a positive charge. Dispatched ambiguity cannot take this shortcut. | A proposed free hit can reuse this financial mechanism; do not invent provider usage, zero-value ledger entries or another wallet. Durable hit/dispatch exclusion still needs new coordination. |
| [Usage read projection](../src/Modules/Usage/Infrastructure/PostgreSqlUsageReadStore.cs), [rollup store](../src/Modules/Usage/Infrastructure/PostgreSqlUsageRollupStore.cs) | Charge comes from settlement; token fields require verified provider evidence; selected provider/model comes from attempts. Hourly rollups are rebuildable, not an admission source. | A hit must be explicitly represented with no provider attempt; do not reuse a source request's tokens/charge/latency. Cache counters need new read-model inputs. |
| [USAGE-004 evidence](21_USAGE_ROLLUPS.md), [integration tests](../tests/Integration/UZLLM.Persistence.IntegrationTests/UsageRollupIntegrationTests.cs) | Replay, competing Workers, late settlement/refund corrections and source invalidation tested. | Dependency is satisfied; this does not make analytics suitable for hot-path routing or implement cache-hit accounting. |

Existing [ranker tests](../tests/Contract/UZLLM.Gateway.ContractTests/PerformanceRouteRankerTests.cs)
cover sparse samples, hysteresis, invalid weights and error sensitivity;
[Redis integration](../tests/Integration/UZLLM.Persistence.IntegrationTests/ProviderPerformanceIntegrationTests.cs)
covers bounded shared windows and expiry. Two local clients are not OPS-006's
distinct-host qualification. No response-cache body store, affinity store or
cache-hit execution branch was found in the inspected routing/Gateway code.

Metric limitation: non-streaming `firstTokenMs` is assigned after the complete
provider response, so it is completion latency, not measured upstream TTFT.
Streaming uses Gateway-observed event arrival; effective output rate can still
reflect network/client backpressure. Preserve mode isolation and label these
observations honestly; they are not hardware/provider throughput guarantees.

Documentation corrections, not algorithm changes: Gateway §4's generic tie
description must match the actual price ranker; §15's statement that detailed
latency/throughput is still future P1 is stale after ROUTING-004. API §6's
`x-uzllm-cache` is an illustrative future header, not evidence of cache support.

## 3. Decision matrix and hard boundaries

| Situation | Proposed decision | Never do |
|---|---|---|
| No new opt-in | Existing strategies, defaults, fees and serial attempts unchanged. | Turn on affinity/content retention from an environment tag or default `auto`. |
| Explicit request strategy | Overrides strategy only; `auto` uses the effective frozen project/global weight profile. | Override key/project/operator/privacy restrictions or choose BYOK implicitly. |
| Healthy eligible mappings + fresh performance | Rank within the requested canonical model using the existing formula and hysteresis. | Substitute another model for a better score without explicit fallback consent. |
| Sparse/stale/malformed/unavailable advisory samples | Deterministic priority among still-eligible mappings; persist a bounded reason. | Reuse daily usage rollups as fresh scores, bypass circuit/quota checks, or open admission during Redis admission failure. |
| BYOK/Hybrid | Explicit BYOK remains primary; only opted-in Managed fallback can be ranked. | Rank away a selected BYOK key, charge Managed and BYOK provider usage together, or pool another tenant's metrics. |
| Affinity reference valid | Prefer the referenced candidate only within the currently authorized set and supported strategy. | Treat an affinity key as auth, provider conversation state, privacy proof or guaranteed prompt-cache savings. |
| Affinity stale/missing/ineligible/unavailable | Ordinary eligible ranking; write a new pointer only after a verified successful attempt. | Add retries, cross models, resurrect a disabled credential or relax a pin. |
| Response-cache prefer, eligible hit | After normal admission/hold, commit no-dispatch hit choice, finalize zero-charge release, then deliver fresh response identity. | Serve before DB financial confirmation, copy original provider usage or charge a fixed/percentage hit fee. |
| Cache miss/error/corruption/expiry | While still Reserved and no hit decision committed, continue ordinary serial provider execution under that same hold/deadline. | Serve stale content, retry a dispatched unknown request, or execute after a Released hit settlement. |
| Unsupported opt-in/feature conflict | Safe `400`/policy error before hold or dispatch. | Silently strip unsupported fields or convert streaming to non-streaming. |
| Provider timeout/5xx/partial output/unknown usage | Existing evidence, settlement/reconciliation and no-replay rules. | Interpret failure as zero cost or hide it behind another cached response. |

Worst-case reservation remains the maximum charge across the bounded selected
candidate plan, never the expected weighted score or an assumed provider cache
discount. Two primary-model attempts, one per explicit fallback model, at most
four attempts and one total deadline remain. No parallel hedges/speculative
execution, customer-controlled retry budget or post-output failover.

## 4. Proposed routing policy, evidence and affinity

### Versioned policy and sample provenance

Proposed Owner-only `GET/PUT /management/v1/organizations/{organizationId}/projects/{projectId}/routing-policy`
uses session auth, CSRF on mutation, expected version and atomic audit.
Missing policy returns version 0/null and inherits deployment defaults; PUT
`policy:null` resets only this new group, not existing project settings/budgets.
Stale version → `409`; other tenant → `404`; wrong role → `403`; no-store.

Illustrative **future**, not current wire:

```json
{
  "expectedVersion": 0,
  "policy": {
    "autoWeights": { "price": 2500, "error": 2000, "latency": 3500, "throughput": 2000 },
    "affinity": { "enabled": false, "ttlSeconds": 300 }
  }
}
```

Propose integer basis-point weights, each 0–10000 and sum exactly 10000,
converted to the existing decimal formula. Null weights inherit global config;
they affect only `auto`, never allocation percentages. Window/minimum samples/
hysteresis remain operator-controlled; customers cannot disable confidence,
health or money gates. Persist the effective project version or startup-config
digest. Take one immutable policy snapshot per logical request; hard policy
updates still recheck at admission/dispatch. Changing weights does not reset
budgets, grants, usage or reservations.

Version performance keys by logical operation + mapping + stream mode; initially
reuse the same five-minute/ten-success/256-sample/10%-hysteresis rules. Do not
mix embeddings into chat, BYOK into global Managed windows, or cache hits into
provider throughput. Old Redis windows are discarded by versioned keyspace,
not backfilled from hourly rollups. Missing new windows take deterministic
priority during warm-up. A separate cold model/operation is not made healthy
by a different workload's measurements. No new learned predictor is justified.
Decision metadata distinguishes `StreamFirstVisible` from `CompletionLatency`;
do not relabel non-streaming samples as actual first-token measurements or add
an unbounded buffering queue to hide slow-client effects.

Usage owns a metadata-only request decision snapshot (strategy/default source,
policy/config version, candidate-plan/price/fee references, score inputs,
sample count/window/time, hysteresis/fallback reason). Persist with admission;
append actual per-attempt selection with durable dispatch. Record all selected
plan candidates (at most four), and at most eight diagnostic alternatives with
explicit omitted count/truncation. Diagnostics cannot truncate eligibility or
change ranking. Do not claim full historical replay of omitted candidates.
Admission decision write failure is `503` before provider I/O; never put
prompts, affinity IDs, raw secrets or unbounded upstream errors in this record.

### Metadata-only affinity

Propose `uzllm.affinity_key` as a non-empty UUID, explicitly supplied per request
and allowed only after project affinity opt-in. First profile: text chat,
streaming/non-streaming, effective `priority` or `auto`. Reject combinations
with `price`, `latency`, `throughput`, Responses, embeddings or explicit BYOK/
Hybrid rather than silently changing those meanings. TTL proposed 300 seconds,
Owner-selectable 60–600; metadata only, no chat history or upstream cache handle.

Scope the Redis reference by organization/project/API-key identity, canonical
model, logical operation, stream mode and policy version; HMAC the client UUID
with a purpose-separated versioned server key. Value references mapping and
credential identity/generation, not plaintext credentials. Every read re-runs
current permissions, model capability, provider status/health/quota, privacy
and financial admission. A provider pin always wins over an incompatible hint.
Use bounded compare-and-set/NX writes after verified successful provider work;
concurrent writers may choose different eligible providers, so no single-provider
conversation guarantee. Reads never extend TTL. No affinity lease is held
across provider I/O. Redis affinity failure restores ordinary ranking only if
the independent mandatory admission/health checks still pass.

## 5. Response cache proposal — explicit retention and bounded scope

Separate gateway response reuse from provider prompt-cache usage. Provider
cached-input counters continue to be priced by the actual adapter/Catalog
snapshot; they do not imply a gateway hit or a discount on the reservation.

Propose a disabled-by-default project Owner policy plus a deployment gate,
with `GET/PUT .../response-cache-policy` under the same tenant/project prefix.
Use version/CSRF/audit/denial semantics above. Example policy is
`{ "enabled": true, "ttlSeconds": 60 }`, wrapped with `expectedVersion`;
integer TTL 1–300 seconds (default 60), no sliding expiry. Null policy disables
the feature. Disabling/changing/resetting the policy
increments an authoritative PostgreSQL epoch; cache TTL alone cannot enforce
opt-out. Owner copy must explicitly consent to retaining generated content.
This is separate from the existing payload-retention switch; neither silently
enables the other. Strict privacy prohibits incompatible cache use, including
the region-only strict profile in PRIVACY-003. Policy enablement conflicts
are denied, not silently resolved by changing privacy or payload settings.

Proposed request hint: `uzllm.cache="prefer"|"bypass"`. Omission is bypass,
even on an enabled project. `prefer` requires both gates and the supported
profile; otherwise safe `400 cache_not_enabled` / `cache_feature_unsupported`
or `403 cache_policy_conflict` before reservation. There is no cache-only,
public CDN or forced-hit mode. Existing clients remain uncached/no-store.

First eligible profile deliberately narrow: Managed-only, non-streaming
`chat.completions`, priority routing, no affinity, model fallback, tools/tool
history, vision/remote-URL content parts, structured output, refusal, hosted state or provider
passthrough. Require explicit temperature 0, exact supported parameters and
known deterministic serialization; do not promise provider determinism.
Only complete text with `finish_reason=stop`, at most 64 KiB serialized normalized
cache payload before encryption,
verified usage, completed server delivery and a financially finalized,
non-unresolved source can fill.
No caching of errors, incomplete/truncated output, streams, unknown usage or
pre-settlement results. Responses/embeddings/BYOK/Hybrid are deferred.

Key scope is organization + project + API-key identity/current authentication
fingerprint generation (opaque server metadata, never logged) + current cache epoch +
logical operation + normalized generation input + actual primary mapping,
credential generation, upstream model, route-policy/config and price/fee
versions. Normalize only known typed fields: preserve message/array/text order
and exact strings, canonicalize object-field order/numeric values, include all
effective defaults, and reject duplicate/unknown fields. Idempotency key and
fresh request IDs are not cache-content equivalence. Use versioned HMAC-SHA256
over purpose/tenant-bound serialization, not plaintext prompts/unsalted hashes
in Redis keys, telemetry, audit or URLs. Key/credential/config changes cause a
miss; no relaxation of provider pin, access or model identity on lookup.

Store only bounded normalized assistant text/finish semantics and private
origin references, encrypted with purpose- and tenant/key/epoch-bound AEAD
using versioned envelope protection. Reuse existing cryptographic patterns,
not provider credentials or an unversioned shared key. No raw request body,
upstream request ID, original response ID or billable usage copied into output.
Authenticate the full scope fingerprint and absolute expiry inside the capsule;
editing outer Redis metadata must not extend or transplant a valid entry.
Expiry is source-response completion time plus the policy TTL, not fill time;
late settlement/fill beyond that deadline is skipped. TTL limits reuse age,
not factual freshness or an external data source's correctness.
Propose an optional dedicated non-persistent cache Redis instance with strict
memory quota and no backups/AOF, not the admission/health Redis: content
eviction/load must not exhaust mandatory admission memory or create hidden
payload backups. This extra resource is justified only if cache execution is
approved; leave the feature disabled if unavailable. Bound entries to 100 per
project and overall memory initially 256 MiB; TTL/capacity eviction is a miss.
Verify region, encrypted-key distribution, persistence settings and memory
limits before activation; these are proposed defaults, not measured capacity.

## 6. Financial, replay and failure semantics — approval requested

| Case | Proposed customer/provider accounting | Required behavior |
|---|---|---|
| Ordinary request, cache bypass/miss | Existing actual verified provider cost + current frozen fee policy. | Existing holds, price rechecks, evidence and exactly-once settlement; native cached input remains its subset, not double-counted. |
| Valid gateway hit | **Zero provider cost and zero customer fee for this logical request**; no provider tokens generated. | Still pass full normal admission and acquire the ordinary worst-case wallet/project/key hold first. Reuse undispatched zero-charge Released settlement; no UsageCharge ledger entry or copied source charge. |
| Fixed request fee / percentage markup configured | Zero on a gateway hit; ordinary pricing on a miss. | A hit never charges the source request's fixed fee again. A different cache fee requires a separately approved pricing contract/task. |
| Insufficient wallet/budget, recovery debt hold, operator pause | Deny even if content might exist. | Cache is not an admission bypass. Temporary holds mean a cached request still needs normal funding/headroom and concurrent hits can be denied. Explicit conservative trade-off. |
| Reversal/debt created after admission | Preserve the existing hold and recovery-debt rules. | Releasing a hit's hold may repay existing debt through the normal compensating ledger path; zero cache charge does not promise an unchanged wallet balance. |
| Duplicate client key | Existing `409` with original request identity, including hit requests. | No response-body idempotency replay or second hold/execution; mismatched body remains conflict. Cache and idempotency are different mechanisms. |
| Dispatched upstream unknown/settlement failure | Existing pending evidence/recovery/exposure semantics. | No cache fill, free-hit substitution or second execution to make the request look successful. |
| Cache write failure after known settlement | Original charge unchanged; skip fill. | Cache outage cannot roll back an upstream charge or turn it into a refund. |
| Hit delivery fails/client disconnects | No charge; retain delivery outcome and release confirmation. | Never reinterpret cached text as provider execution or execute again after releasing its reservation. |

Proposed hit lifecycle, deliberately retaining the existing finance machinery:

1. Authenticate and validate the supported opt-in; perform tenant/key/project/
   capability/credential/operator/health/quota and distributed admission checks.
   Build the normal primary/eligible plan and reserve its maximum through TX-02.
   Cache lookup happens **after** admission; no PG transaction spans Redis I/O.
2. Read/decrypt/validate one entry within a proposed 25 ms configurable lookup
   budget, bounded by the existing total deadline. Miss/timeout/corrupt/expired
   entry continues ordinary execution only while Reserved, before any hit choice.
   A separate cache Redis fault is a miss; the mandatory Redis/DB outages still
   fail closed. Ignore late results after a timed-out/cancelled lookup; they
   cannot choose a hit after the miss path has started. No lookup retry amplification.
3. Commit a unique metadata-only `HitChosen` decision against the logical
   request under its request lock; require a still-Reserved reservation, live
   deadline, no provider attempts, current key/project/origin eligibility,
   project epoch/privacy/policy and matching origin versions. Compare the
   authenticated entry expiry to PostgreSQL UTC time, not Redis TTL alone, so
   cache/node clock skew cannot authorize expired content. Preserve the
   logical request's frozen fee snapshot; do not silently reprice it. The same
   request fence must prevent attempt creation/MarkDispatched after HitChosen
   and prevent choosing a hit after attempt creation. Use project-policy anchor locking
   consistent with PRIVACY-003; deterministic lock order, no lock across I/O.
4. Call existing `ReleaseUndispatchedAsync` and require the unique Released,
   zero-charge, non-unresolved settlement. Resolve a lost commit acknowledgement
   by reading that identity; until confirmed, emit no success/content and never
   fall through to provider execution. Do not modify ledger rows directly.
5. Record request execution source `GatewayCache`, hit origin/version and
   delivery status through Usage, never fabricated provider `UsageEvidence`.
   Render a new request/completion ID/time, actual canonical model and
   `usage:null` (no newly observed provider tokens). Proposed `X-Uzllm-Cache: hit`
   identifies reuse; omit executed-provider headers rather than impersonating
   a new upstream call. Origin is separate authorized metadata, not an attempt.
   Keep HTTP no-store; this is not browser/CDN caching.
6. Crash before HitChosen is an ordinary undispatched reservation; crash after
   HitChosen must never dispatch. Recovery reuses idempotent undispatched release
   and records abandoned/undelivered state, without regenerating content or
   charging. A released hit cannot turn into a paid miss. Do not rely on cache
   bytes surviving for financial recovery. All zero-charge settlement/outbox,
   holds and delivery transitions remain independently retryable and observable.

Fill only after ordinary source usage and settlement are committed. Recheck
enabled epoch/privacy before Redis publication, use absolute expiry and bounded
NX writes: first valid fill wins until expiry, no extension by later fills or
reads. A failed/late publication is skipped. A stale fill cannot become
reachable under a newer epoch. Opt-out blocks **new HitChosen decisions** at its
DB commit and schedules bounded old-entry deletion; unavailable cache storage
may defer physical removal until TTL/repair. Already authorized/in-flight RAM
delivery may finish; no retroactive erasure guarantee. Existing legal/audit
retention and backup rules remain. Start a restored cache empty; do not restore
body snapshots or revive prior opt-ins automatically after quarantine.

No request coalescing, background refresh or stale-while-revalidate in this
profile. Concurrent independent misses may each execute and charge normally;
ordinary admission/concurrency caps contain stampedes. Never share another
request's reservation or hold a distributed cache lock across provider I/O.
Cache reuse is a latency/cost optimization, not an outage bypass: no eligible
normal route means no cache admission even if an old body exists.

## 7. Persistence, reads and ownership

Use existing schemas/context and explicit future Migrator changes only:

| Owner / proposed data | Constraints / responsibilities |
|---|---|
| Projects: advanced routing policy and cache policy/head/epoch | Tenant-composite project FK; unique project/version; project-row serialization for create/update/opt-out; actor/reason and audit in one transaction. No duplicate financial limits. |
| Usage: routing decision and bounded candidate/attempt explanation | One immutable request decision; tenant/request and attempt FKs; reference policy/config/price versions, normalized scores and reason codes. No content; cap diagnostic cardinality. |
| Usage: cache decision/origin/delivery metadata | Unique request ID; tenant/project/key-bound references; state/epoch/expiry indexes for recovery. Mutually exclusive with provider dispatch at the logical request fence. Body bytes only in encrypted TTL cache, never this table. |
| Usage read models | Add logical cache-hit/miss/bypass/reason counts, zero settled charge, no provider attempt/token fabrication. Keep source provider latency/TTFT/throughput and cache lookup/delivery latency separate. Extend invalidation triggers for decision changes. |

Routing Domain owns pure weight/scoring/affinity applicability rules;
Application composes eligible inputs through contracts. Projects owns Owner
policy use cases. Routing Infrastructure owns advisory Redis references;
Routing Domain/Contracts also own response-cache applicability and scoped-entry
contracts; its `Infrastructure/ResponseCache` adapter owns purpose-bound
encryption/TTL/body access. No new cache microservice/module assembly is required.
Gateway owns bounded HTTP parsing, orchestrated fences and response delivery;
Billing alone owns reservation/finalization. Usage/Worker own metadata recovery,
rollups, expiry/deletion work and diagnostics. No controller billing rules or
financial mutation inside provider adapters/cache stores. Preserve frozen
architecture-test seams; use explicit contracts rather than cross-module SQL
or SDK types in policy code.

USAGE-004 remains replacement-based, replay-safe hourly analytics with live
edges and explicit `dataAsOf`/pending backlog. Add cache dimensions/counters
without moving financial truth into rollups. A hit counts once as a logical
request, zero new provider attempts/tokens and its zero settled charge; token
fields in Activity stay null when there is no provider evidence. Cache-origin
model/provider is a separate field, not a selected upstream attempt. Explain
pending finalization versus true zero charge; never convert null/unknown to 0.
Export/Analytics must use those same definitions. Do not report “saved USD” from
an unexecuted counterfactual as realized spend or a wallet credit.
Group a hit by its new logical request's UTC hour, not its source response's
hour; late hit finalization rebuilds that new request's original hour.

Backend API/data precede UI. Future Owner routing/cache controls and Activity
decision panels use tenant-safe read permission, no-store and generated client
contracts; ordinary developer/read-only roles cannot enable retained content.
Show sample freshness, priority fallback, actual versus origin provider, hit
charge 0 and required admission headroom. A preview is not a health/price
guarantee. Follow the preserved mockup's visual direction with improved
localized accessibility; no new browser inference/playground or secrets.

## 8. Mandatory designed scenarios — not executed feature tests

| ID | Observable acceptance / required test |
|---|---|
| RTE-001 | Preserve price/score/hysteresis ties, including DeepSeek versus Google; invalid basis-point sums and stale project versions denied; pure + HTTP/PG tests. |
| RTE-002 | Empty/sparse/stale/future/malformed windows and advisory Redis errors restore eligible priority; no cold-operation/BYOK/cache sample contamination; fake-clock + Redis tests. |
| RTE-003 | Key/project/provider/model/privacy/operator restrictions and Hybrid BYOK-first order survive all weights/hints; denied admission has no hold or dispatch; Gateway/PG tests. |
| RTE-004 | Changed price/policy/key/credential during ranking cannot use stale admission; durable decision snapshots reflect effective source and bounded alternatives; PG concurrency/rollback tests. |
| RTE-005 | Two-node affinity races, expiry, revocation, bad pin/scope and metadata Redis loss choose only eligible mappings; no prompt storage or provider-state promise; Redis/Gateway tests. |
| RTE-006 | Worst-case holds and four-attempt/deadline bounds survive scoring/affinity/fallback; no replay after timeout/partial SSE/cancel; Gateway/financial/SSE tests. |
| CCH-001 | No opt-in means no lookup/fill; strict/privacy or unsupported profile is denied before hold; separate cache and payload controls tested with Owner/CSRF/tenant isolation. |
| CCH-002 | Equivalent typed request serialization hits only the exact tenant/project/key/epoch/model/mapping/generation/price/fee scope; changed text/parameters or duplicate fields cannot collide; crypto/contract fixtures. |
| CCH-003 | Free hit under nonzero fixed/percentage fee releases the full real hold, creates one zero-charge Released settlement, no UsageCharge ledger entry, attempts or copied provider usage; real PG replay/concurrency. |
| CCH-004 | 50 concurrent requests cannot overspend or bypass exhausted project/key budgets, debt, disabled key or operator pause, even on hits; post-admission reversal preserves holds and normal debt recovery; PG/Redis admission tests. |
| CCH-005 | HitChosen versus attempt creation/MarkDispatched/expired-hold reconciliation is fenced; crash before/after hit choice, zero settlement and delivery yields safe release and no paid fallback/re-execution; PG/Worker fault tests. |
| CCH-006 | Duplicate key/body conflicts retain original request ID without another hit/hold; response uses fresh completion identity and null usage; native-compatible client/wire fixtures. |
| CCH-007 | Only completed verified financially final primary text fills; unknown/partial/refusal/oversize/fallback/settlement failures never fill; failed cache write never changes posted source money. |
| CCH-008 | TTL/epoch/revoke/opt-out, late fill, malformed AEAD, cache timeout/eviction/outage and restored empty cache behave as specified; no stale serve or resurrection; Redis/PG race and restore fixtures. |
| CCH-009 | Distinctive synthetic prompt/output absent from ordinary keys/logs/trace/audit/exports; encrypted cache bounded by tenant/purpose and region/retention controls; content-sink/security inspection. |
| CCH-010 | Late hit-state publication, competing Workers and rollup replay count one request/zero attempts/charge 0, no invented tokens or savings; analytics/export/browser and PG tests. |

Positive tests use synthetic accounts/entries. Existing tests and the baseline
below do not implement RTE/CCH scenarios or qualify a real cache deployment.

## 9. Benchmark and activation plan — designed, not performed

Use the repository [load harness](../deploy/OPS-006-VALIDATION.md) and pinned
Gateway/Management/Worker/Migrator/edge/config revisions. Compare the same
deterministic providers/prices/input-output distributions with bypass baseline,
each strategy, cold/warm/sparse windows, affinity hit/miss, cache cold/warm and
eviction, cache-disabled project and mixed tenants. Include sequential identical
prompts **and** unique prompts; report actual hit rates, not a manufactured
100%-hit headline. Concurrent duplicate client keys and independent keys are
different scenarios. Check text-only cache separately from live incremental SSE.

Repository qualification: isolated repeatable fixtures; synthetic upstream
latency/errors/token counters; request-ID/ledger/hold conservation; distinct
cache outage versus admission Redis outage; PG failures; failed settlement,
opt-out/epoch races; crash between every hit-state boundary; replay/restore.
Measure p50/p95/p99 gateway overhead, TTFT, provider completion and client
delivery separately, cache lookup/decrypt/fill latency, hit/miss/error reason,
CPU/memory, DB queries/locks, Redis operations/evictions, sample age and recovery
backlog. Never add body/tenant/key IDs as metric labels.

Production-like gate: two Gateway instances on **different hosts**, TLS edge,
at least 10 RPS for 60 seconds and 200 simultaneous uncached streams, then
mixed/cache-churn profiles under declared quotas. Retain NFR-010 overhead targets
(p50 <25 ms, p95 <100 ms, p99 <250 ms excluding provider time), NFR-011 immediate
forwarding, zero financial over-admission/duplicate charges/cross-tenant hits,
and eventual reservation conservation after faults. Report deterministic local
fixtures versus real-provider and commercial/region evidence separately.
Do not count cache responses as provider success, provider speed or 200 streams.

Rollback: deployment gates off; disable project cache/advance epoch, expire/delete
body entries, preserve decryptors through maximum TTL and all metadata/ledger
history; normal uncached routing resumes only where ordinary gates pass.
Unknown upstream work still reconciles. Do not roll back populated immutable
decision/financial evidence destructively. Public/paid activation additionally
requires existing OPS-006/007/LAUNCH-003 and external business gates. No result
from this specification, local Docker pair or green CI substitutes for them.

## 10. Proposed decomposition — unscheduled, approval required

IDs below are designs, not active register entries. Preserve all historical
ROUTING-003–006 work. Specification approval does not promote feature execution.
Dependencies on unbuilt privacy contracts require their separate promotion;
do not silently implement another task to satisfy them.

| Proposed P2 task | Objective / likely files | Dependencies / references | Acceptance, required tests and completion |
|---|---|---|---|
| ROUTING-008 | Persist bounded structured route decision/attempt evidence; Routing/Usage/Persistence/Gateway/Migrator. | Approved ROUTING-007; DX-002, USAGE-004; FR-068, §4/7. | Immutable tenant-bound plan/score/source references; admission/dispatch auditability, omitted count and rollback; RTE-003/004 PG/wire tests and explicit migration evidence. No new ranking behavior. |
| ROUTING-009 | Version project auto weights with Owner API and precedence; Projects/Routing/Management. | ROUTING-008, PROJECTS-002; FR-013/063–065, §4. | Basis-point validation/inheritance/version/audit; preserve hard controls and existing ties; RTE-001/003/004 HTTP/PG plus generated-contract parity. |
| ROUTING-010 | Isolate operation-scoped performance windows and explain priority fallback; Routing/Gateway. | ROUTING-008; FR-064/065/067, §4. | Bounded cold-window migration, sparse/stale/corrupt dependency behavior and no BYOK/hit pollution; RTE-002/006 fake-clock/Redis/Gateway tests. |
| ROUTING-011 | Add scoped metadata-only affinity; Routing/Gateway/Projects. | ROUTING-009/010; §4, FR-060–068. | HMAC UUID scope, TTL/CAS, explicit supported combinations, fresh eligibility and no additional attempts; RTE-003/005/006 fixtures, contracts and tenant checks. |
| CACHE-001 | Add Owner opt-in/epoch and bounded encrypted TTL store; Projects/Management/cache Infrastructure/Persistence/deploy. | Approved ROUTING-007; PRIVACY-006 separately approved/promoted; §5, NFR-050–053. | Separate retention consent, disabled gates, qualified dedicated cache options, purpose-bound crypto/capacity/epoch; CCH-001/002/008/009. No cache serving yet; migration/config/secret guidance required. |
| CACHE-002 | Coordinate hit choice, dispatch exclusion and existing zero-charge release; Gateway/Usage/Billing contracts/Worker. | CACHE-001, ROUTING-008, BILLING-003; §6, money invariants. | Exactly-once zero settlement, no fake attempts/usage/ledger, no paid fallback after hit choice, crash recovery and replay; CCH-003–006 PG/financial/fault tests. Uses synthetic preloaded entries; feature remains gated. |
| CACHE-003 | Fill only verified financially final text and enforce stale-fill/opt-out deletion; Gateway/cache Infrastructure/Worker. | CACHE-002; §5/6. | Primary completed text bounds, no pre-settlement/unknown fill, immutable epoch and bounded cleanup; CCH-007–009 Redis/PG fault tests, no posted-charge changes. |
| USAGE-006 | Add cache outcome/decision reads, rollup/export definitions and generated contracts; Usage/Management/Worker/docs/api. | CACHE-002/003, ROUTING-008, USAGE-004/005; §7, FR-068/095/096. | Null provider tokens versus known zero charge, once-only logical counts, authorized origin/decision and replay/late updates; CCH-010/RTE-004 PG/export/schema tests. |
| FRONTEND-012 | Owner routing/cache controls and Activity explanations; dashboard/admin/api-client. | ROUTING-009/011, CACHE-003, USAGE-006; §7, ADR-016. | Real backend first, truthful headroom/retention/preview/freshness, improved mockup-aligned localized a11y UX; browser/security/API acceptance and screenshots. No browser-secret inference. |
| OPS-008 | Qualify advanced routing/cache performance, failure and rollback; deploy/tests/Load/runbooks. | ROUTING-010/011, CACHE-003, USAGE-006, OPS-006; §9, NFR-010/011/030–035/066. | Dated local reports and distinct-host feature qualification, RTE/CCH faults, conservation, content-sink/memory and restore/rollback checks. Unavailable staging evidence means Blocked, not completion from local clients. No paid activation on absent external evidence. |

Recommended order follows rows; ROUTING-009/010 and the separately authorized
cache foundation can progress independently once their dependencies exist.
No new task is started or promoted by this document.

## 11. Validation and review handoff

Documentation-only task: no runtime/API artifacts, frontend, migration,
configuration, secret or live infrastructure changes. The baseline validates
existing routing/financial/usage behavior, not the proposed features.

Fresh local validation on 2026-10-18, Windows/Docker disposable PostgreSQL 17
and Redis 8, unchanged runtime sources at `ae52e2e`:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0; existing ASPIRE010 CLI-bundle warning, zero errors. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/routing-007-20261018` | PASS, exit 0. Passed: 1028, Failed: 0, Skipped: 0 across 16 assemblies; includes 234 PostgreSQL integration cases (15m53s). All sixteen reports aggregated, zero aborted. |
| Static documentation checks | PASS: 104 local link targets/six linked anchors; valid illustrative JSON/fences and weights summing to 10000; 16 unique future scenarios and ten unused task IDs with dependency-safe internal order. Unbuilt privacy and staging dependencies remain explicit; no feature task promoted. Other backlog/history rows unchanged. |
| `git diff --check` / staged whitespace and scope review | PASS; exactly nine documentation paths, no implementation or generated-contract changes. |

TRX files stay in the ignored local artifacts directory, not Git. RTE-001–006
and CCH-001–010 are designed-only acceptance scenarios, not passing new feature
tests. No frontend/browser feature, advanced-cache benchmark, live provider/
region qualification, hit-fee activation or production gate pass is claimed.

Approval requested: accept the bounded profile, versioned weights/operation
isolation and metadata affinity, plus the conservative **free hit after normal
worst-case financial admission**, explicit short-lived encrypted cache retention
on an optional isolated resource, ten-task decomposition and benchmark plan;
or specify changes. Approval completes the specification, not implementation,
new pricing activation or launch readiness. Stop before another task.
