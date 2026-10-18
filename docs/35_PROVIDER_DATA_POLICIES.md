# Provider Data-Policy Provenance — CATALOG-003

## Scope and authority

NFR-052 requires understandable upstream retention/training metadata. DX-002
provides versioned contracts. User approval to continue to the next independent
task selects CATALOG-003 while OPS-006 is externally blocked and REFUND-002
awaits a financial-policy decision. Neither blocker is relabeled complete.

This task adds metadata and audited publication, not upstream-policy enforcement,
ZDR routing, payload-retention changes, residency pinning or new UI screens.
Existing chat/Responses/embeddings routing, secrets and billing are unchanged.
No real provider policy is seeded or inferred from its brand, adapter, endpoint,
account, credentials or gateway payload opt-out. PRIVACY-003 remains a separate
policy/specification task.

The subsequent [PRIVACY-003 draft](39_PRIVACY_ELIGIBILITY_SPEC.md) separates
private credential-scoped qualification from this public metadata. It awaits
approval and does not change CATALOG-003's contract or enable strict routing.

## Meaning of the metadata

- Retention: `Unknown`, `MayRetain`, `NoRetentionReported`.
- Training: `Unknown`, `MayTrain`, `NoTrainingReported`.
- Residency: `Unknown` or `ProviderReported`, with up to 16 reported region
  labels. Labels are descriptive, not normalized routing eligibility.
- Every publication requires an HTTPS source URL, a past/current UTC observation
  date after the Unix epoch, applicability scope and conditions. Provider account, endpoint, opt-in,
  abuse-monitoring and regional exceptions belong in these mandatory texts.
- Unknown residency has no regions. An absent publication yields explicit
  unknown classifications, no regions and null provenance; it never defaults
  to no retention/training. A later publication can retract earlier claims to
  unknown without destroying their history.

`NoRetentionReported` and `NoTrainingReported` mean only what the dated source
reports under its stated conditions. They are not independently verified claims,
platform guarantees or ZDR certification. There is no `zdr` boolean. Observations
are not automatically refreshed or guaranteed current; consumers must show the
date, scope and conditions, and must not hide unknown states or conditionality.
Live commercial/account eligibility and legal review remain external obligations.

## Ownership and transaction boundary

Catalog Application validates classifications, bounds, identifiers, HTTPS URLs,
UTC dates and region consistency. URLs reject credentials, query strings,
literal IP/localhost references and control characters; the server never fetches
or resolves a source, so provenance is not authenticated automatically.

These are shared catalog disclosures visible to authorized customers across
tenants, not private per-credential policies. Operators must publish only
public documentation and nonsensitive applicability text: no tenant/account
identifiers, private contract evidence, internal links or secrets. URL syntax
validation cannot prove that a source or its contents are publicly accessible.

Catalog Infrastructure serializes publication on the provider row and rejects
an observation that does not advance the latest source observation. PostgreSQL
microsecond normalization makes replays deterministic. Immutable versions are
stored in `catalog.provider_data_policy`, with provider/operator FKs and a unique
provider/observation index. UPDATE/DELETE triggers protect evidence; the
DDL-denied runtime cannot truncate it. Concurrent identical publications result
in one version and one audit event, with the others conflicting.

Management orchestration commits publication and `provider-data-policy.published`
audit together, including actor, provider, version, observation and reason.
Audit failure rolls back the publication. There is no external side effect to
deliver, so no new outbox/job/Worker consumer is required.

## HTTP contracts

See [API Contracts](11_API_CONTRACTS.md#provider-policy-provenance-catalog-003)
and [manual HTTP examples](../src/UZLLM.Management.Api/ProviderDataPolicies.http).
The customer route is tenant/project/session authorized and `no-store`; it does
not depend on model prices or a fee policy. It exposes an active provider's
latest conditional observation and omits operator identity and audit reasons.
Operator history/exact-version reads require recent MFA. Publication additionally
requires CSRF. History is bounded to 1–100 entries; identical/non-advancing
publication is `409`, not a second publication. No existing endpoint is removed.

## Migration and rollout

Apply `20261017010000_AddProviderDataPolicies` only through the explicit Migrator
before running the new Management API. It introduces one Catalog table, indexes
and mutation-protection function/trigger; no financial schema changes or new
configuration/packages. The Down migration refuses to discard populated policy
evidence. Do not disable triggers or truncate production evidence for rollback;
retain the schema when reverting application binaries. Disposable test resets
explicitly clear only their own fixture data before exercising historical Down.

## Acceptance evidence

Validated locally on 2026-10-17 using Windows, the repository-pinned .NET 10 SDK,
Node 24.19.0 and disposable PostgreSQL 17/Redis 8 fixtures. The task source was
unchanged between the final baseline and focused reruns. Unit cases extend the
existing Catalog suite; infrastructure cases use real Management
HTTP/session/MFA/CSRF, not mocked backend routes.

| Requirement | Exact regression evidence |
|---|---|
| Unknown policy is explicit, not marketed as ZDR | `Customer_policy_returns_explicit_unknown_and_enforces_tenant_project_boundaries`; `PublishAsync_preserves_conditional_unknown_and_dated_provenance` |
| Dated provenance and safe publication | `Publication_requires_operator_recent_mfa_csrf_and_records_dated_audit`; `PublishAsync_rejects_invalid_claims_provenance_or_bounds_before_store` |
| Concurrent correction/history and database protection | `Concurrent_publication_preserves_history_unknown_correction_and_append_only_evidence` |
| Audit and publication are atomic | `Audit_failure_rolls_back_policy_and_requires_transaction` |
| Timestamp/boundary precision | `PublishAsync_normalizes_observation_to_database_precision_for_replay_order`; `PublishAsync_accepts_exact_limits_and_current_utc_observation` |
| Versioned public contract and operator/CSRF metadata | `Provider_policy_wire_documents_unknown_provenance_and_operator_security` |

Initial focused infrastructure run: 3 passed, 1 failed because the new POST
had not yet attached the existing CSRF filter. The filter was added; the failing
assertion was preserved. Final focused rerun: 4 passed, 0 failed/skipped.

| Validation command / evidence | Final result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS, zero errors; existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/catalog003-baseline-final` | 1006 passed, 0 failed, 0 skipped; 16 suites |
| Catalog / Gateway contract / PostgreSQL-Redis / architecture / Aspire suites within that baseline | 33 / 360 / 229 / 15 / 2 passed |
| Catalog project, Release `--no-build --filter FullyQualifiedName~ProviderDataPolicyServiceTests` | 28 passed, 0 failed/skipped; `artifacts/test-results/catalog003-focused-unit-final` |
| Persistence Integration project, Release `--no-build --filter FullyQualifiedName~ProviderDataPolicyIntegrationTests` | 4 passed, 0 failed/skipped; `artifacts/test-results/catalog003-focused-current-final` |
| `dotnet test UZLLM.slnx --configuration Release --no-build --list-tests` plus baseline TRX attribution | 13 discovery entries; MemberData expands at runtime; all 33 new task cases actually executed and passed |
| `npm ci --prefix frontend/apps`, `npm run api:generate --prefix frontend/apps`, `npm run api:check --prefix frontend/apps` | PASS; checked-in schemas, examples and generated client match |
| `npm run api:test --prefix frontend/apps` | 15 passed, 0 failed/skipped, including serializer-derived policy fixtures |
| `npm run api:types --prefix frontend/apps`, `npm run build --prefix frontend/apps` | PASS; dashboard and admin |
| `npm audit --omit=dev --prefix frontend/apps` | Zero production dependency vulnerabilities |
| Semantic comparison against base HEAD `dd9ce37` | All previous 109 Management operations and existing schemas unchanged |
| Local Markdown targets / `git diff --check` | 85 valid links / PASS |

TRX and build outputs are ignored local artifacts, not a release manifest or
hosted-CI record. Test containers were cleaned up; existing user PostgreSQL and
pgAdmin containers were left running. No user database was migrated. The
pre-existing dashboard chunk-size warning and one high dev-dependency advisory
reported by `npm ci` remain outside this task; production audit is zero.

No browser/TLS-edge acceptance run or live provider-policy verification is
claimed for this API-only task. Real HTTP/PostgreSQL authorization/publication
tests prove the scoped repository behavior; they do not pass two-host load,
merchant/provider, legal/residency or paid-launch gates. REFUND-002 remains
blocked pending an explicit financial-policy decision.
