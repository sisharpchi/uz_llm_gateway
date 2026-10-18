# Privacy Eligibility — ZDR and Regional Processing Specification

## 1. Status, authority and scope

**PRIVACY-003 · P2 · Approved specification · 2026-10-18.** Draft delivered in
`c5a8d4e`, approved by the user's subsequent Continue directly answering its
specification-review request. This completes the specification task, not a
privacy-routing implementation. The bounded profile, evidence/denial contract
and eight-task decomposition below are approved project design. Feature
scheduling/promotion, private provider-account qualification and production
activation require separate decisions; no follow-on task is started.

Keep NFR-050–053, [ADR-011](15_ARCHITECTURE_DECISIONS.md#adr-011--separate-payload-retention-from-request-metadata),
FR-060–068, [financial invariants](06_DOMAIN_AND_BUSINESS_LOGIC.md#12-corrected-ownership-and-state-model)
and the [billing model](09_BILLING_WALLET_AND_PAYMENTS.md). Use the existing
modular monolith, three hosts and PostgreSQL transaction coordinator. This is
an incremental contract proposal, not a new service, database or ledger model.
Current [generated API contracts](api/README.md) remain the implemented wire
authority. None of the proposed fields/routes below exists yet.

In scope: prospective inference-content handling; exact upstream eligibility;
regional **provider processing** restrictions; Managed/BYOK/Hybrid/fallback
behavior; evidence lifecycle, denial cases and executable decomposition.
Out of scope: legal certification, whole-platform residency, a promise of zero
retained information, retroactive erasure, upstream account onboarding,
custom endpoints, new modalities, cache implementation and production rollout.
No price/plan change is proposed; contractual guarantees/pricing need business
approval. OPS-006/007 and LAUNCH-003 remain unchanged.

## 2. What the repository actually implements

Inspected on clean `main` at `8ad70cb`; these are existing behaviors, not new
claims produced by this task:

| Component | Observed implementation | Missing for strict eligibility |
|---|---|---|
| [Catalog contracts](../src/Modules/Catalog/Contracts/ProviderDataPolicyContracts.cs), [service](../src/Modules/Catalog/Application/ProviderDataPolicyService.cs), [store](../src/Modules/Catalog/Infrastructure/PostgreSqlProviderDataPolicyStore.cs) | Append-only provider-wide observations; explicit Unknown, dated source URL, scope/conditions, descriptive region labels. Latest observation is not verified entitlement. | Exact account/credential, operation/model/feature and endpoint qualification; expiry/revocation and enforcement. |
| [Gateway parser](../src/UZLLM.Gateway.Api/Inference/ChatRequestParser.cs) | Allows only enumerated `uzllm` routing/BYOK fields; unknown extensions fail. | Privacy extension and normalized hard constraints; do not send the proposed example today. |
| [Gateway tenant/credential reads](../src/UZLLM.Gateway.Api/Inference/GatewayReadStore.cs) | Reads project retention flag; selects active platform credential or explicit tenant/project-authorized BYOK credential. | Privacy-aware credential selection; a provider label alone cannot qualify its selected credential. |
| [Inference orchestration](../src/UZLLM.Gateway.Api/Inference/InferenceGateway.cs) | Rechecks key/project restrictions at admission; reserves before dispatch; records each attempt; optional encrypted payload capture; financial finalization/recovery. | Privacy checks before reservation and every dispatch, immutable decision evidence and request-specific no-capture guard. |
| [Provider contracts](../src/Modules/Providers/Contracts/ProviderContracts.cs), native adapters | Normalized requests/events/errors; adapter-specific configured base addresses. Google uses Gemini Developer API, not Vertex AI. | Qualified endpoint profiles/account binding and adapter feature-policy assertions. Changing a deployment URL is not regional qualification. |
| [Payload store](../src/Modules/Usage/Infrastructure/PostgreSqlPayloadRetentionService.cs) | Project opt-in, locked policy checks, separate encrypted request/response rows and expiry/disable deletion. | Per-request strict prohibition resilient to concurrent policy changes; encryption is still retention. |
| [Privacy UX](../frontend/apps/dashboard/src/PrivacyPage.tsx) | Owner-only payload control; explains metadata/provider/backup exclusions. | Strict-policy eligibility, conflicts and region status; existing switch is not ZDR. |

Dependency CATALOG-003 is substantiated by its [unit regressions](../tests/Unit/UZLLM.Catalog.Tests/ProviderDataPolicyServiceTests.cs)
and [PostgreSQL/HTTP tests](../tests/Integration/UZLLM.Persistence.IntegrationTests/ProviderDataPolicyIntegrationTests.cs):
Unknown/default behavior, bounds, tenant/operator/MFA/CSRF isolation, immutable
publication, concurrency and audit rollback. They do not test strict routing.
The [CATALOG-003 handoff](35_PROVIDER_DATA_POLICIES.md) explicitly disclaims it.

## 3. Claims must remain separate

| Claim | Meaning in this proposal | Does not imply |
|---|---|---|
| Gateway no durable inference content | No prompt, output, tool arguments/results, schemas, vectors or provider error bodies persisted by gateway-owned payload/cache/telemetry paths for a strict request. Bounded transient RAM is necessary. | No request metadata, no provider retention, secure deletion of every RAM copy, or erasure of old backups. |
| Upstream ZDR arrangement | Exact provider product/account/model/operation/features are covered by a reviewed no-content-retention arrangement with explicitly disclosed exceptions. | No training by itself, all provider features eligible, no legal/safety exceptions, or universal certification. |
| No upstream training | A separately evidenced restriction on training/improvement for the exact service/tier/account. | No abuse logs, application state, caching or cross-border processing. |
| Provider processing region | Qualified provider execution for this attempt is restricted to an approved region identifier. | Provider storage region, gateway/edge/database/support region, or zero cross-border transmission. |
| Whole-platform residency | Would require evidence for edge, gateway, storage/replicas/WAL/backups, Redis, telemetry, notifications, support and subprocessors. | It is **not offered by this first profile**. |

Customer copy must say what is enforced and what remains retained. Never render
`NoRetentionReported`, `ProviderReported`, HTTPS, `store=false`, a local
environment tag, a nearby IP, encryption, or an empty Activity body as “ZDR
verified”. Do not call a processing-only region badge “all data stays here”.
Compliance/healthcare suitability is not established by this document.

Financial/audit/operational metadata remains governed by NFR-051 and the
[existing retention controls](10_SECURITY_OBSERVABILITY_RELIABILITY.md).
Request/attempt IDs, tenant/key/model identities, provider request IDs, timing,
token counts, price/FX/fee versions, reservation/ledger/evidence, and existing
idempotency digests are not prompt replay storage. They may still be sensitive:
restrict access and explain their retention. A digest is not anonymization.
Do not erase/rewrite money evidence or treat unknown usage as zero.

## 4. Dated public-provider evidence matrix

Sources below were read on **2026-10-18**. These short observations guide the
proposed qualification process; public documentation is mutable, may include
exceptions and is **not proof for any configured UZLLM credential**. No source
was imported into Catalog, and no real account, contract or regional deployment
was verified. All actual strict-route qualifications are presently **Unknown**.

| Actual adapter product | Public evidence and limits | Safe qualification conclusion |
|---|---|---|
| OpenAI direct API | [Data controls](https://developers.openai.com/api/docs/guides/your-data): ZDR needs approval/configuration; eligibility varies by endpoint/features. Current documentation includes safety/private-retention exceptions and PSP-specific conditions. Regional storage and processing are distinct, with project/endpoint/model constraints; system data is excluded. | Need exact approved account/project, model/features, effective retention mode and regional endpoint evidence. `store=false` alone is insufficient. Deny strict no-content-retention if the effective arrangement requires content retention; do not implement PSP/customer-controlled storage implicitly. |
| Anthropic direct Claude API | [API retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention): arrangement and feature eligibility matter; non-eligible features may still run, and safety/legal retention exceptions exist. [Covered-model policy](https://privacy.claude.com/en/articles/15425996-data-retention-practices-for-covered-models) describes model/workspace-specific retention requirements and separately notified exceptions. | Need exact organization/workspace, model and feature coverage, including current notices. Successful API auth is not ZDR proof. Do not generalize direct-API terms to Bedrock/other products or infer a processing region. |
| Google Gemini Developer API | [ZDR guide](https://ai.google.dev/gemini-api/docs/zdr): paid-service training restrictions differ from abuse/state retention; stateful/grounding/cache features have separate conditions. It directs workloads requiring guaranteed ZDR/enterprise agreements to Vertex AI. [Service terms](https://ai.google.dev/gemini-api/terms) distinguish tiers and regional conditions. | Existing Developer API adapter is not Vertex. Do not advertise guaranteed ZDR or regional processing from paid billing alone. Require exact applicable proof; no automatic qualification or new Vertex adapter in this task. |
| DeepSeek direct API | [Published privacy policy](https://cdn.deepseek.com/policies/en-US/deepseek-privacy-policy.html) discusses service input handling, improvement, retention and processing/storage in China. This broad policy is not evidence of an API-account-specific ZDR arrangement or a selectable regional endpoint. | Applicability and any negotiated API exceptions need verification. No positive API ZDR/no-training/region qualification can be established from this review. Unknown fails strict eligibility; do not seed a universal provider claim from consumer/general terms. |

Interpretation above is deliberately narrower than a legal warranty. Provider
account enrollment, current exception notices, permissible resale, processing
terms and region coverage need the provider/commercial/security owner. A fixture
proves our enforcement with synthetic evidence, not upstream compliance.

## 5. Approved first profile and precedence

The approved profile has **two independent hard constraints**: upstream ZDR
arrangement and provider processing region. Either makes a request “strict” for gateway content
handling. A region-only request may still encounter disclosed upstream
retention/training; it must not receive a ZDR badge. A ZDR request additionally
requires separately qualified no-training coverage for this profile. Generic
“no training only” routing is deferred rather than confused with ZDR.
`require_zdr` means a qualified **upstream arrangement**, not an absolute
no-exceptions warranty. A model/account mode explicitly requiring retained
content cannot qualify by borrowing another model's arrangement. Statutory
and exceptional safety limitations must remain visible; this profile cannot
serve a requirement that forbids every possible upstream legal/safety exception.

Proposed future extension for all supported inference operations:

```json
{
  "uzllm": {
    "privacy": { "require_zdr": true, "processing_region": "eu" }
  }
}
```

This example is **not a current API contract**. `require_zdr`, if supplied,
must be `true`; `false`, null, empty/unknown fields and an empty privacy object
are `400 invalid_privacy_policy`. `processing_region` is an exact, lowercase,
1–32-character registry identifier (`[a-z][a-z0-9-]*`), not an arbitrary URL,
country claim or free-form Catalog region label. `eu` is illustrative, not an
enabled region. At least one constraint is required; omission without project
restrictions retains today's behavior. Unknown/unqualified regions are rejected,
not guessed from names. No storage-region or whole-platform-residency field.

Owner-only project hard policy uses the same optional constraint pair through
proposed `GET/PUT /management/v1/organizations/{organizationId}/projects/{projectId}/privacy-policy`.
GET and mutations require current Owner authorization; PUT also requires CSRF,
expected policy version and atomic audit. Missing row means no strict policy.
GET returns `{ "version": 0, "policy": null }` when absent; PUT accepts
`{ "expectedVersion": 0, "policy": { "require_zdr": true } }`, with the
optional processing region as above. `policy: null` deliberately removes future
project constraints; it is not accepted in an inference privacy extension.
Every successful change increments version and preserves history, including
removal. PUT stores a new version; stale expected version returns `409`; cross-tenant
project returns `404`, unauthorized role `403`, and responses are no-store.
No production activation follows from saving a policy: report eligibility
separately and fail closed when no qualified route exists.

- Effective ZDR is project requirement OR request requirement. An explicit
  request cannot relax it. No key/project restriction or financial check is
  overridden, and no new execution-mode default is introduced.
- Processing region: inherit project value; identical explicit value is
  allowed; another value conflicts (`403 privacy_policy_conflict`). With no
  project region, an explicit request may constrain it.
- Initial strict profile requires project payload retention **disabled**.
  Reject conflicting policy enablement/request with `409 privacy_payload_conflict`;
  never silently change the project switch or delete earlier payloads. A
  request-specific durable prohibition still prevents capture if retention is
  enabled concurrently after a strict request was admitted.
- All inference keys in the project inherit the hard policy, including admin
  users' keys. Only Owner can weaken future project policy, with audit and
  versioning; this cannot change an already admitted request's strict snapshot.
- Managed stays default. Explicit BYOK must qualify that exact tenant/project
  credential; Hybrid needs existing explicit managed-fallback consent **and**
  independent qualification of each managed candidate. No credential substitution
  or opt-in to fallback is implied by a privacy setting.

First strict capability profile: bounded **text-only** chat and the existing
stateless text Responses subset (streaming/non-streaming), plus existing
OpenAI text/float embeddings only when separately qualified. Initially deny
vision/remote URLs, tools/tool-result history, structured-output schemas,
provider-hosted tools/state/files/batch, explicit/extended persistent caches,
custom endpoints and modalities beyond those profiles. Do not silently strip
fields or turn a supported ordinary request into an unsupported strict one.
Provider-default transient caching requires explicit review/coverage in the
qualification; a cached-token price is not evidence of safe retention.

Accepted trade-offs: conservative denials can leave **zero eligible
providers**; strict mode may reduce model availability, performance and
failover. No price advantage, zero-retention-without-exceptions guarantee or
regional SLA is promised. Material provider exceptions must be shown before
opt-in; if requirements cannot be met, deny rather than silently downgrade.

## 6. Private qualification lifecycle and persistence proposal

Retain public `catalog.provider_data_policy` unchanged. Add private qualification
through Providers contracts/Application, not hidden booleans in Catalog. Bind:

- provider product; platform or tenant-owned credential ID **and secret/account
  configuration generation**; provider account/project/workspace proof;
- provider mapping and immutable upstream model snapshot, logical operation,
  actual upstream operation and exact permitted feature profile/version;
- configured endpoint-profile ID/version/host and processing-region evidence
  (storage region separately recorded if known, never substituted for processing);
- explicit retention, training, abuse/state/cache exceptions, legal limitations,
  evidence references, public-policy version/observation and account proof;
- observed/effective UTC dates, `validUntil`, reviewer/operator identity, reason,
  immutable version, revocation/supersession and publication audit IDs.

Credential generation means the actual upstream secret/account configuration,
not its encryption wrapping-key version. KEK-only rewrapping preserves that
identity and history; replacing the upstream key/account requires a new
qualification. Never infer an account's entitlement from key prefix or a
successful BYOK authentication test.

Approved maximum **30-day review validity**, shortened by contract/account
expiry or provider notice. This is a project safety refresh policy, not a provider
guarantee. Expired, future-effective, contradictory, revoked, missing or
scope-mismatched proof is ineligible. Newly discovered contradictory policy
invalidates qualification immediately; never wait out the 30 days. Automatic
public crawling/LLM interpretation cannot activate evidence. A newer Catalog
observation invalidates qualification bound to the previous public version
pending review, even if its labels look unchanged; Unknown corrections cannot
retain a prior positive entitlement. A new observation alone cannot requalify it.

Private documents stay in an approved restricted evidence store, with opaque
references/integrity digests in PostgreSQL; do not upload contracts, raw keys,
account identifiers or private links into shared public disclosure. Publication
requires local operator authorization, recent MFA, CSRF and reason plus atomic
audit. Tenant Owners may submit non-secret evidence for their own BYOK key,
but cannot self-certify it; cross-tenant key/qualification discovery is denied.
No extra independent-maker/checker rule is invented for this non-financial task.

Proposed data ownership, using existing schemas/context (no migration now):

| Owner / prospective record | Constraints and transactions |
|---|---|
| Projects: versioned project privacy policy/current head | Tenant-composite project FK; unique project/version; serialize changes with the project anchor row. Policy, retention conflict check and audit commit together. |
| Providers: immutable endpoint profiles and qualification versions; revocation/current eligibility projection | Credential/provider and mapping FKs, tenant-composite ownership for BYOK; unique credential-generation/scope/version; effective/expiry indexes. Public policy version is an evidence reference, not sufficient qualification. Revision/revoke/projection/audit commit together. Never mutate historical proof. |
| Usage: request privacy snapshot; attempt qualification/endpoint/version references | One request snapshot, one decision per attempt with tenant-bound FKs and bounded reason codes. Persist with admission/dispatch respectively; keep original proof resolvable after revocation/deletion of a credential. No content in the snapshot. |

Reuse existing shared transaction coordination. A private qualification's
current-head projection may change; immutable versions and revocation events
must remain append-only. Runtime roles cannot rewrite them. New schema requires
an explicit Migrator migration and populated-history rollback protection only
in a separately approved build task. No financial partitioning or new datastore.

## 7. Hot-path and failure contract

```text
authenticate/tenant/key + bounded parse + distributed admission
  → effective hard privacy policy + supported feature check
  → authorized model/provider/credential candidates
  → authoritative private eligibility filter → existing routing score
  → financial admission + request privacy snapshot (one DB transaction)
  → each attempt: recheck policy/evidence/endpoint + durable dispatch fence
  → provider I/O outside transaction; bounded uncached response/SSE
  → content-free usage evidence → existing settlement/recovery/analytics
```

1. Resolve policy and candidate proof in PostgreSQL; filter before performance/
   price/priority/sticky ranking. Redis is advisory, never authority for proof.
   No scraping/provider-account verification/network terms fetch on hot path.
   Batch candidate proof reads with bounded indexed scope lookups; do not add
   per-token DB calls or analytic aggregation. Measure strict/non-strict overhead
   against NFR-010 and forwarding/backpressure against NFR-011 before activation.
2. Before reservation, reject incompatible payload/feature/region and no-route
   cases without a wallet hold, provider call or captured body. Authentication,
   limits and ordinary permission errors retain precedence and safe responses.
3. At financial admission recheck the project policy under its existing shared
   transaction boundary; persist effective immutable request requirements with
   usage/hold/idempotency claim. Include effective policy identity in replay
   reasoning; existing same-key duplicate remains `409`, never execute again.
4. At **each** attempt's dispatch transaction recheck current hard restrictions,
   active credential generation, qualification expiry/revocation, model/feature
   and configured endpoint profile. Intersect original strict snapshot with
   current restrictions; a change cannot weaken the admitted request. Policy
   conflict rejects further dispatch, rather than silently rerouting regions.
5. Dispatch lock order: existing logical request → project anchor → provider
   anchor → selected credential → attempt transition. Project policy and payload
   opt-in serialize on the project anchor even when no policy row exists;
   qualification publication/revocation takes provider then credential. Catalog
   publication already locks the provider: compare the current public evidence
   version under that same fence, not an unfenced cached observation. Acquire
   multiple same-kind anchors in UUID order. Financial admission must preserve
   its operation/request identity → wallet → BYOK credential → budget mutation
   order; insert project/provider policy fences before the wallet and never
   pre-lock a credential ahead of wallet/request finalization. Qualification
   writers must not acquire wallet/request locks in reverse order. Exercise
   admission, settlement, rotation and revoke together in PRV-003/010. Commit
   decision evidence and durable MarkDispatched together through explicit
   module contracts. No lock spans provider I/O; controllers own no business rule.
6. The dispatch commit is the linearization boundary: revocation committed
   earlier prevents the attempt; already authorized/dispatched work is in flight,
   including a crash/send gap. Revocation cannot retract bytes already sent or
   promise instantaneous upstream erasure. Recovery treats ambiguous dispatch
   conservatively, never resends it on restart. Qualification time validity is
   evaluated at the same boundary; expiry does not retrospectively cancel an
   already authorized attempt or waive its accounting.
7. Carry opaque qualified execution context to the adapter; resolve only its
   allowlisted operator-managed endpoint profile. Disable redirects/unsafe
   endpoint drift; never fall back to the default global host. Validate actual
   native endpoint/flags, not just the public logical route (Responses currently
   uses native chat adapters). Unsupported qualification fails before dispatch.
8. Mark strict content capture prohibited at every capture boundary, even after
   owner toggles or Worker retries; do not instantiate the response-capture
   wrapper. No durable prompt/response cache, body logging, exception dumping,
   dead-letter body or content-bearing audit/notification/trace. Responses
   `store=false` and HTTP no-store remain necessary but insufficient alone.
9. Same-model/cross-model fallback, Hybrid managed fallback and every retry
   retain all hard constraints; current attempt limits, cost ceiling and
   no-fallback-after-output rules remain. Unknown cost/provider timeout is not
   a privacy-safe free retry. If undispatched proof expires, release through
   existing idempotent financial finalization; dispatched unknown usage follows
   existing reconciliation/debt rules, without retaining content.
10. DB cannot attest/commit → `503 privacy_eligibility_unavailable`, no new
    dispatch. Redis rate/concurrency outage still fails existing admission
    closed; missing ranking statistics cannot relax privacy. Worker failure
    never releases unknown financial exposure by age alone. Settlement retries
    and payment processing do not need prompt bodies.

Proposed pre-output errors use the existing OpenAI envelope/no-store semantics:
`400 invalid_privacy_policy` or `privacy_feature_unsupported`, `403
privacy_policy_conflict`, `409 privacy_payload_conflict`, `503
privacy_no_eligible_provider` (no qualified candidate), or `503
privacy_eligibility_unavailable` (dependency/attestation failure). Do not expose
other tenants' credential/account/evidence IDs or raw upstream error text.
After SSE starts use the existing operation's terminal error behavior, without
restarting/failing over. Expose safe request ID and bounded reason only.

## 8. Ownership, UI and operational activation

- **Projects/Domain + Application:** pure hard-policy/precedence invariants;
  Owner use cases, versioning and retention conflict orchestration. No provider
  HTTP or money mutation.
- **Providers/Domain:** deterministic qualification scope/time/exception
  compatibility rules over normalized evidence; no SDK, database, web fetch or
  automatic legal interpretation.
- **Providers/Application + Infrastructure:** private qualification/endpoint
  state, authoritative checks, encryption references and audited persistence.
  Provider adapters own native URL/flags/translation, not tenant policy/billing.
- **Routing/Application:** filter eligibility before ranking; normalized opaque
  decision contracts, no provider SDK types or fetching public terms.
- **Gateway:** parse/propagate constraints, admission/dispatch fences and safe
  wire/SSE; Billing/Usage keep their current transaction/invariant ownership.
- **Usage/Worker:** content-free immutable decision/evidence, retention guard,
  expiry/review alerts and recovery; no automatic evidence activation.
- **Management/admin/frontend:** Owner controls and operator evidence workflow,
  tenant-safe reads, truthful capability/Unknown/expired/conflict display.

Proposed Owner-authorized eligibility preview under the same project prefix,
`GET .../privacy-policy/eligibility?model=...&operation=...`, checks the current
project policy for the first text profile. Operation is `chat.completions`,
`responses` or `embeddings`; optional `providerKeyId` selects an explicitly
authorized BYOK credential, otherwise preview is Managed. It never implies
Hybrid consent or checks arbitrary additional request features. It returns safe
policy version, constraints and eligible/ineligible/Unknown reasons for the
caller's authorized catalog; never private evidence, plaintext keys or other
tenant credentials. No strict project policy means `not_requested`, not a
positive qualification. It is advisory, not admission or a reservation; every
actual request's constraints/features are checked again.
Request Activity can later show effective restrictions, qualified endpoint
region and safe decision versions, not contracts or content. Backend persistence
and routes precede UI; no fake enabled toggle while enforcement is absent.
UI follows the preserved mockup's visual direction with improved accessibility,
localized explanations and explicit provider-vs-platform distinctions.

Before activating this optional feature: all scoped synthetic tests pass;
independent security/content-sink review; exact deployed endpoint/config and
provider-account entitlements verified; region egress and redirect behavior
qualified; terms/exceptions and evidence-refresh owner approved. Disable by
default via deployment gate; loss of qualification disables affected strict
routes, not ordinary tenant permissions or accounting. A restore starts
quarantined with strict dispatch disabled until evidence, generations, endpoints
and revocations are revalidated; never resurrect eligibility from backup.

Metrics use bounded codes/provider/region-profile dimensions, no content or
credential/account IDs: denied eligibility/conflict/expiry, qualification age,
capture-guard violation, dispatch fence denial and reconciliation backlog.
Alert before expiry and on revocation or unexpected endpoint/capture; capture
violations require incident handling, not a retroactive claim of ZDR. Store
provider notice history and audit activation/revocation. Content opt-out/TTL
does not erase historical backups, legal holds, provider records or customer
copies. Keep the existing restore scrub and approved retention schedule.

## 9. Mandatory future acceptance scenarios — designed, not executed

| Scenario | Observable acceptance and required test |
|---|---|
| PRV-001 · No inferred entitlement | All public labels/empty metadata/paid/no-training/`store=false` without exact proof deny ZDR; unit + Gateway fixture asserts zero provider calls/holds/payloads. |
| PRV-002 · Scope and tier | Wrong product/account/workspace/credential generation/model/operation/features or endpoint denies, including Developer API versus Vertex; replacing upstream key invalidates proof, KEK-only rewrap does not change identity; unit + native adapter fixtures. |
| PRV-003 · Time and revocation | Effective/expiry boundary, conflicting correction and explicit revoke deny; fake-clock unit tests plus two-node PostgreSQL dispatch/revoke race. |
| PRV-004 · Regions | Storage-only proof, global endpoint, unknown label, redirect and endpoint-version drift cannot satisfy processing restriction; HTTP fixture checks destination and no secondary request. |
| PRV-005 · Inheritance | Project ZDR/region inherited; matching/stronger request allowed, weakening/conflicting request denied; Owner/CSRF/version/tenant HTTP and PostgreSQL tests. |
| PRV-006 · Capture race | Strict request versus retention opt-in/update/response-finalization cannot produce a payload row/capture wrapper; concurrent PostgreSQL tests. Existing non-strict retention remains unchanged. |
| PRV-007 · Private evidence boundary | Tenant A cannot read/certify B's BYOK proof; Owner cannot publish operator qualification; recent MFA/CSRF/audit failure rolls publication back; HTTP/PG tests. |
| PRV-008 · Retry/fallback | Cheapest/latency/auto/sticky, same/cross-model and Hybrid never select unqualified credential/region; one fully qualified fallback allowed within existing limits; fixture + financial PG tests. |
| PRV-009 · Streaming and unknown cost | Partial output/disconnect/timeout has no fallback or durable content; usage/unknown evidence and exactly-once settlement/recovery persist; SSE/PG failure tests. |
| PRV-010 · Replay and policy changes | Duplicate key returns original `409` without dispatch; policy strengthen/credential rotation between admission and dispatch denies stale eligibility; concurrent PG tests. |
| PRV-011 · Infrastructure and crash | DB/read/commit failure prevents dispatch; Redis loss obeys fail-closed admission; crash after dispatch authorization is unknown, never replayed; failure fixtures plus bounded-read/load and SSE-forwarding measurements against NFR-010/011. |
| PRV-012 · Content sinks | Distinctive synthetic input/output/error/schema/vector sentinels absent from DB except allowed metadata digests, logs/traces/cache/outbox/audit/notifications/export/dumps; real-stack adversarial inspection. Financial evidence stays intact. |
| PRV-013 · Restore and expiry jobs | Restored proof never enables strict traffic before requalification; repeated expiry alert is bounded, historical decisions survive; Worker/restore integration drill. |
| PRV-014 · Truthful UX and qualification | Region-only never shown as ZDR; unavailable/expired preview cannot enable route; browser + generated-contract tests; live qualification evidence separately linked before activation. |

Synthetic proof cases must cover all four adapter products and modes without
asserting actual commercial entitlement. Positive fixtures qualify only their
fake account/product/scope. These scenarios are a **test plan**, not 14 passing
tests; the existing baseline below does not implement them.

## 10. Approved task decomposition — unscheduled, not executable approval

Specification approval accepts this decomposition, not its execution. Do not
add these to the active register or start them without explicit execution
approval/promotion and a current dependency check. Order below is recommended;
none bypasses P0 launch gates.

| Approved design ID / P2 (unscheduled) | Objective / likely files | Dependencies and references | Acceptance / required tests / completion |
|---|---|---|---|
| PRIVACY-004 | Add versioned private qualification/endpoint evidence contracts and PG persistence in Providers/Persistence/Migrator. | Approved PRIVACY-003; CATALOG-003, SECURITY-006; §6, NFR-041/045/052, ADR-011. | Composite ownership/FKs, immutable versions, generation/scope/expiry/revocation, runtime rewrite denied and populated rollback protection; PG migration/concurrency tests PRV-002/003. Complete only after explicit migration/test evidence. |
| PRIVACY-005 | Operator publication/revoke and Owner BYOK evidence submission; Management/Providers/Audit. | PRIVACY-004, SECURITY-004; §6, NFR-044/045. | Recent-MFA/CSRF/reason, no self-certification, tenant isolation, atomic audit rollback and private-reference hygiene; PRV-007 HTTP/PG. No public evidence leak. |
| PRIVACY-006 | Project hard policy, request parser/normalized constraints and no-capture guards; Projects/Gateway/Usage/Management. | Approved PRIVACY-003; existing PROJECTS-002/PRIVACY-001/002/DX-002; §5, NFR-050/051/053. | Versioned Owner policy with retention conflicts and strict guards; feature-bound validation and inheritance/denial tests PRV-005/006/010. Hidden/disabled until enforcement tasks qualify; no usable strict promise yet. |
| PRIVACY-007 | Filter qualified credentials and fence admission/every dispatch; Routing/Providers/Gateway/Usage/Billing coordination. | PRIVACY-004–006; §7, FR-060–068, existing finance invariants. | Immutable request/attempt snapshots, lock ordering/race linearization, expiry/DB/Redis failure and replay safety; PRV-001/003/008–011 unit/PG/SSE tests. No network I/O in DB transaction. |
| PRIVACY-008 | Bind native adapter execution to qualified endpoint/feature profiles; ProviderAdapters + Gateway contracts. | PRIVACY-007; §4/5/7, ADR-010. | Allowlisted exact hosts/config, no redirects/global fallback; native mapping and per-operation/text/embedding feature compatibility fixtures PRV-002/004/008. Includes fake positives, no live credential inference. |
| PRIVACY-009 | Private-evidence expiry/revocation operations, metrics/runbook/restore and content-sink qualification; Worker/Observability/deploy/tests. | PRIVACY-005/007/008; §8, NFR-042/051/053/066. | Bounded alerts, quarantine restore, no body-bearing sinks, financial recovery unchanged; PRV-009/011–013 plus real-stack evidence. No live provider certification from fixtures. |
| PRIVACY-010 | Safe eligibility preview/Activity reads and generated contract/client surface; Management/Usage/docs/api. | PRIVACY-007–009, DX-002; §8, NFR-044/052. | Authorized preview is advisory, provenance is non-secret, states/reasons match enforcement, schema/client parity and tenant HTTP tests PRV-007/014. Backend ready before UI. |
| PRIVACY-011 | Owner policy/eligibility and operator workflow UX; dashboard/admin/api-client. | PRIVACY-005/010; §8, ADR-016. | Mockup-aligned improved localized/a11y UI; explicit processing-vs-storage/platform distinction; no misleading activation; PRV-014 browser plus real API coverage. Screenshots/evidence required. |

Qualification/activation remains a separate release decision, not another
“software-complete means ZDR” task. Review public terms, exact account
enrollment, private agreements/notices, regional host/config, subprocessor and
retention exceptions, legal copy, monitoring ownership and infrastructure
boundaries for the deployed revision. These external inputs need not block
specification or synthetic fixtures; absent them, keep strict routes disabled.

## 11. Validation and approval handoff

This task changes documentation only. No runtime/API artifacts, migrations,
configuration, provider policy seeds or frontend files are changed. Public
primary sources and local implementation were cross-checked, with Unknown
preserved instead of fabricating provider qualifications.

Draft-stage local validation on 2026-10-18 (Windows, Docker disposable PostgreSQL 17 /
Redis 8; unchanged runtime sources at `8ad70cb`):

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0; existing ASPIRE010 CLI-bundle warning, no build errors. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/privacy-003-20261018` | PASS, exit 0. Passed: 1028, Failed: 0, Skipped: 0 across 16 assemblies; includes 234 PostgreSQL integration cases (16m20s). All 16 TRX reports aggregated and checked. |
| Documentation checks | PASS: 89 local link targets and five linked heading anchors; balanced fences/valid illustrative JSON; 14 unique designed scenarios; eight proposed task rows, dependency order checked, none promoted. Other backlog rows unchanged. |
| `git diff --check` / staged whitespace and scope review | PASS; only this task's nine documentation paths. No implementation/generated-contract changes. |

TRX evidence stays in the ignored local artifacts directory, not in Git. The
baseline exercises existing behavior only. No browser/live-provider/region
qualification or new strict-runtime acceptance was run or claimed. PRV-001–014
remain designed-only scenarios; account evidence is Unknown, and existing
production gates remain unpassed.

### Approval-only closeout — 2026-10-18

The user's Continue directly answers the review request for `c5a8d4e`. It
approves the bounded profile: independent ZDR/processing-region restrictions,
text-first scope, no-training qualification for ZDR, denial of payload-opt-in
conflicts, private scoped evidence with a 30-day maximum validity, and the
eight-task decomposition. This satisfies the remaining specification-review
criterion. No financial, retention, upstream legal/safety or region guarantee
is broadened. Approval is **not feature scheduling/activation**.

The prior sixteen TRX reports were re-read: **1028 passed, zero failed/skipped**,
including PostgreSQL 234. Git comparison to `8ad70cb` verifies runtime, test,
configuration and dependency inputs are unchanged. No new restore/build/test
run or PRV strict-runtime pass is claimed for this approval-only closeout.
Fresh closeout checks PASS: 89 local link targets and five linked heading
anchors; balanced fences/valid illustrative JSON; 14 unique future scenarios
and eight dependency-ordered task definitions unchanged from the reviewed
draft. All other backlog/history rows and production gates remain unchanged;
exactly nine documentation paths change. `git diff --check` and staged
whitespace/scope checks pass. The eight follow-on feature tasks remain
unscheduled. Do not begin another task without separate approval.
