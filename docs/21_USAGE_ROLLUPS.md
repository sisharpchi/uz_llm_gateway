# USAGE-004 — Durable usage aggregation

## Scope and authority

FR-096 / ADR-014 are implemented with rebuildable PostgreSQL UTC-hour read
models. Wallet, ledger, reservations, settlement and payment accounting remain
authoritative and strongly consistent. Analytics cannot admit spending or
post money. Redis, provider HTTP, payload bodies and secrets are not involved.
CSV export remains `USAGE-005`.

## Publication and recovery

- `usage.rollup_queue` coalesces invalidations **per request**, avoiding a shared
  organization/hour queue-row lock on the inference path. Transactional triggers
  cover request status/completion, provider selection, evidence, settlement and
  wallet-credit refunds. A source rollback rolls back its invalidation.
- `usage.rollup_source` reads one logical request, its last selected provider,
  verified evidence and posted settlement/refunds. Unknown evidence contributes
  no invented tokens or charge. Requested model identity remains unchanged.
- Worker starts a cycle immediately, then polls every five seconds. A cycle
  processes at most 20 pages of 100 invalidations; the store accepts 1–1000.
  PostgreSQL transaction-scoped advisory lock `876312904` serializes the short
  rebuild between Workers. Busy replicas yield; no network call holds the lock.
- Each batch captures queue IDs and PostgreSQL `xmin` row versions without
  holding queue-row locks across the hourly scan. It atomically replaces affected
  hourly rows, updates `usage.rollup_bucket.data_as_of`, then acknowledges only
  unchanged claimed versions. Replacement, not increments, makes replay harmless.
  The trigger's conflict UPDATE creates a new queue version while preserving its
  oldest enqueue time. Concurrent source/financial commits need not wait for the
  scan; changed versions remain queued instead of losing a late invalidation.
- Crash, cancellation or SQL failure rolls back publication and acknowledgement;
  old rows remain readable and the next cycle retries. Late evidence, settlement
  and refund changes rebuild the **request's original hour**, not today's hour.

## Read contract

Summary, daily series and breakdowns combine persisted complete UTC hours with
live source reads for partial boundary hours. A request-ID filter uses live
source rows only. Activity/detail stay live and retain nullable unknown values.
All analytics statements within one response use a read-only repeatable-read
snapshot; responses are not guaranteed identical across separate HTTP requests.
Existing tenant, 90-day range, dimension and `[from,to)` filters remain enforced.
PostgreSQL timestamps have microsecond precision.

`chargedMicroUsd` remains the gross settled charge. Additive decimal-string
fields `refundedMicroUsd` and `netChargedMicroUsd` explicitly report linked
wallet-credit refunds and gross minus refunds; they are **not wallet balance**.
Payment reversal, top-up, recovery debt and external provider exposure are not
inference-charge refunds and do not silently change these figures.

`dataAsOf` is the oldest materialized full-hour snapshot in the requested
window, not HTTP response time or a promise that every hour is equally fresh.
It is Unix epoch during initial backfill when a required bucket is absent;
live-only/empty reads use database read time. `aggregation.pendingBucketCount`
and `oldestPendingAt` expose outstanding hours/oldest invalidation, scoped to
tenant/time and immutable project/key/model filters. They intentionally include
all statuses/providers/stream modes so changes **out of** a filter stay visible.
`source` distinguishes `HourlyRollupsWithLiveEdges` from `AuthoritativeRequest`.
Historical unchanged buckets can have old snapshot times without a backlog.

Existing dashboard Analytics queries refresh every ten seconds while visible,
show pending aggregation and gross/refund/net separately. Five-second polling
is the normal-load target, not a hard latency SLA during backfill, outage or
overload; combined with UI refresh, normal visible updates target roughly
15 seconds plus query time. Background tabs do not poll. No mock data enters
the deployed apps.

## Deployment and operations

Apply `20261011103552_AddDurableUsageRollups` with the explicit Migrator before
deploying new Worker/Management binaries. It queues existing requests for bounded
backfill; until drained, historical analytics may be incomplete and explicitly
pending. No runtime host applies DDL. Runtime DML permissions are sufficient.
Downgrade only after stopping new binaries; derived rows can be discarded and
rebuilt, but authoritative usage/financial history is retained.

Inspect backlog age (no tenant IDs or payloads in telemetry labels):

```sql
SELECT count(*) AS pending_requests,
       clock_timestamp() - min(enqueued_at) AS oldest_pending_age
FROM usage.rollup_queue;
```

Investigate sustained age above 60 seconds, Worker failure logs, DB saturation
and a live competing Worker transaction before restarting. Never delete queue
rows to conceal lag. A controlled full rebuild can requeue existing requests
with `INSERT ... SELECT` using the migration's UTC-hour expression and
`ON CONFLICT DO NOTHING`; run bounded batches, preserve history and record the
operator action. Large-traffic qualification and real two-node/restore evidence
remain `OPS-006–007`; local rollup tests do not pass paid-launch gates.

## Validation

Evidence from 2026-10-11; all database scenarios use disposable PostgreSQL,
not production. Backlog acceptance is: “Reprocessing does not double count;
`dataAsOf`/late corrections explicit. Database and Worker replay tests.”

| Requirement | Automated evidence |
| --- | --- |
| Reprocessing does not double count | `Rollup_replay_and_competing_workers_do_not_double_count` runs competing PostgreSQL-backed Worker cycles, duplicate invalidation and replay; asserts exact request/token/charge totals and an empty queue. |
| `dataAsOf`/late corrections explicit | `Late_evidence_settlement_and_refund_rebuild_original_hour` checks stale watermark/pending hours before publication and original-hour correction: 170 input, 40 output, 4300 gross, 1000 refunded, 3300 net micro-USD. |
| No lost concurrent invalidation | `Source_commit_during_publication_remains_queued_for_next_worker_cycle` gates acknowledgement; a source write commits without waiting for the scan, remains queued, then changes the next snapshot. |
| Atomic rollback and recovery | `Failed_rebuild_rolls_back_projection_and_preserves_queue` and `Rolled_back_source_change_never_invalidates_or_changes_rollup` verify old snapshots, queue/source atomicity and successful retry. |
| Tenant scope, UTC boundaries and mutable filters | `Rollups_preserve_filters_utc_edges_and_tenant_isolation` covers offsets, microsecond half-open edges, all dimensions, live request-ID reads and changes out of a status filter. |
| Backfill, runtime privileges and bounded work | `Migration_backfills_existing_requests_and_runtime_can_aggregate`, `Rollup_accepts_batch_boundaries_and_leaves_unclaimed_requests`, and `Rollup_invalid_batch_is_rejected_before_database_access` cover migration down/up, DML-only runtime, model drift and 1/1000 accepted versus 0/1001 rejected batches. |
| Unknown usage and provider corrections | `Unrouted_unknown_and_empty_tenants_do_not_invent_tokens_or_charges` and `Late_attempt_moves_provider_dimension_without_adding_a_logical_request` pin empty/unknown and last-attempt semantics. |
| Worker replay/lifecycle | Six `UsageRollupWorkerTests` cases cover drain/20-page bound, empty/busy exit, cancellation, new-scope retry and secret-redacted failures; PostgreSQL integration replay composes the actual `UsageRollupCycle` with the actual store. |
| Wire and visible refresh | Serializer-derived OpenAPI fixture plus `usage rollup wire keeps gross refunds and net amounts distinct and reports lag`; Playwright `analytics refreshes rollup counters and makes pending corrections and refunds explicit` verifies a timed counter correction and pending/refund/net UI. |

| Command | Result |
| --- | --- |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; 0 errors, existing AppHost `ASPIRE010` warning remains. |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests --configuration Release --filter 'FullyQualifiedName~UsageReadIntegrationTests' --logger trx --results-directory artifacts/test-results/usage004-targeted` | PASS: 20 cases (13 new, 7 retained). |
| `dotnet test tests/Unit/UZLLM.Worker.Tests --configuration Release --no-build --filter 'FullyQualifiedName~UsageRollupWorkerTests' --logger trx --results-directory artifacts/test-results/usage004-worker` | PASS: 6 cases. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/usage004` | PASS: 596/596 across 16 test projects, no failures/skips; includes 188 PostgreSQL integration cases and 2 real-stack Aspire cases. |
| `npm run api:generate --prefix frontend/apps` and `npm run api:check --prefix frontend/apps` | PASS: documents, clients and serializer examples match actual hosts. |
| `npm run api:test --prefix frontend/apps` | PASS: 6 wire/schema cases. |
| `npm run api:types --prefix frontend/apps` and `npm run build --prefix frontend/apps` | PASS: shared types, dashboard and admin. |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | PASS: 30 passed, 1 existing Aspire-URL-only smoke skipped. |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: 0 production vulnerabilities; no dependency changes. |
| `git diff --check` | PASS |

TRX files are local ignored artifacts, not production certification. No
executed mutation score or coverage percentage is claimed. The optional static
source/test pairing script could not run because its Python runtime was absent;
inline assertion/gap review and the actual .NET/browser suites were used.
Existing dev-tool advisory handling remains documented in
[API validation](api/VALIDATION.md). Two-host load, fault/restore and live
merchant/provider verification remain separate gates.
