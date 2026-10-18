# Gateway Capability Expansion Specification

## 1. Status, authority and decision

**GATEWAY-008 · Approved specification · 2026-10-18 · P2 specification only.**
Dependencies GATEWAY-005/006/007 are completed. This document does not add a
route, change validation, publish a capability, alter pricing or approve feature
execution. Until separately implemented and qualified, the current contracts in
[Responses JSON](32_RESPONSES_API.md), [Responses SSE](33_RESPONSES_SSE.md) and
[embeddings](34_EMBEDDINGS_API.md) remain authoritative runtime behavior.

FR-032/040–045 justify capability discovery and bounded parity work; FR-045
makes new modalities conditional on demand, not a requirement to implement every
native provider API. ADR-004/010 govern compatible wire/provider boundaries;
ADR-009/020/022/024 and Domain/Billing retain financial authority. Existing
privacy, budgets, authorization and execution-mode restrictions remain hard
restrictions, never defaults that a new feature may override.

Approved first expansion design: stateless Responses **client-executed function calls**,
separately qualified function SSE, bounded non-streaming JSON-schema output, and
text embeddings with qualified dimensions/base64 encoding. First execution
qualification is OpenAI only; Anthropic/Gemini/DeepSeek chat support does not
prove corresponding Responses feature support. Image/audio/video, hosted tools
and persistent agent state are not scheduled by this design.

## 2. Verified repository baseline

Inspected implementations, not merely filenames:

| Evidence | Actual behavior / gap |
|---|---|
| `src/UZLLM.Gateway.Api/Inference/ResponsesRequestParser.cs` | Stateless text/messages; strict field allowlist, `store:false`, max 256 input messages. Tools, schema controls and images rejected before reservation. |
| `OpenAiResponsesWriter.cs` in the same directory | Text/refusal lifecycle and named SSE events; tool output rejected. Current response advertises no tools. Output accumulation is bounded; terminal response follows financial cleanup. |
| `ChatRequestParser.cs` | Chat already accepts function definitions/history, JSON formats and HTTPS user-image URLs. This is not Responses parity; its approximate image token estimate is not proof of a new modality's financial upper bound. |
| `src/Modules/Providers/Contracts/ProviderContracts.cs` | Normalized tool calls/deltas and JSON format exist; tool definitions lack strict-schema semantics and chat requests lack explicit parallel-tool control. Usage has input/output/cached/reasoning token counters, not image/audio/hosted-tool units. |
| Native adapter wire mappers under `src/ProviderAdapters/` | OpenAI/Anthropic chat tool mapping exists; Google mapper rejects tools; DeepSeek mapper rejects tools and JSON-schema format. A broad provider brand flag cannot grant support. |
| `EmbeddingsRequestParser.cs`, `src/ProviderAdapters/UZLLM.Provider.OpenAI/OpenAiEmbeddings.cs` | Text batch 1–32, each at most 8192 UTF-8 bytes; float-only upstream/output, finite vectors up to 4096 components. No requested dimensions or expected-dimension profile. |
| `GatewayCostEstimator.cs`; `src/Modules/Billing/Domain/RequestCostCalculator.cs` | Context-bound conservative reservation; token-only versioned pricing. Nonempty `ExtraPricingJson` is unsupported, not an extensible metering engine. Cached/reasoning tokens are subsets, not additional charges. |
| `src/Modules/Catalog/Contracts/CatalogContracts.cs`; Gateway `InferenceGateway.cs` | Flat capability flags and mappings; `/v1/models` filters access but has no operation/feature qualification matrix. |
| `src/UZLLM.Management.Api/Catalog/CustomerCatalogEndpoints.cs` | Customer catalog is chat-oriented. Embedding discovery and operation-aware quickstarts need backend work before UI claims. |
| Gateway endpoint registration and existing contract/financial tests | Models, chat, Responses, embeddings only. No image-generation, audio, video, realtime or hosted-tool service. Existing tests prove only the existing subsets. |

## 3. Wire / cost / SSE compatibility matrix

“Proposed” means unavailable today. Unsupported existing-route fields remain
400 before holds/dispatch; nonexistent modality routes remain 404. Qualification
is per operation, feature, canonical model, mapping and version, not provider name.

| Operation/profile | Current wire | Proposed next wire | Cost/evidence | Streaming / fallback |
|---|---|---|---|---|
| Chat text/tools/formats | Existing bounded chat contract | Unchanged | Existing token snapshots | Existing chat rules; no feature expansion inferred |
| Responses text/refusal | JSON and named SSE, stateless | Preserve | Input/output, cached/reasoning subsets | Preserve existing SSE; no fallback after any event |
| Responses function turn | Rejected | Function JSON; later separate function SSE | Normal token price; each model turn gets its own hold and fixed fee | Same-model, same-feature verified pre-execution fallback only; none after output/uncertain execution |
| Responses structured output | `text` control rejected | `text.format` bounded strict JSON schema, non-streaming only | Token price; malformed output can still be billable | No schema SSE or automatic repair/retry |
| Embeddings | Text inputs, float output | Qualified `dimensions`; `encoding_format:float|base64` | Input-token price, one fee per batch; no dimension/encoding surcharge invented | No SSE; no cross-model fallback; exact dimension preserved |
| Images/audio/video/realtime | No dedicated route | Deferred; see §9 | Current pricing/usage models insufficient | No promise of binary, job or realtime protocols |

Existing `uzllm` routing/modes retain their operation-specific restrictions.
Managed remains default; BYOK/Hybrid require explicit request selection and
consents. Never silently use a saved key, enable Managed fallback, select a
different model/vector space or relax project/API-key/provider restrictions.

## 4. Operation qualification and discovery

Catalog owns immutable published feature-profile versions associated with a
canonical model/provider mapping. A profile records operation, supported feature
set, bounded limits, expected/default embedding dimension and allowed requested
dimensions where applicable, adapter profile revision, qualification evidence
reference, effective window and operator publication identity. Supersede/revoke
versions; never edit the historical version attached to a request. No credentials,
customer payloads, provider SDK types or financial balances belong in this row.

Admission requires the intersection of current tenant/project/key permissions,
model/mapping capabilities, published profile, supported adapter behavior,
runtime feature gate and compatible effective price. An advertised flag alone
is insufficient. Missing metadata denies **new** features; legacy calls retain
existing behavior. Feature checks happen before a hold; the selected profile
version is frozen with dispatch evidence. Revocation prevents new dispatch, not
rewriting completed evidence. Recheck the publication/version in the existing
atomic admission/dispatch fence; already-fenced attempts may finish within their
deadline after revocation. Do not promise instantaneous cancellation of packets
already in flight. No provider I/O occurs in the transaction.

Proposed additive discovery, not today's generated API:

```json
{
  "id": "qualified-model-alias",
  "operations": {
    "responses": { "text": true, "function_json": true, "function_sse": false, "json_schema": false },
    "embeddings": { "float": false, "base64": false, "dimensions": [] }
  }
}
```

Retain legacy fields. Management project catalog and Gateway `/v1/models`
derive the same authorized operation intersection; omit unsupported operations
or advertise false, never infer live health/quota/wallet eligibility. Embedding
entries expose default dimension and qualified dimension set, never upstream
secrets/endpoints/private evidence. A model may expose a supported operation
without exposing an unsupported one. New query/filter fields must be explicitly
specified in CATALOG-006 and regenerated contracts before UI consumption.
Legacy text/float discovery derives from existing eligibility; absence of a new
profile must not falsely hide an already supported legacy operation.

## 5. Responses client function contract

### JSON request and stateless history

Proposed `tools` accepts at most 16 flat definitions
`{type:"function",name,description?,parameters,strict:false}`. Name is 1–64 ASCII
letters/digits/underscore/hyphen; optional description is at most 4096 UTF-8 bytes;
parameters is a bounded JSON object (16 KiB UTF-8, depth 8). Reject unknown/
duplicate fields, duplicate names and hosted-tool types. Omitted `strict` means
false in this bounded profile; `strict:true`
remains unsupported until separately specified. Do not claim native strict-tool
semantics from the existing normalized DTO. No schema fetching or execution.

`tool_choice` permits `auto` (default with tools), `none`, `required`, or
`{type:"function",name}` referencing a declared tool. With no tools only omitted
or `none` is valid. `parallel_tool_calls` is omitted or false; true is rejected.
Extend the minimal normalized request and native mapping to actually request
nonparallel execution, not merely echo false in the public response.

Keep current text input shapes and allow explicit history items of type
`function_call` and `function_call_output`. Calls carry `call_id`, `name` and
`arguments`; outputs carry the matching `call_id` and string `output`. Require
a preceding call in the submitted history, no duplicate/orphan output or
ambiguous outstanding parallel calls, at most 32 pairs within the existing 256
input-item bound. Historical names must be declared; call IDs are nonempty
bounded opaque strings (at most 200 characters), never authorization evidence.
Every submitted historical call must have exactly one subsequent output before
the next user/model turn; an unresolved historical call is rejected pre-dispatch.
Historical argument/output strings are each at most 64 KiB UTF-8. Map history
to normalized assistant/tool messages; no server response lookup, conversation,
`previous_response_id`, background mode or stored execution state.

Per new response allow at most one function-call output item, optionally one
text/refusal message. The function item has a stable opaque item `id`, separate
`call_id`, declared `name`, complete `arguments` JSON string and status. Arguments
must be a valid JSON object without duplicate members, depth at most 8 and at
most 64 KiB UTF-8. This is syntax/bound validation, **not** proof of argument
schema conformance, authorization or execution safety. Refuse extra calls and
unsupported output instead of silently dropping/truncating them.

Minimal future request (not accepted by today's parser):

```json
{
  "model": "qualified-model-alias",
  "input": "What is the weather in Tashkent?",
  "tools": [{ "type": "function", "name": "get_weather", "parameters": { "type": "object", "properties": { "city": { "type": "string" } } }, "strict": false }],
  "tool_choice": "auto",
  "parallel_tool_calls": false,
  "store": false
}
```

History call/output allowlists are respectively `type,call_id,name,arguments`
and `type,call_id,output`, with optional bounded `id` and `status:completed` on
the call so a completed output function item can be resubmitted. Reject other
fields/statuses. Existing text-message shapes remain unchanged. Both historical
and new arguments obey the syntax/depth/bounds above. Existing total HTTP body
and context limits take precedence even if each item passes its local bound.

The client owns tool execution and must authorize/validate arguments itself.
The gateway never invokes a function, URL, shell, code interpreter or MCP
server. A valid upstream tool finish completes this model turn; it does not
create `required_action` or an unsettled financial tool job. A continuation is
a new request with a new operation identity/idempotency key, hold and fee.
Reusing the previous operation key retains the current 409 duplicate policy;
no result replay or exactly-once client-function execution is promised.

### Separate function SSE profile

Preserve existing Responses lifecycle/sequence ordering, not chat chunks or
`[DONE]`. Add `response.output_item.added` for a function item, then
`response.function_call_arguments.delta`, `.done`, and `response.output_item.done`.
Keep stable item/call IDs and output indices; function items have no text
content-part events. Assign the at-most-two output indices by first appearance,
keeping text/refusal in one message and the call in its own item. Never reindex
an emitted item. Buffer identity metadata up to 4 KiB within the existing provider
deadline until native name/ID are available, then forward deltas promptly.
Missing/oversized identity is invalid output. Enforce the same single-call,
64 KiB argument and existing 1,048,576-character total text/refusal bounds.

Emit arguments `.done` only after valid complete arguments and native completion.
Length/filter finishes are incomplete, never executable completed tool items;
invalid JSON/identity/index, extra calls, overflow or late/duplicate terminal
events produce a normalized failure rather than a fabricated complete item.
Partial argument bytes alone never authorize execution. `response.completed`
still requires successful financial cleanup; known usage survives output errors.
No fallback after any SSE bytes, including lifecycle events. Disconnect uses
the existing independent bounded cleanup path; inability to recover usage is
Unknown, not zero. Test slow readers and fragmented UTF-8 without whole-response
buffering. Retain current write/cleanup time budgets rather than new unbounded
background work.

## 6. Structured JSON output profile

Proposed non-streaming `text.format` accepts default text or
`{type:"json_schema",name,schema,strict:true}`. First version rejects tools plus
schema, schema plus SSE, `response_format` aliases, implicit JSON mode and
reasoning controls. OpenAI is first qualified; other adapters' chat formats do
not establish this operation's qualification.
`text` accepts only `format`; text format is `{type:"text"}` or the schema
shape above. Schema names use the same 1–64-character naming alphabet as tools;
unknown/duplicate fields and omitted/false schema strictness reject before hold.

Use a deliberately small local schema subset: root object; objects with
`properties`, `required` and `additionalProperties:false`; primitive
string/number/integer/boolean/null types; arrays with one `items` schema; and
typed scalar enums. Every declared property is required (nullable type unions
may include null); reject absent/unknown required names. Maximum schema 16 KiB
UTF-8, depth 8, 256 nodes, 64 properties total and 64 scalar enum values, each
string at most 256 characters. Nullable unions contain exactly one primitive
type plus null. Reject unknown keywords, references, combinators, regexes,
remote resolution and unsupported constraints before dispatch. Tool parameter
syntax in §5 does not inherit these strict-output guarantees.

Return JSON as ordinary `output_text`, not a new parsed SDK object. Bound
generated structured text to 64 KiB UTF-8 and validate locally using the same
subset, depth at most 32 and at most 8192 visited value nodes. Reject duplicate
members; never permit a resolver or unbounded validation walk. Only a valid
stop-completed document claims schema success. A refusal
is valid refusal content, not JSON; length/filter is incomplete and may contain
partial JSON. Invalid supposedly complete output yields normalized 502
`provider_output_invalid` after retaining usage and attempting settlement.
If financial finalization fails, existing 503 `finalization_pending` takes
precedence; a validation error must not conceal an unreconciled financial state.
Do not repair, retry, refund automatically or double-charge reasoning tokens.
Schema examples are synthetic and do not establish live model availability.

```json
{
  "model": "qualified-model-alias",
  "input": "Return a greeting.",
  "text": { "format": { "type": "json_schema", "name": "greeting", "strict": true, "schema": { "type": "object", "properties": { "message": { "type": "string" } }, "required": ["message"], "additionalProperties": false } } },
  "stream": false,
  "store": false
}
```

## 7. Embedding dimension and encoding profile

Preserve text input limits, batch semantics, explicit modes and input-only
pricing. `dimensions` is an integer in 1–4096 and must belong to the published
mapping/model allowlist (at most 16 values). Omission means its qualified default
dimension for calls using new controls. Untouched legacy float/default calls
retain their existing validation; no mandatory qualification backfill is implied.
First adapter is native OpenAI; only a model independently qualified
for dimension control accepts it. Forward requested dimensions upstream; never
truncate, pad, quantize or project vectors in the Gateway. Require exactly the
expected dimension for every result; mismatch is malformed output with separate
usage evidence, not permission to infer zero cost.

Always request float vectors upstream. Public `encoding_format` is float
(default) or base64 of finite IEEE-754 binary32 components in little-endian order.
For `[0.25,-0.5]`, bytes `00 00 80 3e 00 00 00 bf` encode as `AACAPgAAAL8=`.
Enforce cardinality/index/order, finite values, expected dimensions and existing
8 MiB upstream response cap before public serialization. Worst raw vector bytes
are `32 * 4096 * 4`; base64 bound is `32 * 4 * ceil((4096 * 4) / 3)` plus bounded
JSON framing. Encoding is neither compression nor encryption; never log vectors.

Dimensions and encoding participate in the existing request digest. Same-model
eligible fallback must preserve exact requested dimension and feature contract;
each mapping has its own frozen qualification version, not the first mapping's
profile ID. Cross-model fallback
remains forbidden. Equal dimensions do not imply equal vector spaces or model
revisions. Existing clients that omit controls retain float/default behavior;
legacy default-vector calls do not require an invented new price or discount.
No token-ID input, SSE, alternate provider, vector database or inference cache.

```json
{
  "model": "qualified-embedding-alias",
  "input": ["first text", "second text"],
  "dimensions": 256,
  "encoding_format": "base64",
  "uzllm": { "routing": "priority" }
}
```

This example requires 256 to be explicitly qualified for that model/mapping;
it is not evidence that a currently seeded model supports dimension reduction.

## 8. Financial, persistence and security invariants

Keep operation-first/wallet-before-budget locking, one logical reservation,
versioned prices/fees, append-only evidence/ledger and outbox. Reuse TX-02
admission, TX-04 usage evidence and TX-05 settlement boundaries defined in
Billing; never hold a DB transaction over provider I/O. Redis supplies limits
and leases, never spend authority. Duplicate operation identity remains 409
within the existing retention window, independent of new feature controls.

Tools/schema/history count toward provider input context. Cost reservation
remains the conservative context/output bound across eligible attempts, not the
approximate input estimate. Function argument output is ordinary output tokens;
cached/reasoning counters remain subsets. Embeddings remain input tokens only,
with one fee per batch. Nonempty extra-pricing dimensions remain denied until
a separately approved metering/price/reservation design exists.

Deterministic financial fixtures, in USD micros (synthetic, not live rates):

| Fixture | Expected result |
|---|---|
| Input 12, output 5, cached-input subset 4, reasoning-output subset 2; per-million rates input 1,000,000 / output 2,000,000 / cached 500,000 | Provider cost `(12-4)*1 + 4*0.5 + 5*2 = 20`. No extra reasoning charge. |
| Above usage, 1000 bp markup and fixed fee 3 | Managed 25; BYOK platform fee 5 and external estimate 20. Explicit existing mode/consent rules still apply. |
| Context bound 100, output cap 20 at those input/output rates | Provider hold 140; Managed hold 157; BYOK platform hold 17, plus existing external exposure accounting. |
| Embedding context bound 4 per input, two inputs; rate 1,000,000, no markup, fixed fee 2; actual input usage 5 | Provider bound 8, wallet hold 10, actual charge 7. Dimension/encoding changes do not change the tariff. |

Cancellation/timeout after dispatch, partial SSE and output validation failure
do not prove zero provider cost. Preserve known evidence; otherwise retain
Unknown/reconciliation semantics and aging alerts. If DB evidence/settlement
fails, do not return financial success or release based on missing usage.
Existing bounded recovery, reservation reconciliation and fail-closed admission
apply. Provider outage/429 fallback must be verified nonexecution and within
existing attempt/deadline/hold; never amplify retries or rerun a client tool.

Proposed persistence is small: immutable Catalog feature profiles/publication
head, tenant-safe profile/version reference and bounded feature flags on request
attempt/evidence read models, embedding dimension/encoding metadata. Catalog
profile identity includes model/mapping FK, operation and version; uniqueness
and non-overlapping publication windows prevent ambiguous selection. Preserve
foreign keys and historical versions during revocation. Actual migration belongs
to CATALOG-005, not this task. That foundation defines the minimal frozen
dispatch/evidence profile reference; each feature task writes it before serving.
USAGE-008 adds the projection/read/export surface, not a later repair of missing
financial provenance. No new wallet, tool execution, conversation or
payload tables. Financial evidence remains authoritative, analytics asynchronous.

Gateway owns HTTP parsing/writing and feature admission; Providers contracts
own minimal normalized data; adapters own native translation and usage, never
wallet policy. Catalog owns qualification; Billing owns money; Usage owns
tenant-filtered metadata reads; Worker owns existing recovery. Management owns
operator publication APIs with RBAC/recent MFA/CSRF/audit and customer reads.
No SDK types cross adapter boundaries. Preserve platform/BYOK secret encryption,
bounded I/O/cancellation, tenant isolation and default payload nonretention.
Tool names/arguments/results/schema contents/vectors are sensitive payloads,
not metric labels. IDs do not grant retrieval or tool execution. No new user-
supplied outbound URL or remote schema resolution is introduced.
Metrics use bounded operation/feature/outcome codes; profile/request/tenant IDs
belong in authorized metadata/traces, not new high-cardinality metric dimensions.
Alert on feature validation/Unknown/finalization failures and use observed
latency/TTFT for qualification without logging argument or schema contents.

## 9. Deferred modalities: entry gates, not hidden backlog

| Capability | Why not scheduled / evidence needed before a new task |
|---|---|
| Responses vision / inline images | Existing Chat HTTPS vision is unchanged. Require exact input transport/size policy, eligible adapter/model evidence and a conservative image/pixel/context price bound; `imageCount*1000` alone is not such proof. No Gateway fetch/SSRF surface without separate security design. |
| Image generation/editing | Demand evidence plus per-image/quality/resolution/count tariffs, maximum provider exposure, cancellation/partial charging, binary upload/output retention/security and public wire contract. Current token-only calculator is insufficient. |
| Speech/transcription/audio input | Duration/audio-token metering, upload/body caps, usage provenance, streaming/cancellation charging and privacy policy. No invented conversion to text tokens. |
| Video/realtime | Product demand and separately bounded job/session protocol, durable lifecycle, lease/timeout/cancellation cost ceiling and recovery; WebSocket/job semantics are not SSE parity. |
| Hosted web/file/code/MCP tools | External side effects, extra tool price units, egress/auth/SSRF and consent need separate design. Client function DTOs grant none of these. |
| Stateful Responses/background/retrieval | Retention, tenant lookup/ownership, cancellation, durable jobs and billing/replay policy require separate tasks. Existing `store:false` remains. |
| Other advanced controls | Strict function schemas, schema SSE, reasoning effort, token-ID embeddings and additional providers require their own compatibility/usage proof, not passthrough fields. |

No image/audio/video implementation tasks are proposed without these inputs.
This is an explicit disposition of conditional FR-045, not removal of demand-
driven scope. Commercial model availability, quotas, live usage/cost parity and
data-policy attestations remain external activation evidence, not fixture tests.

## 10. Designed acceptance fixtures — not executed feature tests

| ID | Required observable evidence |
|---|---|
| GWC-001 | Current text JSON/SSE/float contracts remain unchanged; unknown/duplicate fields and unimplemented feature controls return 400 before hold/dispatch. |
| GWC-002 | Operation/profile/model/mapping/adapter/gate intersection denies unsupported features; revocation and stale publication cannot dispatch. Legacy no-feature calls remain compatible. |
| GWC-003 | Tenant A cannot discover/use models or request metadata excluded by its permissions or use B's credentials; only operators publish shared Catalog profiles. Explicit routing cannot bypass project/key/budget/operator restrictions. |
| GWC-004 | Function JSON maps declared name/arguments, single-call control and native tool finish; stable item/call identity, normal token charge. |
| GWC-005 | Orphan/duplicate history output, undeclared name, oversized arguments, duplicate JSON members and unsupported strict/hosted/parallel tools fail safely. No tool side effect occurs. |
| GWC-006 | New stateless continuation gets new fee/hold; replayed operation key yields 409 without second dispatch/charge, including concurrent copies. |
| GWC-007 | Function SSE identity/delta/done ordering and sequence survive fragmented UTF-8 and text+call output; first chunks visible before provider finish. |
| GWC-008 | Second call, index mutation, invalid arguments, extra finish, oversized delta and late usage fail without fabricated completion; known usage still settles. |
| GWC-009 | Client/provider disconnect and slow reader bound memory/cleanup; no replay after lifecycle bytes; Unknown never becomes zero. |
| GWC-010 | Valid bounded schema maps correctly; missing required property, unknown keyword/ref/regex, depth/size excess, tools+schema and schema+SSE reject before admission. |
| GWC-011 | Valid stop output passes local schema validation; refusal/incomplete distinguished; invalid complete output is safe 502 while known cost is settled once. |
| GWC-012 | Embedding dimension allowlist/default and finite exact-length vectors enforced; no silent truncation, cross-model fallback or price discount. |
| GWC-013 | Base64 byte-order fixture above round-trips finite floats; max batch/dimension has bounded serialization; float default and ordered indices unchanged. |
| GWC-014 | Invalid dimension/type/encoding rejects before reservation; changed controls participate in operation digest, valid native request uses float plus explicit dimensions only when requested. |
| GWC-015 | Financial arithmetic in §8, cached/reasoning subsets, one batch/turn fee, concurrent low wallet and recurring budgets verified against PostgreSQL. |
| GWC-016 | Provider success plus DB evidence/settlement failure recovers exactly once; Unknown timeout and stale hold alert without zero-charge inference. |
| GWC-017 | Redis/DB faults fail closed where required; verified pre-execution fallback preserves feature/dimension/hold, no hidden retries on uncertain execution. |
| GWC-018 | Catalog/Activity/detail/export show authorized feature/profile/dimension metadata, not arguments/schemas/vectors/credentials or unqualified capabilities. |
| GWC-019 | Localized accessible catalog/quickstart and request detail use real API metadata; tools shown client-executed and embeddings vector-space warning visible. No unavailable controls. |
| GWC-020 | Distinct-host TLS-edge fault/load run measures TTFT/p95/p99, 200 streams, bounds and financial reconciliation; rollout/rollback and dashboards exercised. Single-host fixtures do not pass staging gate. |

Required layers: pure parser/writer/schema/price tests, native HTTP/SSE fixture
tests, PostgreSQL migration/tenant/locking/recovery tests, Redis faults, real
edge/browser acceptance, then separate-host qualification. New cases above are
designs only; existing test success does not execute these future behaviors.

## 11. Approved decomposition — unscheduled, execution approval required

Every row is P2 and requires separate implementation approval/registration.
Complete only when its listed criteria and tests pass, docs/generated contracts
match actual behavior and its focused commit exists. No feature serving before
the relevant qualification/rollout gate; no automatic promotion from this spec.

| Proposed task | Objective / likely files | Dependencies / references | Acceptance, required tests and completion |
|---|---|---|---|
| CATALOG-005 | Versioned operation-feature qualification contracts and PG persistence; Catalog/Providers contracts, Persistence/Migrator. | Approved GATEWAY-008, GATEWAY-007; §4/8, FR-032, ADR-009/010/024. | Immutable model/mapping/version binding, bounded defaults/sets, unique identity/effective publication, revoke history and minimal dispatch/evidence reference; GWC-002/003/012 PG/migration/architecture tests. No endpoint/feature serving. |
| CATALOG-006 | Operator publish/revoke API and authorized operation-aware discovery; Management/Gateway/Catalog/docs/api/api-client. | CATALOG-005, SECURITY-004, APIKEYS-004, PROJECTS-002, DX-002; §4, FR-032/040. | MFA/CSRF/RBAC/audit, expected publication version, legacy compatibility and same authorized catalog intersection; GWC-001–003/018 HTTP/PG/generated contract tests. No claim of live readiness. |
| GATEWAY-009 | Bounded Responses function JSON and stateless history; Gateway/Providers contracts/OpenAI adapter. | CATALOG-006, GATEWAY-005, BILLING-003; §5/8, FR-043, ADR-010/020/022. | Native nonparallel mapping, no server execution, known/Unknown evidence and fee-per-turn; GWC-001/003–006/015–017 parser/native/PG tests and real edge JSON acceptance. Serving gate defaults off. |
| GATEWAY-010 | Separate Responses function SSE writer/native qualification; Gateway/OpenAI adapter. | GATEWAY-009, GATEWAY-006; §5, FR-041/043, NFR-011. | Bounded incremental output forwarding, stable indices/events, safe disconnect/incomplete/errors and no post-output fallback; GWC-007–009/016/017 contract/SSE/PG/edge tests. No hosted tools. |
| GATEWAY-011 | Strict bounded Responses schema JSON; Gateway/Providers/OpenAI adapter. | CATALOG-006, GATEWAY-005, BILLING-003; §6/8, FR-043, ADR-020. | Shared bounded subset validator for request/output, refusal/incomplete behavior and billable invalid output; GWC-010/011/015/016 unit/native/PG/edge tests. No schema SSE/tool combination. |
| PROVIDER-017 | Qualified native embedding dimension control; Providers contracts/OpenAI adapter. | CATALOG-006, GATEWAY-007; §7, FR-044, ADR-010. | Optional normalized dimensions, native float request, exact default/requested vector length, usage retained on malformed output; GWC-012–014 adapter fixtures. No alternate provider or public activation. |
| GATEWAY-012 | Embedding dimension admission/base64 public serialization; Gateway/Catalog/docs/api/api-client. | PROVIDER-017, CATALOG-006, BILLING-003; §7/8, FR-044, ADR-020/022. | Qualified dimensions/encoding, legacy float behavior, bounded binary32 encoding and unchanged token pricing; GWC-012–017 contract/PG/edge tests and generated parity. |
| USAGE-008 | Project/read bounded feature/profile/dimension metadata from durable dispatch evidence; Usage/Persistence/Management/export. | GATEWAY-009/010/011/012, USAGE-004/005, DX-002; §8, FR-090–096/131–132. | Tenant-safe detail/activity/export and legacy nullable metadata, no sensitive payload/metric labels; GWC-018 PG/HTTP/export tests. No new cost authority. |
| FRONTEND-014 | Operation-aware catalog/quickstart and capability Activity UX; dashboard/api-client. | CATALOG-006, USAGE-008; §4/9, FR-032/090–096, ADR-016. | Show only qualified controls, client-tool/vector-space/cost warnings, improved mockup-aligned localized accessible UX; GWC-019 browser/real API acceptance and screenshots. Backend reads precede UI. |
| OPS-010 | Capability rollout, telemetry and distinct-host qualification; Observability/Worker/deploy/tests/Load. | GATEWAY-009/010/011/012, USAGE-008, OPS-006; §8/10, NFR-060–073. | Bounded labels/alerts and default-off rollback with GWC-009/015–017/020 financial/load/fault evidence; exact deployed profile/provider costs recorded. Missing staging is Blocked, not local PASS. |

Recommended sequential order is the table order. After CATALOG-006, schema
JSON and native embedding work are independently executable from function
JSON/SSE, but that does not authorize batching. OPS-006 remains externally
Blocked; these specifications neither replace it nor pass LAUNCH-003. Existing
approved privacy/custom-endpoint/cache specs are not implementation dependencies
for this fixed native-provider expansion; their features must not be implied.

## 12. References, validation and review handoff

Official public documentation reviewed 2026-10-18:
[OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling)
documents client function round trips, nonparallel control and argument SSE
events; [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses)
describes Responses `text.format`, schemas and refusal handling;
[create embeddings](https://developers.openai.com/api/reference/resources/embeddings/methods/create)
documents optional dimensions for supporting models and float/base64 formats.
The stricter bounds, local schema subset, qualification policy, encoding
implementation and accounting rules here are repository proposals, not claims
that every supplier model supports them. No live price/model availability or
commercial/privacy attestation was inferred from reference documentation.

Draft validation on 2026-10-18, Windows/.NET 10 and disposable Docker PostgreSQL
17/Redis 8; runtime/test/dependency inputs remain at `f92b463`:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0, projects up to date. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0, zero errors; existing ASPIRE010 CLI-bundle warning (2m32s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/gateway-008-20261018` | PASS, exit 0. Sixteen TRX reports: 1028 passed, zero failed/skipped/aborted; PostgreSQL 234/234 (16m16s for that assembly). |
| Static specification checks | PASS: 131 local links/nine anchors across eleven documents, balanced fences/four valid JSON examples, 20 unique designed cases and ten unused/unpromoted follow-on IDs. Internal dependency order and ten completed existing prerequisites checked; OPS-006 remains Blocked. Base64 byte order and all financial fixture arithmetic checked. |
| Scope/history | Only eleven documentation paths; other backlog/history rows unchanged. No runtime/test/frontend/configuration/dependency/generated-contract change. |
| `git diff --check` and staged scope/whitespace review | PASS; task-only documentation changes. |

TRX evidence stays in ignored local artifacts, not Git. Existing tests exercise
existing behavior, not GWC-001–020; no new feature fixture, frontend build,
live provider call, migration or production activation is claimed by this task.
At the draft handoff, product review of the bounded profile and decomposition
was pending; approval-only closeout follows. Feature execution remains separate.

### Approval-only closeout — 2026-10-18

The user's Continue directly answers the review request for `352df4c`. It
approves operation-feature qualification/discovery, stateless client-executed
function JSON with separate SSE qualification, bounded non-streaming schema
output, qualified embedding dimensions/base64, the wire/cost/failure matrix,
20 designed scenarios and ten-task decomposition. Image/audio/video and hosted/
stateful execution remain deferred behind the documented entry gates. This
completes specification review, not feature scheduling, price changes,
implementation approval, live provider qualification or production activation.

The prior sixteen TRX reports were re-read: **1028 passed, zero failed/skipped/
aborted**, including PostgreSQL 234. Git comparison with tested `f92b463`
confirms runtime, test, configuration and dependency inputs are unchanged.
No new restore/build/test run or GWC feature acceptance is claimed for this
approval-only closeout. Fresh static checks verify 131 local Markdown targets
and nine linked anchors, balanced fences/four valid JSON examples, 20 unchanged
designed scenario definitions and ten unchanged dependency-ordered task rows.
Financial arithmetic and base64 byte order are rechecked. Other backlog/history
rows and launch gates remain unchanged; exactly eleven documentation paths
change. Whitespace and staged scope checks pass. Follow-on tasks remain
unscheduled; OPS-006's separate-host requirement remains unmet. Stop before
SECURITY-007 or any feature task without separate user approval.
