# Discretionary Wallet Credits

Task: `REFUND-002`. User-confirmed policy, 2026-10-17: credit-only and a different
recently MFA-verified operator approves **every positive amount**. This is an
explicit independent P1 selection while OPS-006 requires unavailable staging.

## Scope and ownership

Billing owns positive-credit/independent-checker/capacity invariants, row locks,
ledger posting and existing recovery-debt behavior. Management orchestrates
the shared transaction and audit; its operator/session/MFA/CSRF boundary is
unchanged. PostgreSQL owns financial truth. No external calls, Redis money,
new queue/outbox side effect, provider change or new frontend screen is needed.
Existing wallet reads and admin ledger naturally include the credit.

This is not a settlement-linked refund, payment reversal, cash payout or
manual debit. Historical usage, budget captured spend, provider cost, fees and
FX are untouched. System debt recovery may still append `AdjustmentDebit`;
that is not permission for operator-authored debits.

## Operator workflow

Use [HTTP examples](../src/UZLLM.Management.Api/wallet-adjustments.http) and
[API contracts](11_API_CONTRACTS.md#discretionary-wallet-credits-refund-002).
Provision two operators through the existing privileged process. Both enroll
MFA; each mutation needs a current grant, recent verification and CSRF.

1. Maker requests an organization credit with a stable support key and reason.
   The wallet must already exist. `201` returns a Pending resource/Location;
   money and reservations are unchanged.
2. A different operator reads the immutable amount/organization/reason and
   approves with an independent reason. The approval request cannot override
   money, organization or direction. Every amount requires this step.
3. `200` returns Approved with checker evidence. The wallet credit, debt
   recovery and approval audit are committed atomically.

Authorization is checked when each action happens, not retroactively against
the historical maker's current grant. Pending requests never auto-post or
expire; there is no edit/delete/reject/cancel workflow. Leave an unsuitable
request unapproved; different terms need a new key. Approval is irreversible;
corrections require a separately authorized compensating workflow, not editing
history or an implicit debit endpoint.

## Idempotency and failure behavior

Organization/key is unique; identical normalized maker/amount/reason replays
return the original resource (`duplicate: true`), including its current approval.
Changed terms or maker conflict. One approval per request is enforced in the
database; replay needs the same checker and reason, a different checker conflicts.
A wallet row lock serializes competing approvals and all other wallet work.
One ledger reference `(organization, AdjustmentCredit, wallet_adjustment, request)`
backs up the approval uniqueness. Lost HTTP responses can be safely retried
with the same terms; no replay creates more money or audit rows.

Creation/request audit share a transaction. Approval, ledger credit, available
debt recovery and approval audit share another transaction. Failures or
cancellation **before commit** roll back. A lost commit acknowledgement/HTTP
response can mean either a committed or uncommitted transaction: read/retry
the same organization/request key or approval's request resource ID/checker/reason,
never assume zero credit
or switch to a new key. Database effects are all-or-none and replay is safe.
Overflow is rejected before
approval/posting. Active holds remain intact; debt recovery consumes only
available credit and keeps spending held until debt reaches zero. A database
outage cannot fall back to Redis or an unaudited credit. No async delivery
guarantee is implied because there is no external side effect.

## Migration and operations

Explicit Migrator only: `20261017080259_AddWalletCreditAdjustments` creates
`billing.wallet_adjustment` and `billing.wallet_adjustment_approval`, account/
wallet/composite tenant FKs, positive-credit/two-person CHECKs, unique operation
indexes and immutable UPDATE/DELETE triggers. Runtime cannot TRUNCATE.
Down refuses any populated request/approval history; do not discard financial
evidence to downgrade. Test-only cleanup is limited to disposable fixtures.
No configuration or production credential change is required.

Operator history is bounded to 1–100 newest requests; filter by organization
when investigating. Correlate request ID with ledger reference and
`wallet-adjustment.requested` / `wallet-adjustment.approved` audit resource ID.
Never put secrets or raw customer payloads into support reasons.

## Validation evidence

The checks below passed on 2026-10-17. This is repository validation, not a
production launch-gate pass.

Targeted test evidence (disposable PostgreSQL 17 / Redis 8, real loopback HTTP):

| Acceptance requirement | Concrete regression evidence |
|---|---|
| Credit-only; every positive amount requires a different operator | `Every_positive_credit_requires_a_different_operator`, `Zero_and_debits_are_forbidden` |
| Request creation posts no money; immutable terms, actor/reason and replay/concurrency | `WalletAdjustment_parallel_requests_and_approvals_post_once_with_immutable_terms` asserts no initial credit, one posting/audit pair, unchanged replay, conflict and UPDATE/DELETE/downgrade/runtime-TRUNCATE denial. |
| Independent competing checkers; cancellation rollback | `WalletAdjustment_different_checkers_race_and_cancelled_audit_never_duplicate_credit` |
| Audit failure/uncommitted approval rollback | `WalletAdjustment_audit_failure_rolls_back_request_approval_and_credit` |
| Active holds, recovery debt and historical spend preserved | `WalletAdjustment_recovers_debt_preserves_hold_and_does_not_restore_spend_budget` verifies a settled charge, captured budget and active hold before credit and debt recovery. |
| Organization isolation, supported balance capacity and database two-person guard | `WalletAdjustment_organization_keys_are_isolated_and_overflow_cannot_post` asserts separate same-key tenant resources, composite FK/self-approval CHECK, exact balance and rejected overflow. |
| Session/operator/recent-MFA/CSRF and immutable HTTP payloads | Extended `Admin_routes_require_operator_recent_Mfa_and_Csrf_for_mutations` exercises 401/403/400/404/409, maker/checker 201→200, replay, detail/list, revoked and stale sessions using actual endpoints. |
| Exact money and nullable Pending approval in JSON/client contracts | `Published_examples_use_real_parser_writer_and_management_serialization`, Node `wallet credits expose exact amounts independent approval and protected mutation contracts`, shared schema-example test and negative TypeScript fixtures. |

Pure-policy boundaries include normalized Unicode scalar key/reason lengths/control characters,
missing actors, UTC/PostgreSQL timestamp precision and Int64 balance capacity.
Unit sources: [WalletAdjustmentPolicyTests](../tests/Unit/UZLLM.Billing.Tests/WalletAdjustmentPolicyTests.cs).
Financial sources: [wallet adjustment regressions](../tests/Integration/UZLLM.Persistence.IntegrationTests/BillingFinancialIntegrationTests.WalletAdjustments.cs).
HTTP source: [admin boundary tests](../tests/Integration/UZLLM.Persistence.IntegrationTests/AdminIntegrationTests.cs).

### Commands and reports — 2026-10-17

Local Windows/.NET 10.0.401, xUnit/VSTest and Node 24.19.0; hosted CI is not
claimed. Commands are from the repository root (VSTest positional paths).

```powershell
dotnet test tests/Unit/UZLLM.Billing.Tests/UZLLM.Billing.Tests.csproj --configuration Release --filter FullyQualifiedName~WalletAdjustment --logger trx --results-directory artifacts/test-results/refund002-unit-final
dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~WalletAdjustment|FullyQualifiedName~Admin_routes_require_operator_recent_Mfa_and_Csrf_for_mutations" --logger trx --results-directory artifacts/test-results/refund002-final
dotnet restore UZLLM.slnx
dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental
dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/refund002-baseline
npm ci --prefix frontend/apps
npm run api:generate --prefix frontend/apps
npm run api:check --prefix frontend/apps
npm run api:test --prefix frontend/apps
npm run api:types --prefix frontend/apps
npm run build --prefix frontend/apps
npm audit --omit=dev --prefix frontend/apps
npm ci --ignore-scripts --prefix examples/node
npm test --prefix examples/node
node deploy/tests/run-launch-acceptance.mjs --host-publish
git diff --check
```

| Check | Observed result |
|---|---|
| Final policy tests | PASS: 17, failed 0, skipped 0; all 17 also discovered by root solution. |
| Final PostgreSQL/HTTP filter | PASS: 6, failed 0, skipped 0 (5 new financial tests + extended existing admin test). |
| Restore / non-incremental Release build | PASS; the full test command started only after both exited 0. |
| Full Release suite | PASS: 1028, failed 0, skipped 0 across 16 assemblies; PostgreSQL 234/234. Root discovery includes all 17 new policy cases and 5 new financial cases. |
| OpenAPI generation/drift, strict TypeScript | PASS; generated documents, real serializer fixtures and client types match. |
| API schema regressions | PASS: 16, failed/skipped 0, including exact money, Pending null approval and protected immutable commands. |
| Dashboard/admin production builds | PASS; pre-existing dashboard 590.52 kB chunk warning remains. |
| Frontend production dependency audit | PASS: 0 vulnerabilities with `--omit=dev`; `npm ci` still reports one existing dev advisory. No dependency upgrade/suppression. |
| Node SDK regressions | PASS: 33, failed/skipped 0; SDK dependency audit reports 0 vulnerabilities. |
| Disposable TLS stack regression | PASS: 5/5 real-service scenarios with freshly host-published Gateway/Management/Worker/Migrator and current migration. No mocked backend. |
| Diff and file hygiene | PASS: task-only paths, local Markdown targets, credential-pattern review and whitespace checks. No comprehensive secret-scan/coverage claim. |

The acceptance summary is `artifacts/acceptance/summary.json`, environment
`disposable-local-docker`, completed `2026-10-17T08:59:08.544Z`, project
`uzllmaccc39aee89`, `passed: true`. Its revision is pre-commit HEAD
`2b4f07c9fbe621b48149ed55340f10ca51c7a2e8`; it ran the uncommitted task source,
not a pinned release artifact. All 24 changed implementation/contract files
remained SHA-256 identical through final validation. The harness removed only
its own disposable containers/volume/temp certificates; no project containers
remain. Existing user services were not stopped. Hosted CI and staging were
not run; the real-stack run is a regression, not a second backlog task.

Initial task-local failures were not accepted as completion: fixture setup
incorrectly assumed identity registration created an organization, then
duplicated the organization-triggered wallet, and expected a positive rather
than signed-negative debt-recovery entry. Those test assumptions were corrected
to the actual repository rules. Failed TRX remains under `refund002-financial`
and `refund002-financial-recheck`; the final filter passes in `refund002-final`.
The schema/example test also caught missing nullable approval in inline list
items and an over-broad first fix affecting shared `oneOf` references. The
final correction targets only the list property; no assertions were weakened
and no runtime financial rule was changed to pass a test.

No test was skipped, no live credential was used, and no shared database was
migrated. Two-host load/HA/PITR, live merchant/provider verification and
fiscal/retention/incident-owner prerequisites remain external or later launch
gates; this task cannot pass them.
