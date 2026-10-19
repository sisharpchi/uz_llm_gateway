# Retention Policy and Audit Atomicity

**SECURITY-009 · Completed · 2026-10-19.** No production activation.

## Scope and authorization

SECURITY-009 was explicitly promoted by the user's 2026-10-19 Continue after
ENTERPRISE-002 closeout. This is the conditional P0 correction identified in
[SECURITY-007 §2](43_GUARDRAIL_PII_POLICY_SPEC.md#2-verified-baseline-and-concrete-gap),
not authorization to implement guardrails or other P2 proposals. Dependencies
SECURITY-007, PRIVACY-002 and SECURITY-003 are completed. FR-091,
NFR-045/050 and ADR-011/024/025 remain authoritative.

The existing Owner-only retention controls are offered in the customer UI.
Previously their policy/deletion transaction committed before HTTP appended
the audit event. An audit failure could therefore return failure after enabling
capture or deleting payloads. Completed PRIVACY/FRONTEND history is preserved;
this follow-up closes that specific gap rather than claiming it was tested before.

## Ownership and transaction

`Usage.Application.PayloadRetentionPolicyService` owns the transaction:

```text
Existing session + Owner + CSRF + bounds/encryption validation
  -> shared ITransactionCoordinator.BeginAsync
  -> Usage policy store: project lock -> policy lock -> policy write
     -> shorten existing expiry/delete expired rows, or opt-out deletion
  -> Audit.Contracts.IAuditTrail -> same scoped FoundationDbContext
  -> one commit
```

`IPayloadRetentionPolicyStore` is a transaction-bound persistence port; it
rejects calls without an existing transaction and never commits independently.
`IPayloadRetentionPolicyService` is the mutation contract used by the existing
Management endpoint after Owner authorization. `IPayloadRetentionService`
retains reads/capture/sweep, not a separately committing mutation method.
Usage references Audit **Contracts**, not its Infrastructure; no architecture
exception or new database is introduced.

Only Management registers `AddUzllmPayloadRetentionPolicies`; the shared Usage
capture/read registration does not add a new Audit dependency to Gateway or
Worker service construction. Existing Worker and inference behavior is unchanged.

Policy, updater, expiry changes, opt-out deletion and audit either commit
together or roll back. Audit insertion failure, cancellation before commit or
database failure must not return success. The existing transaction scope rolls
back on disposal without using the canceled request token. Missing/out-of-tenant
projects return 404 without a mutation audit. A commit completed before a lost
response may already have succeeded; cancellation is not proof of rollback
after commit. Read the current policy before retrying an uncertain write.

## Preserved behavior and limits

- Disabled by default; 60–10080 minutes; Owner/session/CSRF and project/tenant
  checks stay unchanged. Admin alone cannot enable retention or inspect bodies.
- Successful opt-out immediately deletes existing project payload rows;
  shortening cannot extend expiry and deletes rows newly past their limit.
  Usage metadata, ledger and audit legal hold remain untouched.
- The existing `payload_retention.updated` event retains actor, tenant/project,
  IP and only `Enabled`/`RetentionMinutes` settings, never prompt/response text.
- Existing project/policy lock order still serializes capture against policy
  changes. No provider calls, remote side effects, Redis dependency or network
  work is introduced inside this transaction.
- Audit/storage exceptions return a safe HTTP 500 Problem Details response with
  no-store; internal exception details/payloads are not returned. Actual request
  cancellation still propagates rather than being converted to success.
- No new route, success DTO, schema/migration, secret,
  frontend page, consent/version/receipt/idempotency protocol or guardrail
  exclusion is introduced. PUT still replaces state and records each successful
  update; it is not an exactly-once command-replay API.

The existing failure status is now explicitly described as 500 Problem Details
in OpenAPI/client artifacts and a serializer-derived fixture. Regeneration must
not introduce unrelated operations or change the successful policy DTO.

## Acceptance and validation

The GRD-003/004 references apply **only** to the existing retention/audit rollback
invariant. Guardrail version/receipt replay and concurrent guardrail-vs-retention
enablement remain future GUARDRAILS-001/003 work, not passing fixtures here.

| Requirement | Concrete regression evidence |
|---|---|
| Audit failure/cancellation rolls back policy/expiry/deletion | `Policy_http_audit_failure_or_request_cancellation_rolls_back_all_effects`: enable/shorten/disable × post-INSERT exception/actual HTTP cancellation. Fresh database contexts verify no new audit/policy or lost/shortened payload; retry verifies one committed update and exact audit settings. |
| Owner/CSRF/tenant/no-store preserved | `Policy_http_is_owner_only_csrf_protected_and_never_cacheable`: anonymous, missing CSRF, nonmember/Admin denial, mismatched tenant/project 404, invalid bounds, no effects on denial and successful no-store policy/read. |
| No unaudited independent transaction | `Policy_store_rejects_mutation_without_an_enclosing_transaction` rejects before tracked/database changes. |
| Opt-out/encryption/expiry still work | `Opt_in_encrypts_payload_and_opt_out_deletes_it_without_deleting_metadata` and `Expiry_deletes_only_payload_and_shorter_policy_reduces_existing_expiry` use the audited application path. |

Fault injection uses the real PostgreSQL audit store and flushes the event before
throwing or waiting for request cancellation. This verifies rollback of both
persisted effects, not just a mocked exception before database work. All fixture
accounts, keys and prompts are synthetic; no live provider/payment operation.

Validation: 2026-10-19, Windows/PowerShell, .NET SDK 10.0.401 and Docker
disposable PostgreSQL 17/Redis 8. Working tree based on `1158230`; no live
merchant/provider or shared database is used. This is not a production-gate pass.

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0, zero errors; existing ASPIRE010 warning (1m45s). |
| Focused `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PayloadRetention --logger trx --results-directory artifacts/test-results/security-009-focused-recheck` | PASS, exit 0: 10 passed, zero failed/skipped. The first full run also verifies all ten with final Management-only DI wiring. |
| `npm run api:generate --prefix frontend/apps` / `api:check` | PASS, exit 0; only retention PUT 500 schema/client and its fixture change; Gateway artifacts unchanged. |
| `npm run api:test --prefix frontend/apps` / `api:types` | PASS, exit 0: 16 schema tests; TypeScript negative/positive fixtures compile. |
| `npm run build --prefix frontend/apps` | PASS, exit 0: dashboard/admin; existing dashboard >500 kB chunk warning. No UI change or screenshot claim. |
| `dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx --results-directory artifacts/test-results/security-009-20261019-recheck` | PASS, exit 0: sixteen Completed TRX reports, 1035 passed, zero failed/skipped/aborted. PostgreSQL 241/241 (13m47s), retention 10/10, provider health 3/3, architecture 15/15. Source/assertions unchanged from the initial run. |
| Static docs/scope, `git diff --check` and staged review | PASS: local targets/anchors and balanced tables/fences, unique register IDs and satisfied dependencies; only SECURITY-009 added to register, old history unchanged; financial/schema/routing sources untouched. Task-only paths staged and reviewed. |

Initial focused run: 5 passed/5 failed. Four failures were a new fixture's
repeated API-key prefix; one exposed the missing no-store header on an unhandled
audit exception. The fixture now uses unique prefixes, and the retention handler
returns a safe no-store failure response. Initial failure evidence is preserved
under `artifacts/test-results/security-009-focused`; only clean rechecks count
toward acceptance.

The first parallel Release run completed with **1034 passed, 1 failed, zero
skipped** across sixteen TRX reports (PostgreSQL assembly: 240 passed/1 failed,
13m32s). The sole failure is the unchanged
`ProviderHealthIntegrationTests.Global_outage_blocks_all_credentials_then_admits_one_cross_node_probe`
failure (`Open` expected, `Healthy` observed at line 80). That fixture uses a
400 ms open interval; scheduler/Redis timing is a possible explanation, not a
proven root cause. Routing source and the health fixture are unchanged. Its
isolated three-case recheck passed, exit 0, in
`artifacts/test-results/security-009-health-recheck`. This does not replace a
clean full-suite result; no unrelated routing fix or disabled test is included.
The full-suite recheck uses `-m:1` to serialize MSBuild test-project scheduling;
it still executes every test and preserves each financial/Redis concurrency
test's own parallel operations. Source, assertions and time bounds are unchanged.
The complete recheck passed as shown above; its sixteen reports were reparsed
to verify totals and all ten retention/three provider-health outcomes. No test
is skipped. Serialization does not fix the existing health fixture's observed
timing sensitivity under parallel project load; that risk is retained rather
than represented as an unrelated routing fix. TRX files stay in ignored local
artifacts, not Git.

## Remaining independent gates

OPS-006 still requires two distinct staging hosts; OPS-007/LAUNCH-003 depend on
that evidence. Approved retention/legal schedules, backup restore/rotation,
provider/merchant and all other [launch prerequisites](18_PRODUCTION_READINESS.md)
remain separate. No guardrail feature or other P2 task is promoted by this fix.
