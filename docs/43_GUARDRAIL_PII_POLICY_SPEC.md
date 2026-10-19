# Guardrail and PII Policy Specification

## 1. Status, authority and bounded decision

**SECURITY-007 · Approved specification · 2026-10-18 · Specification only.**
PRIVACY-002 is completed; its legal/backup schedule and OPS-006 staging gates
remain external prerequisites. This document implements no scanner, policy,
redaction, route, migration or UI. Current API rejection rules and generated
contracts remain authoritative until separately approved implementation.

Authority: Functional Requirements §14 names Guardrails/PII redaction as P2;
NFR-042/044–046/050–053 constrain secrets, authorization, abuse and privacy.
FR-075–077/090–096/130–132 and ADR-004/010/011/020/022/024/025 preserve wire,
provider, retention, accounting and session boundaries. PII inspection consent,
payload retention consent, upstream data-policy disclosure and BYOK/Hybrid
consent are **different permissions**, never interchangeable checkboxes.

Approved first profile design: Owner-enabled, project-wide, deterministic **local input
syntax inspection**, default Disabled. Observe records bounded metadata without
blocking a match; Block denies a match before financial admission. Separately
implemented Redact replaces matches in ordinary text before dispatch with
additional transformation consent. No remote classifier, LLM judge, regex DSL,
custom plugin, output classifier, hosted tool or separate scanner service.

These heuristics do not establish that data is personal, identify an individual,
prove lawful processing, prevent every injection, or guarantee PII-free output.
They must not be marketed as anonymization, compliance certification or ZDR.
Provider refusal and application/tool authorization remain independent layers.

## 2. Verified baseline and concrete gap

| Source inspected | Actual behavior / implication |
|---|---|
| `src/UZLLM.Gateway.Api/Inference/InferenceGateway.cs` | Shared chat/Responses/embeddings path authenticates, parses bounded input, resolves restrictions/limits/candidates, reserves, then may capture raw request and complete response. No guardrail inspector or input transformation. |
| `GatewayOptions.cs`, operation parsers and provider contracts in the same host/modules | Default body bound 1 MiB, configurable up to 4 MiB; bounded normalized text plus existing operation-specific tools/images/formats. Those features are not automatically inspectable by a text guardrail. |
| `src/Modules/Usage/Contracts/PayloadRetentionContracts.cs`; `Infrastructure/PostgreSqlPayloadRetentionService.cs` | Disabled-default opt-in policy, encrypted payload capture, owner-scoped retrieval, shortening/opt-out deletion. This is retention/encryption, not semantic PII redaction. Capture currently checks retention policy, not a durable per-request guardrail prohibition. |
| `src/UZLLM.Management.Api/PayloadRetentionEndpoints.cs`; Usage service above | Policy mutation commits its own transaction before the endpoint calls `audit.RecordAsync`. `PostgreSqlAuditEventStore` then saves separately. Audit failure can therefore follow a committed policy mutation. This source-level ordering risk is not a newly executed fault-test result. |
| `src/BuildingBlocks/UZLLM.Persistence/PostgreSqlRetentionSweep.cs`, `RetentionSchedule.cs` | Approved-reference minimization clears specific old metadata, not arbitrary future guardrail decisions; audit/financial records remain LegalHold. New diagnostic data requires an explicit expiry/read/sweep design. |
| Payload protection, retention endpoint and retention sweep integration tests | Existing encryption/AAD, opt-out/expiry, role/CSRF/no-store, restore and financial-history preservation have coverage. They do not prove atomic policy/audit rollback or any PII detection accuracy. |
| [FRONTEND-009](30_ALERTS_PRIVACY_UX.md), [privacy eligibility design](39_PRIVACY_ELIGIBILITY_SPEC.md), [capability expansion](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md) | Current UI controls retention, not guardrails. Strict privacy and new capabilities are approved specifications, not runtime features or permission to scan more surfaces. |

This source matrix describes the 2026-10-18 design-review baseline. SECURITY-009
was separately promoted on 2026-10-19; see the [corrective task and current validation](46_RETENTION_AUDIT_ATOMICITY.md).
It does not implement a guardrail or qualify new consent/version/receipt rules.

Preserve completed delivery history. Proposed SECURITY-009 closes the existing
retention/audit ordering gap before new guardrail consent depends on it; do not
silently claim PRIVACY-002 or FRONTEND-009 supplied that atomicity. It is a
corrective P0 prerequisite **when payload controls are offered at paid launch**,
not implementation authorized by this P2 specification or a new passed gate.

## 3. Threat and coverage matrix

| Threat / limitation | First-profile decision |
|---|---|
| Keys bypass project policy through another endpoint, mode or request flag | All existing inference operations/modes share the policy gate; no request-level disable/skip or trusted-key exemption. |
| Obfuscated/encoded identifiers or semantic injection | Inspect JSON-decoded text only. Do not recursively decode base64/HTML, execute code, fetch URLs or claim universal detection; known misses remain in the corpus/disclosure. |
| PII split into images, tool arguments, schemas, content-part arrays or future fields | Reject unsupported guarded profiles, do not silently skip fields. No OCR, image fetch or arbitrary JSON inspection. |
| PII split across separate text messages | Each complete field is inspected; cross-message reconstruction is not a first-profile guarantee. Explicitly test/disclose this evasion rather than label it safe. |
| Detector CPU/memory abuse | Fixed reviewed local algorithms, shared request/scan bounds, cancellation and deadline; rate/concurrency admission precedes scanning. No customer regex/backtracking or remote dependency. |
| False positives on examples, shared addresses, order-like numbers | Syntax match is not identity proof; no automatic exemptions. Owner may select Observe or change policy with audit, never a caller bypass token. |
| Output contains generated/reconstructed personal data | Output is uninspected. Ordinary SSE still streams immediately; no output-safety badge or retroactive promise that delivered bytes can be recalled. |
| Diagnostics become a second payload store | No snippets, spans, matched-value hashes, reversible maps, bodies or vectors in decision rows/logs/audit/notifications. |

### First supported request shapes

- Chat: text-only messages with string content for every role. Reject content
  arrays, image URLs, tool definitions/history, `stop` and non-text response
  formats while the policy is enabled. Do not change ordinary Disabled behavior.
- Responses: existing input string or text-message items with string content,
  plus instructions. Reject content arrays, tools/schema controls and future
  state/modality fields. A later GATEWAY-008 implementation needs separate
  guardrail coverage qualification; a capability flag does not widen coverage.
- Embeddings: existing string or 1–32 strings; inspect every element. No token
  IDs or new modality/input controls. Any future dimensions/base64 intersection
  needs explicit qualification, not provider passthrough.

Sampling, output bounds and existing routing fields are validated normally.
Project/key/model/provider IDs select server-configured resources; arbitrary
user metadata/headers are not forwarded to providers or used as an inspection
escape. All human-authored provider-bound text in supported shapes is scanned,
including system/developer/assistant history, not only the last user prompt.
This is bounded preflight screening, not a complete data-loss prevention system.

## 4. Detector and transformation contract

Published local profile `local-pii-v1` initially permits exactly these detector
codes; policy chooses a unique nonempty subset of at most two:

| Detector | Deterministic syntax / explicit limitations |
|---|---|
| `email_ascii_v1` | Candidate at most 254 ASCII characters. Local part 1–64 letters/digits or `._%+-`, no leading/trailing/repeated dot; domain at least two dot-separated labels, each 1–63 alphanumeric/hyphen characters, no leading/trailing hyphen; final label 2–63 letters. Whole-candidate boundary prevents matching a valid suffix inside an oversized/invalid candidate. No DNS, quoted/Unicode address or RFC-completeness claim. |
| `uz_phone_shape_v1` | Literal `+998` followed by nine ASCII digits; ASCII spaces/hyphens may separate digits, total candidate at most 32 characters. Previous/next character cannot be a letter, digit or plus; do not match a valid-length prefix of a longer digit candidate. No national-format conversion, operator allocation, subscriber validation or ownership claim. |

Candidate extraction must be linear/bounded, not a pathological regex. Limits:
256 KiB total inspected UTF-8 text, 256 fields, at most 100 nonoverlapping
matches, 50 ms total local evaluation deadline with cooperative cancellation.
The existing body/context limits still apply; limits are ceilings, **not** a
latency/SLO claim. Too much text/too many matches is incomplete inspection and
fails closed, never silently truncates to an Allow decision. Bound candidates
before allocating substrings and count UTF-8 bytes, not just UTF-16 length.

Email candidate boundaries include adjacent email-alphabet characters and
Unicode letters/digits: never extract an ASCII suffix from a longer invalid or
international candidate. Phone candidates consume the maximal contiguous
digit/ASCII-space/hyphen run after the prefix, excluding trailing separators
from the match; extra digits invalidate the entire candidate. Bound scan work
even when an invalid run exceeds the candidate cap. Detector grammar/algorithm
changes require a new reviewed profile/version and fresh Owner acceptance,
not a silent replacement of `v1` semantics during a deployment.

Each field is inspected independently without rewriting case/whitespace or
normalizing Unicode in the forwarded text. JSON escapes are decoded once by
the existing parser: `\u0040` is an `@` for inspection. No arbitrary decoding,
cross-message concatenation or international-address inference. Resolve overlap
deterministically by start offset, longest match, then detector code; do not
double-count or apply a replacement twice.

| Action | Match | No match | Error / incomplete coverage |
|---|---|---|---|
| Disabled | No scan, existing behavior | Same | Existing behavior |
| Observe | Allow original input; metadata says observed, not redacted | Allow original input | Fail closed, no dispatch |
| Block | 403 before hold/dispatch; generic policy error | Allow original input | Fail closed, no dispatch |
| Redact (separate build step) | Replace spans with `[REDACTED_EMAIL]` / `[REDACTED_PHONE]` | Allow original input | Fail closed, no dispatch |

Redact operates on a separate normalized text copy; never in-place edits the
raw body or sampling/model/tool/security fields. Apply spans right-to-left per
field, preserve all unmatched text exactly, then revalidate transformed field,
aggregate body/context and adapter constraints. Placeholders may increase token
count; no savings promise. No reversible mapping, per-value pseudonym or raw
original-plus-redacted dual store. Temporary buffers are bounded/discarded;
managed strings/browser memory cannot be promised cryptographic erasure.

No automatic repair, rescan loop, model swap, charge/refund, prompt rewriting
or upstream tool call follows a detection. Output bytes, function outputs,
generated images and vectors are not redacted by this profile.

## 5. Owner policy, consent and future wire

Proposed, **not existing**, Owner-only
`GET/PUT /management/v1/organizations/{organizationId}/projects/{projectId}/guardrail-policy`.
GET absent policy returns `{"version":0,"policy":null}`; an immutable Disabled
version retains history rather than deleting consent records. PUT requires
current Owner/session CSRF, nonempty UUID `operationId`, `expectedVersion`,
bounded known policy fields and metadata-only atomic audit. Admin, Developer,
ReadOnly, BillingViewer and API keys cannot mutate it; operators cannot silently
consent for a tenant. No new tenant-MFA requirement is invented from operator MFA.
Application commands enforce the same resource/Owner boundary as HTTP; hidden
UI controls or caller-supplied account/project IDs are not authorization.

```json
{
  "operationId": "00000000-0000-4000-8000-000000000043",
  "expectedVersion": 0,
  "policy": { "action": "Block", "profile": "local-pii-v1", "detectors": ["email_ascii_v1", "uz_phone_shape_v1"] },
  "consent": { "textVersion": "guardrails-v1", "acknowledgeLimitations": true }
}
```

Server-recognized consent copy discloses local inspection scope, false positives/
negatives, unsupported shapes, uninspected output, metadata retention and no
ZDR/legal promise. Observe explicitly forwards matched content upstream; a
successful inspection is not anonymization. Redact additionally requires
`acknowledgeInputTransformation:true`; false/missing fails validation. Each
new enabled policy version requires consent for that exact profile/detector/
action set. Detector upgrades do not silently change the accepted policy.
Disabled PUT uses `policy:{"action":"Disabled"}` and omits consent; no other
fields are accepted for Disabled. Removal/weakening is still explicit/versioned/
audited; consent is not inferred from payload retention, provider-key ownership
or an environment tag. Audit reason uses fixed change codes, not pasted content.

Policy/head/receipt/audit commit together. Unique tenant/project/operation ID
makes an identical mutation replay return its original version/receipt without
another audit event; changed body with the same ID returns 409. Stale expected
version returns 409. No automatic retry with a new ID after uncertain commit;
reload metadata/reuse the same operation identity. Missing session 401, wrong
role 403, foreign project 404, invalid/unknown/duplicate fields 400; no-store on
all outcomes. No policy-saving response claims the feature is operational.

GET returns current `version`, `policy`, nullable `consent` (accepted copy version,
UTC acceptance time and actor ID), and `capabilityStatus:Ready|Unavailable`.
Disabled/absent consent is null; current status reflects deployed support, not
privacy certification. An absent row still uses version 0/policy null. PUT
returns its immutable `version`, `policy`, `operationId` and `auditEventId` receipt;
replay returns that receipt even if the current head later changed. Read the
current head separately after uncertain writes. No consent free text, sample,
match details, IP address or provider secret in these responses.

No inference extension is introduced: request-level `guardrails:false`, skip
headers and per-key exemptions remain unsupported. All project keys inherit the
policy for Managed, explicit BYOK and Hybrid. Only an authorized Owner can
weaken future policy; no request routing, budget, wallet or operator ceiling is
relaxed. Provider-supplied text and client roles cannot modify control policy.

## 6. Retention, audit and financial boundaries

Enabled guardrails require project payload retention **off**. Setting an enabled
guardrail policy or enabling retention while the other is active returns
409 `guardrail_payload_conflict`; neither silently flips settings or deletes
existing data. Owner must explicitly opt out of retention first. The new
guardrail module and Usage policy/capture paths share the project anchor lock
and explicit contracts, not cross-module Infrastructure calls.

Freeze a durable per-request payload-capture prohibition with admitted guarded
work. CaptureRequest/CaptureResponse must check it even if guardrails were later
disabled and retention re-enabled. Reuse a single content-prohibition contract
with the separately approved [strict privacy design](39_PRIVACY_ELIGIBILITY_SPEC.md),
not conflicting copies. Strict privacy input eligibility still applies; a local
guardrail does not qualify a provider/region/account or authorize a cache.
Response caching/custom endpoints/new capability profiles are outside the first
guarded profile until separately reviewed. Legacy Disabled behavior is preserved.
Bind that proof to the tenant/project/request and immutable policy version;
it is write-once and cannot be cleared by later policy updates or runtime state
transitions. Prove that invariant against PostgreSQL, not only a cached boolean.

Policy/consent audit stores actor, tenant/project, version/profile/action,
detector codes, accepted copy version, operation ID and fixed change code;
never input/matches. Existing audit legal hold remains separate from diagnostic
decisions. Decision metadata records correlation ID, tenant/project/key IDs,
operation, policy/profile version, outcome and saturated count (0–100), timing,
expiry and optional admitted request ID. No spans, text hashes, match values,
request body or classifier explanations. Existing financial operation digest
remains unchanged for duplicate suppression; do not duplicate it as PII evidence.

Per-request match/category/timing diagnostics expire under an explicitly approved
schedule using `Retention:DiagnosticDays`; refuse feature activation without its
reference. Reads hide expired records immediately and Worker deletes bounded
batches. No new indefinite decision archive is hidden under financial LegalHold.
Only minimal dispatch policy/version/no-capture proof needed for accounting
provenance follows financial evidence retention, without match details. Restores
must scrub expired decisions, preserve consent revocations and keep traffic
quarantined until current policy/consent state is independently reconciled.
The existing payload restore scrub alone does not implement this new requirement.
Financial/request proof references immutable policy versions, not an expiring
diagnostic decision row. Decision-to-request links must not prevent diagnostic
expiry or cascade deletion into financial evidence.

Blocked/incomplete inspection has **zero reservation, zero provider attempt and
zero customer fee**. It consumes existing RPM/lease work; lease always releases.
A pre-admission denial does not create a financial idempotency claim or ordinary
Usage request. Reusing that request key after correcting input/policy can therefore
admit once; admitted requests retain the existing 24-hour 409/no-body-replay rule.
Each denied HTTP attempt has a new correlation ID; internal event retry uses the
same decision identity, not a second customer charge or high-volume audit event.

After dispatch, actual provider usage for transformed/original text remains
billable. Cancellation, partial output, provider refusal, Unknown usage and DB
finalization failure use existing evidence/reconciliation; never assume scanning
or a failed client response means zero provider cost. No guardrail service fee
or new tariff. Financial example: actual transformed input 14/output 5, rates
1/2 USD micros per token, no cached subset => provider 24; 1000 bp markup plus
fixed fee 3 => Managed 30, BYOK fee 6/external estimate 24. Context/output bound
100/20 at those rates => provider hold 140, Managed 157, BYOK fee hold 17. A
pre-dispatch Block creates none of these holds/fees.

## 7. Hot path, version fences and failures

```text
authenticate / resolve tenant / bounded parse / existing duplicate check
  -> distributed RPM + concurrency admission
  -> authorized operation/model + current project policy/retention snapshot
  -> supported-shape projection + bounded local input evaluation
  -> Block/error: bounded decision record -> safe denial; release lease
  -> Allow/Redact: revalidate text + eligible routes + conservative cost bound
  -> atomic admission: current-version/no-capture fence + decision + financial hold
  -> dispatch fence: policy still current -> native provider call outside transaction
  -> unchanged JSON/SSE / usage evidence / settlement or Unknown recovery
```

Guardrails reads authoritative PostgreSQL policy; Redis cannot override consent.
Disabled/missing policy keeps current behavior. Active policy with missing/
revoked detector profile, unavailable evaluator, incompatible code/gate or
unavailable policy/required decision persistence returns 503; never per-node
fail-open. Registry/profile and consent have to be supported before accepting
a new enabled policy; disabled activation gates return a service error, not a
paper promise of protection. Restore cannot resurrect a previously revoked
consent without independent reconciliation.

Evaluate once outside the financial transaction; no scanner work under wallet
locks. Use existing transaction coordination and a narrow explicit guardrail
admission-proof contract. Shared ordering is operation identity, project content
anchor/current policy, wallet, budgets; configuration/capture paths take the
same project anchor before their policy rows. Preserve wallet-before-budget
ordering and test deadlock/rollback paths. No broad cross-module access exception,
separate database, event bus or generic security workflow engine.

Current policy version and payload prohibition must be committed with admission,
and rechecked at the existing atomic pre-dispatch boundary for each attempt.
Changed version returns 409 `guardrail_policy_changed`, not an unbounded rescan.
If a hold already exists, release it only with proven undispatched state; keep
the claimed operation identity so client retry needs a new key. Once a dispatch
is fenced, an Owner update cannot recall an in-flight packet; it governs later
dispatches. Existing admitted snapshot/no-capture and financial cleanup remain
binding. Never rerun a detector against already-emitted output or silently change
the request midway through fallback.

| Condition | Proposed status / behavior |
|---|---|
| Match under Block | 403 `guardrail_blocked`, generic message, no match/count/offset echo; normal correlation header. |
| Unsupported guarded shape | 400 `guardrail_profile_unsupported`, no hold or provider call; no automatic downgrade to Disabled. |
| Scan text/match bound exceeded | 400 `guardrail_input_limit`, inspection incomplete, no content echo/dispatch. Existing raw body overflow remains 413. |
| Deadline, profile/gate, DB or evaluator failure | 503 `guardrail_unavailable`, no unsafe Allow or health/circuit penalty to the provider. |
| Project policy/retention conflict | 409 `guardrail_payload_conflict`; no silent policy switch or deletion. |
| Version changed before admission/dispatch | 409 `guardrail_policy_changed`; proven-undispatched cleanup only. |
| Existing financial claim | Existing 409 idempotency conflict, no second dispatch/body replay. |
| Client disconnect before dispatch | Cancel work and release lease/any proven-undispatched hold; no claim that provider ran. |
| Failure/Unknown after dispatch | Existing evidence/reconciliation and finalization errors; no new refund/retry rule. |

Block decisions persist in their own short metadata-only transaction. Admitted
Allow/Redact decisions and their request link commit in TX-02 with the hold;
financially rejected admissions need no additional PII diagnostic record.
Decision-write failure prevents dispatch, never returns a success badge. A DB
outage may prevent a durable denial record; return safe failure and bounded
failure telemetry rather than pretending an audit event exists.

Current Managed default and explicit BYOK/Hybrid remain unchanged. Redact uses
the same transformed copy across verified pre-execution eligible attempts;
actual usage/price versions govern settlement. No fallback after any output,
including SSE lifecycle events. SSE is **input-screened/output-uninspected**:
no full-response buffering, no content-dependent stream delay, no `safe:true`
badge. Output blocking/redaction would require a separately priced, bounded
delivery/evidence design and cannot erase already-delivered fragments.

## 8. Ownership, persistence and UX

Future implementation adds at most one small **Guardrails capability module**,
not a service deployment. It follows Domain/Application/Infrastructure/Contracts
and shared FoundationDbContext rules; no SDK or host types in its contracts.

| Owner | Responsibilities / proposed records and constraints |
|---|---|
| Guardrails Domain/Application | Fixed profile/action invariants, consent/version validation, deterministic local evaluation/replacement decisions. Contracts accept bounded text fields plus immutable tenant/project policy snapshot, not native provider SDKs. No wallet, role or provider routing ownership. |
| Guardrails Infrastructure / Persistence | `gateway.guardrail_policy_version`, current head and idempotent mutation receipt; tenant-composite project FK, unique project/version and tenant/project/operation ID. Immutable versions/consent and audit append together; expected-version head update prevents lost updates. |
| Guardrails diagnostic store | `gateway.guardrail_decision`: unique correlation/decision identity, tenant/project FK, optional admitted request link, bounded outcome/count/timing/expiry. Index tenant/project/time/ID and expiry/time/ID. Nonfinancial diagnostic expiry, not append-only financial history; partition only after measured volume. |
| Usage | Durable request no-capture/proof reference and authorized safe projection via contracts. No guardrail body store; normal billing rollups/export must not treat pre-admission denials as paid executions. |
| Gateway | Existing wire parsing, supported-shape projection, sequencing, fences, safe errors and transformed native request construction. No policy decisions in controllers; no detector/financial behavior inside adapters. |
| Management / Projects / Organizations / Audit | Reauthorize Owner/resource, CSRF, project anchor and metadata-only audit/receipts through contracts. Existing retention mutation audit must become atomic before new consent depends on it. |
| Worker | Explicit bounded decision expiry, restore quarantine checks and existing financial recovery; never asynchronous scanning after content was already sent. |
| Frontend | Owner consent/configuration and bounded metadata diagnostics using typed actual APIs; no raw-text “test your prompt” upload, preview store or browser scanner. |

Proposed Owner-only
`GET .../projects/{projectId}/guardrail-decisions?cursor=<opaque>&limit=50`
returns at most 100 unexpired newest-first records with validated scope-bound
cursor and `nextCursor`. Allowlisted fields: decision/correlation/admitted IDs,
operation, UTC timestamp/expiry, action/profile/policy version, outcome and
saturated match count; no bodies, offsets, hashes, account emails or key secrets.
Reads require current Owner authorization, no-store and metadata-only access
audit before data is returned. `limit` is integer 1–100 (default 50); invalid or
foreign-scope cursors return 400 without data. Generic Activity for other authorized project roles can expose a
policy-rejection/transformation status only if its actual backend persists it;
never detailed PII categories or inferred patient/customer identity. Extending
Activity/export is specified/tested in USAGE-009, not assumed from a badge.

Owner UI must show current organization/project/environment as metadata,
supported shapes, input-only limitation, deterministic false-positive/negative
warning, exact copy/profile versions, payload-retention conflict, action and
new-version confirmation. Separate additional Redact consent; no default-on
check boxes or silent profile upgrade. Uncertain writes reload metadata without
automatic new operation IDs; scope changes clear private dialogs/cache. No
matched samples, raw-body downloads, clipboard detection data or URLs carrying
content. Mockup-aligned improved UI, English/Uzbek/Russian, keyboard/focus/error
handling and real API acceptance/screenshots are required in its future task.

## 9. Evaluation, operational and external activation gates

Keep a versioned **synthetic-only** corpus with detector/profile version,
expected syntax matches, action/outcome and explicit expected misses. Include
English, Uzbek Latin/Cyrillic and Russian surrounding text, JSON escapes,
boundaries, line breaks, oversized and adversarial inputs. Report TP/FP/FN/TN
per detector and supported surface; no aggregate accuracy claim from a toy
dataset. Defined syntax fixtures must be deterministic/exact; unsupported or
encoded semantic PII is a documented limitation, not relabeled a true negative
to inflate recall. Do not use live customer payloads to tune or review rules.

Record corpus size/composition, expected-miss count, review owner and acceptance
thresholds before activation. Format heuristic acceptance is conformance to §4,
not an unmeasured population-level recall percentage. Product/security owners
must explicitly accept false positives/negatives for the intended workflow;
otherwise remain Observe/disabled or do not offer the profile. Observe is still
consented processing and never a claim that data was protected from upstream.

Metrics use bounded action/outcome/operation/detector-version registry labels,
not tenant/key/profile UUIDs, matches or function names. Traces/logs exclude raw
content and exception messages containing candidates. Alert on evaluator/time
bound failures, policy/profile incompatibility, decision-write failures, expiry
backlog and sudden outcome shifts. Customer webhooks/Telegram receive no match
values/categories; do not create a content-bearing incident outbox.

Qualify max-size/adversarial CPU/memory and p95/p99 incremental latency with
NFR-010 end-to-end overhead, not only a microbenchmark. Retain 10 RPS/60s and
200-stream distinct-host evidence, version/retention races and fail-closed DB/
Redis/evaluator faults. Rollout gates default off. Disabling a gate with active
policies fails closed; it cannot silently bypass them. Before downgrading to
an old binary that does not understand guardrail policies, deny guarded traffic
at ingress/disable its project keys and prove containment. No magic rollback
guarantee from an unread database flag.

External evidence: lawful purpose/authority and notice/consent wording reviewed
for the customers' use cases; approved diagnostic/consent/backup schedule and
incident owner; workflow-specific false-positive/negative acceptance; production
egress/telemetry/content-sink/restore review. No government-identifier/biometric,
medical, legal or financial regulatory certification is inferred from software.
This optional feature does not replace provider data-policy, payment, HA/PITR
or paid-launch evidence. OPS-006 remains blocked by absent distinct staging hosts.

## 10. Designed acceptance scenarios — not executed feature tests

| ID | Required evidence |
|---|---|
| GRD-001 | Current Disabled/missing policy preserves chat/Responses/embeddings behavior; unknown skip body fields reject and skip headers never override project policy. |
| GRD-002 | Owner/session/CSRF and tenant matrix for policy/read/diagnostics; Admin/operator/API key cannot impersonate consent or read other-tenant decisions. |
| GRD-003 | Expected-version race and identical/conflicting mutation replay; policy/receipt/consent/audit commit atomically, including injected audit failure. |
| GRD-004 | Existing retention mutation plus audit rollback is tested; concurrent retention-enable/guard-enable permits at most one, with no silent deletion/toggle. |
| GRD-005 | Every supported text role/instructions/embedding input is inspected; escaped `\u0040` matches. Content arrays/vision/tools/schemas/stop/new fields cannot silently escape coverage. |
| GRD-006 | Exact email/phone syntax boundaries, Unicode surrounding text, invalid/oversized candidates and synthetic documentation-address false positives; no magic demo allowlist. |
| GRD-007 | Base64/HTML/zero-width/international/semantic and cross-message evasion documented as misses/unsupported cases; never claim universal injection or PII detection. |
| GRD-008 | Observe matches still dispatch original input; Block matches create no financial request/hold/provider call/fee and release leases. |
| GRD-009 | Redact requires extra consent and deployed support; deterministic placeholders, exact unmatched text, overlap/double replacement and post-transform bounds tested. No reversible map or implicit savings. |
| GRD-010 | Raw operation digest/24-hour duplicate conflict remains; denial has no claim and an eventual corrected retry can admit once; admitted concurrent duplicates never double execute/settle. |
| GRD-011 | Byte/field/match/deadline/cancellation and pathological-candidate bounds fail closed without truncation/regex DoS, including Observe. |
| GRD-012 | All Managed/BYOK/Hybrid/provider/model/default/fallback paths inherit policy, permissions, budgets and wallet controls; no explicit-route or saved-key bypass. |
| GRD-013 | Policy/profile change between scan/admission/dispatch rejects stale proof, safely releases proven-undispatched hold; already-fenced calls finish without retroactive capture. |
| GRD-014 | Durable no-capture prevents request/response retention even after policy-disable/retention-enable race; strict privacy/cache intersections never silently weaken either policy. |
| GRD-015 | Synthetic financial arithmetic, low-wallet concurrency and transformed-input provider evidence; known/Unknown, cancel/refusal and DB settlement failure retain normal recovery, not automatic zero/refund. |
| GRD-016 | Redaction/evaluation exception, logs/traces/OTLP/outbox/webhook/Telegram/database inspection finds no sample, matched hash/span or raw text. |
| GRD-017 | Owner decision cursor/read/export isolation, safe generic Activity, immediate expiry and bounded Worker purge; no financial rollup inflation from blocked requests. |
| GRD-018 | Restore quarantine reconciles consent/revocation, expires decisions, scrubs payloads and proves no capture/dispatch before release; existing backup bytes not falsely erased. |
| GRD-019 | Localized accessible Owner consent/conflict/uncertain-write/scope change UX against real backend, no raw prompt form/browser storage, screenshots. |
| GRD-020 | Distinct-host load/fault/content-sink qualification, explicit expected-miss report, alert/rollback containment and safe SSE; provider output remains visibly uninspected. |

Required layers: pure detector/policy/transformation corpus tests; actual
Gateway HTTP/provider fixtures and denial/stream tests; PostgreSQL transactions/
tenant/migration/locking/retention/reconciliation; Redis/cancellation faults;
real-edge browser acceptance; then distinct-host production qualification.
Existing passing suites do not execute GRD-001–020.

## 11. Approved follow-on design — unscheduled, execution approval required

At specification approval, all nine IDs below were proposals. SECURITY-009
was subsequently promoted separately into the executable P0 register on
2026-10-19; its scoped correction/evidence is in document 46. The other eight
rows remain unpromoted P2. Approval of this document
alone does not promote any task or authorize implementation. Every task is
complete only with its listed criteria/tests passing, matching docs/contracts
and a focused commit. Table order is the recommended sequential order.

| Proposed task / priority | Objective / likely files | Dependencies / references | Acceptance, required tests and completion |
|---|---|---|---|
| SECURITY-009 / P0 when payload controls offered | Make existing retention mutation/audit atomic using shared transaction boundary; Usage contracts/Infrastructure, Management payload endpoints, Audit contracts. | Approved SECURITY-007, PRIVACY-002, SECURITY-003; §2/6, NFR-045/050, ADR-011/024/025. | GRD-003/004 fault injection proves audit failure/cancellation rolls back policy/expiry/deletion; endpoint remains Owner/CSRF/no-store, opt-out semantics preserved. PG/HTTP regression and architecture tests; no guardrail feature yet. |
| GUARDRAILS-001 / P2 | Small Guardrails contracts/domain and immutable policy/head/receipt/diagnostic schema; new capability module, Persistence/Migrator/architecture tests. | SECURITY-009; §4–8, NFR-050–052, ADR-024. | Tenant/project FKs, unique version/operation/decision identity, bounded action/profile/consent/expiry contracts and safe rollback; GRD-002/003/017 PG/migration/architecture tests. No public policy or execution. |
| GUARDRAILS-002 / P2 | Bounded local syntax detectors and typed text projection/evaluation result; Guardrails Domain/Application, Gateway projection fixtures. | GUARDRAILS-001; §3/4/9, NFR-046/060. | Fixed versioned email/phone grammar, deterministic synthetic corpus with expected misses and byte/field/match/time bounds; GRD-005–007/011 unit/projection/adversarial tests. No provider calls, customer regex or enablement. |
| GUARDRAILS-003 / P2 | Owner versioned policy/consent API and atomic retention exclusion; Guardrails/Management/Usage/Audit, generated API/client. | GUARDRAILS-002, DX-002; §5/6, ADR-011/025. | GRD-002–004/014 HTTP/PG/replay/concurrency/rollback and generated-contract parity, deployed profile/action gate and consistent project lock order. No inference serving yet. |
| GUARDRAILS-004 / P2 | Observe/Block admission/dispatch fence and durable no-capture proof across existing operations/modes; Gateway/Guardrails/Usage/financial coordination. | GUARDRAILS-003, BILLING-003, OPS-005; §6/7, FR-075–077/130–132. | GRD-001/005/008/010–016 PG/Redis/Gateway/edge tests; no pre-admission fee, policy races fail closed, no provider I/O/scanning in transaction. Feature gates default off, legacy operations unchanged. |
| GUARDRAILS-005 / P2 | Separately consented input-only Redact action; Guardrails/Gateway/provider contract fixtures. | GUARDRAILS-004; §4–7, ADR-020/022. | GRD-009/010/012/014–016 pure/native/PG/edge tests prove exact replacement, post-transform validation, actual token billing and original duplicate digest. No output redaction or new fee. |
| USAGE-009 / P2 | Authorized metadata reads/Activity projection and diagnostic expiry/restore hooks; Guardrails/Usage/Management/Worker/Persistence. | GUARDRAILS-004/005, USAGE-005, DX-002; §6/8, FR-090–096, ADR-011. | GRD-016–018 tenant/Owner/cursor/export/expiry/restore tests; metadata-only, denial not paid usage, expired records unreadable, financial evidence retained. No body sample store. |
| FRONTEND-015 / P2 | Owner guardrail consent/action and metadata diagnostics UX; dashboard/api-client. | GUARDRAILS-003/005, USAGE-009, FRONTEND-009; §5/8, ADR-016. | GRD-019 browser and real API tests/screenshots; backend-first supported-action controls, no raw prompt playground, clear uninspected-output and heuristic warnings, improved mockup-aligned localized accessibility. |
| OPS-011 / P2 | Corpus review, bounded telemetry and guarded rollout/fault/restore qualification; Observability/Worker/deploy/tests/Load. | GUARDRAILS-004/005, USAGE-009, FRONTEND-015, OPS-006; §9, NFR-010/060–073. | GRD-011/013–016/018/020 dated CPU/memory/latency/stream/fault and restore/content-sink evidence plus expected-miss/rollback review. Missing staging means Blocked, not local qualification. |

Detector corpus work can proceed independently of policy HTTP work once its
contracts exist, but table order avoids batching. GATEWAY-008, strict privacy,
cache and custom endpoint feature implementations are **not** prerequisites for
the bounded existing native text profile. If later combined, their own coverage,
consent, egress and financial gates also apply; no automatic feature union.
No output scanning, semantic moderation or remote classifier task is smuggled in.

## 12. References, validation and product review

Official references reviewed 2026-10-18:
[OWASP prompt-injection prevention](https://cheatsheetseries.owasp.org/cheatsheets/LLM_Prompt_Injection_Prevention_Cheat_Sheet.html)
describes layered controls and guardrail limitations, not a complete defense from
one filter; [OWASP logging guidance](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)
supports excluding sensitive content and protecting event data. The detector
grammar, limits, transactions, consent and fee rules above are repository
proposals, not OWASP-certified detection or legal advice. No live customer data
or external scanning service was used in writing this specification.

Draft validation on 2026-10-18, Windows/.NET 10 and Docker disposable PostgreSQL
17/Redis 8; runtime, test and dependency inputs remain at `f2d86d4`:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0, projects up to date. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0, zero errors; existing ASPIRE010 CLI-bundle warning (2m18s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/security-007-20261018` | PASS, exit 0. Sixteen TRX reports: 1028 passed, zero failed/skipped/aborted; PostgreSQL 234/234 (15m55s for that assembly). |
| Static specification checks | PASS: 148 local Markdown targets/14 linked anchors across thirteen documents; balanced fences/one valid JSON example; 20 unique designed scenarios, nine new/unpromoted task IDs and dependency-safe order with seven completed existing prerequisites. Financial fixture arithmetic checked. |
| Scope/history | Only thirteen Markdown paths; other backlog/history rows and launch-gate statuses unchanged. No runtime/test/frontend/configuration/dependency/generated-contract edit. |
| `git diff --check` and staged scope/whitespace review | PASS; task-only documentation changes. |

TRX stays in ignored local artifacts, not Git. No new GRD feature fixture,
browser flow, live PII/classifier, migration or production qualification is
claimed. New GRD scenarios are designed, not run.
At the draft handoff, product review of the profile/trade-offs, consent,
retention/audit corrections and decomposition was pending. Approval-only
closeout follows; feature execution and conditional P0 promotion stay separate.

### Approval-only closeout — 2026-10-18

The user's Continue directly answers the review request for `a2ab220`. It
approves the bounded local input-only Observe/Block profile, separately built
Redact with additional consent, deterministic detector limits/known misses,
Owner/version/replay boundary, retention exclusion, financial/audit rules,
20 designed scenarios and nine-task decomposition. This satisfies specification
review, not implementation, live detection accuracy, legal approval, policy
activation or paid launch. SECURITY-009 remains a conditional P0 corrective
design for the unfixed retention commit-before-audit risk; neither it nor the
eight P2 follow-ons is promoted into the executable register by this approval.

The prior sixteen TRX reports were re-read: **1028 passed, zero failed/skipped/
aborted**, including PostgreSQL 234. Comparison to tested `f2d86d4` confirms
unchanged runtime, test, configuration and dependency inputs. No new restore/
build/test run or GRD feature acceptance is claimed for this approval-only
closeout. Fresh static checks verify 148 local Markdown targets/14 linked
anchors, balanced fences/one valid JSON example, 20 unchanged designed cases
and nine unchanged dependency-ordered follow-on rows. Financial fixture
arithmetic is rechecked. Only thirteen documentation paths change; other
backlog/history rows and launch gates remain unchanged. Whitespace and staged
scope checks pass. OPS-006 still requires unavailable distinct staging hosts;
the retention/audit defect is not fixed. Stop before DX-004 or any follow-on
implementation without separate user approval.
