# SLO-001 — Unsampled execution evidence and collector import

## Scope and authority

The user's Continue separately approves this independent P2 increment after
REPORTING-003, with ENTERPRISE-002/OPS-003/OPS-005 complete. Follow document 45
§6/7/9/10, NFR-001/013/070–073 and ADR-011/014/017. OPS-006 remains Blocked by
unavailable distinct staging hosts. This is capture/import evidence, **not** an
H/G/P/C/U calculator, monthly report, contractual SLA, new financial authority,
fiscal feature, UI or launch-gate pass. SLO-002 requires separate approval.

## Implemented ownership and flow

```text
TLS edge: fresh execution ID + fixed-field buffered log
                   |
Gateway: pre-auth Started -> Attempt(s) -> Terminal
                   |
bounded memory -> background signed private spool
                   |
Worker: verify producer/kind/region/HMAC -> atomic Usage-owned PG import
                   |
ops.sli_manifest + sli_observation + immutable sli_import_receipt
```

Observability owns the neutral allowlisted protocol, buffer and file transport;
Gateway supplies execution facts; Usage Domain assesses capture-quality warnings;
Usage Infrastructure owns PostgreSQL import; Worker composes and polls. No
building-block-to-module or Usage-to-Billing reference, new service/database,
runtime migration, financial mutation or inference-side PostgreSQL write is added.
There is no public/customer/operator upload endpoint or arbitrary query language.
Import receipts are the append-only operational import audit, not user-account
audit events. No external side effect is dispatched, so import creates no outbox
event. Source rows and receipt commit together; receipt failure rolls both back.

Gateway captures all three inference paths before authentication, independently
of trace sampling. The 1,024-entry queue uses only a short in-memory critical
section and TryRecord; inference never awaits a file, collector or database sink.
A background one-second export holds at most one pending signed batch and the
queue. The private spool is capped at 128 entries. Full/unavailable sinks retain
that pending batch while new overflow is counted by sequence. Crash/restart,
failed serialization, missing heartbeats and missing predecessors remain gaps,
not zero-loss claims. No automatic inference probe or financial fail-open exists.

Metadata is restricted to UUIDs, bounded enums/status codes, timestamps and
delivery/origin flags. No prompt, response, URL/query, IP, headers, credentials,
provider error message, model label, tenant identifier or monetary amount is in
the observation shape. Request/attempt UUIDs allow later authorized internal
correlation. Existing X-Request-Id and trace behavior are unchanged.

## Trust, stream and error semantics

Nginx always replaces public `X-UZLLM-Edge-Execution-Id` with its random 128-bit
`$request_id` (32 hexadecimal characters). Gateway accepts it only over HTTPS
from an explicitly configured **physical socket peer**; forwarded client-IP
headers never establish trust. It strips the header after resolving it. Direct,
malformed, combined, cleartext and untrusted-peer values get a fresh local ID
with TrustedEdge=false and cannot establish edge coverage. Restrict Kestrel
ingress to the private edge; an IP allowlist is not client-certificate identity.

Started and Terminal share one execution ID; provider failover produces separate
Attempt IDs, not new logical executions. Explicit successful JSON/terminal SSE
writing is required for TerminalSuccess. HTTP200, partial output, cancellation,
missing terminal records and settlement failure do not independently prove H.
An edge record cannot claim protocol success. It distinguishes a recognized
Gateway response from a pre-Gateway failure without logging response headers;
a proxied provider 502 is not automatically an edge-origin failure.

Preserve normalized provider category/status/certainty, actual attempt credential
mode, request-selected Managed/BYOK/Hybrid mode, Gateway failure, cancellation and
failure timestamps. Unknown cost remains independent of delivery evidence.
Current limiter 429 lacks a reliable operator-ceiling/customer-bound discriminator:
it is deliberately **not** automatically PolicyRejection. SLO-002 must retain
Unknown unless independently sufficient provenance exists. Equal timestamps,
missing events or conflicting origins likewise cannot justify a provider/client
exclusion. No routing, retry, reservation or settlement behavior changes.

## Protocol, bounds and persistence

Version 1 envelope is producer + base64 exact payload + lowercase SHA-256 +
HMAC-SHA256 over `producer + LF + digest + LF + base64Payload`. Import requires
a deployment-configured producer key (at least 32 random bytes), source kind
and exact region; a digest alone is not authentication. JSON rejects unknown,
duplicate, missing mandatory and numeric-enum fields. Bounds: 2 MiB envelope,
1,024 observations, one-hour UTC collection interval; no truncation. Metadata
includes epoch, batch number, attempted sequence range, accepted count, dropped
count, interval, clock/continuity declarations, terminal coverage and receivedAt.

Migration `20261019155630_AddSliObservationEvidence` adds three `ops` tables.
Unique producer/epoch/batch and producer/epoch/sequence plus event identity fence
duplicates; composite source FKs and source/region/time/execution indexes support
future bounded reads. A producer/epoch transaction advisory lock serializes only
imports. Exact replay appends a Duplicate receipt and returns the existing
manifest; changed terms, overlapping ranges or duplicate events conflict without
partly inserting a new source. Other producer/epoch deliveries remain independent.
Identical execution IDs from edge/Gateway or epochs are **not** multiple logical
requests: later classification must correlate/deduplicate by trusted execution ID.

Accepted receipts seal membership, with deferred count/source checks at commit;
no xmin or timestamp commit cursor is used. Manifest/receipt UPDATE/DELETE and raw
observation UPDATE are rejected. Populated history blocks migration Down. Raw
observation expiry follows the existing explicitly approved diagnostic schedule;
each bounded delete atomically appends an Expired count/digest receipt. Manifest
identity/count/hash and import history survive; duplicate replay cannot resurrect
expired observations. Future report membership must reference manifests, not
cascade to raw data. Raw expiry means unavailable reclassification evidence,
never permission to treat the old source as complete. No legal schedule is assumed.

Missing predecessor/sequence, dropped records, unverified clock, terminal partial,
epoch boundary, discontinuous collector, zero traffic, unreachable probe, clock
skew (>1 minute future) and ingest lag (>2 minutes) are recorded separately.
Warnings reflect evidence **at import time**; late predecessor arrival does not
rewrite old receipts. Partial/Unknown terminal coverage requires later start/end/
edge correlation; it is not a precomputed success count. No ratio/NoData report
exists in this task. Clock/continuity booleans alone cannot qualify an interval.

## Configuration and operational handoff

Capture and import are disabled by default. Apply the explicit Migrator using
the existing migrator connection before enabling Worker import. Do not point
tests or manual migration commands at an unknown/shared database.

Optional `deploy/compose.sli.yml` overlays the existing production topology.
Supply private node-local spool and edge-log directories, region, **physical edge
peer** and separate random base64 producer-key files (`gateway-a.key`,
`gateway-b.key`, `edge-a.key`, `probe-a.key`). Keep directories inaccessible to
customers; grant only the required service UID access. Never commit the files or
keys. Existing certificates, network isolation and server-certificate verification
still apply. Use private authenticated transfer for remote collectors/nodes;
there is no public receiver or newly introduced shared filesystem requirement.

Gateway `Sli:Capture`: Enabled, Producer, Region, Directory, Key,
TrustedEdgePeers array, ClockSynchronized (default false). Worker `Sli:Import`:
Enabled, Directory, Producers:{producer}:{Key,Source,Region}. Key-per-file
secrets use the existing double-underscore names. Rotate by adding a **new producer
identity/key**, preserving old trust while queued old envelopes drain; do not
replace a key and make old evidence unverifiable. Restart creates a new epoch,
not proof of clean predecessor closure.

With Node 22+, run `node deploy/sli/collect.mjs /private/collector-config.json` for a **closed**
edge-log segment or independent readiness observation. The config contains
producer, source (`Edge`/`Probe`), region, keyFile (base64), stateFile,
outputDirectory, and segmentFile (Edge) or HTTPS url (Probe). It never contains an
inference/API key. Only `/edge/health` and `/health/ready` are probe targets;
no redirects, request bodies, paid inference or response content retention.

Example Edge configuration (paths are deployment-local, not repository files):

```json
{
  "producer": "edge-a", "source": "Edge", "region": "test",
  "keyFile": "/private/sli-keys/edge-a.key",
  "stateFile": "/private/sli-state/edge-a.json",
  "outputDirectory": "/private/sli-inbox",
  "segmentFile": "/private/sli-logs/closed-segment.log",
  "clockSynchronized": false
}
```

Schedule readiness from an independent host/network; local health is not external
coverage. DNS/TLS/timeout and failed statuses remain unsuccessful probe evidence.

Close/rotate Nginx logs before collecting; limit each segment to 512 KiB/1,024
records, splitting rather than truncating. Nginx buffered logs can be lost on
crash/disk failure and record only completed HTTP executions; they cannot prove
DNS/TLS reachability or closure of hung requests. CollectorContinuous remains
false conservatively; independent source/probe/clock/sequence reconciliation is
required before any later completeness claim. Empty segments produce heartbeats,
not 100% availability. A repeated nonempty segment is rejected for review.

Collector private state persists exact pending bytes before delivery; retry after
lost acknowledgement reuses those bytes. One writer holds an exclusive lock.
After a crash, stop that writer, inspect the lock/state/pending output and reconcile
before manually removing a stale lock; never reset epoch/sequence to hide loss.
Worker imports at most 16 files per two-second poll with a 30-second per-file
deadline, ignores `.tmp`, retries transient DB failures and quarantines rejected/
conflicting deliveries. Success removes only that consumed spool file; immutable
PG source/audit remains. Review quarantined files privately, not in ordinary logs.
Keep imports and ordinary Worker dispatch disabled in a restored quarantine until
source/digest/sequence/retention checks finish; don't replay business side effects.

The PostgreSQL sweep does **not** purge filesystem spools, quarantine files or
edge logs. Deploy approved private rotation/expiry for those separately, preserving
source/count/gap audit before expiry. Missing proof stays incomplete. Source volume
and database/retention throughput require operational qualification, not just fixtures.

## Validation and limitations

Final evidence verified 2026-10-20, on Windows with .NET SDK 10.0.401, Node
24.19.0 and Docker 29.7.2. PostgreSQL 17/Redis 8 tests used disposable containers;
no shared database was migrated. Required commands/results:

| Command / check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, 43 projects |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx --results-directory artifacts/test-results/slo-001-final` | PASS: 1211, zero failed/skipped, 17 Completed TRX reports |
| `node --test deploy/sli/collect.test.mjs` | PASS: 7 |
| `node --test deploy/load/qualify-report.test.mjs` | PASS: 10 analyzer regressions, not OPS-006 qualification |
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:ops002 .` then `node --test deploy/tests/payment-edge.test.mjs` | PASS: 17 edge/payment cases including real TLS and `nginx -t` |
| Production Compose and production + `deploy/compose.sli.yml`, `config --quiet` | PASS with synthetic required variables; no deployment |
| `dotnet ef migrations has-pending-model-changes --project src/BuildingBlocks/UZLLM.Persistence/UZLLM.Persistence.csproj --configuration Release --no-build` | PASS, no drift; design-only dummy connection |
| `npm run api:check --prefix frontend/apps` | PASS, both wire surfaces/client artifacts unchanged |
| `npm run api:test --prefix frontend/apps` and `npm run api:types --prefix frontend/apps` | PASS: 18 schema/client tests and type checking |
| Dashboard/admin Docker build stages | PASS; existing dashboard chunk warning; no UI/browser change claimed |
| `git diff --check` | PASS |

Release totals include PostgreSQL 273, Gateway 397, Observability 41, Usage 16,
Worker 81, architecture 15 and AppHost 2. All 78 added .NET cases pass: capture
38, Usage 16, Gateway 13, PostgreSQL 6, Worker options 5. Evidence files are local
ignored artifacts, not production gate attestations. The Windows shell lacks
`sh`; the shell wrapper was not run. Real Node/Docker edge fixtures performed
configuration checks instead. No npm-audit pass or line-coverage claim is made.

Task-specific mapping:

| Scenario / requirement | Exact evidence |
|---|---|
| ENT-012: origin/credential/policy facts, not financial zero | `Observation_pre_admission_denials_preserve_policy_vs_dependency_vs_ambiguous_capacity`, `Observation_BYOK_authentication_remains_distinct_from_platform_dependency_failure`, `Service_failure_and_client_disconnect_order_is_recorded_independently_of_finances` |
| ENT-013: terminal streams and one execution across attempts | `Observation_stream_error_disconnect_and_settlement_failure_never_become_HTTP200_success`, `Observation_same_model_failover_keeps_one_execution_with_separate_attempts`, `Observation_native_responses_SSE_records_terminal_protocol_success_without_content` |
| ENT-015: trusted identity/replay/conflict/clock/sequence | `Parallel_import_replay_conflict_overlap_and_restart_preserve_one_explicit_source_set`, `Only_TLS_and_physical_trusted_peer_can_supply_execution_identity`, `Independent_Node_edge_fixture_verifies_in_Dotnet_with_exact_known_digest` |
| ENT-016: gaps/zero traffic/probe failures/no sink wait | `Concurrent_overflow_and_heartbeats_disclose_every_lost_sequence_without_sink_waits`, `Missing_out_of_order_dropped_clock_zero_traffic_and_probe_evidence_remains_explicit`, Node `independent readiness probe never calls inference or treats DNS TLS timeout as success` |
| ENT-018: atomic bounded import/audit/cancel/replay | `Receipt_audit_failure_and_cancellation_leave_no_partially_accepted_source`, `Import_commit_failure_rolls_back_rows_receipt_and_all_financial_side_effects`, `Private_spool_recovers_after_sink_failure_and_committed_response_loss_without_reexecuting` |
| Retention/sealing | `Published_source_membership_is_sealed_and_retention_keeps_manifest_and_expiry_receipt` plus migration up/down regressions |
| Edge privacy/forgery | real Docker/TLS `SLO_001_edge_replaces_forged_execution_ids_and_logs_only_allowlisted_metadata` |
| Endpoint coverage | `Capture_matches_the_case_and_trailing_slash_semantics_of_inference_endpoints` |

Initial focused validation exposed a PostgreSQL data-modifying CTE composition
error in retention and an xUnit assertion-analyzer warning; both were corrected,
not suppressed. Later clean results, not those attempts, are completion evidence.
Static pairing could not run because Python is unavailable; no pairing/line-coverage
percentage is claimed. Test design followed the inline research/plan/quality review.

Independent staging probes, actual retention/NTP/ingress approvals, source-loss
monitoring and distinct-host load/fault/HA/PITR qualification remain operational
gates. Existing source-map-js dev dependency advisory (GHSA-68fv-2mgg-jv7q), Aspire
warning and dashboard chunk warning are unrelated and not fixed/waived here.
No public service, real provider, merchant callback or paid traffic was activated.

Engineering references checked 2026-10-19: official
[Nginx request identity variables](https://nginx.org/en/docs/http/ngx_http_core_module.html#var_request_id)
and [buffered/escaped access logging](https://nginx.org/en/docs/http/ngx_http_log_module.html).
