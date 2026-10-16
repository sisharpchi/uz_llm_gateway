# Bounded Embeddings API

Task: `GATEWAY-007`; FR-032/044/046–047/050–056/075–077/100–106/130–132;
ADR-004/005/010/020/022. User approval selects this independent P1 task while
OPS-006's distinct staging hosts remain unavailable. This is not a launch gate.

## Supported contract

`POST /v1/embeddings` uses the project Gateway Bearer key, JSON and optional
UUID `Idempotency-Key`. Example (synthetic catalog code, not a live price/model):

```json
{"model":"example-embedding","input":["Hello","World"],"encoding_format":"float"}
```

| Feature | Boundary |
|---|---|
| Input | One nonblank string or 1–32 nonblank strings. Each <=8192 UTF-8 bytes AND catalog context; no truncation. UTF-8 bytes conservatively bound byte-tokenized OpenAI input; this is intentionally stricter than the provider's token limit. |
| Response | `object:list`, canonical model, ordered `data` with `object:embedding`, zero-based index, finite float vector (1–4096 values, consistent dimension across the batch). |
| Usage | `prompt_tokens=total_tokens`, positive provider input count; null if missing/contradictory. Internal output/cache counts zero, no reasoning count. |
| Provider | Native OpenAI `/v1/embeddings`; provider HTTP/auth/error parsing stays inside the adapter. No chat request is sent. |
| Mode | Managed default. BYOK requires explicit granted credential; Hybrid additionally requires `allow_managed_fallback:true`. Existing credential health/caps and all hard project/key/operator restrictions apply. |
| Routing | Deterministic priority or price, with project inheritance and explicit override. Inherited latency/throughput/auto returns 400 unless explicitly overridden; embedding observations never pollute chat windows. |
| Unsupported | Token-ID inputs, base64, dimensions, user passthrough, streaming, cross-model fallback, custom endpoints, other providers. Unknown/duplicate fields fail with 400 before admission. |

No response-body replay or vector store is introduced. All responses/errors use
`no-store`. Existing opt-in encrypted payload retention remains independent;
ordinary logs, metrics and Activity contain no text/vector payloads. The separate
customer Management/dashboard catalog remains chat-only; Gateway `/v1/models`
exposes eligible embedding metadata/prices subject to project/key permissions.

## Catalog and accounting

Publish an active embedding-only canonical model with `Embeddings`, positive
per-input context and `maxOutputTokens:0`, an active OpenAI mapping, and a frozen
input rate with output rate zero, cached rate null, extra pricing `{}`. Credentials
and effective fee policy must exist. A capability flag alone never enables a
chat adapter to generate vectors. Chat rejects zero-output models.

Migration `20261017000000_AddEmbeddingModelLimits` admits zero-output **only** for
embedding-only JSON capabilities; all legacy positive-output rows remain valid
and untouched. Catalog creation requires zero output for embedding-only models.
Use the explicit Migrator, never API/Worker startup. Downgrade fails rather than
rewriting live zero-output models; roll the application forward or keep the
additive schema during an application rollback. Disposable fixture cleanup is
not a production downgrade procedure.

Rollout order: run the Migrator, upgrade every Gateway instance, then publish
embedding models/mappings. For an application rollback, disable embedding
mappings and retain the additive schema; an older Gateway has no embeddings
endpoint. Do not force a schema downgrade or rewrite referenced model history.

One batch uses one request, hold and settlement. Provider upper bound is
`ceil(min(context,8192) * input_count * input_rate / 1_000_000)` (minimum one
micro-USD hold). Managed fee/markup applies once, not per input/vector. BYOK
uses the same external upper bound and existing separately versioned wallet fee.
Actual settlement uses provider input usage and frozen price/fee versions with
existing rounding, budget buckets, hold release, debt and exposure rules. No
new wallet state, per-request cost ceiling or vector-based price dimension.

Operation identity is `embeddings` for 24 hours: the same UUID on chat/Responses
is independent, but repeats/changed embedding bodies return 409 with the original
request ID and no second dispatch/hold. TX-02 admission, provider network outside
transactions, TX-04 durable evidence and TX-05 settlement remain unchanged.
Retrying without the same UUID is a new potentially billable operation; clients
must not automatically retry ambiguous outcomes with a fresh key.

Cancellation stops provider I/O; bounded independent cleanup persists evidence
and settles or retains pending evidence. Timeout/5xx/malformed shape has unknown
certainty, never automatic replay or a zero-cost release. Only verified
pre-execution 429/capacity rejection can use another eligible same-model candidate
under the original hold/deadline (including explicitly consented Hybrid). A pinned
provider is not silently substituted. Settlement failure returns 503
`finalization_pending`, leaving durable recovery work.

## Validation evidence

Completed repository validation on 2026-10-17; no production qualification or
paid-launch claim. All figures below are local, not hosted CI evidence.

| Requirement | Automated behavior evidence |
|---|---|
| Shape | `Embeddings_native_request_maps_text_float_credentials_and_reorders_vectors_without_chat`; `Embeddings_cardinality_mismatch_does_not_return_a_partial_batch`; `Embeddings_vector_dimension_bound_is_enforced_without_truncation`. |
| Input/capability bounds | `Embeddings_parser_bounds_batch_and_UTF8_bytes_not_UTF16_characters`; `Embeddings_catalog_context_limit_applies_per_input_not_to_the_whole_batch`; `Embeddings_only_model_is_discoverable_but_cannot_be_used_for_chat_generation`. |
| Usage and cost | `Embeddings_reservation_covers_all_batch_inputs_but_adds_fixed_fee_once`; `Embeddings_invalid_or_missing_usage_remains_unknown`; `Embeddings_input_only_evidence_survives_scope_loss_and_settles_once`. |
| Authorization and hard financial gates | `Embeddings_permissions_reject_before_reservation_and_dispatch`; `Embeddings_financial_denial_never_dispatches`; `Embeddings_project_price_default_is_inherited_but_cannot_enable_Byok`. |
| Duplicate/concurrent financial outcome | `Embeddings_concurrent_duplicate_admission_and_settlement_posts_one_input_charge`; `Embeddings_duplicate_is_operation_scoped_and_never_reserves_or_dispatches_again`. |
| Cancellation, recovery and replay safety | `Embeddings_disconnect_cancels_upstream_but_keeps_independent_unknown_evidence_cleanup`; `Embeddings_ambiguous_provider_failure_does_not_replay_or_release`; `Embeddings_Hybrid_fails_over_only_after_verified_rejection_under_one_hold`. |
| Wire/client parity | `Published_examples_use_real_parser_writer_and_management_serialization`; Node `embeddings schema bounds text batches and rejects unimplemented encoding and controls`; compile-only float/text acceptance and token-ID/base64 rejection. |

The 2026-10-17 local Gateway contract run passed 359 cases (66 embeddings),
zero failures/skips; the focused PostgreSQL run passed three cases, zero
failures/skips. Reports: ignored `artifacts/test-results/gateway007-contracts`
and `gateway007-postgres-final` (including full-ID targeting). OpenAPI generation/check, Node contract tests (15),
contract TypeScript and both frontend production builds passed. Production
dependency audit passed with zero vulnerabilities; `npm ci` still reports one
pre-existing high development dependency advisory. The unchanged dashboard
bundle is 590.52 kB and retains its existing size warning. Neither warning is
suppressed or repaired as part of this Gateway task.

`node deploy/tests/run-launch-acceptance.mjs` passed all five real browser suites
through Nginx TLS, Gateway, Management, Worker, PostgreSQL, Redis, Mailpit and the
native HTTPS provider fixture. The embeddings increment proves two ordered
vectors, four measured input tokens, exactly four micro-USD debit, full hold
release, one Activity row, replay 409 and permission denial 403 without another
debit. Existing chat/Responses/SSE/customer flows also pass. Artifact:
ignored `artifacts/acceptance/summary.json`, final corrected-ID Compose project
`uzllmacc6fa510eb`, completed `2026-10-16T20:05:06.459Z`; revision records base
`d96be1772ad7ba8732c7c281f3f4c46b64bfd338`; the run exercised the task's then-uncommitted
code, but the artifact does not hash/pin that working tree or define a deployed
release manifest. Stack cleanup completed; pre-existing user
PostgreSQL/pgAdmin containers and `.env` were not modified.

Repository restore and non-incremental Release build passed; build retains one
pre-existing `ASPIRE010` warning. The initial full solution run passed 973 tests
across 16 suites with zero failures/skips. The final corrected-ID run also passed
973 tests across 16 suites, zero failures/skips, including 225 PostgreSQL/Redis
integration cases (15m23s), 359 Gateway contracts, five Catalog unit cases,
15 architecture checks and two Aspire integration cases. Final reports are
ignored `artifacts/test-results/gateway007-baseline-final`; the initial run does
not establish full-ID resolution.

Final review corrected the new migration's timestamp to EF's 14-digit format;
the SQL/constraint is unchanged. The PostgreSQL recovery test additionally
verifies `IMigrationsIdGenerator` name/ID recognition and target-by-full-ID
migration execution. The final PostgreSQL, real-stack and repository runs use
this corrected ID; the earlier default-only run is not full-ID targeting evidence.
The [pinned EF Core ID generator](https://raw.githubusercontent.com/dotnet/efcore/v10.0.3/src/EFCore.Relational/Migrations/Internal/MigrationsIdGenerator.cs)
confirms the format; production code uses the public migration interfaces,
not that internal implementation.

| Command/check | Final result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; existing ASPIRE010 only |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/gateway007-baseline-final` | PASS: 973, failed 0, skipped 0 |
| PostgreSQL project `--filter FullyQualifiedName~Embeddings` (Release) | PASS: 3, failed 0, skipped 0; corrected-ID targeting |
| `npm run api:generate`, `api:check`, `api:test`, `api:types`, `build` with `--prefix frontend/apps` | PASS; 15 Node contract tests; both production builds |
| `node deploy/tests/run-launch-acceptance.mjs` | PASS: final corrected-ID 5/5; isolated resources cleaned up |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production advisories |
| Local Markdown targets / `git diff --check` | PASS: 93 targets; no whitespace errors |

All 32 non-documentation task paths remained hash-identical between final
validation and handoff review. A basic private-key/token-pattern scan of the
44 task files found zero matches; this is not a comprehensive security audit.

Official protocol checked 2026-10-17: [OpenAI Create embeddings](https://developers.openai.com/api/reference/resources/embeddings/methods/create).
The upstream supports broader inputs/encodings/dimensions; this Gateway advertises
only the subset above. Fixtures use synthetic keys, vectors and prices; live
provider commercial/quota/usage verification remains an external launch gate.
