# INVOICE-001 — External fiscal and contract/report evidence

## Approval and scope

The user's Continue separately promotes document 45's INVOICE-001 after
REPORTING-003 and SLO-002. OPS-006 remains externally Blocked. This increment
records external provenance, not invoices issued by UZLLM, tax calculation,
payment receipts, debt, postpaid billing, contractual availability or remedies.
BILLING-004 claims/credit links and FRONTEND-017 UI remain unpromoted.

Billing owns policy/contracts and PostgreSQL evidence. Management owns current
session/operator/recent-MFA authorization at both facade and HTTP boundaries.
Existing statement and Usage SLI contracts supply immutable report reads; no
Billing→Payments cycle, provider SDK, architecture exception, Gateway hot-path
change, external HTTP call, Worker job or new infrastructure is introduced.

## Default-deny versioned profiles

`Reporting:Evidence` binds `AllowLiveRegistration` (default false) and `Profiles`
(default empty, maximum 32). Profiles are deployment configuration, not a browser
approval API. Each explicitly defines ID/version, Synthetic, canonical stable
issuer/jurisdiction/series, currency, integer unit scale (0–6), document kind,
controlled issuer-identity/tax/correction/archive references, effective UTC window,
optional revocation instant/supersession reference, and finite service scopes.
Each scope declares ID/region/Chat|Responses|Embeddings/All|Json|Sse. No SQL,
wildcard, inferred currency scale, tax rate, URL or current FX conversion.
Configuration binds strictly: malformed/unknown profile fields fail startup,
not silent array-item omission. Nullable revocation/supersession fields have
explicit optional defaults; absence never discards an otherwise valid profile.

Finance, Legal, Security and Operations responsibilities each require reviewer
IDs, immutable approval references and dates no later than the effective date.
Signed contract references additionally require a Commercial responsibility.
These are transcribed external approvals, not authentication claims or software
verification of their truth. Actual named reviewers and retained signed records
are independent activation prerequisites. Different responsibilities need not
imply different application accounts; none grants an operator permission.

Profiles are active on `[effectiveFrom,effectiveTo)`, and blocked at/after
revokedAt. Live profiles additionally require AllowLiveRegistration. Synthetic
profiles are explicitly labeled and prohibited in the Production environment.
No sample/live profile is shipped in appsettings. Test profiles are synthetic
(`TEST`, scale 3); they do not prescribe UZS/USD, a jurisdiction or tax policy.

The first accepted operation stores exact profile JSON/hash by ID/version;
changed terms cannot reuse that version. Supersession needs a new version and
reference, preserving old bytes. Deployment withdrawal/revocation denies new
registrations/links; previously committed identical commands may replay their
original result after withdrawal, but only after fresh operator authorization.
Old responses describe frozen historical policy, not current live approval.
The registry is captured at startup, not hot-reloaded. Apply deployment
withdrawal/revocation to every Management replica with a controlled restart;
do not claim global revocation while an old enabled configuration still serves.
Keep dated deployment revocation/supersession evidence with the signed external
approval archive; do not overwrite stored profiles to reflect a new decision.

## Fiscal observation and external identity

`Registered` means an operator recorded approved-profile external evidence,
**not** that UZLLM issued or independently authenticated a legal document.
The command includes original external operation reference, process-verification
reference, reason and optional predecessor. A document contains external number,
date/kind, controlled customer identity snapshot reference, exact total/net/tax
unit strings, lowercase SHA-256, archive and issuer-verification references.
References are plain bounded ASCII identifiers, never URLs, paths or uploads.
No remote retrieval occurs. A checksum establishes integrity, not authenticity.

Total is a positive canonical integer string, at most 38 digits. Net and tax
must both be known or both null; known nonnegative parts must sum exactly to
total. Null is unknown, not zero/exemption. Currency and scale come only from
the frozen profile. This supports aggregate evidence, not multirate invoice
line calculation; do not activate a profile whose approved requirements need
unsupported fields. Do not mix fiscal units with wallet micro-USD/tiyin or
revalue historical prices/FX. External documents may have arbitrary numbers;
this first controlled-reference profile deliberately rejects unsupported syntax
instead of stripping characters, fetching URLs or assuming legal equivalence.

Issuer identity is canonical `(issuerId,jurisdiction,series)`, independent of
profile version and organization. Document number is unique across registrations
within that identity; new UUIDs, tenants or profile versions cannot recycle it.
Operations have a separate issuer-scoped identity. Finance must govern issuer
aliases/canonicalization in approved deployment profiles; software does not
infer legal entity equivalence from a display name or tax-ID-shaped string.

- Unknown external result: store Unknown without document/link; do not issue or
  retry an external integration. There is no issuance connector in this task.
- Reconciliation: append a predecessor-linked Registered/ReviewRequired/Cancelled
  observation using the same external operation identity. Never overwrite Unknown.
- Registered correction: new operation, new external number, new immutable record
  and predecessor; same-number local replacement is rejected by this profile.
- Registered cancellation/review/unknown correction: new externally evidenced operation, linked
  predecessor; no financial mutation. ReviewRequired cannot publish a fiscal link.
- A cancelled chain cannot be reopened, and one predecessor cannot fork. The
  current leaf describes disposition; an old Registered row remains historical,
  not proof it is currently valid. Expose supersession through predecessor links.

Changed replay terms/maker or duplicate business identity conflict. Byte-identical
normalized replay returns the same record without another audit. A failed local
response does not mean the transaction or external issuance did not occur.

## Contract and report linkage

Contract references freeze organization, stable contract ID/policy version,
customer-identity reference, finite scope, effective interval, optional signed
agreement reference/digest, actor/reason and approved profile. Unsigned and signed
records alike remain EvidenceOnly. No implicit remedy, cap, rate, currency,
claim window, credit request, self-approval or inferred eligibility is introduced.

New contracts cannot overlap current effective windows in the same tenant/scope.
Amendments use the selected current predecessor and a new policy version;
revocation appends the same frozen contract terms with Revoked disposition.
Superseded/revoked references cannot acquire new report links. Old evidence and
links survive; historical revocation does not debit or erase a prior credit.

A link selects one closed UTC month and explicit immutable contract/statement/
SLI version IDs, with at least one report. The entire month must fit the contract
window. Statement reads constrain organization and posting month. SLI scope ID,
region, operation, mode and execution month must exactly match the frozen scope.
An operator may link Incomplete/NoData engineering evidence but it never becomes
a qualified contractual report. Freeze source digests and coverage; report IDs
remain FK-protected. Revisions require new explicit links, never moving `latest`.
Repeated report combinations cannot create duplicate links. Later claim business
uniqueness/overlap/credit allocation belongs to BILLING-004, not this journal.

## Persistence and transaction boundary

Explicit migration `20261020074043_AddReportingEvidence` adds two billing tables:
`reporting_profile` and a discriminated `reporting_evidence` journal for fiscal,
contracts and links. Embedded command identity/hash acts as the immutable receipt;
no separate mutable financial state or redundant per-kind receipt table.

Tenant/kind-composite predecessor FK, same-tenant contract and statement FKs,
SLI version FK, unique predecessor, issuer document/operation fences, contract
policy-version and link-combination indexes prevent cross-tenant or duplicate
history. DB triggers reject profile/evidence UPDATE/DELETE and invalid contract
link kinds. Runtime TRUNCATE/DDL remains denied; populated Down refuses erasure.

One transaction serializes this **low-volume administrative journal** with a
dedicated advisory lock, validates replay/profile/business identity and report
references, writes frozen evidence/profile/receipt and real-actor audit, then
commits. It never takes a wallet/admission lock. Lock wait is capped at 5 seconds,
statements at 30 seconds; API total deadline 30 seconds. No issuer/network call
or remote renderer in the transaction. There is no side effect to dispatch, so
no artificial outbox notification or Worker abstraction is added.

Audit failure rolls everything back; cancellation propagates. An ambiguous commit
response is recovered by the original idempotency key in a fresh request/scope,
not a replacement operation. Discard failed scoped DbContexts before retry.
Immutable JSON is capped at 32 KiB, SHA-256 checked on reads and never silently
truncated. Audit contains IDs/kind/state/profile version/synthetic flag, not
legal identifiers, document numbers, raw documents, tax details or operator reason.

## Operator API

Base `/management/v1/admin/organizations/{organizationId}/reporting-evidence`:

| Method/path | Behavior |
|---|---|
| POST `/fiscal` | Register external observation/reconciliation/correction/cancellation. |
| POST `/contracts` | Register contract provenance/amendment/revocation. |
| POST `/links` | Pin selected report versions as EvidenceOnly. |
| GET `/{kind}` | `fiscal`, `contracts` or `links`; newest recordedAt/UUID keyset. |
| GET `/{kind}/{id}` | Same-org immutable operator provenance, or 404. |

POST returns 201 plus Location, including exact replay. Commands use UUID
idempotencyKey and reason, strict undeclared-field rejection, 8 KiB body cap
(including chunked/case-variant paths). Lists default20/max100 and protected
organization/kind/page-size-bound cursor, maximum2048 characters. UTC input
timestamps require PostgreSQL microsecond precision; finer signed terms are
rejected, not silently rounded. Recorded timestamps use that same precision
in both JSON and columns, preserving stable pagination even at equal instants.
No arbitrary
export, customer provenance route, profile-approval endpoint or bearer scheme.
All routes recheck current session/operator/recent MFA; mutations require existing
session-bound CSRF. All responses no-store. Current Owner/Admin/ReadBilling and
Gateway/automation credentials do not grant access to this operator-only surface.
Safe 401/403/404/400/409/413/503/504 failures; client abort is not success or a
manufactured server timeout. Examples: `src/UZLLM.Management.Api/ReportingEvidence.http`.

## Activation, retention and quarantined restore

Keep live registration disabled until finance/legal approve canonical issuer/
customer identities, jurisdiction, numbering/correction/cancellation vocabulary,
currency/scale and complete tax breakdown, signatures, verification provenance,
archive/access and retention/legal-hold schedule. Security/operations approve
deployment controls and restore ownership; commercial/legal supply each signed
agreement and measurement/remedy/dispute terms before future SLA qualification.
Actual authority/ERP protocol, issuance and live verification need a separately
approved integration task. Synthetic tests cannot pass any of these gates.

Preserve profile/evidence/command hashes, actor/tenant/predecessor links, source
report IDs/digests and immutable statement/SLI versions under financial/audit
legal hold. A local pg_dump/pg_restore into a new quarantined disposable database
checks historical bytes/digests without starting Workers or issuer calls. At
production restore, independently reconcile every profile hash, evidence hash,
receipt/business identity, tenant/FK and supersession chain against the external
archive and deployment approvals before enabling registration or future dispatch.
Never regenerate legal numbers, reset operation identities, delete evidence or
replay financial approvals to repair metadata. Local restore is not HA/PITR/RPO
qualification; OPS-006/007 and the paid-launch gates remain independent.

## Validation

| Requirement | Exact regression evidence |
|---|---|
| ENT-001: approved/default-deny profile, synthetic/non-fiscal boundary | `Default_registry_and_live_flag_never_substitute_for_approved_profile`; `Deployment_binding_rejects_synthetic_profiles_in_Production`; `Evidence_profile_withdrawal_preserves_replay_but_blocks_new_registration_and_same_version_redefinition` |
| ENT-001: strict deployment binding | `Invalid_deployment_profile_is_rejected_instead_of_silently_dropped_from_the_array` (missing issuer and document-kind partitions) |
| ENT-009: issuer/operation identity, concurrency, replay and external uncertainty | `Fiscal_parallel_replay_and_issuer_identity_fences_include_other_tenants`; `Fiscal_evidence_reconciles_unknown_once_and_preserves_correction_cancellation_and_financial_history` (initial Unknown and later Unknown correction/review, same-operation reconciliation, new-number correction, cancellation and old bytes) |
| ENT-009: exact profile-defined units and null tax | `Exact_profile_units_preserve_unknown_tax_and_large_values_without_USD_or_FX_conversion`; `Monetary_bounds_and_explicit_breakdown_reject_nonexact_or_inconsistent_units`; `Exact_38_digit_boundary_and_zero_net_are_valid` |
| ENT-009: no arbitrary fetch or tax override | `Controlled_references_reject_fetch_urls_paths_and_control_characters`; `Fiscal_wire_rejects_undeclared_issuance_and_tax_overrides`; `External_evidence_wire_keeps_operator_only_exact_units_and_no_issuance_or_entitlement_overrides` |
| ENT-010: no automatic entitlement; finite scope/window/overlap/amendment/revocation | `Contract_and_link_remain_evidence_only_and_reject_unbounded_or_unsigned_claim_inputs`; `Contract_report_scope_requires_each_independent_identity_region_operation_and_mode`; `Contract_links_pin_same_tenant_month_and_scope_without_qualifying_signed_or_unknown_SLI` |
| ENT-017: application and real HTTP permissions/tenant/CSRF/no-store/cursors | `Evidence_facade_rechecks_session_operator_and_MFA_for_every_call_before_store_access`; `Evidence_HTTP_rechecks_operator_MFA_CSRF_no_store_bounds_and_tenant_kind_cursor`; contract/report test also checks cross-tenant contract and statement IDs |
| ENT-017: body/time/cancel bounds and safe failures | `Evidence_body_cap_applies_to_chunked_case_variant_before_binding`; `Evidence_errors_and_deadlines_are_safe_and_restore_abort_token`; `Evidence_client_abort_propagates_without_success_or_server_timeout_body`; `Evidence_list_rejects_invalid_page_bounds_before_store_access` |
| ENT-020: immutable source/receipt and recovery, external approvals stay separate | `Evidence_audit_failure_rolls_back_profile_receipt_and_record_then_retries_without_financial_effect`; `Evidence_unknown_commit_reply_recovers_same_receipt_and_preserves_quarantined_restore_history` (actual commit interceptor, DB guards, runtime truncate denial, quarantined pg_dump/restore, artifact/profile hashes, corrupt-read denial and populated Down refusal) |
| Stable temporal provenance | `UTC_input_rejects_offset_and_submicrosecond_loss_instead_of_changing_signed_terms`; real HTTP pagination uses equal seven-digit timestamps and verifies microsecond normalization/UUID continuation |

Final verified evidence — 2026-10-20, Windows/.NET SDK 10.0.401, Node 24.19.0,
Docker 29.7.2, disposable PostgreSQL 17/Redis 8. No shared database migrated.

`dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx
--results-directory artifacts/test-results/invoice-001-final-serial` exits 0:
**1386 passed, zero failed/skipped**, seventeen Completed TRX reports. All 68 new
cases pass: 48 policy, 12 facade/bounds, 1 wire, 7 PG/HTTP. Counts include PG 294
(23m35s), Gateway 427, Billing 136, architecture 15 and real AppHost smoke 2.
Every new task case was independently matched in the final TRX with Passed
outcome. No superseded/aborted run contributes to that final total.

Completed supporting checks on the same source:

- `dotnet restore UZLLM.slnx` and full non-incremental Release build: PASS,
  zero errors; existing ASPIRE010 warning only.
- Focused `ReportingEvidencePolicyTests`: 48 PASS, zero failed/skipped.
- `npm run api:generate --prefix frontend/apps` and serial `api:check`: PASS.
- `npm ci --prefix frontend/apps`, `api:test` (18 PASS), `api:types`, and both
  workspace builds: PASS, lockfile unchanged; existing dashboard chunk warning.
- Model drift command with design-only dummy port-1 connection: PASS, no drift;
  no shared database migrated.
- Solution `--list-tests --no-build -m:1`: all 68 task cases discovered
  (48 policy, 12 facade/bounds, 1 wire, 7 PostgreSQL/HTTP).
- Static comparison: all old 110 Management paths/schemas and all Gateway
  artifacts unchanged; five additive Management paths/operations.
- Documentation links/fences and working/staged `git diff --check`: PASS;
  task-only candidate scope reviewed, no configuration secrets or lockfile edit.

Intermediate failures are not final evidence. Initial wrong identity method and
fixture operation property were corrected. Disposable migration-reset truncation
was extended for the new FKs; the CSRF fixture now supplies each route's valid DTO
without relaxing denial assertions. Two deployment-binding failures exposed
required nullable constructor arguments silently dropping a profile; explicit
optional defaults plus strict binding and default/invalid-profile regressions
now pass. The first full run was superseded/interrupted, never claimed clean.

A subsequent concurrent full run hit the **unchanged** usage CSV cancellation
test's five-second wait timeout. It passes alone (1/1); the final full run uses
`-m:1` to sequence project runners. It does not serialize the concurrency inside
reservation/replay/race tests or skip cases. The intermediate API-check child
fixture process also exited without diagnostic output; a fresh serial API check
passes. No exporter/timeout/assertion was modified to hide either failure.

Aspire smoke starts `npm ci` in the same frontend workspace. Interrupted or
concurrent install/build left packages missing in one intermediate frontend run;
a fresh locked install and separate build pass. No UI/source/package version or
lockfile change. Run frontend builds after the smoke install, not concurrently.

`npm audit --json --prefix frontend/apps` reports exit 1: one existing high
transitive `source-map-js` advisory
[GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q).
No audit PASS or remediation is claimed. Python is unavailable for static
source/test pairing; no coverage/mutation percentage is claimed. OPS-006,
HA/PITR, SLI monthly capacity and live fiscal/provider/merchant/legal/commercial
approvals remain independent; no paid traffic, deployment or SLA activation.
