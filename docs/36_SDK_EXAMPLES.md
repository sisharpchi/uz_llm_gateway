# DX-003 — Bounded SDK examples

## Scope and ownership

NFR-084 and ADR-004/010/020/022; dependencies DX-001, DX-002 and GATEWAY-005–007
are delivered with linked contract evidence. User approval selects this P1 task
while OPS-006 lacks distinct staging hosts and REFUND-002 lacks financial policy.
Neither blocker is resolved or bypassed as a production gate.

[Node examples](../examples/node/README.md) use the official OpenAI SDK against
the implemented Gateway v1 subset. Each CLI invocation runs one operation:
models, chat JSON/SSE, Responses JSON/SSE or bounded float embeddings. The SDK
dependency is isolated in `examples/node`; no SDK types/dependency enter business
modules, provider adapters or browser bundles. No API, migration, host settings,
financial state or routing semantics change.

The backlog makes the playground **optional**. This increment deliberately uses
terminal/server examples and preserves the existing secret-free dashboard curl
quickstart. The aspirational Playground in document 13 remains unimplemented;
there is no new browser secret collection/proxy or claim that it is complete.
The root frontend mockup is unchanged.

## Safety and compatibility

Explicit Gateway origin and project key only; no default OpenAI destination or
OPENAI_API_KEY fallback. HTTPS required except loopback HTTP; URL credentials,
paths/queries/fragments and redirects rejected. Inference needs explicit billing
consent, uses bounded output, Managed default and a preprinted UUID claim.
Automatic SDK retries are disabled. Cancel/timeout/error is not zero spend;
missing usage remains null, terminal failure is not successful stream completion,
and duplicate claims return 409/original request ID rather than replaying a body.
No implicit BYOK/Hybrid or override of tenant/key/budget/operator restrictions.

The CLI prints generated text only to the invoking terminal and documents its
sensitivity. Error output omits upstream messages/bodies/headers/secrets. Server
process environment supplies credentials; no browser persistence or TLS bypass.
SDK/versioned OpenAPI compatibility is tested, not assumed from SDK types.

## Validation evidence

Local Windows / .NET 10 / Node 24 / Docker Desktop, 2026-10-17. Initial checkout
was clean at `d41137a211d9751f4f5d7009c7c3bdacd0787ef9`. Public/synthetic fixtures
do not qualify live provider/merchant accounts, hosted CI, two-host load or paid
launch. The acceptance artifact records base HEAD plus the task's working-tree
execution, not a hash/pin of a deployed release.

| Check | Result |
|---|---|
| `npm ci --ignore-scripts --prefix examples/node` | PASS; pinned SDK 7.36.0, Node >=22. |
| `npm test --prefix examples/node` | PASS: 33, failed 0, skipped 0. Six real-SDK transport/schema cases, nine real Responses serializer fixtures, null usage, error/retry, redaction, cancellation and missing-terminal boundaries. |
| `npm audit --prefix examples/node` and `--omit=dev` | PASS: zero advisories. |
| `npm ci --prefix frontend/apps` | PASS; one pre-existing high development advisory, no package upgrade. |
| `npm run api:check --prefix frontend/apps` | PASS; Gateway/Management documents, generated clients and serializer examples unchanged. |
| `npm run api:test --prefix frontend/apps` | PASS: 15, failed 0, skipped 0. |
| `npm run api:types --prefix frontend/apps` | PASS. |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin; unchanged 590.52 kB dashboard warning retained. |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- tests/catalog.spec.ts --workers=1` | PASS: 3, failed 0, skipped 0; existing secret-free quickstart, tenant switch and denial. |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production advisories. |
| `node deploy/tests/run-launch-acceptance.mjs --host-publish` | PASS: 5/5 browser suites, zero failures/skips, including six CLI commands and five duplicate denials. Fresh application/edge images; disposable resources cleaned. |
| `dotnet restore UZLLM.slnx` | PASS. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS: zero errors; pre-existing ASPIRE010 warning retained. |
| Initial Release solution test run | FAIL: 1005 passed, 1 failed, 0 skipped across 16 suites. Existing `ProviderHealthIntegrationTests.Global_outage_blocks_all_credentials_then_admits_one_cross_node_probe` failed at line 93 (expected Open, actual Healthy). PostgreSQL/Redis: 228 passed, 1 failed, 15m30s. This is not passing baseline evidence. |
| Exact failed health test, unchanged Release `--no-build` recheck | PASS: 1, failed 0, skipped 0; `artifacts/test-results/dx003-health-recheck`. No source, assertion, timer or runner change. |
| Unchanged full Release solution rerun | PASS: 1006 passed, 0 failed, 0 skipped across 16 suites; PostgreSQL/Redis 229 (15m30s), Gateway 360, architecture 15 and Aspire 2. `artifacts/test-results/dx003-baseline-recheck`. Original failure retained separately. |
| Final diff, local links and secret-pattern review | PASS; task-only paths, no backend/generated wire changes, no whitespace errors or basic private-key/token-pattern matches. This is not a comprehensive security audit. |

CI installs/tests the isolated SDK package before the existing real-stack job.
No hosted CI execution is claimed. Browser output/screenshots remain the existing
customer screens; this task adds no UI screen requiring a new design screenshot.

Real-stack project `uzllmacc6c61ca89`, completed `2026-10-17T06:42:40.450Z`;
ignored `artifacts/acceptance/summary.json`. Four SDK text operations each debit
11 micro-USD; two-input embeddings debit 4 micro-USD. The wallet moves from
10,999,952 to 10,999,904 micro-USD, with zero hold, no extra duplicate debit and
five matching Activity request IDs. Models discovery is read-only. The test
compares dashless Gateway correlation IDs with Management UUIDs explicitly.
TLS trust is scoped to SDK child processes using the disposable edge certificate,
not a validation bypass. User `.env`, databases, containers and root mockup were
not changed; pre-existing `postgres`/`pgadmin` containers remain running.

The initial baseline health failure uses a real Redis 400 ms open duration;
an elapsed cooldown is one possible explanation, **not a proven root cause**.
The [existing test](../tests/Integration/UZLLM.Persistence.IntegrationTests/ProviderHealthIntegrationTests.cs)
and routing implementation are unchanged by this task. Preserve the original
TRX in `artifacts/test-results/dx003-baseline`. No deadlines, assertions,
parallelism or health behavior were changed. The isolated recheck and one bounded
full rerun passed with unchanged source. This is an observed validation limitation,
not a health fix or a claim of production reliability. All nine task implementation,
configuration and test files remained SHA-256 identical during final validation.

Reproduce the final full baseline from the repository root:

```sh
dotnet restore UZLLM.slnx
dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental
dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/dx003-baseline-recheck
git diff --check
```

The exact failed-test recheck used the Integration project in Release with
`--no-build --filter FullyQualifiedName=UZLLM.Persistence.IntegrationTests.ProviderHealthIntegrationTests.Global_outage_blocks_all_credentials_then_admits_one_cross_node_probe --logger trx --results-directory artifacts/test-results/dx003-health-recheck`.
Do not replace full acceptance with that isolated test or overwrite the original
failed report. No next backlog task was started.
