# Invoice, Account Statement and SLA Reporting Specification

## 1. Status, authority and first profile

**ENTERPRISE-002 · Approved specification · 2026-10-19 · Specification only.**
REFUND-002 is completed. This document implements no report, invoice issuer,
contract, tax calculation, SLA entitlement, credit, endpoint, migration or UI.
The user's Continue approves draft `a21a231` and its bounded profile/decomposition.
Feature execution still requires separate approval/promotion. Neither an
approved specification nor green CI permits paid launch.

FR-124–126 require audited corrections, payment reconciliation and incident
controls, **not** invoicing rules or a compensation tariff. Product Scope §2.4,
Functional Requirements §14 and Roadmap Phase 8 name invoices/contractual SLA
as P2. NFR-001/010/013/030–035/042–045/050–051/070–073/084 and ADR-005/006/
011/017/019–025 govern precision, evidence, retention and authorization.

Approved first-profile design: **monthly prepaid account statements and operational
SLI reports**, with immutable source membership, reproducible calculations,
explicit completeness and append-only revisions. Statements are not tax
invoices, payment receipts, revenue recognition, postpaid accounts receivable,
debt collection, proof of provider invoice accuracy or demands for payment.
SLIs measure service; the 99.9% monthly Gateway target is not a contractual SLA.

Future fiscal-invoice evidence and contract/claim linkage are separately gated.
No new subscription, credit facility, cash refund, discretionary debit, automatic
service credit, PDF renderer, ERP integration or tax authority connector in the
first profile. Existing settlement/refund/payment and maker/checker rules stand.

## 2. Verified baseline and missing evidence

| Source inspected | Actual behavior / implication |
|---|---|
| `src/Modules/Billing/Contracts/BillingContracts.cs`, `FinancialContracts.cs`; financial store | Immutable signed ledger postings, balances/holds/debt, settlements and price/fee references exist. There is no invoice aggregate, taxable amount, revenue-recognition rule or contractual credit calculation. |
| `src/Modules/Billing/Infrastructure/PostgreSqlSettlementRefundStore.cs`; wallet adjustment store/admin service | Refunds counter-post a specific settlement; positive discretionary credits require independent approval and preserve captured budgets/active holds. They are different corrections, neither is a fiscal credit note or cash payout. |
| `src/Modules/Payments/Contracts/PaymentContracts.cs`, `CustomerBillingContracts.cs`; PaymentService | Quotes/intents preserve tiyin, fees, credit and FX; callbacks produce linked credits/reversals. Merchant observations are transcribed evidence, not independently verified invoices. List DTOs do not establish fiscal compliance. |
| `src/Modules/Usage/Infrastructure/PostgreSqlUsageReadStore.Analytics.cs`, usage rollup store/contracts | Analytics groups request-start hours, rebuilds changed buckets and exposes freshness/pending. A late refund can change historical request-period totals. These useful mutable read models cannot be frozen financial statements or complete outage denominators. |
| `src/BuildingBlocks/UZLLM.Observability/ObservabilityServiceCollectionExtensions.cs` | OTLP metrics/traces and bounded outcome/provider labels exist. Generic success/error/canceled counters do not persist complete tenant SLA evidence or a proven Gateway-vs-provider attribution. Sampled/missing telemetry is not zero failure. |
| `src/Modules/Usage/Contracts/UsageContracts.cs`; Gateway; `deploy/nginx/edge.conf` | Persisted logical requests/attempts have separate execution/delivery/financial states. Failures before admission or before reaching the app may have no request row; a stream can return HTTP 200 then fail. Edge health alone is not inference availability. |
| Billing/Payments `.csproj`, Organizations authorization contracts | Payments already depends on Billing; a reverse Billing->Payments reference would cycle. Current customer roles include ReadBilling; operator/recent-MFA/CSRF is independent. Report authority must not come from Gateway or automation keys. |
| Refund/adjustment, usage rollup/read, observability and admin tests | Existing 1028-test baseline covers current financial/tenant/telemetry behavior, not immutable monthly reports, fiscal issuance, SLA attribution or compensation eligibility. Proposed scenarios below are not those executed fixtures. |

## 3. Document kinds and approval boundary

| Kind | Authority / allowed first-profile behavior |
|---|---|
| `AccountStatement` | Internal non-fiscal snapshot of one organization's wallet postings and separately described usage/payment context. Clearly labeled **Not a tax invoice / No payment due from this statement**. |
| `OperationalSliReport` | Versioned engineering evidence with scope, numerator/denominator, exclusions, unknowns and coverage. Operator-only first; not automatically a customer SLA certificate. |
| `ExternalFiscalEvidence` | Later immutable reference to a document issued through an approved external fiscal process, with issuer/number/date/currency/amount/hash/provenance and supersession evidence. A digest is integrity metadata, not proof of issuer authenticity or legal acceptance. |
| `ContractSlaReport` | Later report under a signed, versioned customer agreement and measurement policy. It may reference an operational report but cannot retroactively redefine its denominator or hide missing data. |
| `SlaClaim` | Later evidence-backed review, not a ledger posting. Accepted claim and approved amount do not bypass REFUND-002's independent maker/checker. |

Approval path: product reviews this bounded specification; finance approves
statement vocabulary, posting basis, rounding and reconciliation; legal/fiscal
owners approve seller/customer entity details, jurisdiction, tax treatment,
fiscal document format/numbering/signatures/corrections/archive and authority
integration; operations/security approve measurement, evidence access and
retention; commercial/legal sign each SLA, remedy basis/caps and dispute rules.
Record names, dated immutable references, effective dates, policy versions and
revocation/supersession. These roles are **review responsibilities**, not new
customer roles or self-granted operator permissions.

No jurisdiction, tax rate, VAT exemption, invoice numbering or cash-credit
conversion is inferred from UZS payment, organization name, a tax-ID-shaped
string or this document. Missing approved fiscal policy blocks fiscal document
registration/issuance, not synthetic fixtures or non-fiscal design work. Missing
signed SLA/measurement/credit terms permits operational reporting only, never
invented entitlement. Product approval does not substitute for these approvals.

## 4. Financial source-of-truth and correction matrix

| Fact | Authoritative source / statement treatment |
|---|---|
| Posted wallet movement | `billing.ledger_entry` signed amount, organization, type, reference identity and occurredAt; include each ID once. No deduction from charts, callbacks or current price. |
| Charge and financial outcome | Final settlement, frozen admission/evidence and referenced prices/fee versions. Use actual wallet `Charged`, not uncapped charge, estimated hold, provider cost or external BYOK spend. |
| Refund | Settlement refund plus linked `Refund` posting in its posting period; original service/request date is context, never a rewritten charge/budget. |
| Discretionary/SLA credit | Approved wallet adjustment and linked `AdjustmentCredit`; pending request is not money. Do not net it against historical inference charges or relabel as a settlement refund. |
| Top-up / FX / payment fee | Intent/quote frozen principal and fee in tiyin, credit in micro-USD and FX snapshot/rate. Reconcile to its one TopUp posting; provider reconciliation cases remain separately disclosed. No current FX revaluation. |
| Payment reversal / debt | Actual `payment_reversal` and `debt_recovery` postings plus reversal/debt evidence. Full external reversal amount, recovered credit and remaining debt are distinct; do not subtract the entire reversal from posted balance twice. |
| Holds | Snapshot reservation/financial-wallet state, separate from posted movement. Holds are not consumption, tax, sales or a second debit. |
| Provider cost / exposure | Internal estimate/evidence and immutable late-exposure/external-spend adjustments. Operator-only annex, not customer revenue or independently verified provider bill. |
| Tokens / latency / request outcomes | Optional labeled usage annex with operation/model/request scope and completeness. Never make a missing token count zero or use this annex as the financial total. |

Billing reporting owns reconciliation and statement rules. Reuse Billing/Usage
contracts; define a minimal Billing-owned payment-evidence read port implemented
by Payments using its existing stores, under the caller's shared context/snapshot.
No Billing->Payments project reference, direct controller SQL, provider SDK types,
new database or broad architecture exception. Customer authorization belongs in
the application facade, as well as endpoints; no billing rules in renderers.

### Period, cut-off and snapshot

First financial period is a UTC calendar month `[from,to)`, one organization,
all projects. Display timezone/localization cannot change its boundaries.
Statement lines use ledger **posting occurredAt**, not request startedAt or
payment-provider observation time. Preserve both when relevant; a request
started in September and settled in October is October posted consumption.
Its refund posted in November remains November credit even if analytics updates
September. There is no inferred tax/service-supply date.

Worker builds a bounded Repeatable Read snapshot on the primary PostgreSQL
database. Freeze exact ledger/source row IDs, signed units, source values,
linked terms, `snapshotCapturedAt`, period, calculation/schema version and
source digest. UUIDv7, timestamp or maximum ID is **not** a commit watermark:
concurrent older transactions may commit later. Do not select a new source set
while exporting another page or call existing analytics methods that start an
independent/nested transaction.

Opening balance is the sum of visible ledger postings before from; movement
is the visible period sum; closing = opening + movement. For historical months,
closing is not today's wallet: reconcile `closing + visible post-period
movement = posted wallet at the same snapshot`. Also reconcile available =
posted - active holds at snapshot and debt from its authoritative state.
No wallet lock/provider I/O is needed for a report; read-only work must not
hold admission locks or add synchronous hot-path writes. Report publication
cannot post any money or mark reconciliation cases resolved.

`Complete` means the stated snapshot's financial source coverage reconciles,
not that all providers/merchants or tax documents are final. Pending evidence,
pending settlement, unmatched references and merchant mismatches appear as
separate counts/status. A financial imbalance or missing mandatory source link
blocks publication; pending undecided inference is disclosed, not fabricated
into a charge or erased to force a final-looking report.

### Immutable publication and revisions

Build request -> Pending -> bounded Worker build -> immutable Published snapshot,
or visible Failed attempt with reason. Claim jobs using existing lease/retry
infrastructure; report head/generation/operation uniqueness selects one winner.
Publish manifest, lines, source digest, audit and outbox atomically. First profile
stores bounded JSON/CSV data in PostgreSQL, not a new document storage service;
no PDF or remote render inside a transaction. Notify after commit only.

Use versioned safe commands and UUID Idempotency-Key, scoped by organization,
kind, operation and key. A repeated identical normalized command returns the
same job/report; changed period/revision/reason conflicts. Lock report head,
then allocate revision and receipt; don't lock wallet. Lost commit response
recovers by the original operation identity. Generation/source mismatch on
publish fails/retries, audit/outbox failure rolls publication back. An issued
snapshot is never UPDATE/DELETE-corrected or silently rebuilt by a usage job.

Late settlement in the next month is a new posting there, not an earlier-period
rewrite. A newly observed old-period/backdated posting or wrong report mapping
requires a new explicit revision with reason, new source membership and a link
to its predecessor/delta. Published prior bytes and accounting remain intact;
show supersession rather than deleting a number. Separate snapshot cut-offs may
make neighboring published months differ: expose the bridge/revision, never
silently recompute an old opening balance. Financial errors still require
authorized compensating ledger operations before a corrected statement can
reflect them; report revision itself never invents a refund or debit.

### Exact synthetic financial fixture

All USD amounts below are micro-USD; UZS data is a separate annotation.
Opening posted = 1,000,000; TopUp = +8,000,000; UsageCharge = -3,000,000.
An active hold of 1,500,000 leaves available 4,500,000. Reversing the whole
8,000,000 top-up recovers 4,500,000 by ledger debit, leaves posted/held
1,500,000 and creates debt 3,500,000. A settlement refund +500,000 and approved
discretionary credit +200,000 trigger debt recovery -700,000 in total.

Final posted = `1,000,000 + 8,000,000 - 3,000,000 - 4,500,000 + 500,000 +
200,000 - 700,000 = 1,500,000`; held = 1,500,000; available = 0; debt =
2,800,000. Gross usage = 3,000,000, refunded usage = 500,000, net usage =
2,500,000; discretionary credit and debt recovery do not change those totals
or restore captured budgets. Example top-up principal 10,000,000 tiyin
(100,000 UZS), zero fee and frozen 1,250,000 tiyin/USD produces 8,000,000
micro-USD. Do not add UZS principal to USD movement or treat top-up as a second
usage sale. These are synthetic arithmetic fixtures, not approved commercial,
tax, fee or exchange-rate policy.

## 5. Fiscal invoice and service-credit boundaries

A separately approved follow-on may record **external fiscal evidence metadata**,
not manufacture an invoice from a monthly total. Require approved policy reference,
issuer/customer legal identity snapshots, external document identity/date/kind,
currency and exact monetary/tax breakdown defined by that approved profile,
issuer-verification provenance, content digest and controlled archive reference.
Use plain bounded reference IDs, not arbitrary fetch URLs/uploads or a new
SSRF/file-execution surface. No automatic document download or legal verification
from a checksum. Unknown tax remains unknown, not numeric zero/exempt.

An external number is unique within its approved issuer/jurisdiction/series
identity. Replay must match immutable terms; correction/replacement/cancellation
is a new linked evidence record under the approved external fiscal process,
not a local overwrite, recycled number, ledger mutation or automatic wallet
refund. A mismatch opens review and blocks publication of the affected fiscal
link. External registration timeout remains Unknown and reconciles by external
operation identity before retry; never assume a failed HTTP response means no
document was issued. Selecting an actual authority/ERP protocol and implementing
issuance require a later separately approved integration task, not this plan.

For a future SLA claim, freeze organization, signed contract/policy version,
period/scope, eligible charge references/basis, report version, incident/evidence,
claimed/approved currency/amount, actor/reason and disposition. No default
remedy percentage, claim window, cap, support response time or payment deadline
is invented. Until signed terms define them, status is EvidenceOnly/ReviewRequired,
not EligibleForCredit. No retroactive contract changes or exclusion edits.
First claim scope is one monthly remedy per stable contract/service scope:
unique `(organization, contractId, UTC month, serviceScope, remedyKind)`, with
policy version frozen in its terms, not used to bypass uniqueness. New UUIDs
or amended policy versions cannot create the same monthly claim twice. Reject
overlapping active contract scopes/effective windows; aggregate incidents as
claim evidence rather than one automatic credit per incident.
Scope IDs resolve to a finite registered operation/mode/deployment-region
definition, not customer SQL, wildcard predicates or caller-reported region.

Approved positive USD-credit remedy can link to **one** existing REFUND-002
request using a stable claim identity and exact frozen terms. A unique
`(organization_id, claim_id)` credit link and unique adjustment-request link
prevent two jobs/claims from minting the same remedy twice. Same identity with
changed amount/contract conflicts. Acceptance of a claim posts no money; the
pending adjustment request, immutable claim link, audit and command receipt
must commit together under the shared coordinator, using stable
`sla:<claim-uuid>` request identity and existing wallet-first lock order.
Reuse the Billing store contract inside that transaction, not the Admin service
that opens its own transaction. Crash/response loss retries the same identity;
never leave a newly generated financial request unlinked to its claim.
A later, separate existing approval transaction posts credit. The
different recently MFA-verified checker still approves every amount, using
the existing atomic credit/debt/audit transaction. No worker self-approval,
debt bypass, hold release or budget reset. Reversal or dispute after credit
requires a separately authorized compensating policy, never automatic debit.
Charge-specific refund remains REFUND-001; no double recovery under two labels.
Claim review records existing refund/credit allocations against the signed
eligible-charge basis; later overlapping corrections open review, not automatic
debits or a second remedy. The new claim path deduplicates its business identity;
general discretionary credits still require finance's evidence review and
maker/checker. This is not a claim that today's arbitrary manual credit/refund
paths automatically deduplicate semantic business causes across all workflows.

## 6. Operational SLI and contractual SLA design

NFR-001's 99.9% is an internal monthly target for Gateway/control handling,
excluding proven upstream outages. Management availability, provider attempts
and end-to-end model delivery are separately measured. No successful build,
healthcheck, HTTP-200 header, reservation, token count or low-traffic month
establishes contractual availability.

### Event scope and classification

First operational report covers logical inference HTTP executions at the TLS
edge for chat, Responses and embeddings, with operation/mode/region/time and
deployed calculation version. One edge execution identity counts once; repeated
collector delivery deduplicates. A deliberate client retry is a separate HTTP
execution, while fallback attempts inside one logical request are not new
Gateway denominator events. Duplicate-suppression 409 is a policy rejection,
not another paid request. Provider-attempt reliability has its own denominator.

| Class | Evidence / treatment |
|---|---|
| H: handled successfully | Complete protocol-valid JSON or terminal SSE success. HTTP 200 followed by partial/error/missing terminal output is not H. |
| G: Gateway-attributed bad | Proven edge/app/DB/Redis/credential-deployment/capacity failure or Gateway forwarding/normalization defect. Operator emergency shutdown and capacity 429 are not silently excluded as customer policy. |
| P: provider-attributed bad | Verified upstream overload/outage/timeout evidence with no Gateway defect. Exclude from Gateway-only denominator, retain in end-to-end result. BYOK credential rejection must remain distinguishable from provider-global outage. |
| C: client/policy excluded | Proven malformed/unauthorized/unsupported request, customer quota/budget denial, duplicate conflict or client cancellation before a service failure. Platform auth/dependency failure is not a customer 401. |
| U: unresolved attribution | Transport ambiguity, missing correlation, incomplete stream or conflict that cannot be proven P/C. Remains explicit; never silently excluded or called success. |

Gateway-caused failure takes precedence over a provider error when both occur.
Use authenticated protocol/error/evidence and timestamps, not just status codes
or the current generic `outcome` metric. For client disconnect, record delivery
state and any preceding service failure; financial Unknown and SLI origin are
separate dimensions. A provider timeout may be P only with sufficient origin
evidence; it still must not imply zero provider cost or authorize fallback.

Record exact counts `H,G,P,C,U`. Gateway known-outcome ratio is H/(H+G), labeled
known-only; conservative bound is H/(H+G+U). Any U or missing observation
interval makes target qualification **Incomplete**, even if the known ratio is
high. End-to-end uses H/(H+G+P+U) for this successful-delivery definition;
provider attempts are reported separately. Empty denominator is NoData/null,
never 100%. Freeze UTC calendar-month bounds; don't mix time-based minutes
and request-based percentages or convert error budgets without a new policy.

Synthetic example: H=840, G=10, P=50, C=100, U=0 gives 1000 observed executions,
Gateway ratio 840/850 and end-to-end 840/900. Add U=5: conservative Gateway
bound is 840/855, and qualification is Incomplete, not a justified provider
exclusion. Display rounding never changes target comparison; use integer
counts/exact fixed precision. With complete coverage, compare bad budget to
the versioned 99.9% threshold without rounding a near miss into a pass.

### Coverage and evidence ingestion

Current Usage begins too late for a full denominator; existing OTLP counters
can restart/drop and traces can be sampled. Add bounded **unsampled metadata**
edge/Gateway execution observations and source manifests through the operational
collector/Worker, not synchronous per-request PostgreSQL writes. Edge assigns a
trusted execution ID, Gateway correlates it with internal request/attempt IDs;
caller-supplied correlation is not a dedup key. Use a dedicated trusted
`X-UZLLM-Edge-Execution-Id` UUID: strip any public
value and regenerate at the edge, validate only across the configured internal
TLS/trusted-proxy boundary. Preserve existing X-Request-Id/trace semantics;
do not trust a direct public header or use it as a metric label. Capture
pre-admission errors and terminal SSE outcome without retaining bodies, headers, credentials or
prompts. Collection outage cannot block inference or waive financial admission.

Versioned source manifests record producer/sequence range, count/hash, UTC
interval, ingest/clock/terminal completeness and duplicate/conflict state.
Only trusted deployment collectors can submit; no customer event upload or
arbitrary telemetry-query language. A digest is not source authenticity.
At monthly close, exclude all unclosed/late stream observations from H, classify
them U until reconciled. Stream start anchors its request month; a terminal
event after month end can complete a later report revision, not rewrite it.

Use independent external probe/collector coverage to detect edge/DNS/TLS/node
unreachability and telemetry gaps; a local healthcheck does not supply this.
Readiness/catalog probes are explicitly readiness, not model-output success;
no automatic paid inference probes in this profile. Probe failure or an
unobserved interval marks coverage Incomplete/NoData; no-request outages do
not disappear into a perfect ratio. Export both observed ratios and coverage,
but publish no contractual SLA compliance from an incomplete interval.

Operational report policy/revisions and incident/exclusion evidence are
append-only and audited. No arbitrary operator exclusion percentage or mutable
SQL filter. Planned maintenance is not excluded by default; a future signed
contract must define any exclusion prospectively, with separate raw counts and
approved incident evidence. A contract report is blocked until exact scope,
measurement, exclusions, coverage, claim terms and independent review are signed.

## 7. Ownership, persistence and transaction boundaries

Keep modular monolith/three hosts. Billing owns statements/reconciliation and
commercial contract/claim/credit links; Payments owns frozen payment/merchant
evidence. Usage owns usage and operational SLI policy/calculation/read evidence
in its Domain/Application/Infrastructure/Contracts folders, not invoice truth
or contractual remedies. Observability owns bounded transport/instruments and
neutral collection ports, OperationalWork owns leases/outbox, not fiscal/SLA
business rules. Gateway/Worker composition connects those ports to Usage; do
not add a building-block reference back to a business module or Usage->Billing
dependency (Billing already references Usage).
Management application services compose authorization/DTOs; Worker builds
reports and delivers committed notifications. No new service or database.

Proposed future tables, **no migration in ENTERPRISE-002**:

Execution note: REPORTING-002 separately implements the statement tables,
operations/receipts and attempts described below; see document 48. Fiscal, SLI
and SLA tables remain proposals. ENTERPRISE-002's specification-only boundary
is preserved, not retroactively treated as runtime implementation.

- `billing.statement_head` (org/UTC period/kind, generation/latest version) and
  append-only `statement_version`, `statement_line`, `statement_source` with
  unique `(org,period,kind,revision)`, `(version,source_kind,source_id)` and line
  ordinal. Tenant-composite FKs, exact signed units, predecessor, calculation
  and source digest, audit and operation identity; no cascading financial delete.
- Versioned approved commercial/legal profile references, external fiscal
  evidence and corrections with issuer-scoped document uniqueness, tenant
  links, immutable amount/currency and approval provenance. Profiles do not
  overwrite FX, tax or signed agreement history.
- SLI source manifests/observations and immutable policy/report/incident-
  annotation versions in `ops`; unique producer/epoch/sequence and trusted edge
  execution identity, plus report/observation membership. Index UTC scope,
  source completeness, pending imports and report head, not payload contents.
- SLA claim/credit-link identities owned by Billing, linked to existing wallet
  adjustment request with same-org FK, monthly business-claim uniqueness and
  one-claim/one-request uniqueness, plus immutable reviewed benefit allocations.
  No new wallet balance or autonomous financial posting table.
- Report command receipts and existing outbox/audit references; bounded mutable
  job/head coordination is separate from append-only published evidence.

Do not assume MAX(UUID)/occurredAt/xmin is a durable cross-transaction cursor.
Every published source set is explicit. Build at Repeatable Read; transaction-
scoped report-head serialization and uniqueness protect publish/revision;
audit/outbox/receipt result share that commit. For asynchronous statements,
REPORTING-002 commits the immutable command-acceptance receipt, pending
operation, requested audit and job together first. Publication atomically
advances that operation's result/version link with artifacts, publish audit and
outbox; the original receipt resolves the same operation, never a replacement
receipt or a prematurely Published response. Financial correction uses the existing
wallet-first transaction, never inside report rendering or provider I/O.
Read ports enlist in the caller's snapshot, no nested independent commit.

Customer queries always constrain organization before loading source/manifest/
artifact IDs; composite foreign keys prevent cross-tenant invoice/claim links.
Operator-only provenance and SLI global/raw counts never leak through customer
serialization. Customer DTOs allowlist safe source identity, posting category,
date and amounts; exclude raw MetadataJson, operator reasons/maker-checker IDs,
merchant statement references and internal provider-cost/exposure data. Do not
serialize domain/evidence entities wholesale. Event/manifest volume may later need monthly partitioning after
measurement; no partitioned financial ledger or separate analytics DB now.

## 8. Proposed APIs, security, retention and UX

Original proposed routes follow. The subsequent user approval separately
promotes REPORTING-003: the seven statement/customer/operator routes below
are implemented in [document 49](49_STATEMENT_API.md), with generated contracts.
SLI/fiscal/claim routes remain unimplemented proposals; no new bearer scheme.
Browser customer base `/management/v1/organizations/{orgId}/statements`:
GET list, GET `/{id}`, GET `/{id}/lines`, GET `/{id}/export?format=csv`.
Use existing ReadBilling permission (Owner/Admin/BillingViewer); project-granted
Developer/ReadOnly and Gateway keys cannot read org-wide money. Version ID is
immutable; exports must select that ID, not a moving `latest` after a click.

Operator base `/management/v1/admin/organizations/{orgId}/reports`:
POST `/statements`, POST `/statements/{id}/revisions`, GET `/jobs/{id}`;
operational SLI builds/reads under `/management/v1/admin/sli-reports` with
explicit period/operation/region scope. Current operator grant/recent MFA for
reads/mutations and CSRF for mutations, plus idempotency/expectedGeneration and
reason on builds/revisions. No public customer SLI-global counts, fiscal-issue,
claim payout or automation-bearer endpoint in the first API slice.

Metadata includes kind, org, UTC period, version/predecessor, generation,
sourceAsOf, completeness/pending counts, totals/units, digest, calculation/policy
version and non-fiscal/non-contractual label. Preserve versioned source field
names and 180-day breaking-version policy; generate examples/client/wire tests
only with actual implementation. Account statement money is integer micro-USD/
tiyin strings; any later fiscal currency/scale requires its approved profile,
never an assumed two-decimal currency or current FX conversion. Use checked
wide integer/decimal aggregates; reject overflow, never wrap/clamp totals.
Ratios retain numerator/denominator, unknown is null plus reason.

Lists default 20/max 100, tenant/filter/version-bound opaque keyset cursor;
lines default 50/max 100, stable ordinal. Build one month at a time, default
maximum 100,000 source lines and 32 MiB artifact; reject an oversized report
without truncation and require a separately approved scaling plan. Worker
snapshot/build deadline 30 seconds with bounded retry/backoff; transient timeout
is a Failed attempt, not a partly Published statement. API read/export deadline
30 seconds; new report body cap 8 KiB. CSV escapes spreadsheet formulas and
labels fixed units; no template execution, PDF HTML or external logo fetch.

401 missing session, 403 role/MFA/CSRF denial, 404 missing/out-of-tenant resource,
400 invalid UTC month/fields, 409 command/source/generation conflict, 413 bound,
503 unavailable dependency, 504 deadline. Pending build returns 202 job/Location;
repeated command returns the same identity/state. No partial artifact on failure;
reads/export no-store, safe errors/correlation, cancellation throughout I/O.
This means no partially **published** report, not atomic network delivery:
a disconnect can truncate a download. Return Content-Length and a fixed
`X-UZLLM-Report-Sha256` for the selected version/format bytes; clients verify
before accepting the file. Export audit records access/request, not proof the
recipient received every byte. Retrying reads the same immutable version.
Audit create/publish/revision/approval/export access using real actors; never
log invoice legal identifiers, address, raw fiscal content or payment secrets.
Keep metric labels bounded by operation/kind/outcome, never tenant/document ID.

Published financial statements/fiscal/claim evidence remain under existing
financial/audit legal hold until signed archive policy. Raw operational SLI
observations use the approved diagnostic/operational schedule, with report
manifests/classified aggregate counts preserving ratio reproduction and no
deleted payload samples. Do not promise fresh attribution/reclassification
after raw evidence expires. Published membership references immutable manifest
identity, not a cascading FK to expiring raw observations; expiration must not
mutate old report bytes or silently break its source digest.
Missing approved observation retention sufficient for a contract period blocks
contract qualification. Restore quarantines reports until source/ledger/digest/
correction links and sequence coverage reconcile; do not regenerate legal
numbers or replay adjustment approvals/notifications as new operations.

Future customer billing statement UX clearly separates posted, held, available,
debt, actual usage, refunds and credits, shows cut-off/pending/revision labels,
and never calls a download an invoice or SLA guarantee. Admin shows provenance,
coverage/Unknown, approval refs and claim-to-adjustment state, not editable
ledger fields. Backend data precedes UI; follow/improve the existing mockup
style, Uzbek/Russian/English and accessible empty/error/tenant-switch states.
No new financial/fiscal personally identifying fields are collected in this task.

## 9. Designed acceptance scenarios — not executed feature tests

| Case | Required evidence / fixture |
|---|---|
| ENT-001 | Statement label/kind never implies tax invoice, revenue, payment due or contractual SLA; unconfigured fiscal/contract profile cannot issue/register/qualify. Pure contract/profile tests. |
| ENT-002 | Exact USD/tiyin/FX, charge-vs-cost, BYOK fee/external spend and no double counting reconcile to source IDs. Unit/PG fixtures including §4 arithmetic. |
| ENT-003 | Statement uses posting month; request-period analytics differs intentionally for late settlement/refund. UTC month/year/leap-day/offset tests, no FX revaluation. |
| ENT-004 | Repeatable Read freezes explicit membership; concurrent delayed commit/UUID ordering cannot mix pages/snapshot. PG barriers, historical balance bridge and wallet reconciliation. |
| ENT-005 | Parallel identical build/publish/revision produces one report/receipt/audit/outbox; changed key terms or generation conflicts. PG replay/race/fault tests. |
| ENT-006 | Published data is immutable; late old-period rows/corrected mappings produce explicit predecessor/delta revision with old bytes intact. PG/migration/restore tests. |
| ENT-007 | Pending evidence/settlement/merchant mismatch remains disclosed; imbalance or mandatory link failure blocks publication, never posts/releases money. PG and dispatcher/ledger assertions. |
| ENT-008 | Refund/adjustment/reversal/debt/active holds match §4, no budget restoration or full-reversal double debit; reporting is read-only to finances. PG financial regression. |
| ENT-009 | Issuer/series/document identity deduplicates external evidence; changed terms/correction/cancel is new linked evidence, hash not authenticity; Unknown external outcome reconciles before retry. Protocol-free synthetic metadata tests. |
| ENT-010 | No signed SLA terms means EvidenceOnly; compensation has no invented cap/rate/currency; report approval never grants operator/maker/checker rights. Policy/HTTP tests. |
| ENT-011 | One monthly business claim links one same-org adjustment; new UUID/policy-version duplicate, concurrency/replay/changed terms, overlapping benefit review, self-approval, stale MFA, debt/holds and lost commit preserve existing two-person posting. PG/HTTP tests. |
| ENT-012 | H/G/P/C/U classification handles DB/Redis/edge failure, capacity-vs-customer 429, BYOK auth, cancellation and origin conflicts without declaring financial cost zero. Unit/edge/provider fixtures. |
| ENT-013 | SSE HTTP 200 then failure/missing terminal/late terminal is never success; fallback attempts do not multiply logical denominator. SSE/native/collector tests. |
| ENT-014 | 840/850, 840/900 and Unknown bound 840/855, zero denominator and exact target boundary pass; missing coverage prevents a compliance pass. Pure calculator tests. |
| ENT-015 | Trusted producer identity/sequence/clock manifests deduplicate deliveries and detect gaps/conflicts/restarts; forged customer events cannot qualify service. Collector/PG/edge fixtures. |
| ENT-016 | Edge/DNS/TLS outage before app/usage, zero-traffic interval, missing telemetry and probe disagreement yield Incomplete/NoData, never 100%; no paid probes or sink backpressure in inference. Fault/load tests. |
| ENT-017 | Tenant/project/role denial across list/detail/source/cursor/export/claim IDs; operator raw data stays hidden, all responses no-store, CSV safe. HTTP/PG/generated-wire tests. |
| ENT-018 | Size/time/cancellation/audit/outbox/DB failure yields no partial Published artifact or financial effect; committed response loss recovers same operation. PG/Worker fault tests. |
| ENT-019 | Localized customer/admin UI distinguishes snapshots, pending/revision, invoice/SLA limits and immutable credits against real API; mockup-aligned accessible screenshots contain no secrets/legal personal data. Browser tests. |
| ENT-020 | Retention, quarantined restore, source digest/sequence audit, notification replay, distinct-node operational evidence and signed external approvals remain separate gates. Restore/runbook/security review. |

## 10. Approved follow-on design — unscheduled, execution approval required

**2026-10-19 execution update:** separate user approvals promote REPORTING-001
and then REPORTING-002 into document 16's executable register. Follow
[source reconciliation](47_STATEMENT_SOURCE_RECONCILIATION.md) and
[internal publication/revision scope](48_STATEMENT_PUBLICATION.md).
The subsequent Continue also promotes REPORTING-003's bounded statement API
in [document 49](49_STATEMENT_API.md). All other rows below stay unpromoted
design proposals; no fiscal/SLA feature or launch-gate pass is inferred. The original table and
dated specification validation below remain historical design evidence.
SECURITY-009's earlier dated retention/audit finding was separately closed by
[the correction in document 46](46_RETENTION_AUDIT_ATOMICITY.md); it is not a
current reporting defect or authorization for guardrail implementation.

Originally all nine IDs were P2 design proposals, not executable register additions.
Only REPORTING-001/002/003 are now promoted as recorded above. Each remaining ID
needs separate approval/promotion; table order is recommended. Completion needs
listed fixtures, generated contracts/docs as relevant, Release baseline and a
focused commit. No task may mark a missing fiscal/SLA external approval passed.

| Proposed task | Objective / likely files | Dependencies / references | Acceptance, tests and completion |
|---|---|---|---|
| REPORTING-001 | Pure statement reconciliation/period/source contracts and safe payment evidence port; Billing/Payments/Usage contracts. | Approved ENTERPRISE-002, REFUND-002, BILLING-003; §3/4, NFR-030–035, ADR-005/006/019/024. | ENT-001–004/007/008 unit/PG projection tests; exact fixture, posting-vs-request month, no cyclic reference/financial mutation. No publication yet. |
| REPORTING-002 | Versioned statement/head/source/receipt schema and bounded Worker snapshot/publish/revision; Billing/Persistence/Migrator/Worker/Audit. | REPORTING-001, OPS-003; §4/7, ADR-011/017/024. | ENT-004–008/018/020 PG migration/rollback/race/restore tests; one atomic publish, immutable old version, source membership and balance bridge. No customer endpoints yet. |
| REPORTING-003 | Tenant-authorized statement metadata/lines/CSV and operator job/revision API; Management/Billing/generated client. | REPORTING-002, DX-002, USAGE-005; §8, FR-123, NFR-044/084. | ENT-001/003/005/017/018 HTTP/PG/schema/CSV tests; existing ReadBilling/operator/CSRF/no-store, explicit version exports/bounds. No fiscal label or bearer scope. |
| SLO-001 | Unsampled safe edge/Gateway observations and trusted collector manifest import; Gateway/Observability/Usage/OperationalWork/Worker/deploy. | Approved ENTERPRISE-002, OPS-003, OPS-005; §6/7, NFR-001/013/070–073. | ENT-012/013/015/016/018 unit/edge/PG/collector tests; trusted identity/epoch/sequence, pre-admission errors, terminal streams, loss disclosure and no content/hot-path blocking. No contractual report. |
| SLO-002 | Versioned monthly SLI classification/calculation/report and operator read API; Usage Domain/Application/Infrastructure/Contracts, Worker/Management. | SLO-001; §6/8, NFR-001/084. | ENT-012–018 PG/wire/calculator fixtures prove exact ratios, provider separation, Unknown/NoData/coverage, immutable revisions and operator scope. No automatic maintenance exclusion or customer SLA certificate. |
| INVOICE-001 | Approved-profile/external fiscal reference and contract/report linkage only; Billing/Management/Audit/Persistence. | REPORTING-003, SLO-002; §3/5/7, FR-125, ADR-006/019/025; actual finance/legal profile required for live registration. | ENT-001/009/010/017/020 synthetic profile/tenant/replay/correction tests; issuer uniqueness and approvals, no arbitrary URL fetch, local issuance or tax assumptions. Missing live policy blocks activation, not synthetic fixtures. |
| BILLING-004 | Immutable SLA claim review/provenance and unique existing adjustment link, not new credit engine; Billing/Management/Admin/Audit. | INVOICE-001, REFUND-002; §5, FR-080/124, ADR-006/021/024/025. | ENT-008/010/011/017/018 PG/HTTP tests; no automatic entitlement, duplicate recovery, self-approval, debit or budget reset. Actual claim eligibility needs signed contract terms. |
| FRONTEND-017 | Customer statement/version/export and operator SLI/evidence/claim state UX; dashboard/admin/shared client. | REPORTING-003, SLO-002, INVOICE-001, BILLING-004, FRONTEND-010; §8, ADR-016. | ENT-001/017/019 browser/real API tests/screenshots; accurate labels and unknowns, backend-first permissions, improved mockup/localization/accessibility. No fiscal issuer or editable ledger UI. |
| OPS-013 | Reporting retention/restore/sequence/rollout runbooks, alerts and optional enterprise qualification; deploy/Observability/tests/Load. | FRONTEND-017, OPS-006; §3/6–8, NFR-060–073. | ENT-016/018/020 fault/load/restore/content-sink and separate-host evidence; named finance/legal/incident approvals, blocked on absent qualification inputs. No MVP gate passes from local fixtures. |

Financial statement and SLI calculator work can proceed independently once
their input contracts exist, but the table defines one safe sequential order.
Fiscal/SLA commercial decisions are external activation prerequisites, not
software tasks. SAML/SCIM, automation, guardrails, custom suppliers and advanced
cache are not prerequisites for this prepaid profile. Do not turn the last P2
register row into automatic authorization to execute these proposed tasks.

## 11. External prerequisites and validation

Before offering fiscal documents: named finance/legal sign-off, seller/customer
entity and jurisdiction, approved issuing/archive system and protocol, tax and
FX recognition/rounding dates, invoice/correction numbering and signature rules,
actual authority/issuer credentials, sample accepted documents, verification and
reconciliation process, retention and dispute owner. No statutory rate or format
is asserted here; Uzbekistan-specific fiscal interpretation must be reviewed
through the appropriate official process by the business's qualified advisers.

Before selling an SLA: signed service scope/period/regions/operations, objective
and formula, exclusions/maintenance, measurement source/coverage/retention,
credit basis/rates/caps/currency/claim deadlines/support terms and independent
review/incident ownership. Missing evidence blocks contractual activation.
OPS-006 still needs distinct staging hosts; managed HA/PITR, merchant/provider
agreements, fiscal policy and legal schedules remain independent launch gates.
SECURITY-009's unrelated retention commit-before-audit risk remains unfixed.

Primary engineering references reviewed 2026-10-18:
[Google SRE service-level objectives](https://sre.google/sre-book/service-level-objectives/)
distinguishes measured indicators/objectives from agreements with consequences;
[implementing SLOs](https://sre.google/workbook/implementing-slos/) discusses
event ratios and measurement coverage. The exact classes, formula, limits,
posting/snapshot design and all legal/commercial boundaries above are repository
proposals, not a Google SLA or a tax authority-approved accounting policy.

Draft prepared 2026-10-18; final validation 2026-10-19 (Asia/Tashkent),
Windows/.NET 10 and Docker disposable PostgreSQL 17/Redis 8. Runtime/test/
dependency inputs remain at `79a0089`. The run folder uses its starting date:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0, projects up to date. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0, zero errors; existing ASPIRE010 CLI-bundle warning (2m29s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/enterprise-002-20261018` | PASS, exit 0. Sixteen Completed TRX reports: 1028 passed, zero failed/skipped/aborted; PostgreSQL 234/234 (15m43s for that assembly). |
| Static specification checks | PASS: 161 local Markdown targets/28 anchors across fourteen paths; balanced fences/tables, 20 unique designed cases, nine collision-free/unpromoted task IDs, dependency-safe order and seven completed existing prerequisites. OPS-013 retains blocked OPS-006 qualification. Statement/debt/FX/tiyin, SLI population and exact target-boundary arithmetic checked. |
| Scope/history | Fourteen documentation-only paths; other backlog/history rows and launch gates unchanged. No runtime/test/frontend/configuration/dependency/generated-contract edit. |
| `git diff --check` and staged scope/whitespace review | PASS; task-only documentation changes. |

TRX stays in ignored local artifacts, not Git. No ENT feature fixture, new API
operation, fiscal invoice, live SLA, credit posting or production qualification
is claimed. ENT-001–020 are designed acceptance scenarios, not tests executed
by the unchanged baseline.
At the draft handoff, product review of the non-fiscal prepaid statement/
operational SLI first profile, immutable posting/snapshot/revision and
classification/coverage rules, external approval path, credit boundary and
nine-task decomposition was pending. Approval-only closeout follows; feature
execution and task promotion remain separate.

### Approval-only closeout — 2026-10-19

The user's Continue directly answers the review request for `a21a231`. It
approves the non-fiscal prepaid statement/operational SLI design, immutable
posting-period snapshots and revisions, exact source reconciliation,
classification/coverage/Unknown rules, external fiscal/SLA approval path,
existing independent maker/checker credit boundary, twenty designed cases and
nine-task decomposition. This satisfies ENTERPRISE-002's specification review,
not implemented reporting, fiscal issuance, contractual entitlement, task
promotion, finance/legal approval or production activation. All nine follow-ons
remain unscheduled and require separate execution approval/promotion.

The prior sixteen TRX reports were re-read: **1028 passed, zero failed/skipped/
aborted**, including PostgreSQL 234. Comparison to tested `79a0089` confirms
unchanged runtime, test, configuration and dependency inputs. No new restore/
build/test run or ENT feature acceptance is claimed for this approval-only
closeout. Fresh static checks verify 161 local Markdown targets/28 linked anchors,
balanced fences/tables, twenty unchanged designed cases, nine unchanged
dependency-ordered follow-on rows and seven completed existing prerequisites.
Statement/debt/FX/tiyin and SLI population/target-boundary arithmetic is
rechecked. Only fourteen documentation paths change; other backlog/history
rows and launch gates remain unchanged. Whitespace and staged scope checks pass.
OPS-006 still needs unavailable distinct staging hosts; OPS-007 and LAUNCH-003
remain dependency-blocked. SECURITY-009's separate retention/audit risk remains
unfixed and unpromoted. No ready executable register task remains after this
closeout; stop before promoting or implementing any follow-on without separate
user approval.
