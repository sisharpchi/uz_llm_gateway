# Project defaults — PROJECTS-002

## Confirmed scope

User decision 2026-10-13 resolves the earlier ambiguity: spend means existing
project Lifetime/Daily/Weekly/Monthly budget policies. No per-request cost cap,
max_cost/max_cost_usd, financial table/state, or budget duplication is added.
FR-013, FR-011/060–068/100–104; NFR-044/046/066; ADR-003/004/007/013/015/024.
This explicit P1 selection does not pass the blocked OPS-006 launch gate.

## Contract and ownership

`GET/PUT /management/v1/organizations/{organizationId}/projects/{projectId}/settings`
returns the settings group directly, e.g.:

```json
{"routing":"price","environment":"Production","allowedModels":["gpt-test"],"allowedProviders":["openai"]}
```

PUT replaces the group; null/omitted routing means deterministic priority.
Explicit `uzllm.routing` overrides strategy only; `priority` restores deterministic
selection. Model/provider allowlists are **hard restrictions**, not overridable
preferences. Null list is unrestricted; empty list denies all. Up to 128 canonical
codes/list, normalized like key permissions, max 200 characters/code. Unknown
fields and invalid values return 400. Environment accepts 1–32 ASCII letters,
digits, `_`, `-`; it has no effect on execution, safety, retention or money.

Projects Domain owns normalization/evaluation; Application owns authorization and
transactional replacement/audit (`project.settings_changed`); Infrastructure owns
HTTP/PostgreSQL. Owner/Admin writes (ManageProjects); reads require ReadProjects
including current grants. Session/CSRF/no-store apply. Foreign tenant 403; missing,
wrong path organization and archived project 404. Budget APIs/ManageBilling remain
separate, never writable through this settings endpoint.

## Admission and consistency

Gateway loads defaults with its existing authoritative project lookup, no Redis
policy cache. Eligible models/providers must satisfy project **and** key policies;
explicit provider pins, same-model failover, cross-model fallback, BYOK and Hybrid
cannot widen either. Discovery is filtered too. Hard project restrictions and
active scope are re-read after routing I/O before reservation. Changes afterward
affect future admission, not already admitted streams; no transaction spans
provider I/O. Strategy remains its initial request snapshot, not reranked on every
policy edit. Dependency/read/invalid stored policy failures fail closed.

Managed stays default; a project cannot store a credential or fallback opt-in.
BYOK needs explicit request key selection; Hybrid additionally needs explicit
managed fallback consent and its existing wallet reservation. Environment cannot
override operator pause, rate/concurrency or financial gates. Existing budget
admission/holds, settlement and unknown-usage handling are unchanged.

## Persistence and rollout

Reuse `gateway.project.settings_json.defaults`; existing `{}` inherits legacy
behavior. Atomic JSONB update preserves sibling groups, RPM/concurrency columns
and budgets. Whole-group concurrent PUTs are last-commit-wins, never field merges;
audit and update commit/rollback together. No migration/new configuration needed.
Upgrade all Gateway nodes before exposing settings writes. Old Gateway binaries
ignore restrictions/defaults; do not roll binaries back with active policies
unless admissions are closed or policies are reset through an authorized process.
No shared/development/production database is modified during tests.

Cache/retention APIs, UI lifecycle screens (FRONTEND-006), custom provider ordering,
project default models/credentials and implicit fallback remain outside scope.

## Validation

| Requirement | Exact automated evidence |
| --- | --- |
| "default inheritance" and "explicit routing overrides" | `Default_inheritance_and_explicit_strategy_override_do_not_change_restrictions`; `Project_routing_defaults_inherit_and_explicit_requests_override_only_strategy` |
| "tenant isolation" | `Project_settings_HTTP_enforces_current_roles_tenant_scope_csrf_and_reset`: outsider, wrong path organization, Owner/Admin, Developer/ReadOnly/BillingViewer, current grants, CSRF and archived-project assertions |
| "budget enforcement" | `Project_routing_defaults_cannot_bypass_financial_denial`; extended real PostgreSQL `Every_project_and_key_window_is_a_hard_gate_and_releases_with_the_reservation` proves four budget holds/caps persist across settings edits |
| "forbidden providers/models" | `Project_forbidden_routes_never_reserve_or_dispatch_even_with_explicit_overrides`: models, providers, deny-all, pin, fallback, BYOK/Hybrid and admission-time tightening; `Project_restrictions_filter_discovery_and_intersect_key_permissions_before_ranking`; `Project_denied_stream_never_starts_SSE_or_provider_execution` |
| "execution-mode safety" | `Project_defaults_preserve_explicit_BYOK_and_Hybrid_consent`; inherited/override test above asserts Managed with an available BYOK credential; `Environment_metadata_does_not_override_operator_pause_or_enable_BYOK` |
| "operator limits" | `Project_defaults_cannot_bypass_distributed_operator_limits` asserts 429/503 and zero reserves/dispatch |
| "avoid unnecessary abstractions or duplicate budget logic" | Existing Projects/settings_json, Billing budgets and Gateway admission reused; no new module/table/migration. `Settings_cannot_enable_execution_modes_or_duplicate_financial_controls` |
| "Resolve the misleading max_cost_usd example" | API §3 corrected; `Future_cost_and_provider_request_extensions_remain_unsupported` proves unsupported parser fields remain rejected |
| Authorization, audit and consistency | HTTP test above; `Concurrent_project_settings_replace_atomically_and_failed_audit_rolls_back` proves last-writer whole-group update, 12 audit entries and rollback on audit failure |
| Wire/client | Serializer-derived settings GET/PUT examples; `project settings expose only defaults metadata and hard allowlists with protected operations`; generated OpenAPI/client, positive/negative type fixtures |

### Commands — 2026-10-13

| Repository-root command | Result |
| --- | --- |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; zero errors, existing ASPIRE010 warning |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; zero errors, existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build` | PASS: 732 tests across 16 projects, zero failures/skips; includes all 200 PostgreSQL/Redis, 182 Gateway, 40 Organizations, 15 architecture and 2 Aspire cases |
| Focused `ProjectSettingsTests` | PASS: 18 cases |
| Focused project Gateway cases + existing CSV disconnect | PASS: 29 tests after pinned-model fixture correction |
| Focused `ProjectSettingsIntegrationTests` + existing four-budget gate | PASS: 3 real PostgreSQL/HTTP tests |
| `npm ci --prefix frontend/apps` | PASS; existing development advisory noted below |
| `npm run api:generate --prefix frontend/apps` | PASS; OpenAPI/client/examples regenerated |
| `npm run api:check --prefix frontend/apps` | PASS; no drift |
| `npm run api:test --prefix frontend/apps` | PASS: 10 schema/example tests |
| `npm run api:types --prefix frontend/apps` | PASS: positive/negative call fixtures |
| `npm run build --prefix frontend/apps` | PASS: dashboard and admin |
| `npm run test:e2e --prefix frontend/apps -- --workers=1` | 36 PASS, one existing Aspire-URL test skipped; wrapper ignored worker flag, actual run used 8 workers |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production dependency vulnerabilities |
| `git diff --check` / `git diff --cached --check` | PASS |

An earlier full run was interrupted after the new pin fixture failed (its helper
had removed the model prefix) and an existing CSV disconnect test hit its 5-second
timeout. Fixture corrected; CSV source/timeout unchanged, targeted rerun and final
complete baseline passed.
Neither that interrupted run nor a browser skip is passing acceptance evidence.
Full `npm audit` still reports the pre-existing dev-only source-map-js high advisory
GHSA-68fv-2mgg-jv7q, also recorded in `api/VALIDATION.md`; no dependency upgrade is
bundled here. Production audit above is distinct, not a claim the full audit is clean.

Tests use synthetic identities and
disposable PostgreSQL/Redis, not shared or production data. Browser routes are
mocked regression checks, not new settings acceptance; the actual session/HTTP/PG
tests above provide that evidence. No UI screen/screenshot change. Python was
unavailable for optional static source pairing; no coverage/mutation score claim.
Live provider/merchant and two-host staging qualification remain separate gates.
