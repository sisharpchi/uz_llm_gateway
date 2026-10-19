# REPORTING-001 — Statement source reconciliation

## Scope and authority

The user's 2026-10-19 approval promotes only REPORTING-001 from
[the approved design](45_INVOICE_SLA_REPORTING_SPEC.md#10-approved-follow-on-design--unscheduled-execution-approval-required).
ENTERPRISE-002, REFUND-002 and BILLING-003 are completed dependencies.
OPS-006 remains Blocked; this independent increment does not qualify paid launch.

This is an internal read-only foundation, **not a published statement, tax
invoice, payment demand, receipt, SLA report or credit entitlement**. No migration,
API, UI, job, commercial policy, fee or financial mutation is introduced.
REPORTING-002 must be separately approved before persistence/publication work.

## Components and invariants

- Billing owns `StatementPeriod`, source contracts and `StatementReconciler`.
  Periods are UTC calendar months `[from,to)`, all projects of one organization.
  Signed micro-USD ledger postings determine opening/movement/closing; a
  historical closing plus visible post-period movement must equal snapshot posted.
- Settlement links use actual `Charged`, never uncapped price/provider cost or
  external BYOK spend. Refunds remain in their posting month; a later-month net
  usage total may be negative. Adjustments, holds and debt are not usage.
- Explicit ledger, financial-link, hold, reversal, debt and usage-evidence IDs,
  nullable tokens, price/fee references and frozen payment terms enter the
  versioned SHA-256 source digest. The digest is integrity metadata, not fiscal
  authenticity or a durable commit watermark.
- Payments implements Billing's `IStatementPaymentEvidenceReader`. It projects
  only selected same-tenant intent/quote/FX fields and a tenant-linked open-case
  count plus paid intents lacking their exact top-up posting. No raw metadata,
  merchant statement reference, secret or operator reason.
  Unknown-tenant merchant observations cannot be attributed to a customer and
  remain in existing operator reconciliation, not leaked through this count.
- Both PostgreSQL readers require the **caller's same scoped Repeatable Read
  transaction**. They neither begin/commit a nested transaction, call analytics,
  lock wallets, nor save changes. Capture time is PostgreSQL transaction time,
  not evidence that every earlier timestamp/UUID has committed.
- Each materialized source query is bounded at 100,000; excess is rejected,
  never silently paginated/truncated. This conservative initial projection may
  reject a tenant with excessive lifetime history. REPORTING-002 must preserve
  exact membership while enforcing the design's overall source/artifact/deadline
  bounds; this increment does not implement a snapshot publisher or artifact.
- Reconciliation errors (duplicate/foreign sources, missing mandatory links,
  wrong amounts/FX, wallet/hold/debt mismatch) make `FinanciallyReconciled=false`.
  Pending evidence/settlement, merchant cases and unmatched paid sources remain separately disclosed,
  not invented zero charges or external finality. Arithmetic overflow throws;
  money never wraps, clamps or uses binary floating point.
  Pending counts and holds describe the organization at capture time, not a
  reconstruction of its historical request-start population.
- `AddUzllmStatementSources` is an explicit composition-root opt-in alongside
  Payments. It is not enabled in the Gateway, so reporting introduces no new
  Gateway dependency or hot-path query. Future customer reads still require
  REPORTING-003's authorized Application facade; raw source readers are not APIs.

## Acceptance evidence

Validation is on Windows/.NET SDK 10.0.401 with disposable PostgreSQL 17/Redis 8
and Docker 29.7.2, starting from `0fc1caa`. No production or shared database was used.
Final baseline passed on the unchanged production/test inputs below; test
artifacts are ignored local output. This records repository acceptance only.

| Command / check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS, exit 0; final production build 1m16s, zero errors, existing ASPIRE010 warning. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS after final test-assertion correction; zero errors, same existing warning. |
| `dotnet test tests/Unit/UZLLM.Billing.Tests/UZLLM.Billing.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~StatementReconciliationTests --logger trx --results-directory artifacts/test-results/reporting-001-final-unit` | PASS: 36 passed, zero failed/skipped. |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~BillingFinancialIntegrationTests.Statement_ --logger trx --results-directory artifacts/test-results/reporting-001-final-pg-recheck` | PASS: seven passed, zero failed/skipped (45s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build -m:1 --logger trx --results-directory artifacts/test-results/reporting-001-baseline` | PASS, exit 0: 1078 passed, zero failed/skipped/not-executed, sixteen Completed TRX reports. PostgreSQL 248 (16m03s), Billing 76, Gateway 360, architecture 15, AppHost 2. Project scheduling is serial to avoid the unrelated 400ms provider-health timing flake documented in task 46; financial/concurrency tests retain their own parallel operations, assertions and original code. |
| Scope/static review | No schema/config/dependency/wire/frontend change, financial write or broad architecture exception. Local Markdown file targets, task-only diff, staged scope and whitespace pass. |

Development findings retained: the initial PG-test compile failed EF1002 on a
test fingerprint helper's interpolated table name; it now uses one constant SQL
union without suppression. The first final PG run was 6 PASS / 1 FAIL because
a substring privacy assertion matched legitimate `ReasoningTokens`. It now
excludes exact `Reason`, `MetadataJson` and maker/checker JSON property names;
no financial assertion or production behavior was weakened. Failed TRX remains
in `artifacts/test-results/reporting-001-final-pg`; clean recheck is recorded below.
The static test-pairing helper could not run because Python is unavailable;
no packages were installed, and no coverage percentage is claimed.

| Case | Direct regression evidence |
|---|---|
| ENT-001 | `Exact_fixture_separates_usage_reversal_debt_holds_and_frozen_tiyin` pins the sole AccountStatement kind and exact non-fiscal/no-payment-due label. No issuance API/profile is introduced. |
| ENT-002/008 | `Statement_exact_financial_fixture_is_read_only_and_preserves_budget_debt_and_hold` executes §4 amounts using real financial stores; checks every balance, gross/refund/net, tiyin/FX and unchanged financial/audit/outbox fingerprint and captured budget. |
| ENT-002 | `Statement_byok_reports_only_wallet_fee_not_external_provider_spend`; `Statement_late_settlement_and_refund_use_posting_month_not_request_analytics` distinguish wallet fee/capped charge from external spend/provider cost. |
| ENT-003 | `Period_is_half_open_utc_calendar_month`, `Period_uses_utc_instant_not_display_offset`, `Late_refund_uses_posting_month_with_negative_period_net_and_historical_bridge`, and the PG late-settlement test pin UTC boundaries, leap/year/offset and posting-vs-request semantics. |
| ENT-004 | `Statement_repeatable_read_excludes_delayed_older_uuid_commit_and_reconciles_historical_bridge` uses separate PG transactions and explicit commit barriers, checks identical membership/digest while frozen and changed membership in a new snapshot. No public page/export exists yet. |
| ENT-007 | `Statement_payment_port_is_tenant_safe_and_pending_missing_links_never_mutate_finances`; `Invalid_sources_or_balances_block_financial_reconciliation` retain pending/unknowns, deny foreign IDs and expose invalid mandatory links without financial effects. |
| Bounds/failure | `Statement_readers_require_caller_repeatable_read_and_honor_cancellation_and_bounds`, `Statement_source_limit_rejects_overflow_without_truncating_or_mutating`, `Empty_snapshot_reconciles_but_overflow_never_wraps_or_clamps`. |

No claim is made for ENT-005/006 publication/revisions, ENT-009–020 fiscal/SLA,
HTTP authorization/export/UI or production restore/load qualification. Existing
financial recovery and merchant/provider/legal approvals remain unchanged.
