# BYOK provider parity — BYOK-003

## Scope and architecture

FR-055–056, FR-067/105, NFR-041–044/060–063, ADR-010/013/024.
ROUTING-006 and SECURITY-006 are completed prerequisites. Explicit user selection
authorizes this P1 task while OPS-006 remains externally blocked.

Active `google` (Gemini Developer API key) and `deepseek` catalog providers now
use the same encrypted organization keys, explicit project grants, canonical
model restrictions, estimated external-spend holds, wallet-fee budgets and
health boundaries as OpenAI/Anthropic. `ByokCredentialService` extends its
provider allowlist; `FixedEndpointByokVerifier` handles fixed verification I/O.
Native generation/SSE HTTP translation stays inside the existing adapters,
which already consume normalized tenant-aware `ProviderExecutionContext`.
No provider SDK/type or new cross-module implementation dependency is added.

No migration, new DTO/route, production configuration, pricing rule, routing
strategy, frontend or custom endpoint is introduced. Frontend configuration UX
remains FRONTEND-008. Operator-published active mappings/prices and the `byok`
fee-policy version are still required; zero fee does not bypass external caps.

## Fixed verification protocol

| Provider code | Fixed HTTPS GET | Authentication |
| --- | --- | --- |
| `openai` | `https://api.openai.com/v1/models` | Bearer header |
| `anthropic` | `https://api.anthropic.com/v1/models` | x-api-key + anthropic-version |
| `google` | `https://generativelanguage.googleapis.com/v1beta/models` | x-goog-api-key |
| `deepseek` | `https://api.deepseek.com/models` | Bearer header |

Redirects/cookies remain disabled in DI. No key enters a URI/query. A linked
five-second total deadline covers both headers and the error-only body read;
caller cancellation propagates, internal timeout/network failure is Unavailable.
Success is Valid; 401/403 is Invalid; 402/429/5xx/redirect is Unavailable. Google
400 is Invalid only with structured `google.rpc.ErrorInfo` reason
`API_KEY_INVALID`; generic/billing-precondition/malformed errors are Unavailable.
The Google body limit is 16 KiB inclusive. Bodies/messages are never returned,
persisted or logged. Each test makes one GET, without retry, outside a DB
transaction; the existing post-I/O transaction reauthorizes and rejects races
with key rotation/disable before recording its safe result and audit.

Public primary sources reviewed 2026-10-13:
[Gemini models.list](https://ai.google.dev/api/models#method:-models.list),
[API-key header](https://ai.google.dev/api),
[Google structured error example](https://ai.google.dev/gemini-api/docs/generate-content/api-errors),
[DeepSeek models](https://api-docs.deepseek.com/api/list-models),
[DeepSeek authentication](https://api-docs.deepseek.com/).
The DeepSeek docs were readable through direct HTTPS when the browsing fetch
timed out. A credential-free GET to `/models` returned 401; this is an ingress
observation only, not valid-account or live inference qualification.

Valid means only that model-list authentication succeeded at that moment. It
does not certify model-specific access, billing eligibility, quota/headroom,
customer invoices or a supplier agreement, and does not reset inference health.

## Admission, health and money

Managed remains the default. BYOK/Hybrid require explicit request selection;
Hybrid managed fallback still requires explicit consent and full wallet hold.
Project/key hard restrictions and operator controls cannot be overridden.
Credential 401/402/429 outcomes affect only the tenant credential circuit;
endpoint failures remain mapping-scoped. Authentication, unknown execution and
partial output never trigger managed fallback. Only eligible pre-execution 429
may do so with explicit consent. Redis eligibility/dispatch failure stays closed.

The existing PostgreSQL transaction checks
`external_spent + external_reserved + new_hold <= spend_limit`. Settlement
records catalog-priced external spend and charges only the BYOK platform fee,
releasing unused holds once. Unknown usage retains/reconciles financial evidence;
it is not zero cost. Supplier discounts/taxes/price drift mean this estimated cap
cannot be advertised as a hard guarantee about the customer's provider invoice.

## Acceptance evidence

The task's repository checks passed. They qualify this implementation, not live
provider accounts or production launch gates.

| Requirement | Exact test evidence |
| --- | --- |
| Gemini/DeepSeek BYOK parity | `New_native_adapters_resolve_encrypted_tenant_keys_for_chat_and_sse_only` (real PostgreSQL resolver + native adapters, fixture HTTP; wrong tenant/disabled keys never dispatch) |
| Fixed verification/auth/status | `VerifyAsync_uses_only_fixed_provider_model_endpoint_and_auth_header`, `VerifyAsync_classifies_status_without_echoing_provider_body`, `Google_400_requires_structured_invalid_key_evidence` |
| Body and deadline bounds | `Google_error_body_is_bounded_and_never_treated_as_valid` (16 KiB and +1 valid-JSON boundary), `Google_error_body_read_obeys_total_deadline_not_just_header_timeout`, `Google_error_json_does_not_trust_an_unknown_response_charset`, `Verification_distinguishes_timeout_from_caller_cancellation` |
| Tenant key restrictions | `Byok_http_requires_session_csrf_manager_role_and_never_returns_secret`, `Org_credential_lifecycle_is_masked_encrypted_audited_and_disabled`, `Credential_grants_require_same_tenant_active_project_and_prevent_cross_project_resolution`, `Restrictions_accept_only_provider_models_and_audited_lifetime_spend_cap` parameterized for new providers |
| Quota/health/fallback | `New_provider_byok_rejections_scope_health_and_require_explicit_safe_fallback`; existing Redis `Credential_401_and_429_do_not_poison_managed_or_other_tenant` and `Global_outage_blocks_all_credentials_then_admits_one_cross_node_probe` |
| Spend and settlement | `Byok_zero_fee_external_cap_is_atomic_without_wallet_credit_and_replay_adds_no_hold`, `Byok_verified_usage_spends_external_cap_but_wallet_only_pays_platform_fee` parameterized for new providers; `New_provider_byok_spend_denial_never_dispatches` |
| SSE/unknown safety | `New_provider_byok_chat_and_sse_keep_tenant_context_and_settle_once`, `New_provider_byok_partial_stream_never_replays_or_releases_unknown_cost` |
| Adapter boundary | Existing `UZLLM.Architecture.Tests`; no production adapter SDK/type dependencies added |

All test credentials and HTTP responses are synthetic; no live credential,
paid inference or merchant operation is used. API wire types are unchanged;
artifact drift checks passed without regeneration. Internal test research/review
files stay in non-stageable Git metadata. Python static pairing was unavailable;
no line/branch coverage, mutation score, hosted CI or live provider qualification
is claimed. No frontend code changed, so no new UI/screenshot is claimed.

### Commands — 2026-10-13

Windows/PowerShell, SDK 10.0.401; existing xUnit/VSTest packages, Docker-backed
disposable PostgreSQL/Redis, normalized HTTP fixtures. Command paths below are
relative to the repository root. Focused TRX outputs are in
`artifacts/test-results/byok003-*`; test counts are not a coverage percentage.

| Command | Result |
| --- | --- |
| `dotnet test tests/Contract/UZLLM.Provider.OpenAI.ContractTests/UZLLM.Provider.OpenAI.ContractTests.csproj --configuration Release --filter FullyQualifiedName~ByokVerifierContractTests --logger trx --results-directory artifacts/test-results/byok003-verifier-final2` | PASS: 43, zero failures/skips; includes final UTF-8/deadline/buffer fixtures |
| `dotnet test tests/Contract/UZLLM.Gateway.ContractTests/UZLLM.Gateway.ContractTests.csproj --configuration Release --filter FullyQualifiedName~New_provider_byok --logger trx --results-directory artifacts/test-results/byok003-gateway-final` | PASS: 16, zero failures/skips |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~ByokIntegrationTests\|FullyQualifiedName~Byok_zero_fee_external_cap\|FullyQualifiedName~Byok_verified_usage_spends" --logger trx --results-directory artifacts/test-results/byok003-focused` | PASS: 20, zero failures/skips |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ByokEndpointIntegrationTests --logger trx --results-directory artifacts/test-results/byok003-http` | PASS: 3, zero failures/skips |
| `npm run api:check --prefix frontend/apps` | PASS; OpenAPI/types/serializer examples match |
| `npm run api:test --prefix frontend/apps` | PASS: 10 schema/contract tests |
| `npm run api:types --prefix frontend/apps` | PASS |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; zero errors, pre-existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/byok003-baseline` | PASS: 798 across 16 projects; zero failures/skips; includes 216 PostgreSQL/Redis, 198 Gateway, 62 OpenAI/verifier, 32 Google, 31 DeepSeek, 15 architecture and 2 Aspire tests |
| `dotnet test UZLLM.slnx --configuration Release --no-build --list-tests` | PASS: 798 discovered; 16 new Gateway, 4 native tenant and 43 verifier cases included |
| `git diff --check` / `git diff --cached --check` | PASS |

Review strengthened the over-limit fixture to use valid JSON (not a malformed
body that would remain Unavailable even without a size bound), and added a
stalled-body deadline case and untrusted charset case. These protect observable
failure behavior rather than increasing counts alone. OPS-006 and external
provider/secret-distribution launch gates remain independent blockers. Existing
ASPIRE010 does not prevent the local AppHost smoke; it is not a new dependency
or production-readiness claim.
