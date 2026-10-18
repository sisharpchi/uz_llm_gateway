# Custom Provider Endpoints — Trust, Authentication and Accounting Specification

## 1. Status and bounded proposal

**PROVIDER-010 · P2 · Draft for approval · 2026-10-18.** User Continue selects
this independent specification after ROUTING-007; SECURITY-006 is complete.
OPS-006 remains externally blocked. This document designs the threat model,
wire/financial boundaries and synthetic fixtures; it implements no endpoint,
adapter, migration, UI or live verification. Feature tasks below are unscheduled.

Authority: FR-050–056 for adapters/keys; NFR-041–046/050–053/060–066 for secrets,
authorization, privacy and failure; ADR-008/009/010/011/013/025 for key security,
versioned pricing, adapter isolation, retention, execution modes and operator authorization.
Preserve [project restrictions](25_PROJECT_DEFAULTS.md), [BYOK accounting](09_BILLING_WALLET_AND_PAYMENTS.md#6-managed-vs-byok-billing),
the [approved privacy design](39_PRIVACY_ELIGIBILITY_SPEC.md) and
[routing/cache design](40_ADVANCED_ROUTING_CACHE_SPEC.md). Their approval does
not mean their future enforcement is implemented.

Proposed first profile: **operator-approved, organization-private, public-HTTPS,
Bearer-authenticated OpenAI-compatible text chat, explicitly selected BYOK
only**. Start with non-streaming; SSE is a separately gated follow-on task.
Custom protocol compatibility is not OpenAI identity or commercial approval.
No public self-service URL entry, platform credential use, Managed resale,
Hybrid/managed fallback, cross-model fallback, automatic discovery/import,
private/VPN addresses, OAuth/service accounts, arbitrary headers, mTLS,
tools/vision/structured output/reasoning/cached-input pricing, Responses,
embeddings or gateway response cache in this profile. Do not silently strip an
unsupported field, change execution mode or send content to an official provider.

## 2. Verified repository boundary

Inspected clean `main` at `60fcd85`; these are existing implementation facts:

| Evidence | Actual state / implication |
|---|---|
| [Catalog contracts](../src/Modules/Catalog/Contracts/CatalogContracts.cs), [store](../src/Modules/Catalog/Infrastructure/PostgreSqlCatalogStore.cs) | ProviderModel has nullable EndpointReference. It is persisted metadata, not a resolved tenant endpoint contract. Provider/model catalog is not an organization-private endpoint registry. |
| [Adapter selection](../src/UZLLM.Gateway.Api/Inference/ProviderAdapterSelector.cs), [OpenAI registration](../src/ProviderAdapters/UZLLM.Provider.OpenAI/OpenAiServiceCollectionExtensions.cs) | Selection is by provider code. BaseUrl is operator deployment configuration, not mapping EndpointReference. HTTPS/path/userinfo/query/fragment validation is not DNS/public-address or connect-time SSRF qualification. |
| [OpenAI adapter](../src/ProviderAdapters/UZLLM.Provider.OpenAI/OpenAiChatAdapter.cs), [classifier](../src/ProviderAdapters/UZLLM.Provider.OpenAI/OpenAiErrorClassifier.cs) | Normalized chat/usage/SSE exist. Typed HttpClient uses deployment base URL; registration does not explicitly disable redirects, cookies or system proxy. Official classifier permits fallback on certain 429 responses; do not transfer that trust to arbitrary endpoints. |
| [BYOK service](../src/Modules/Providers/Application/ByokCredentialService.cs), [verifier](../src/Modules/Providers/Infrastructure/FixedEndpointByokVerifier.cs), [DI](../src/Modules/Providers/Infrastructure/ProviderServiceCollectionExtensions.cs) | Only openai/anthropic/google/deepseek identities accepted. Verification uses fixed official model-list URLs, no redirects/cookies, five-second total deadline. No customer URL option. A Valid test is not model/quota/invoice proof. |
| [Secret protection](../src/Modules/Providers/Infrastructure/ProviderEnvelopeSecretProtector.cs), [rotation](../src/Modules/Providers/Infrastructure/ProviderCredentialKeyRotation.cs), [rotation evidence](../deploy/SECURITY-006-VALIDATION.md) | Versioned envelope AEAD binds organization/credential/provider for BYOK; old KEKs survive cutover/rollback. Rotation exists, not endpoint binding. Preserve its format and historical readability. |
| [Gateway scope/credential reads](../src/UZLLM.Gateway.Api/Inference/GatewayReadStore.cs), [execution](../src/UZLLM.Gateway.Api/Inference/InferenceGateway.cs), [cost bounds](../src/UZLLM.Gateway.Api/Inference/GatewayCostEstimator.cs) | Explicit granted BYOK, wallet-fee/external holds and durable attempts exist. No custom endpoint identity/revision/tenant-catalog enforcement. Existing two-candidate Hybrid behavior is not approval for custom fallback. |
| [Webhook network policy](../src/Modules/Notifications/Infrastructure/OutboundWebhookSecurity.cs), [sender registration](../src/Modules/Notifications/Infrastructure/OutboundWebhookDelivery.cs) | Existing public-DNS checks, connect-time pinning and disabled redirects/proxy offer an analogous pattern, not a provider transport. Do not depend on Notifications Infrastructure or copy its address list as permanent comprehensive security proof. |
| [Verifier fixtures](../tests/Contract/UZLLM.Provider.OpenAI.ContractTests/ByokVerifierContractTests.cs), [crypto fixtures](../tests/Contract/UZLLM.Provider.OpenAI.ContractTests/ProviderCredentialSecurityTests.cs), [rotation integration](../tests/Integration/UZLLM.Persistence.IntegrationTests/ProviderSecretRotationIntegrationTests.cs) | Credible fixed-endpoint, tenant/AEAD and cutover dependency evidence; not custom endpoint tests. |

The global BaseUrl override is a trusted deployment/test seam (Aspire injects a
local fixture), **not safe authorization for an untrusted tenant URL**. Keep it
outside customer APIs. Before production, inventory actual official base URLs,
redirect/proxy behavior and egress policy; record any gap in the existing
Security/Provider gate. This spec neither hardens those handlers nor asserts
an exploitable public URL input where none was found.

## 3. Trust model and endpoint identity

Tenant Owner requests onboarding through an operator process; no network call
occurs merely because a URL was submitted. Operator with recent MFA approves
the exact organization, DNS host, fixed protocol profile, models, rates and
evidence reference. Approval alone cannot activate execution.

Create a distinct private catalog provider ID/code `custom_<endpoint UUID N>`
for each endpoint; never reuse official `openai` identity or another tenant's
provider. Protocol profile (`OpenAiTextV1`) is separate from provider identity.
One immutable endpoint ID binds organization, provider ID, HTTPS origin and
`/v1/` base path. Host/path/owner changes require a **new endpoint and freshly
supplied credential**; never automatically copy/resolve an existing official
or differently bound key for a new origin. Syntax cannot prove which supplier
issued a manually pasted secret; Owner consent must identify the approved host
and require a credential specifically issued for that supplier.
EndpointReference becomes an opaque registry ID for this profile, never a
per-request URL. Legacy official null/string references remain unchanged.

Endpoint states: Pending → Approved; Approved → Suspended or Revoked. Only an
Approved endpoint with a currently Qualified binding plus deployment gate,
current project grant, active credential, priced private mapping and compatible
request can execute. Qualified/Expired are derived binding eligibility, not
blanket mutable endpoint states. No automatic reactivation after test success,
restore or circuit recovery.
Suspend may return to Approved only through audited operator approval plus new
qualification; Revoked never returns. Approve/Suspend/Revoke advance head version;
qualification records are per model/credential/operation, so one expired model
does not fabricate qualification for another. Qualified is the effective state
of the requested binding, not a blanket permission for every model on the host.
Append immutable policy/qualification revisions; a serialized head supplies
status/version. Qualification binds endpoint/profile/model/credential generation
and approved price version, has explicit evidence/expiry (maximum 30 days), and
requires requalification on those changes. Safe credential probe alone cannot
qualify billing or enable routing. Network checks still run on every connection.

Future custom credential generation starts at 1 and increments on secret
replacement; it is not UpdatedAt, name change or KEK KeyVersion. Envelope
re-encryption under SECURITY-006 preserves the generation and qualification
scope. Never invalidate all supplier evidence merely because KEKs are rewrapped;
never let an actual replaced key inherit the prior generation's qualification.
These generation fields/binding constraints do not exist today and belong in
the proposed explicit persistence/credential tasks, with protected downgrade.

Private canonical model aliases use reserved `custom-<endpoint UUID N>-<alias>`
namespace, not public model codes or unverified claims of identical weights.
Alias suffix is bounded lowercase ASCII `[a-z0-9-]` (1–64); upstream model code
is operator-approved bounded data, never an absolute/relative URL. Tenant-aware
Catalog/Gateway/model discovery and project/key allowlists must enforce those
identities before activation. No fallback alias to a public model.

## 4. SSRF, credential and cost threat model

| Threat / entry point | Required control / fail behavior | Fixture |
|---|---|---|
| URL parser confusion, userinfo, encoded path, IP spelling | Accept a DNS host field only; construct fixed HTTPS:443 `/v1/` server-side. Reject literal/alternate IP, whitespace, percent escapes, Unicode, trailing dot, wildcard, query, fragment, slash/backslash and control characters in host. | CPE-001/002 |
| DNS rebinding, mixed A/AAAA, CNAME to internal service | Validate complete bounded answer set at new connection, reject any non-public address, connect to the inspected IP without another DNS lookup; ordinary TLS still verifies original host. | CPE-003/004 |
| Redirect, proxy, alternate authority or protocol bypass | No redirects, proxy/env proxy, cookies, automatic credentials, HTTP/3/Alt-Svc upgrade or dynamic Host header. HTTP/1.1 exact-version initial profile; no automatic retry. | CPE-005/006 |
| Credential exfiltration/rebinding across tenants/endpoints | Immutable organization/provider/endpoint relation, explicit project grants, current credential generation; no platform resolution. Existing provider-bound AEAD plus immutable unique provider identity prevents ciphertext transplant. | CPE-007/008 |
| Owner self-approval, stale approval or concurrent revoke | Owner cannot qualify endpoint or set billing rates; operator recent-MFA/CSRF/reason; revision and dispatch fences, atomic audit. | CPE-009/010 |
| Fake usage, forged 429/pre-execution claims, overbilling | Supplier is not an accounting oracle. Bounded validated evidence, frozen operator price/fee, no automatic retries/fallback after dispatch, explicit unresolved state on malformed/missing usage. | CPE-011–014 |
| Request multiplication, stuck probe/SSE, huge response | Existing distributed request/concurrency caps plus endpoint probe limiter, total deadline, decompressed-body/frame limits and bounded handlers/connections. No background inference probe. | CPE-015/016 |
| Prompt/key/URL leakage through logs, metrics, error text | Suppress HTTP loggers/body capture; sanitize request IDs; metadata-only audit/read DTOs; no endpoint hostname or key/tenant IDs as metric labels. | CPE-017/018 |
| Stale configuration after restore/rotation or dependency loss | Restored custom endpoints quarantined; old decryptors retained; no eligibility on unavailable PG/mandatory Redis or unknown policy revision. | CPE-019/020 |

### Network execution profile

Exact approved ASCII DNS name only (labels 1–63, total ≤253; at least two
labels). Reject IP literals, localhost/internal special names, official provider
hosts and any deployment-owned/internal DNS suffix. Do not use suffix matches
such as `endsWith("trusted.example")` as identity. Public DNS alone does not
prove trust: operator evidence must identify supplier/domain control and data
terms. Initial port 443 and exact `/v1/models`, `/v1/chat/completions` only;
upstream model identifiers belong in JSON, never path concatenation.

Before each new connection resolve all A/AAAA answers through the configured
resolver, with cancellation and a two-second connect/DNS budget within the
logical deadline. Cap at 16 addresses; empty/truncated/oversized/mixed results
deny. Reject loopback, unspecified, private, link-local/cloud metadata, CGNAT,
multicast, reserved/documentation/benchmark and organization-owned service
ranges in both families. Normalize IPv4-mapped IPv6; deny transition/NAT64
addresses unless a later explicit policy supports them. Maintain the address
classification against dated special-purpose registries and local routes;
never assert a small RFC1918 list covers all forbidden destinations.

Pin the actual socket to one vetted address; preserve original authority/SNI
and normal certificate chain/hostname checks. No permissive certificate callback,
custom CA upload or loopback exception in production. DNS failure is denial,
not a retry via OS default DNS/proxy. Initial HTTP/1.1 profile uses reusable
bounded handlers but **no reusable connections** (zero pooled lifetime); each
logical attempt has one connect budget and non-replayable request content.
Refuse a second body serialization/socket attempt, including framework retries.
Pooling is a later measured optimization requiring equivalent no-duplicate-send
fixtures, not an assumption that one SendAsync means one upstream execution.
Handler scope is immutable endpoint + policy revision; retire it on change
without canceling financial recovery or moving a stream to another peer.

Use the same dedicated transport policy for probes, JSON and future SSE;
`SocketsHttpHandler.ConnectCallback` is a candidate implementation seam, not
a complete SSRF control. No implicit transport retry on failed writes:
connection failure after the durable dispatch fence is conservatively Unknown.
Network egress rules also deny internal/control-plane destinations. If the
runtime/hosting network cannot uphold pinning/TLS/egress, leave custom gates off.
Synthetic tests inject resolver/socket seams; do not weaken production checks
to make loopback Testcontainers endpoints eligible.

## 5. Proposed management and inference contracts

These are **future** contracts, not changes to current OpenAPI/client artifacts.

| Proposed operation | Authorization / result |
|---|---|
| `GET /management/v1/organizations/{organizationId}/provider-endpoints` and `/{endpointId}` | Owner only; tenant-safe metadata, versions, status, profile, approved host, expiry/reason codes, never secret/ciphertext/raw evidence. Other-tenant ID →404; absent session →401; wrong role →403; no-store. |
| `POST /management/v1/admin/organizations/{organizationId}/provider-endpoints` | Recent-MFA operator + CSRF/reason + scoped idempotency key; immutable host/provider/owner identity and Pending state, no network/secret. Replay same normalized body returns original; mismatch →409. |
| `POST .../provider-endpoints/{endpointId}/approve`, `/qualify`, `/suspend`, `/revoke` under the admin prefix | Expected revision; recent-MFA/CSRF/reason, append audit in same transaction. Qualify records referenced synthetic/live evidence and expiry, not an arbitrary URL fetch. Revoke does not delete financial history. |
| Existing tenant `POST .../provider-keys`, `PATCH .../{id}`, grants/restrictions/test | Separate feature extends allowed custom provider IDs only for owning Owner and currently approved endpoint. Same secret validation/masking; no baseUrl, authHeader, cookie or passthrough fields. Official provider semantics unchanged. |

Example **future operator** create request (all values synthetic):

```json
{
  "name": "Example tenant inference",
  "host": "llm.supplier.example",
  "protocolProfile": "OpenAiTextV1",
  "reason": "Approved onboarding reference CUSTOM-DEMO-001"
}
```

`.example` is documentation data, not a resolvable/approved deployment. Reject
unknown fields/duplicate JSON properties; name 2–120 characters, reason 1–500
non-control characters, bounded body 16 KiB for these metadata endpoints.
Existing secret-input requests retain their current limits, not this example's
shape. Reads never return onboarding evidence URLs or automatically dereference
them; use opaque private evidence references. Approval/qualification controls
must not be exposed as Owner self-certification.

Create returns 201 with opaque endpoint/provider IDs, organization, version 1,
Pending status, profile and host; no credential. Lists are cursor-paged with a
maximum 100 items. Transition bodies carry `expectedVersion` and `reason`;
qualification additionally references credential generation, model/price
version, operation (`ChatJson` or later `ChatSse`), private evidence ID and
`expiresAt`. Missing/stale references or expected version →409; invalid state/
expiry →400. Revoke is terminal; re-onboarding requires a new identity. Mutation
response is the same safe metadata resource; no network call during transition.

Inference reuses `model` plus explicit `uzllm.provider_key_id`; no raw URL or
per-request endpoint/auth object. Omission remains existing Managed behavior,
which **cannot** select a custom private mapping. Initial custom requests require
priority, no fallbacks and `allow_managed_fallback:false`/omitted; reject a
project default that resolves to an unsupported strategy unless request explicitly
overrides it to priority. Preserve all hard key/project/provider/model controls.
An unrelated credential cannot authorize a private model alias.

Safe proposed denials: unsupported profile/extension →400
`custom_provider_unsupported`; inactive/unqualified/expired/revoked binding →403
`custom_provider_unavailable`; dependency/network-policy unavailable →503
`custom_provider_unavailable`; malformed upstream →502 normalized error.
Do not disclose other tenants, resolved addresses, DNS/certificate internals,
secrets or raw upstream messages. Existing clients retain existing errors.
In SSE after headers, use the existing error-event envelope and terminate,
never change HTTP status or restart output.

### Credential verification

Only explicitly requested, active, Owner-authorized custom credential tests
may issue `GET /v1/models`; one request, five-second total deadline, no retry,
no prompt/inference, no response body retention and ≤16 KiB error read if needed.
Reuse shared admission dependency failure semantics; dedicated limits initially
5/minute per organization, one concurrent probe per endpoint, with bounded
operator ceilings and lease cleanup. A body-free 2xx means CredentialAccepted,
not trusted supplier/model/usage/price qualification. 401/403 means Invalid;
redirect, malformed/unavailable/network/timeout means Unavailable. Return current
safe Valid/Invalid/Unavailable DTO only; never auto-qualify or reset inference
health. Recheck current endpoint revision/credential generation after I/O before
committing result/audit. Rotate/disable/revoke races discard stale result (409).

## 6. Provider contract and accounting plan

Use normalized [provider contracts](../src/Modules/Providers/Contracts/ProviderContracts.cs),
not SDK objects outside adapters. New `UZLLM.Provider.OpenAICompatible` adapter
is distinct from official OpenAI identity/classifier. Reuse wire mapping only
through a narrowly factored, tested adapter-local component if needed; do not
reference another adapter's internal implementation or widen architecture seams.
Selector resolves protocol profile separately from private provider code.

Carry an opaque tenant-bound endpoint/revision/profile reference in execution
context, not caller-controlled Uri. Providers resolves approved execution binding
and exact credential; infrastructure chooses transport. Bearer value is set only
on that request, never a shared DefaultRequestHeaders. No gateway Authorization,
cookies, session/CSRF, organization headers or incoming arbitrary headers forwarded.
Fresh opaque gateway request ID may go upstream; provider request ID is length/
control-character validated (≤200) and never trusted as an idempotency guarantee.

`OpenAiTextV1` upstream request is POST JSON with `model`, plain text
system/user/assistant `messages`, explicit `max_tokens` mapped from the validated
output ceiling, optional temperature/top_p and bounded stop strings; `n=1`.
No arbitrary passthrough. JSON uses `stream:false`; future SSE uses `stream:true`
and `stream_options.include_usage:true`, with Accept application/json or
text/event-stream respectively. Qualification proves this exact dialect;
`max_completion_tokens`-only suppliers need another version, not silent retries
with different bodies. Retain current Gateway parameter limits and role/order
validation; adapter supports only the normalized text subset. Generate safe
Gateway response identity/model; an upstream model echo cannot override the
canonical private mapping or pricing. No follow-up URL from a response is fetched.

First JSON profile: one text choice, bounded output, finish stop/length,
explicit prompt/completion/total token integers; total equals input+output,
nonnegative and within qualified context/output bounds. Reject contradictory,
negative, overflowing or unsupported priced dimensions; do not silently zero
cached/reasoning tokens or price tool/image usage as text. Maximum JSON body
8 MiB **after decompression**, no unbounded buffering, JSON depth ≤32. Request
limits remain Gateway's existing limits; supplier profile may only lower them.
Unknown usage retains Unknown even if text was delivered successfully.

Future SSE profile additionally requires incremental bounded event parsing,
usage terminal frame plus explicit end marker, cancellation and existing
post-output no-fallback rule. Cap each decoded frame at 64 KiB and accumulated
normalized terminal snapshot at the existing Responses writer bound if used;
no raw-stream accumulation. Unterminated/error/oversize/stalled stream records
partial delivery and Unknown as appropriate. Independent SSE qualification is
required; JSON success does not enable streaming.

| Situation | Financial/execution action |
|---|---|
| Admission before dispatch | All active wallet-fee/project/key gates plus separate credential external hold in TX-02. Frozen operator-published USD micro-unit text rates and existing `byok` fee version; no owner-supplied invoice price or mutable upstream price field. No eligible price → deny. |
| Valid successful supplier usage | Persist Provider-source evidence with separate proposed SupplierReported quality metadata and endpoint/profile/price versions in TX-04; it is syntactically validated, **not independently invoice-verified**. Do not repurpose Reconciled or invent a current enum value. Existing TX-05 charges only frozen platform BYOK fee, records external estimate and releases holds exactly once. |
| Missing/inconsistent usage or post-dispatch failure, including 401/429/3xx/5xx | No automatic retry, fallback or assumption of free execution. Persist classification/status with Unknown accounting and normal bounded reconciliation; arbitrary error strings cannot prove pre-execution rejection. Failure category and execution certainty are independent. |
| Local denial before durable dispatch | No supplier bytes; existing proven-undispatched release/no-charge path. Dispatch-marked work remains conservative even if socket outcome is uncertain; do not erase its fence. |
| Settlement DB failure / lost acknowledgment | Durable evidence/outbox, unique reservation settlement and retry by same operation identity. Never regenerate content to repair money. Provider HTTP never inside a transaction. |
| Unknown beyond existing evidence horizon | Existing BYOK unresolved/late-external-spend policy; never convert into platform-provider exposure or fictitious verified zero. Later authenticated supplier observation references same attempt and is append-only/idempotent; no new remote invoice scraper in this profile. |

Money remains integer micro-USD, rates fixed precision, no floating point/FX
recomputation or extra wallet. Existing percentage/fixed BYOK fees and fee-free
zero policy remain; this spec introduces no surcharge or per-request max_cost.
Customer wallet fee cannot exceed its reservation; preserve existing handling
of excess/late BYOK external evidence. External spend caps bound the catalog
estimate, **not a malicious supplier's bill**. Disclose this prominently and
retain operator onboarding/abuse suspension controls. No technical scheme here
can prove an arbitrary remote invoice is honest; Managed resale is deferred.

Client idempotency stays operation-scoped duplicate suppression (409/original
request identity), not response replay or upstream exactly-once delivery.
After timeout/cancellation the supplier might still charge. No automatic paid
qualification request; any manually authorized live test has explicit billing
consent and normal financial gates.

## 7. Transactions, ownership and race boundaries

| Owner / future persistence | Required invariants / interfaces |
|---|---|
| Providers: endpoint head, immutable versions and qualification records | Unique endpoint→private provider; organization-composite FKs; unique endpoint/revision; immutable origin/profile/tenant relation; expected-version status updates. Private evidence reference, actor/reason, qualification expiry/revocation; indexes on status/expiry. No credential/prompt in evidence. |
| Catalog: private provider/model/mapping ownership and effective prices | Tenant-composite endpoint/provider/model references; reserved private codes globally unique but never globally discoverable. All catalog lists, customer detail, admin publish, Gateway discovery/admission and exports enforce ownership. Price window exclusion and immutable versions use existing patterns; no global official mapping hijack. |
| Providers: existing credential/grants | Bind BYOK tenant/provider immutably to that endpoint; project composite FKs; preserve AEAD and SECURITY-006 rotation across old/new records. No Platform credential for a custom identity; no key retargeting. |
| Usage: attempt binding/evidence/read metadata | Durable endpoint revision/profile/credential generation/price references at dispatch, tenant/request/attempt FKs; source quality and Unknown distinct from zero. No hostname, secrets or payload in ordinary activity/export. |
| Audit / Outbox | State/version and actor/reason event commit together; transactional outbox for invalidation/expiry alerts. Durable PG status is authority, event delivery/cache invalidation is not revocation authority. |

Endpoint creation/approval/revoke, qualification publication and catalog/price
publication each commit their state and audit atomically. No DNS/probe/live
provider call in those transactions. Qualification may reference observations
gathered earlier; publication rechecks all referenced versions.

Admission and every dispatch recheck current tenant/key/project/grants,
endpoint head, qualification/credential generation, price/capability and
mandatory Redis health/admission through module contracts. Admission must enlist
the binding check inside the existing scoped TX-02, not use an expiring in-memory
authorization proof or nest transactions. Proposed order: existing operation/
logical-request claim → endpoint head → wallet → BYOK credential → ordered
budget buckets. Dispatch: logical request → endpoint head → current credential
binding → attempt transition. Endpoint/qualification mutations: organization
anchor → endpoint head → affected credentials, never wallet/request locks.
Finalization keeps existing request/reservation → wallet → credential/budget
updates and **does not acquire endpoint head**. Key-ring rotation locks only
credential; it must not subsequently acquire endpoint head. This preserves
wallet-before-credential order and prevents a credential→wallet/endpoint cycle.
Status paths must not hold endpoint then wait for an existing request; recovery
uses immutable references without eligibility locks. PG race/deadlock tests are
mandatory before accepting the Gateway task. Atomically validate/freeze
references with durable attempt dispatch;
release transaction **before** network I/O. If no valid fence exists, no send.

Linearization: revoke wins first → no new dispatch; dispatch fence wins first
→ already authorized request may connect/finish after revoke. A checked-out
binding is not permission for another attempt. No impossible guarantee of
instant network recall; disconnect does not establish zero cost. Revocation
does not stop settlement/recovery or delete accounting history. Credential
crypto-shredding remains its separate lifecycle, not a prerequisite for money recovery.
New endpoint revision/secret change invalidates new admission until qualified;
already-dispatched work uses immutable reference, not a newly targeted host.

Domain owns applicability/state invariants; Application owns qualification and
execution-binding use cases; Providers Infrastructure owns registry, crypto and
network policy. Dedicated adapter owns HTTP translation/usage/errors; Gateway
owns parsing/auth/admission/dispatch/response. Billing alone owns money; Usage
owns evidence/read models; Worker owns expiry/alerts/recovery. Catalog owns
private model/pricing and discovery. UI only uses authorized masked DTOs.
No cross-module SQL in routing/adapter, no dependency on Notifications transport,
new microservice, dynamic plugin loader, duplicate ledger or schema-per-tenant.

## 8. Privacy, operations and UX

No training/ZDR/region assertion follows from TLS, hostname or OpenAI-shaped
JSON. Treat supplier policy as Unknown until separately evidenced. Initially
deny strict privacy modes for custom endpoints; future PRIVACY-004–009 scoped
qualification must explicitly support them before any strict activation.
Gateway payload retention remains separately explicit; no cache/affinity enablement.
Endpoint probes and telemetry must not capture output. Metrics use bounded
profile/reason/status, not unbounded provider UUID/code/host labels; traces use
opaque internal endpoint IDs only in authorized diagnostic storage.

Keep health keyed by the unique private mapping/credential/tenant, sharing
only that endpoint's outage evidence across its authorized projects. Probe
success cannot reset inference circuits; no custom BYOK samples enter Managed
performance windows. Existing mandatory health/quota/deadline controls remain,
but arbitrary supplier error codes never gain official-provider retry certainty.

Owner UI eventually shows private alias/profile, qualification expiry/status,
masked key/grants, estimated external spend and actual platform fee separately.
No endpoint editing on credential rotation, raw evidence links, supplier
verification badge from a model-list test, or silent Managed fallback option.
Admin onboarding/status UI requires operator/MFA/CSRF. Backend data/contracts
precede screens; preserve mockup direction and improve localized accessible UX.

Expiry job records one deduplicated audit/outbox alert per expired qualification;
immutable evidence stays intact. Read/dispatch time expiry checks derive denial
from PostgreSQL time and cannot wait for Worker. Redis outage
fails mandatory admission closed; PostgreSQL outage cannot authorize new sends.
Existing dispatched work follows durable evidence/settlement recovery. Alert
on denied network policy, repeated unknown usage, expiry, auth failures and
stale holds without raw URI/error text. No retrying invoice fetch/health probe
to arbitrary URLs returned by the supplier.

Restore quarantines custom endpoint execution until origin/egress/key/price
evidence revalidated; retain immutable usage/ledger and old KEKs per backup
policy. Rollback disables deployment gates and retires new handlers, not populated
financial/schema history. Production activation requires network/region/TLS
qualification, supplier terms/domain ownership, billing consent, incident owner
and existing OPS-006/007/LAUNCH-003 gates. Private-network self-hosting, Managed
custom suppliers and OAuth need separate specifications, not firewall exceptions.

## 9. Designed fixtures — not executed custom-endpoint tests

All fixture data synthetic. Fake resolver/socket/TLS seams provide positive
public-address cases offline; real private/metadata destinations must never be
contacted. Fixture responses contain distinctive canary secrets/content to
detect leakage. Test command success without these assertions is insufficient.

Concrete initial CPE-011/014 fixture: qualified context 100, output ceiling 20,
input/output rates 1,000,000/2,000,000 micro-USD per million tokens, no extra
dimensions; BYOK markup 1000 basis points + fixed 3 micro-USD. Admission holds
external 140 and wallet fee 17. Synthetic upstream response:

```json
{
  "id": "custom-fixture-1",
  "model": "supplier-text-demo",
  "choices": [{"index": 0, "message": {"role": "assistant", "content": "Hello"}, "finish_reason": "stop"}],
  "usage": {"prompt_tokens": 12, "completion_tokens": 3, "total_tokens": 15}
}
```

Expected exact external estimate 18, wallet fee ceil(18×0.10+3)=5, wallet hold
release 12, external hold release 140/spent increment 18. One settlement and
one UsageCharge 5; no managed-provider cost debit. Replay leaves all unchanged.
The fixture with usage removed is delivered/Unknown, not a zero-cost success;
the variant `total_tokens:14` is inconsistent/Unknown. A post-dispatch 429
body `{"error":{"code":"rate_limit_exceeded"}}` never authorizes another send.
Future SSE fixture splits text across frames, then usage-only final frame and
`[DONE]`; removing either terminal evidence or end marker must exercise Unknown
without replay. No fixture contacts the illustrative supplier hostname.

| ID | Required observable fixture / test |
|---|---|
| CPE-001 | Reject localhost, 127.0.0.1, 2130706433, 0x7f000001, IPv6 ::1/mapped loopback, userinfo, ports, URL strings, Unicode/trailing-dot/wildcard/encoded/control hosts before DNS/secret resolution; zero send. |
| CPE-002 | Construct exactly HTTPS:443 /v1/models or /v1/chat/completions from approved identity; upstream model containing path/URL cannot change authority; duplicate/unknown JSON fields and oversized metadata denied. |
| CPE-003 | Empty, >16, private/public mixed A/AAAA, metadata/link-local, CGNAT, documentation/transition/internal-route answers rejected; injected all-public set accepted, deadline/cancellation bounded. |
| CPE-004 | DNS first public then private cannot trigger a second resolution for the same socket; captured peer is vetted, TLS authority remains original. New requests revalidate, zero pooled lifetime and second-connect/body-replay guard asserted; expiry/revision checked even with reused handler. |
| CPE-005 | 301/302/303/307/308 to another public or metadata host yields zero second request/credential forwarding; HTTP_PROXY/HTTPS_PROXY and cookies/default credentials cannot alter destination. |
| CPE-006 | Invalid TLS name/chain, hostile Alt-Svc, request Host override and protocol upgrade fail; no production certificate bypass; transport retries disabled and one send maximum. |
| CPE-007 | Tenant B/key/project or official platform key cannot bind to tenant A endpoint/model; scoped lists/models/detail/exports leak nothing; no hold/dispatch on denied inference. |
| CPE-008 | Swapped tenant/provider/endpoint ciphertext or mutated immutable association rejected; host change requires fresh identity/key; old/new KEK rotation and rollback retain old records, no plaintext/log leak. |
| CPE-009 | Owner cannot publish approval/price/qualification; operator needs active recent MFA, CSRF/reason and expected revision; duplicate creation same body returns original, mismatch conflicts; failed audit rolls back. |
| CPE-010 | Concurrent suspend/revoke/expiry/rotation versus admission/dispatch follows fence ordering; stale probe cannot requalify; already-dispatched work finalizes without any retarget/re-execution. |
| CPE-011 | Text JSON + explicit usage extracts sanitized request ID, records custom SupplierReported provenance and frozen prices; fake OpenAI brand/model fields cannot select official identity. |
| CPE-012 | Missing/negative/overflow/contradictory/over-limit usage or unsupported cached/reasoning dimensions produces Unknown, not billable invented zero; no wallet charge above hold and no fake invoice guarantee. |
| CPE-013 | 401/429/5xx/redirect/timeout/cancel after dispatch never authorize retry/Hybrid/fallback; zero provider calls after partial SSE; outcome/financial certainty asserted separately. |
| CPE-014 | 50 concurrent requests with $1 wallet and 30,000-micro wallet holds admit at most 33 before releases; independently exhausted credential external cap denies even with a zero wallet-fee policy. Duplicate key, evidence/finalization replay and commit-ack loss produce one fee/release; unresolved/late external spend preserved. Use a separate rate/fee profile for this concurrency case. |
| CPE-015 | Probe is GET only, single request, five-second headers/body bound, shared limiter/one concurrent lease; safe result no body or qualification/health reset; caller cancellation and rotation conflict tested. |
| CPE-016 | JSON >8MiB decompressed/depth>32, SSE >64KiB frame/unterminated/missing usage, slow client and disconnect terminate boundedly with correct Unknown/partial state; JSON qualification cannot enable SSE. |
| CPE-017 | Canary prompt/output/key/host absent from HTTP logs, ordinary trace attributes, audit/outbox/metrics/export; only authorized private config reads expose host; sanitized IDs prevent log injection. |
| CPE-018 | Owner/operator/developer/ReadOnly/cross-tenant browser/API matrix, masked secret lifecycle and localized billing warnings; generated schema/client match actual future backend, no self-approved activation. |
| CPE-019 | Restore quarantines endpoints, missing old KEK fails closed, disabled deployment gate prevents every custom path; pool retirement leaves durable settlement recovery intact. |
| CPE-020 | PG/mandatory Redis/DNS/Worker faults and two-node revoke races prove fail-closed sends, expiry without Worker, eventual outbox/hold conservation and bounded diagnostic labels; local results are not distinct-host gate evidence. |

Required harness layers: pure policy/crypto tests; fake upstream normalized
JSON/SSE contract fixtures; pinned-socket/TLS integration; real PG transaction,
FK/migration/tenant/idempotency/race tests; Redis admission/health/probe leases;
real TLS-edge API/browser acceptance; distinct-host fault/load qualification.
No new custom fixture has been implemented or run by this specification task.

## 10. Proposed decomposition — unscheduled, approval required

These eleven IDs are designs, not active register rows or implementation approval.
Preserve all prior provider work; no task may activate before its dependencies.

| Proposed P2 task | Objective / likely files | Dependencies / references | Acceptance, tests and completion |
|---|---|---|---|
| PROVIDER-011 | Endpoint identity/head/version/qualification contracts and PG persistence; Providers/Persistence/Migrator. | Approved PROVIDER-010; SECURITY-006, DX-002; §3/7, ADR-008/010/025. | Immutable tenant/provider/origin FK binding, statuses/expiry/version and rollback protection; CPE-007–010/019 migration/PG/crypto tests. No network or serving. |
| SECURITY-008 | Bounded public-HTTPS pinned transport for custom profile; Providers Infrastructure, deploy security policy. | PROVIDER-011; §4, NFR-041–046/060–063. | Parser/address policy, socket pinning/TLS, proxy/redirect/retry denial and bounded single-connect handlers; CPE-001–006/017 resolver/socket/TLS fixtures and architecture checks. No reference to Notifications Infrastructure. |
| PROVIDER-012 | Operator endpoint creation/approval/revoke/qualification API and audit/outbox; Providers/Management/Audit. | PROVIDER-011, SECURITY-008, SECURITY-004; §3/5/7. | Recent-MFA/CSRF, idempotent metadata creation, expected versions, explicit private evidence and expiry; CPE-009/010/019 HTTP/PG/audit rollback. Qualification requires evidence, not self-asserted probe. |
| CATALOG-004 | Private tenant model/mapping discovery and versioned operator pricing; Catalog/Management/Gateway/Persistence. | PROVIDER-012, APIKEYS-004, PROJECTS-002; §3/6/7, FR-050–056. | Reserved aliases, ownership on every list/read/publish, no official collision or Managed selection, price windows; CPE-007/009/011 PG/HTTP/schema tests. No execution yet. |
| PROVIDER-013 | Endpoint-bound Owner credentials/probe/grants and rotation; Providers/Management. | PROVIDER-012, CATALOG-004, BYOK-003, SECURITY-006; §5/7. | Exact immutable binding, preserved official-key semantics, KEK compatibility, bounded safe probe and stale-result denial; CPE-007/008/010/015/017 HTTP/PG/Redis/crypto tests. No custom inference gate activation. |
| PROVIDER-014 | Non-stream text compatible adapter with conservative usage/errors; ProviderAdapters/UZLLM.Provider.OpenAICompatible. | SECURITY-008, PROVIDER-013; §6, FR-050–053, ADR-010. | Separate identity/profile selection, normalized usage and Unknown, no retries/headers/SDK leaks; CPE-002/005/006/011–013/016 contract tests. Inference serving remains disabled. |
| PROVIDER-015 | Explicit custom BYOK Gateway eligibility/dispatch and financial evidence; Gateway/Providers/Billing/Usage contracts. | PROVIDER-014, CATALOG-004, BILLING-003, APIKEYS-004, PROJECTS-002; §6/7. | Complete lock order, immutable dispatch fence, wallet-fee/external holds/recovery and bounded telemetry labels, no Managed/Hybrid/fallback or strict/cache bypass; CPE-007/010–014/017/019/020 PG/Redis/fault tests and real-stack acceptance. Gates default off. |
| PROVIDER-016 | Separately qualified custom text SSE; compatible adapter/Gateway. | PROVIDER-015, GATEWAY-006; §6/9, NFR-060–063. | Incremental bounds, terminal usage, disconnect/partial/unknown with no replay; CPE-013/016 contract/SSE/financial tests. No new modality or fallback. |
| USAGE-007 | Authorized endpoint/profile/provenance reads; Usage/Management/docs/api/api-client. | PROVIDER-015/016, USAGE-004/005, DX-002; §7/8. | Tenant-safe metadata, no host/secret in Activity/export, supplier-reported source quality and separate fee/external estimate; CPE-007/011/017/018 PG/HTTP/schema/client tests. No UI or new financial authority. |
| FRONTEND-013 | Owner custom credential/status and operator onboarding UX; dashboard/admin/api-client. | PROVIDER-012/013, USAGE-007; §5/8, ADR-016. | Masked credential lifecycle, no self-qualified activation, explicit scope/fee/estimate/expiry warnings, mockup-aligned improved localized accessible controls; CPE-018 browser/real API tests and screenshots. |
| OPS-009 | Custom transport/expiry/restore/runbook and distinct-host qualification; Worker/Observability/deploy/tests. | PROVIDER-015/016, USAGE-007, OPS-006; §8/9, NFR-060–073. | CPE-019/020 dated fault/load/restore and redaction/egress evidence, conservative money recovery and rollback. Missing staging means Blocked, not completion from local Docker; external supplier approvals separate. |

Recommended sequential order is the table order. Existing completed code
dependencies do not supply the unbuilt endpoint contracts; OPS-006 is still
Blocked. Privacy strict support and Managed/private-network expansion remain
outside this decomposition, not hidden additions to PROVIDER-015.

## 11. Sources, validation and review handoff

Security references reviewed 2026-10-18: [OWASP SSRF prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
recommends layered application/network controls and disabling redirects.
[Microsoft ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0)
documents the new-connection seam;
[HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
explain pooled connection/DNS lifetime behavior. The exact tenant/approval,
protocol/fee/cancellation profile above is a proposed repository design, not
an OWASP endorsement, live supplier attestation or a claim that one callback
alone secures egress. Special-purpose address/network policy needs dated review
when implemented and at activation.

Documentation-only change. No implementation/generated wire, frontend,
migration, runtime configuration, secret or deployed service is changed.
Fresh validation on 2026-10-18, Windows/Docker disposable PostgreSQL 17 and
Redis 8, unchanged runtime sources at `60fcd85`:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0; existing ASPIRE010 CLI-bundle warning, zero errors (2m24s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/provider-010-20261018` | PASS, exit 0. Passed: 1028, Failed: 0, Skipped: 0, Aborted: 0 across 16 TRX reports; includes 234 PostgreSQL integration cases (16m7s). |
| Static documentation checks | PASS: 122 local Markdown link targets/eight linked anchors, balanced fences and two valid JSON fixtures; 20 unique designed scenarios, eleven unused/unpromoted task IDs and dependency-safe internal order. Completed code prerequisites checked; OPS-006 explicitly remains Blocked. Exact financial fixture arithmetic checked. |
| Whitespace/history/scope review | PASS; exactly eleven documentation paths. Other backlog/history rows unchanged; no runtime/test/frontend/configuration/dependency/generated-contract change. |

TRX files stay in the ignored local artifacts directory, not Git. Existing
tests validate existing behavior, **not** CPE-001–020. No new custom fixture,
browser feature, live supplier/egress qualification, fee activation or
production gate pass is claimed. Staged whitespace/scope checks pass;
approval remains pending for this task-only draft commit.

Review requested: approve or revise the operator-approved, tenant-private,
public HTTPS/Bearer BYOK-only profile, separate SSE qualification, frozen
operator pricing with supplier-reported usage and **no automatic post-dispatch
retry/fallback**, immutable endpoint/credential binding, eleven-task decomposition
and external activation gates. This draft stays In Progress pending review;
approval would complete the specification, not schedule features. Stop before
another task.
