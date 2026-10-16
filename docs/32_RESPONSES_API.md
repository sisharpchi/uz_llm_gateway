# GATEWAY-005 — Stateless text Responses

## Scope and compatibility

This document preserves GATEWAY-005's non-streaming increment and historical
validation. GATEWAY-006 subsequently enables `stream=true` for the same input
subset; its [SSE contract and evidence](33_RESPONSES_SSE.md) supersede the stream
rejection/unsupported statements below without changing stored-state restrictions.

FR-043, FR-046/047/050–053/075–077/100–106/130–132; NFR-030–035/040–046/
050/060–063/066/070–084; ADR-004/007/010/020/022/024.
User explicitly selected this P1 task while OPS-006 remains externally blocked.
DX-002's generated contracts and BILLING-003's durable recovery remain in use.

`POST /v1/responses` is an **OpenAI-style, stateless non-streaming text subset**,
not the entire upstream Responses service. It translates input into the existing
provider-neutral `ProviderChatRequest` and uses the existing native chat adapters.
No OpenAI SDK types, new provider endpoint, conversation database or financial
state are introduced. Chat's public wire behavior remains unchanged.

Protocol basis reviewed 2026-10-16: [official Responses migration guide](https://developers.openai.com/api/docs/guides/migrate-to-responses)
and [create reference](https://developers.openai.com/api/reference/resources/responses/methods/create).
Their full tool/state/modality surface is intentionally not advertised here.

| Capability | This increment |
|---|---|
| Model | Required active canonical code or permitted provider-prefixed code. |
| `input` | Required nonblank string, or 1–256 messages with `role` (`system`, `developer`, `user`, `assistant`), `content`, optional `type: message`. |
| Message content | Nonblank string or 1–64 `{type: input_text, text: ...}` parts; max 1,048,576 characters per text, plus existing total body cap. |
| `instructions` | Optional nonblank string, prepended as system guidance; instructions plus input <=256 messages. |
| Sampling/output | Optional numeric `temperature` 0–2, `top_p` >0–1, integer `max_output_tokens` 16–Int32.MaxValue, additionally bounded by Catalog/context. Omitted limit uses Catalog maximum. |
| `stream`, `store` | False or omitted only. Omitted `store` is **false here**, unlike OpenAI's stored-response default. |
| Routing/modes | Existing `uzllm` strategy, explicit granted BYOK key, explicit Hybrid consent and bounded fallback models; no new defaults or privileges. |
| Providers | OpenAI, Anthropic, Google/Gemini, DeepSeek through existing chat adapters and their capability/parameter checks. DeepSeek still rejects non-default top_p; no unsupported supplier feature is silently enabled. |
| Output | One assistant `message` with `output_text` and/or `refusal`; `resp_`/`msg_` identifiers derive from the logical request. |
| Completion | `stop` → completed; `length` → incomplete/max_output_tokens; `content_filter` → incomplete/content_filter. Refusal remains billable when usage is verified. |
| Usage | `input_tokens`, `output_tokens`, their cached/reasoning subsets and total. Missing usage is null, **not zero**. Reasoning/cached subsets are never added twice. |
| Not supported | SSE (GATEWAY-006), embeddings (GATEWAY-007), tools/tool results, images/files/audio, structured-output and reasoning controls, previous_response_id, conversation, background, retrieval/list/delete/cancel operations, metadata/include/passthrough. |

Unknown fields, duplicate JSON property names, unsupported item/content types
and invalid types/bounds return the existing OpenAI error envelope with 400
before reservation or dispatch. The same UUID header can be used on chat and
Responses because idempotency is intentionally operation-scoped, not cross-API.

Provider eligibility still checks the actual mapping and adapter, not this table
alone. OpenAI accepts the normalized text roles/sampling subset. Gemini accepts
temperature <=1, leading system/developer guidance and subsequent user/assistant
messages. DeepSeek also requires that role ordering and top_p omitted or 1.
Anthropic accepts temperature <=1 and translates leading system/developer guidance
through its existing Messages adapter. Published prices, model access,
credentials/quota and health remain required; these are not newly qualified live
provider accounts. Provider families do not authorize swapping different canonical
models without explicit fallback.

Anthropic now implements the existing `ILlmProviderAdapter.Supports` seam by
reusing its native mapper validation, with a provisional ceiling only when
Catalog will fill an omitted limit. Invalid sampling/role shapes are filtered
before financial admission without credentials or HTTP. Valid existing chat
requests use the same native mapping; no provider-specific rule leaks into Gateway.

## Execution and accounting

The shared `InferenceGateway` authenticates the key, resolves its tenant/project,
reads authoritative restrictions, applies distributed admission, builds eligible
routes and rechecks permissions/project restrictions before atomic reservation.
It does not infer tenant identity from Responses input. Project default routing
never enables a BYOK key or Managed fallback implicitly.

The operation identity is `responses`; raw request SHA-256 and the existing
organization/key/operation/UUID claim suppress duplicate execution for 24 hours.
Identical or changed-payload repeats return 409 with `X-Original-Request-Id`;
no response body is retained for replay. One worst-case hold covers permitted
attempts. Provider calls remain outside transactions. Only verified
pre-execution transient rejection permits fallback; timeout/ambiguous execution
does not. Unexpected provider tool/non-text/finish shapes fail safely, preserving
any known usage for billing rather than pretending the supplier charged zero.

Verified evidence is persisted before one atomic settlement/hold release.
Missing usage may still return the generated text with `usage: null` while the
hold remains pending evidence. Settlement failure returns 503
`finalization_pending`, preserving evidence and recovery work. Client cancellation
stops provider I/O; cleanup uses an independent bounded token and leaves unknown
usage to the existing reconciler. No gateway transaction spans upstream I/O.

All Responses results/errors are `Cache-Control: no-store`; existing status/error,
request/provider/model/billing-mode headers apply. Trace operation is
`gateway.responses`, reusing bounded/redacted instruments. Activity/analytics
include these requests via the existing durable usage facts; no new filter/UI.
`store: false` means no Responses conversation/retrieval store, **not** upstream
ZDR or a bypass of the separately consented encrypted project payload policy.
Supplier retention still follows provider/account terms and existing adapters.

## Example

```http
POST /v1/responses
Authorization: Bearer <issued-project-key>
Content-Type: application/json
Idempotency-Key: 11111111-1111-4111-8111-111111111111

{"model":"<published-canonical-code>","instructions":"Be concise.","input":"Hello","max_output_tokens":32,"store":false}
```

Read text at `output[0].content[]` entries whose `type` is `output_text`.
`output_text` is an SDK convenience, not an extra top-level wire field. Clients
must handle refusal, incomplete status, nullable usage and normalized errors.
Use [versioned artifacts](api/README.md) for exact schemas and generated types.

## Acceptance evidence

Repository validation on 2026-10-16 (Windows/.NET 10/Docker Desktop). The initial
checkout was clean at `4ef510c7bc8f10710d95521340c2a0881c9d6c79`; acceptance
artifacts identify that base revision plus the reviewed task diff, not a deployed
release or the subsequent task commit. All required repository checks below passed;
this task completion is not a paid-launch gate pass.

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; existing ASPIRE010 warning only |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/gateway005` | PASS: 863 passed, 0 failed, 0 skipped across 16 projects; includes Gateway 255, PostgreSQL/Redis 220, architecture 15 and Aspire 2. TRX files remain ignored local evidence. |
| Gateway filtered `FullyQualifiedName~Responses`, Release | PASS: 57 passed, 0 failed/skipped, including four actual native adapters |
| PostgreSQL filtered `FullyQualifiedName~Responses`, Release | PASS: 2 passed, 0 failed/skipped |
| Anthropic contract project, Release | PASS: 17 passed, 0 failed/skipped (four new eligibility cases) |
| `npm ci --prefix frontend/apps` | PASS; existing development-only advisory, not a clean full dependency-audit claim |
| `npm run api:generate --prefix frontend/apps` | PASS; Gateway schema/types and serializer examples regenerated; Management artifacts unchanged |
| `npm run api:check --prefix frontend/apps` | PASS; no schema/client/example drift |
| `npm run api:test --prefix frontend/apps` | PASS: 13 tests |
| `npm run api:types --prefix frontend/apps` | PASS |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin; existing >500 kB dashboard bundle warning |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: 0 vulnerabilities in production dependencies; no package upgrade |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: 5/5 unchanged-assertion real-stack suites, including the added GATEWAY-005 block |
| Local Markdown links | PASS: 70 links across task documentation |
| `git diff --check` | PASS |

The initial image-build/acceptance run (`uzllmacc225a8213`) had a pre-existing
registration UI 5-second assertion timeout **before** any Responses call while
the full host build was active. The request was canceled during cold PostgreSQL
connection setup; the other four acceptance suites passed. Its summary/trace
remain under ignored `artifacts/acceptance/gateway005-initial/`. No assertion,
timeout, retry policy or application startup code was weakened. With host build
finished, the same five suites passed against a fresh disposable stack using
the just-built images: `uzllmacc7dd4f2f2`, `2026-10-16T05:36:51.054Z` in
`artifacts/acceptance/summary.json`. Both stacks cleaned up; existing user
PostgreSQL/pgAdmin containers were preserved. This records the initial cold-start
qualification limitation rather than presenting every attempt as successful.

No migration, new configuration, dependency upgrade, provider/payment credential,
financial module change or root frontend mockup change was required. The actual
customer UI remains unchanged; generated Gateway types and test fixtures evolve
with the new wire contract. Responses streaming is still GATEWAY-006, not a stub
advertised as supported. Live provider/merchant, two-host load, secret rotation,
HA/PITR and paid-launch gates remain unpassed/external as documented in
[production readiness](18_PRODUCTION_READINESS.md).

| Requirement | Observable regression evidence |
|---|---|
| Explicit capability matrix | Matrix above; `Responses_parser_preserves_instructions_order_sampling_and_request_only_routing`, `Responses_message_bound_includes_instructions`, `Responses_parser_rejects_invalid_or_unsupported_input_items`. |
| Provider isolation/translation | `Responses_text_translates_through_real_native_adapter_and_response_writer` uses actual OpenAI/Anthropic/Gemini/DeepSeek adapters with native synthetic HTTP fixtures; `Admission_support_check_reuses_native_validation_without_network_or_credentials` pins Anthropic's eligibility checks. |
| Idempotent reservation/settlement | `Responses_concurrent_duplicate_admission_and_settlement_post_one_charge` uses PostgreSQL, concurrent operation claims and finalizations; checks one UsageCharge, released remainder, independent chat identity and no foreign-tenant claim lookup. |
| Unknown usage/reconciliation | `Responses_unknown_usage_stays_reserved_then_reconciles_once_without_zero_cost_assumption` checks a retained hold and later reconciled usage; `Responses_unknown_execution_and_settlement_failure_never_release_or_retry` covers timeout, missing usage, unsupported provider output and failed settlement. |
| Normalized errors / pre-dispatch safety | `Responses_unsupported_or_invalid_parameters_never_reserve_or_dispatch`, `Responses_authorization_and_financial_gates_precede_dispatch`, `Responses_duplicate_returns_original_id_before_limits_reservation_or_provider`. |
| Cancellation / fallback / modes | `Responses_client_disconnect_preserves_known_or_unknown_evidence_and_cleanup`, `Responses_verified_preexecution_rejection_can_failover_under_one_reservation`, `Responses_byok_and_hybrid_remain_explicit_request_only_choices`. |
| Terminal/usage shape | `Responses_maps_terminal_status_refusal_and_usage_subsets`; cached/reasoning counts remain subsets; completed, length, refusal and null-usage serializer examples are schema-validated. |
| Versioned wire/client | `Published_examples_use_real_parser_writer_and_management_serialization`; `Responses schema advertises only stateless text non-streaming with existing security and error contracts`; positive/negative generated TypeScript fixtures reject stream/store true. |
| Real service composition | GATEWAY-005 block in `frontend/apps/dashboard/tests/real-stack.spec.ts` calls real TLS edge, Gateway, Redis, PostgreSQL and native OpenAI adapter against the HTTPS fixture. Checks response text/usage, same/changed replay, SSE rejection, key restriction, exact one 11-micro-USD charge and zero remaining hold. |

Test fixtures never contact the official provider URLs: native adapter unit/contract
fixtures intercept HTTP, while real-stack acceptance uses its isolated HTTPS
provider. No live provider, merchant, separate-host load or HA/PITR claim is made.
