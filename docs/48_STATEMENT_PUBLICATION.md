# REPORTING-002 — Immutable statement publication

## Scope and authority

The user's 2026-10-19 Continue separately promotes REPORTING-002 from the
[approved reporting design](45_INVOICE_SLA_REPORTING_SPEC.md#10-approved-follow-on-design--unscheduled-execution-approval-required).
REPORTING-001 and OPS-003 are completed dependencies. OPS-006 stays externally
Blocked. This independent P2 increment does not approve paid launch.

This implements **AccountStatement**, labeled “Not a tax invoice / No payment
due from this statement”. No customer/operator HTTP route, UI, PDF, fiscal
invoice, contractual SLA, commercial policy or financial mutation is introduced.
REPORTING-003 must be separately promoted before an authorized API facade.

## Ownership and transaction boundaries

- Billing Domain owns normalized versioned commands, artifact allowlisting,
  bounds and revision delta. REPORTING-001's reconciliation remains authoritative.
- Persistence owns seven `billing.statement_*` tables: head, operation, receipt,
  version, line, source and attempt. Composite tenant/head foreign keys, unique
  period/head/revision and operation/key identities prevent mixed-tenant links
  and duplicate publication. No separate database or partition is introduced.
- Billing's PostgreSQL store owns its transaction; nested transactions are
  rejected. Request atomically commits normalized-command receipt, pending
  operation, head coordination, requested audit and leased Worker job. A UUID key
  is scoped to organization and Build/Revision; changed normalized terms conflict.
  Actor, period, expected generation/predecessor, reason and schema are pinned.
  Identical replay returns its original operation before checking a newer head.
- Worker builds under **one Repeatable Read transaction on primary PostgreSQL**,
  serializing only the report head. It freezes explicit source IDs/values, not
  a maximum UUID/time watermark. No wallet lock, provider call, Redis dependency,
  independently committed analytics read or Gateway hot-path write is added.
- Version, source rows, stable period-line ordinals, success attempt, advanced
  head/operation, audit and outbox commit together. Membership is inserted before
  the Published transition. A deferred database check validates counts, combined
  persisted bytes, operation lineage and head at commit; no partial version may
  commit. The store independently rechecks the snapshot digest/membership.

## Immutability, revisions and safe artifacts

Database triggers reject UPDATE/DELETE of versions, lines, sources, receipts and
attempts. Head identity and operation terms cannot change; generation cannot
decrease and published operations cannot be rewritten. Once Published, additional
source/line INSERTs are rejected too. These are normal-runtime constraints, not
a promise against a privileged DBA disabling triggers.

An explicit revision supplies current generation, predecessor and a new reason;
it publishes another immutable version with source added/removed/changed counts
and closing-balance delta. Prior JSON/CSV bytes and source membership remain
unchanged. Late next-month settlement stays in that posting month; an observed
backdated posting requires a revision rather than silently recomputing history.

Artifacts use allowlisted typed fields; integer micro-USD/tiyin and frozen FX
annotations are strings, never floating-point currency. Historical closing plus
visible post-period movement reconciles to snapshot posted balance. Holds and
recovery debt remain separate. Unknown evidence/settlement and merchant mismatch
counts are disclosed. Imbalance, missing mandatory link or unmatched paid posting
blocks publication without posting/releasing money or restoring budgets.

Raw ledger metadata, merchant references, actor reasons/IDs and internal provider
cost/exposure are absent from customer artifacts. Internal snapshot/source JSON
is **not** a customer DTO and must never be returned wholesale by REPORTING-003.
CSV contains only a fixed label/header, integer ordinal, UUID, finite ledger
category, UTC timestamp and signed numeric amount; there is no user-text formula,
template execution or network fetch. Customer endpoint authorization, pagination,
content headers and download semantics remain REPORTING-003 work.

## Bounds, retry and operational handoff

- Default maximum **100,000 total persisted source identities**, including
  ledger, links, payment terms, holds/debt/reversals/usage and wallet/pending
  manifests. This is conservative: not 100,000 rows per source family.
- Maximum **32 MiB combined** JSON/CSV, internal snapshot, source and line payload
  bytes; no truncation. Oversize requires a separately approved scaling change.
- Worker deadline 30 seconds; linked cancellation covers database I/O and is
  checked before persistence/commit. Cleanup gets its own bounded five seconds.
  Existing OPS-003 leases/heartbeat/retry/backoff apply; maximum five attempts.
- Failure rolls back all publication rows and records a safe Failed outcome
  (Sources/SourceDigest/Generation/Bounds/Canceled/Timeout/Dependency). A later
  leased attempt keeps the same operation/receipt. A committed operation replay
  is a no-op, including after a lost job acknowledgement. Stale generations never
  overwrite a newer head. A new command can supersede a failed build; old retries
  then fail generation checks, rather than changing the new head.
- If PostgreSQL is unavailable, even failure-record cleanup cannot be guaranteed
  until it recovers. The committed job/lease is the recovery identity, not an
  in-memory success claim. Use existing metadata-only dead-letter inspection
  after repair. There is no generic requeue endpoint; do not reset attempt
  counters, delete receipts or fabricate a financial correction. An exhausted
  Failed build needs an explicitly authorized new command/key at the same
  unchanged generation, not an unbounded retry of its terminal job. A process
  crash/DB outage can leave the operation Pending while its lease dead-letters;
  keep both identities for operator triage. REPORTING-003's job facade must
  expose that terminal job outcome rather than implying an active build or
  silently restarting it. This increment adds no operator repair endpoint.
- `billing.statement.published` is a post-commit lifecycle event. Its inbox-backed
  handler acknowledges/logs once; it does **not** send a customer notification or
  claim exactly-once external delivery. Future authorized surfaces/notification
  work must explicitly subscribe with their own durable idempotent effects.

`AddUzllmStatementPublication()` is opt-in in Worker after Billing/Audit/Payments
composition. Gateway/Management do not register this feature. The internal store
accepts a trusted actor, not a browser authorization decision: REPORTING-003 must
enforce current organization permission/operator grant, recent MFA and mutation
CSRF in its application facade as well as endpoints.

## Migration, retention and quarantined restore

Migration `20261019105755_AddStatementPublication` runs only through the explicit
Migrator, never host startup. Runtime permissions use existing default grants;
DDL remains migrator-only. Schema-only Down is supported; any statement head
causes rollback to refuse history loss. Disposable test reset explicitly truncates
all seven mutually referenced reporting tables, never financial tables by cascade.
Do not apply that fixture cleanup to a shared/production database.

No retention deletion is added. Statement artifacts, source manifests, receipts
and attempts remain subject to legal hold/approved retention; existing operational
audit/outbox retention is not silently extended or represented as permanent
customer evidence. An approved reporting retention/archive policy is required
before exposing this optional product; deletion would need an explicit follow-up,
not trigger bypass. Preserve migration version, source calculation version and
receipt identities in backups.

The local PostgreSQL fixture exercises real `pg_dump`/`pg_restore` into a separate
quarantined database with **no Worker/dispatch started**. It checks old/revised
artifacts, source digest/sequence, pending/committed receipt replay and trigger
protections. Production recovery must additionally verify source membership and
artifact hashes, reconcile financial/operational state, review restored jobs and
external identities before resuming side effects, and collect distinct-host
HA/PITR/RPO/RTO and signed operational evidence. Local restore is not OPS-006/007
qualification, an external notification test or a launch-gate pass.

## Validation evidence

Final-review recheck complete: after the initial 1105-PASS full baseline,
payment-annotation membership was changed from a nested ledger search to a
precomputed period top-up set (linear lookup), with a 1001-payment/month-boundary
regression. No wire/financial semantics change. Focused unit tests pass 12/12;
the final full baseline below validates this exact source revision. No subsequent
implementation edits were made.

| Acceptance | Observable repository evidence |
|---|---|
| ENT-004 | `Publication_repeatable_read_excludes_commit_after_membership_and_does_not_lock_wallet` uses explicit barriers; existing delayed older-UUID statement test and historical opening/movement/closing/post-period assertions remain. |
| ENT-005 | `Publication_parallel_request_and_worker_delivery_choose_one_winner` races identical builds and revisions, then distinct keys; the atomic/replay test pins scoped receipt, changed terms, stale generation and lost acknowledgement behavior. |
| ENT-006 | `Publication_is_atomic_replayable_tenant_scoped_and_revisions_preserve_old_bytes` verifies explicit backdated revision/delta, immutable old bytes, tenant reads/composite FK, UPDATE/DELETE/late INSERT denial and history-preserving Down refusal. Existing migration round-trip tests cover empty schema Down. |
| ENT-007 | `Statement_payment_port_is_tenant_safe_and_pending_missing_links_never_mutate_finances` now publishes disclosed unknown/settlement/merchant counts, then refuses a broken-source revision without changing money; unit imbalance rejection remains explicit. |
| ENT-008 | `Statement_exact_financial_fixture_is_read_only_and_preserves_budget_debt_and_hold` now also publishes the exact fixture and frozen payment annotation, retaining posted/held/debt, usage/refund/adjustment/reversal separation and captured budget. Existing posting-month and BYOK-fee tests remain unchanged. |
| ENT-018 | `Publication_failure_rolls_back_every_publish_row_and_retry_keeps_operation`: audit, outbox, injected DB statement abort, digest, membership, invalid manifest, source/byte bounds, cancellation and deadline. No partial version/source/line/audit/outbox, safe Failed outcome and unchanged finances; next attempt keeps operation. Request-audit failure leaves no head/receipt/job. Unit exact-limit/one-less and invalid attempt 0/6 checks remain. |
| ENT-020 | `Publication_quarantined_dump_restore_preserves_bytes_receipt_and_sequence_without_dispatch` checks real isolated restore, two-version lineage, exact source payload/hash membership, old bytes and pending/committed receipts. `Publication_committed_outbox_consumer_replay_acknowledges_once` checks the internal lifecycle inbox, not external notification delivery. Retention/restore runbook boundaries above keep external/distinct-host gates separate. |

Validated on Windows with .NET SDK 10.0.401, Docker 29.7.2 and disposable
PostgreSQL 17 / Redis 8. Starting HEAD `8b15ca8` was clean; no unrelated changes
were present. No unavailable external gate is represented as passed.

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; zero errors, existing ASPIRE010 warning only. |
| `dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx --results-directory artifacts/test-results/reporting-002-final` | **1106 PASS, 0 FAIL, 0 SKIP**; sixteen Completed TRX reports, all totals executed. PG 264, Billing 88, architecture 15, Gateway 360, AppHost 2. |
| `dotnet ef migrations has-pending-model-changes --project src/BuildingBlocks/UZLLM.Persistence/UZLLM.Persistence.csproj --configuration Release --no-build` | PASS, no model changes; design-only dummy connection, no database migration/application connection. |
| `git diff --check` / staged whitespace review | PASS before task-only commit. |

The corrected focused PG command was:

```powershell
dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~BillingFinancialIntegrationTests.Publication_|FullyQualifiedName~Statement_exact_financial|FullyQualifiedName~Statement_payment_port' --logger trx --results-directory artifacts/test-results/reporting-002-acceptance
```

It passed 17/17. The final full baseline additionally covers the manifest-fault,
CSV formula and many-payment cases added afterward: **28 new cases total**
(12 unit, 16 PG), plus two extended existing financial tests. Every new case is
Passed in the final TRX set. The initial PG baseline took 22m29s and the final
run 14m43s; neither slow completion nor the earlier green revision was substituted
for the final full run. Initial 1105-PASS TRX evidence remains locally under
`artifacts/test-results/reporting-002-baseline`; final evidence is separate.
Project-level `-m:1` avoids the existing
provider-health timing flake under concurrent test projects; in-test concurrency,
assertions, deadlines and fixtures are unchanged. No tests were skipped or
warnings suppressed.

Test-generation self-review checked observable outcomes and assertion quality;
no empirical mutation coverage percentage is claimed. The static pairing script
was attempted once but Python was unavailable (Windows Store alias); no install
or substitute pairing report was claimed. Scratch notes remain Git metadata,
not committed product files. No frontend/npm, live merchant/provider,
production outage, distinct-host or HA/PITR qualification was run or claimed in
this increment. API/generated-client artifacts remain unchanged because no wire
surface was introduced.

Initial checks deliberately remain part of the evidence: one unit pending
fixture omitted its matching merchant-mismatch source count; a test raw SQL
literal and dynamic-identifier analyzer check failed compilation. Corrected
fixtures retain production invariants and use a fixed SQL-identifier allowlist.
The first complete PG run found the transaction-ID membership seal incompatible
with EF savepoint subtransactions. It failed 15/20 cases, with five passing;
the seal was replaced with operation-state sealing plus a separate membership
save before the atomic Published transition, not disabled. The next PG run was
18/20: its remaining failures were an old-version restore head selector and
decimal trailing-zero formatting. The selector now resolves the version's head;
frozen FX uses exact invariant `G29`, not a changed rate or test relaxation.
The corrected focused and final full runs above validate these fixes and the
expanded revision/restore cases. Completion is recorded in the single local
REPORTING-002 commit; no next task or deployment is started.
