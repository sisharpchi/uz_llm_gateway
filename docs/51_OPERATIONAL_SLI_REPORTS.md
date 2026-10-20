# SLO-002 — Versioned operational SLI reports

## Scope and approval

The user's 2026-10-20 Continue separately promotes SLO-002 after completed
SLO-001 (`eb9fe38`), following document 45 §6–8 and ENT-012–018. OPS-006 stays
externally Blocked. This is operator-only engineering evidence, not a contractual
SLA certificate, fiscal document, maintenance exclusion, credit or launch pass.
No Gateway hot-path, routing, wallet, ledger, budget or payment behavior changes.
No UI or provider network calls. INVOICE-001 and later proposals remain unpromoted.

## Ownership and frozen policy

Usage Contracts define inputs/results, Domain owns `sli-v1` classification and
exact arithmetic, Infrastructure owns bounded PostgreSQL snapshot/publication.
Worker uses the existing leased-job/outbox/inbox infrastructure. Management's
facade rechecks session, current operator grant and recent MFA before every read
or mutation, independently of HTTP filters. There is no Usage→Billing reference,
building-block→module dependency, new database/service or architecture exception.

Deployment registers finite `Sli:Reports:Scopes` profiles. Each freezes an ID,
region, operation (`Chat`/`Responses`/`Embeddings`), mode (`All`/`Json`/`Sse`),
version 1, exact Edge/Gateway/Probe producer roster, probe maximum gap (1–300
seconds; first-profile default 300) and optional non-secret independent-probe
approval/evidence reference. No SQL, wildcard scope, customer-reported region,
arbitrary exclusion or maintenance waiver. Empty registry is the default and
rejects new builds; it does not start automatic monthly jobs or enable collection.

Example **synthetic local** configuration, not operational approval:

```json
{
  "Sli": { "Reports": { "Scopes": [{
    "id": "test-chat", "region": "test", "operation": "Chat", "mode": "All",
    "version": 1, "probeMaximumGapSeconds": 300,
    "independentProbeEvidence": null,
    "producers": [
      { "producer": "edge-a", "source": "Edge" },
      { "producer": "gateway-a", "source": "Gateway" },
      { "producer": "probe-a", "source": "Probe" }
    ]
  }] } }
}
```

List every contributing Gateway/edge/probe producer, including other nodes;
never omit an unhealthy producer to improve coverage. Producer keys/trust remain
SLO-001 deployment concerns. A reference alone does not attest independent probe
location, approved clocks, complete collection or contractual terms. Operations
must independently verify them. Profiles are copied into command and published
version evidence, so later configuration changes cannot mutate prior policy.

## Execution classes and separate attempt facts

One trusted edge execution UUID counts once, across producer/epoch duplicates.
Internal fallback attempts never multiply that denominator; deliberate client
HTTP retries have different edge identities. Exact duplicate semantic records
ignore only collector sequence. Conflicting starts/terminals/attempts, untrusted
edge correlation or conflicting operation facts remain U, never H.
Conflicted authenticated source epochs also make their affected executions U;
rejecting a conflicting delivery does not prove the accepted origin was correct.

| Class | `sli-v1` proof |
|---|---|
| H | Correlated trusted start/terminal plus successful edge delivery and explicit protocol-valid Gateway terminal success; not generic HTTP200. |
| G | Proven edge/Gateway dependency/forwarding failure; Gateway defect wins over provider evidence. Verified managed credential rejection is G, not customer policy. |
| P | Matching authenticated upstream rejection attempt/status/certainty for capacity/rate-limit/5xx, with no Gateway defect. |
| C | Proven customer policy rejection or client cancellation strictly before service failure; verified BYOK credential rejection stays distinct from provider-global outage. |
| U | Ambiguous capacity 429, unproven provider timeout/transport, missing/partial terminal, conflicting provenance, unresolved mode or cancellation order. |

Equal cancellation/failure timestamps do not prove a client exclusion. Hybrid
credential attribution uses matching actual attempt credential evidence, not
just request-selected mode. A timeout can remain financially Unknown independently
of this classifier. No classification posts, refunds or releases money.

Mode-specific scopes retain unresolved mode as U instead of silently dropping it.
Earliest trusted execution start anchors the UTC month, not terminal/import time.
A late terminal can complete an explicit later revision; old reports stay intact.
Attempt facts are separately deduplicated by attempt UUID and grouped by observed
credential/fault. Conflicts become Unknown attempt facts. They are **not** a
per-provider commercial uptime percentage: SLO-001's safe protocol has no provider
identity/completion guarantee, and absence of an observed error is not success.

Exact fractions: Gateway known-only H/(H+G), conservative H/(H+G+U), and
end-to-end H/(H+G+P+U). C remains disclosed separately. Empty denominator is
null/NoData, not 100%. Target comparison uses wide integer cross-multiplication
against 999/1000, never display rounding. The approved 840/850, 840/900 and
840/855 fixtures are direct test cases.
Metadata explicitly says `populationBasis=ObservedExecutionsOnly`; the
conservative fraction is not a bound on unobserved requests in missing intervals.
Its fixed `kind=OperationalSliReport` and non-contractual label distinguish it
from account statements, fiscal documents and future signed SLA reports.

## Coverage and qualification

Source manifests/clock/count/drop/raw membership/conflict receipts and every
required producer's UTC interval/sequence chain are frozen. Missing producer,
interval or sequence, restart without proven continuity, unverified clock,
dropped/expired raw evidence, late import (>2 minutes), clock skew (>1 minute),
source/attempt conflict and any U prevent qualification. Probe observations must
cover the month at the frozen maximum gap; failures, absent probes or unverified
independence also prevent qualification. A readiness 2xx is not H.

Coverage is Complete only with all this evidence, Incomplete when observed
executions exist but any proof is missing, or NoData when none exist. Ratios
remain labeled observed/known-only alongside warnings. TargetStatus is Met/Missed
only for complete eligible evidence, otherwise Incomplete/NoData. No automatic
maintenance exclusion. SLO-001's conservative `CollectorContinuous=false` means
its default evidence cannot certify a complete month. This is intentional.

## Persistence, transactions and recovery

Explicit migration `20261020054553_AddSliMonthlyReports` adds six `ops` tables:
mutable head/operation coordination and immutable receipt/version/source/attempt
history. Unique scope/month head, head/revision, operation publication,
kind/idempotency UUID and operation/attempt fences prevent duplicate work.
Composite operation/head and predecessor/head FKs prevent cross-scope revision;
source membership references immutable manifests, not expiring observations.
Database guards seal source membership and published operation terms and validate
publication completeness at commit. Populated history refuses Down. Only the
explicit Migrator may apply schemas; no runtime startup migration.

Command transaction: normalize closed month/reason/identity → replay lock/receipt
check → lock report head → generation/predecessor check → frozen scope/operation/
receipt → real-actor audit → existing leased job → commit. Changed terms conflict;
identical replay returns the original operation even after response loss or a
dead letter. No wallet/admission lock or provider call.

Worker transaction: Repeatable Read snapshot → head lock/generation check → bounded
explicit manifest/raw/conflict membership → calculate → immutable artifact/source/
attempt → advance head/operation → publication/revision audit + outbox → commit.
Late manifests committing after snapshot are excluded until explicit revision;
UUID/timestamp order is not a commit watermark. Bounds are 100,000 input facts,
32 MiB artifact, 30-second build deadline; no truncation. Failure records a bounded
Failed attempt with a safe code, rolls back every partial publication and uses
the existing five-attempt leased retry/dead-letter policy. A committed response
loss reuses the same operation, not a second report. Lifecycle outbox handling is
idempotent internal acknowledgement, not customer notification/SLA activation.

Raw expiry preserves old published classifications, ratios, exact manifest facts,
membership/digests and bytes. It does not promise future reclassification after
raw deletion: a new revision discloses missing raw evidence. Retention never
updates published versions. Source volume and throughput still need operational
qualification. Restore into quarantine, verify artifact/source digest, command,
head/revision and manifest/sequence links before releasing jobs/dispatch. Never
reset identities, delete evidence or replay financial actions to repair a report.

Capacity limitation: SLO-001's one-second Gateway heartbeat produces 2,592,000
manifests per producer in a 30-day month even at zero traffic, exceeding this
specification's 100,000-fact build bound. Such a build fails visibly with Bounds;
it is never truncated or claimed complete. A separately approved aggregation/
scaling design is required before offering these reports at ordinary monthly
collection volume. This task implements the approved bounded profile, not a
production-volume monthly archive or qualification. Do not increase limits or
omit producers silently to make a target pass.

If PostgreSQL itself is unavailable, cleanup cannot manufacture a persisted
Failed attempt; existing leased-job retries/dead-letter visibility and stable
operation identity remain the recovery path once the database returns.

## Operator wire contract

Base `/management/v1/admin/sli-reports`:

| Method / path | Result |
|---|---|
| POST base | Closed `periodStart`, registered `scopeId`, UUID `idempotencyKey`, `expectedGeneration=0`, bounded reason; 202 stable job + Location. |
| POST `/{id}/revisions` | Selected predecessor, positive expectedGeneration, UUID/reason; 202 job or 404/409. |
| GET base `?scopeId=...` | Newest month/revision keyset; default20/max100. |
| GET `/{id}` | Metadata, frozen scope and separate attempt facts. |
| GET `/{id}/sources` | Frozen manifest facts; default50/max100. |
| GET `/{id}/executions` | Frozen classification reason/start/mode; default50/max100. |
| GET `/jobs/{id}` | Stable operation and independent job/dead-letter state. |

All surfaces are operator-only with current recent MFA; customer roles, Gateway
keys and proposed automation credentials grant no access. Mutations require the
existing session-bound CSRF proof. Responses are no-store; DTOs allowlist metadata,
never serialize EF entities. Protected cursors bind scope or immutable version,
row kind and page size. Evidence arrays use stable frozen ordinal continuation;
monthly list uses month/revision keysets. Unknown fields/exclusion overrides are
rejected on command DTOs. Body cap 8 KiB before binding, including chunked/case
variants; 30-second I/O deadline, propagated cancellation and safe errors.

401 session absent; 403 operator/MFA/CSRF denied; 404 missing version/job;
400 invalid month/scope/fields/cursor; 409 replay/generation conflict; 413 bound;
503 dependency/integrity unavailable; 504 deadline. No arbitrary global job/raw
telemetry query or repair/export endpoint. Generated OpenAPI/client/example
artifacts document these additive v1 routes without changing old wire shapes.
Manual examples: `src/UZLLM.Management.Api/SliReports.http`; tests use real
disposable backend HTTP, not merchant/provider/production endpoints.

## Validation evidence

| Requirement | Exact tests / evidence |
|---|---|
| ENT-012: origin, credentials, capacity, cancellation | `Classifier_preserves_origin_credential_capacity_and_protocol_partitions`; `Cancellation_order_precedes_or_preserves_service_failure_without_financial_assumptions` |
| ENT-013: partial/late SSE and fallback population | `Late_terminal_and_duplicate_delivery_keep_start_month_and_one_logical_execution`; `Same_execution_failover_and_attempt_replay_do_not_multiply_either_population`; PG `Late_terminal_revision_uses_start_month_keeps_original_bytes_and_survives_raw_expiry` |
| ENT-014: exact ratios/threshold/NoData | `Exact_populations_provider_attempts_and_unknown_bound_match_the_approved_fixture`; `Target_comparison_uses_exact_integer_boundary_not_rounded_display`; `Empty_and_policy_only_denominators_remain_NoData_not_100_percent` |
| ENT-015: trusted source/snapshot/replay/conflict | Existing SLO-001 signed-origin fixtures plus `Parallel_commands_and_worker_replay_publish_one_immutable_version_receipt_audit_and_outbox`; `Repeatable_read_freezes_membership_while_a_late_manifest_commits_and_revision_discovers_it` |
| Disputed authenticated origin | `Conflicting_authenticated_source_epoch_is_Unknown_in_published_revision_not_known_success` proves H→U in a new immutable version, with old bytes/classification retained |
| ENT-016: coverage/probes/source gaps | `Missing_clock_source_loss_and_probe_disagreement_never_qualify_a_good_ratio`; `Complete_source_and_independent_probe_coverage_qualifies_only_the_known_exact_target`; default conservative SLO-001 evidence remains incomplete |
| ENT-017: operator/MFA/CSRF/scope/wire | `Sli_facade_rechecks_session_operator_and_recent_MFA_before_every_store_access`; `Sli_cursors_are_scope_version_kind_and_page_size_bound_and_preserve_immutable_order`; `Real_HTTP_operator_MFA_CSRF_scope_and_immutable_reads_preserve_no_store_and_job_identity`; `Sli_wire_is_additive_operator_only_with_no_exclusion_overrides_and_explicit_job_errors` |
| ENT-018: bounds/atomicity/cancel/recovery | `Publication_failure_leaves_no_partial_report_or_financial_effect_and_retries_same_operation`; `Committed_reply_loss_recovers_published_operation_without_second_version_or_side_effect`; `Build_deadline_and_client_cancellation_preserve_failed_attempt_and_no_published_version`; `Serialization_stops_at_exact_byte_bound_and_honors_cancellation_without_allocating_unbounded_output`; `Calculator_fact_limit_is_inclusive_and_does_not_truncate_the_next_fact` |
| Immutable history and Worker lifecycle | `Published_history_and_membership_cannot_be_mutated_or_downgraded_away`; `Sli_worker_job_uses_original_operation_and_attempt_with_forwarded_cancellation`; parallel publication test also proves inbox replay |

Final verified evidence — 2026-10-20, Windows, .NET SDK 10.0.401, Node 24.19.0,
Docker 29.7.2, disposable PostgreSQL 17/Redis 8. No shared database was migrated.

| Command / check | Clean result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, all projects up to date |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS, zero errors; existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/slo-002-final-corrected` | PASS: 1318, zero failed/skipped; 17 Completed TRX reports |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --no-build --filter FullyQualifiedName~SliReportIntegrationTests --logger trx --results-directory artifacts/test-results/slo-002-corrected-focused` | PASS: all 14 report/HTTP/recovery cases on the final source |
| `npm run api:generate --prefix frontend/apps` then `npm run api:check --prefix frontend/apps` | PASS; Gateway, Management and serializer fixtures match |
| `npm run api:test --prefix frontend/apps` and `npm run api:types --prefix frontend/apps` | PASS: 18 schema/client cases and strict type checks |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin; existing dashboard chunk warning |
| `dotnet ef migrations has-pending-model-changes --project src/BuildingBlocks/UZLLM.Persistence/UZLLM.Persistence.csproj --configuration Release --no-build` | PASS, no drift; design-only dummy port-1 connection, no migration |
| Solution-level discovery | PASS, new calculator, committed-reply, Worker and wire cases discovered; final full-suite TRX also includes the added source-conflict revision test |
| Static contract/history comparison | PASS: all four Gateway and old 104 Management paths/schemas unchanged; six new paths/seven operations |
| Documentation/whitespace | PASS: balanced fences/local links, `git diff --check` and task-only staged scope/whitespace review |

Final totals include PostgreSQL 287, Gateway 414, Usage 92, Worker 81,
Observability 41, architecture 15 and AppHost 2. All 107 new .NET cases pass:
76 Usage classifier/policy, 17 facade/bounds/Worker/wire and 14 PG report/HTTP.
TRX and intermediate testing notes remain ignored local artifacts, not launch
attestations. No UI changes/browser screenshots or qualifying load/HA/PITR drill
is claimed by these tests.

Initial focused build found a missing test-host namespace import;
it was corrected rather than hiding the test. Clean PostgreSQL report/HTTP runs
expanded from 7 to 11 to 13 passing cases; operator/wire checks pass 17 cases.
The first Node schema run exposed an outdated assumption that all non-statement
202 responses are empty. The fix explicitly requires the two SLI job JSON/Location
responses while preserving empty-body assertions for the other existing operations;
no assertion was disabled.
Final review additionally tightened disputed source epochs and contradictory
JSON/SSE mode evidence to U, rather than merely warning on a known H. The first
full 1317-test run is retained as **interim** evidence, not the final corrected
baseline. The final corrected full run above and fourteen-case focused run
include those changes; no pending or superseded run is reported as final evidence.

Static source/test pairing could not run because Python is unavailable. Tests
follow the inline research/plan workflow; no measured coverage or mutation-run
percentage is claimed. Existing source-map-js advisory, Aspire/chunk warnings,
distinct-host OPS-006, managed HA/PITR and live provider/merchant/business approvals
remain independent. No deployment, paid traffic or contractual SLA was activated.
