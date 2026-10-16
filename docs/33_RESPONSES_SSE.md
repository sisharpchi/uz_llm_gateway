# GATEWAY-006 — Responses SSE

## Scope and protocol

FR-043; ADR-004/010; NFR-030–035/040–046/050/060–063/066/070–084.
Explicit user-selected P1 while OPS-006 remains externally blocked. Dependency
GATEWAY-005 supplies the [stateless text input and financial pipeline](32_RESPONSES_API.md).
No new SDK, database, migration, configuration or execution mode is introduced.
Tools, modalities, stored conversations, resume/replay and embeddings remain out
of scope. Provider-specific types stay inside the existing four native adapters.

Protocol reviewed 2026-10-16 against the [official streaming guide](https://developers.openai.com/api/docs/guides/streaming-responses)
and [Responses event reference](https://developers.openai.com/api/reference/resources/responses/streaming-events).
This is a deliberately bounded compatible subset, not the full OpenAI service.

`POST /v1/responses` accepts boolean `stream: true`; `store` remains false/omitted.
Responses use `text/event-stream`, `Cache-Control: no-store`, and
`X-Accel-Buffering: no`. Request/routing/billing headers and operation-scoped
idempotency are unchanged. A duplicate UUID returns 409; streaming does not replay
or resume a previously delivered body. No provider call occurs inside a DB transaction.

Each frame is `event: <type>\ndata: <JSON>\n\n`. Payload `type` matches the event
name; `sequence_number` starts at zero and strictly increases per response.
Response/item IDs and created_at remain stable. This is **not** chat SSE and has
no `[DONE]` marker. Read the named terminal event, not EOF alone, as completion.

| Stage | Events |
|---|---|
| First supported upstream event | response.created, response.in_progress, response.output_item.added |
| First text/refusal part | response.content_part.added with stable content_index |
| Incremental output | response.output_text.delta / response.refusal.delta, flushed immediately |
| Valid upstream closure and successful cleanup | matching text/refusal.done, content_part.done, output_item.done, then exactly one response.completed or response.incomplete |
| Failure after headers | response.failed with safe error code/message and available snapshot; no completed/incomplete terminal |

`stop` maps to completed; `length`/`content_filter` map to incomplete with
max_output_tokens/content_filter reason. Refusal is billable when usage is known.
Finish/usage events are retained internally; they do not prematurely announce a
successful terminal result. Missing/duplicate/unsupported finish, late text,
unsupported tools, native malformed/truncated stream or timeout fail safely.
Known provider usage is preserved even when delivery fails; missing usage is null,
and duplicate/contradictory or invalid token snapshots are unknown evidence,
never fabricated zero. A valid terminal with missing usage may complete with null
usage while its hold remains pending evidence, exactly as the non-streaming API.
Provider-native closure rules still apply: OpenAI can finish with no usage;
Anthropic/Gemini/DeepSeek adapters may reject missing terminal usage as unknown
execution rather than expose a completed stream. No supplier usage is invented.

Terminal snapshots require bounded accumulation: **1,048,576 UTF-16 characters**
total across text/refusal, at most two content parts. Deltas are still delivered
incrementally; exceeding the bound fails rather than truncating as success.
The snapshot is request-local memory, not conversation storage or replay storage.

## Cancellation and finance

Any emitted event irrevocably prevents fallback, including lifecycle-only output.
Before any event, only the existing verified pre-execution 429/capacity rejection
can fail over under one reservation. Ambiguous execution never retries.

Client disconnect cancels upstream enumeration/transport. Evidence and financial
cleanup use the existing independent 30-second token, then record disconnected
delivery and release the admission lease. Verified evidence settles once; unknown
evidence keeps the hold for reconciliation. A crash after evidence persistence is
recoverable from a new scope. Failed settlement never emits completed: HTTP 503
before headers, or response.failed/finalization_pending after streaming begins.
Writing a terminal failure has a bounded 10-second delivery token linked to client
disconnect; a broken connection cannot undo durable evidence or wallet state.

## Validation evidence

Required repository validation completed on 2026-10-16. Local synthetic provider fixtures
do not qualify live provider accounts, two-host staging, merchant verification,
HA/PITR or paid launch; [production gates](18_PRODUCTION_READINESS.md) remain separate.

Windows/.NET 10/Docker Desktop; initial checkout clean at
`d4580bb00b77800cea91318905abe5c16e8ad5e6`. Ignored artifacts identify this base
revision plus the reviewed task diff, not a deployed release or later task commit.

| Check | Result |
|---|---|
| Gateway `dotnet test tests/Contract/UZLLM.Gateway.ContractTests/UZLLM.Gateway.ContractTests.csproj --configuration Release --no-build --filter FullyQualifiedName~Responses --logger trx --results-directory artifacts/test-results/gateway006-focused` | PASS: 95 passed, 0 failed/skipped; includes 38 new SSE cases and all prior Responses regressions. |
| PostgreSQL `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Responses --logger trx --results-directory artifacts/test-results/gateway006-postgres` | PASS: 4 passed, 0 failed/skipped, including two new durable SSE recovery cases. Disposable PostgreSQL/Redis only. |
| `npm ci --prefix frontend/apps` | PASS; existing development-only high advisory, no dependency upgrade. |
| `npm run api:generate --prefix frontend/apps` / `api:check` | PASS; Gateway metadata/types and serializer SSE fixtures generated with no drift. Management artifacts unchanged. |
| `npm run api:test --prefix frontend/apps` | PASS: 14 tests, 0 failed/skipped; five SSE examples validated frame-by-frame. |
| `npm run api:types --prefix frontend/apps` | PASS; supported streaming and unsupported event/storage/embeddings type fixtures. |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin; existing >500 kB dashboard warning only. No actual UI source changed. |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: 0 production vulnerabilities. |
| `node deploy/tests/run-launch-acceptance.mjs` | PASS: 5/5 real-stack suites, 0 failed/skipped, including incremental browser Responses SSE and exact settlement. Rebuilt current images; no weakened assertions/timeouts/retry policy. |
| `dotnet restore UZLLM.slnx` | PASS. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS: 0 errors; one pre-existing ASPIRE010 warning, not suppressed. |
| First full Release test run | Final TRX: 902 passed, 1 failed, 0 skipped across 16 projects. Existing `RedisLimitIntegrationTests.Expired_leases_are_evicted_without_releasing_newer_leases` encountered a 5-second Redis EVAL timeout during parallel suite startup; this is not passing baseline evidence. Original reports remain in `artifacts/test-results/gateway006-baseline`. No limiter/test/timeout change is made. |
| Unchanged Redis lease recheck (`--no-build --filter FullyQualifiedName~Expired_leases_are_evicted_without_releasing_newer_leases`) | PASS: 1 passed, 0 failed/skipped; `artifacts/test-results/gateway006-redis-recheck`. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/gateway006-baseline-recheck` | PASS, exit 0: 903 passed, 0 failed, 0 skipped across 16 projects; Gateway 293, PostgreSQL/Redis 222, architecture 15 and Aspire 2. One bounded full rerun with unchanged code/tests/timeouts/parallelism. |
| `git diff --check` | PASS. |
| Local Markdown links | PASS: 81 links across task documentation. |

During development, native fixture endpoint paths/model fields were corrected
to satisfy existing adapter validation, and a regression exposed loss of a known
provider request ID when a later normalized exception had no ID. Gateway now
retains that ID for reconciliation. Final focused results above include the fixes;
earlier failed development TRX files remain ignored evidence, not passing checks.
The financial schema, accounting rules and provider-native adapter implementations
are unchanged. Bounded terminal snapshots still require large-output/concurrency
memory and latency qualification in OPS-006; local fixtures cannot pass that gate.
The first full-run Redis timeout did not recur in either the isolated or full
unchanged rerun. This is an observed local validation limitation, not a Redis fix
or a claim of production reliability; both failed and passing reports are retained.

Real-stack summary: `uzllmacc97fa65d5`, completed
`2026-10-16T06:37:23.005Z`, ignored `artifacts/acceptance/summary.json`.
The harness explicitly applied existing migrations with its migrator role and
seeded synthetic provider/pricing/FX/payment identities in its disposable database;
no shared database was migrated. Its stack/scratch resources were cleaned up;
the pre-existing user `postgres` and `pgadmin` containers remain running.

| Requirement | Regression fixture |
|---|---|
| No fallback after output | `Responses_SSE_partial_failure_never_falls_back_or_fabricates_success` checks malformed EOF, known/unknown transport failures, in-band errors, tools, late verified rejection and settlement failure. `Responses_SSE_preexecution_rejection_can_failover_before_any_event` pins the permitted opposite boundary. |
| Terminal/unknown usage settled safely | `Responses_SSE_flushes_deltas_before_terminal_and_financial_cleanup` checks stop/length/filter/null usage, sequence numbers, stable IDs, flush-before-finish and no terminal before cleanup. `Responses_SSE_ambiguous_usage_is_unknown_not_a_settleable_snapshot` rejects duplicate/null/invalid counters. |
| Disconnect settled safely | `Responses_SSE_disconnect_cancels_transport_but_preserves_evidence_and_settlement` proves upstream cancellation/disposal, noncancelled cleanup tokens, delivery status, verified vs unknown evidence and lease release. |
| Durable recovery / one charge | PostgreSQL `Responses_SSE_disconnect_keeps_durable_evidence_and_recovers_exactly_one_charge` disposes the request scope after evidence, recovers known/unknown outcomes, retains the unknown hold, reconciles, rejects duplicate execution and checks exactly one settlement/UsageCharge. |
| Actual provider protocols | `Responses_SSE_native_adapters_translate_incrementally_and_reject_truncated_protocol` runs OpenAI, Anthropic, Gemini and DeepSeek native fixtures through their real adapters and the Responses writer. No network reaches real providers. |
| Invalid/empty/order/bounded output | `Responses_SSE_empty_upstream_is_JSON_error_not_a_successful_empty_stream`, `Responses_SSE_writer_rejects_invalid_event_order_or_shape`, `Responses_SSE_snapshot_bound_is_exact_and_never_truncates_as_success`, `Responses_SSE_snapshot_bound_counts_all_deltas_and_content_parts` and `Responses_SSE_refusal_and_text_keep_content_indices_and_terminal_snapshot`. |
| Wire/client parity | `Published_examples_use_real_parser_writer_and_management_serialization` generates five complete SSE fixtures; Node `Responses SSE serializer fixtures match named event schemas and terminal usage semantics` validates every frame. Compile fixtures admit streaming and reject unsupported tool events/storage/embeddings. |
| Real service composition | GATEWAY-006 block in `real-stack.spec.ts` incrementally reads the browser fetch stream through TLS edge, actual Gateway/Redis/PostgreSQL/native OpenAI adapter and synthetic HTTPS provider; checks delta before terminal, replay rejection, exact single extra 11-micro-USD charge and zero remaining hold. |
