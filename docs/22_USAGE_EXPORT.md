# Bounded usage CSV export — USAGE-005

## Scope and ownership

FR-095 is a metadata export, not a new accounting authority. Usage Application
serializes the existing `IUsageReadStore` projections; Management owns HTTP,
current `ReadUsage` authorization and cancellation; the shared typed client
returns a `Blob`; the deployed dashboard exposes Activity/daily downloads.
No migrations, financial writes, background export queue, payload exports or
changes to the standalone root `frontend/` mockup are involved.

## API and limits

Authenticated GET:
`/management/v1/organizations/{organizationId}/usage/export`.
Owner/Admin/BillingViewer have organization-wide reads. Developer/ReadOnly need
a currently granted `projectId`. An operator grant is not a tenant permission.
Foreign dimension IDs cannot reveal another tenant's rows; grant revocation is
rechecked on the next request. Responses are `no-store`.

| Parameter / bound | Contract |
| --- | --- |
| `kind` | `activity` (default) or `daily`, case-insensitive. |
| Filters | `from`, `to`, `projectId`, `apiKeyId`, `modelId`, `providerId`, execution `status`, `isStream`, `requestId`; exactly the existing usage normalization. |
| UTC window | `[from,to)`, default last 30 days, maximum 90 days. |
| `rowLimit` | Default 1000, inclusive range 1–5000 data rows; header excluded. |
| Encoded file | Maximum 4 MiB, including BOM/header/escaping/CRLF; checked before attachment starts. This is a file bound, not a whole-process memory bound. |
| Deadline | Linked 30-second server cancellation; caller disconnect also cancels I/O and serialization. |
| Pagination | No partial/page export: `limit` or `cursor` returns `400`; too many rows/bytes returns `413`, never truncation. |

Activity fetches at most `rowLimit + 1` rows in a single newest-first
`(startedAt,requestId)` query. Daily reads the existing snapshot time series
(at most 91 UTC dates for a 90-day duration with partial first/last days),
rejecting a lower requested row bound if necessary.
All content is buffered before success. A database failure is not converted to
empty CSV or zero usage. Deadline expiry is `504` Problem Details; client
cancellation is propagated, not relabeled as a successful/partial download.

Success uses UTF-8 with BOM, quoted fields, doubled embedded quotes and CRLF;
media type `text/csv; charset=utf-8`, `Content-Disposition: attachment`, stable
`uzllm-activity.csv` / `uzllm-daily.csv`, and `nosniff`. `X-Usage-From`,
`X-Usage-To`, `X-Usage-Data-As-Of`, `X-Usage-Source`,
`X-Usage-Pending-Buckets`, `X-Usage-Row-Count` describe scope/freshness even
when there are no rows. Invalid input is `400`; authentication/permission
failures remain `401`/`403`. Versioned OpenAPI includes CSV binary output,
limits, headers and failure statuses. See [HTTP examples](../src/UZLLM.Management.Api/usage-export.http).

## Columns and consistency

Activity columns, in order:

```text
requestId,projectId,projectName,apiKeyId,apiKeyName,modelId,modelCode,
selectedModelId,selectedModelCode,providerId,providerCode,startedAt,completedAt,
executionState,deliveryState,financialState,httpStatus,isStream,attemptCount,
durationMs,inputTokens,outputTokens,chargedMicroUsd
```

Daily columns: `day,requestCount,errorCount,inputTokens,outputTokens,chargedMicroUsd,refundedMicroUsd,netChargedMicroUsd`.
Both append `fromUtc,toUtc,dataAsOf,source,pendingBucketCount`.

Timestamps are UTC ISO-8601; days are `yyyy-MM-dd`. Amounts are invariant integer
micro-USD text, including values beyond JavaScript safe integers. Nullable
activity fields are empty, never manufactured zero. Daily verified-token sums,
gross/refund/net and lag match `/timeseries`; late refunds are attributed to the
original request day. Activity is live (`AuthoritativeRequest`, read-time
`dataAsOf`, no rollup backlog); daily reports its materialized watermark and
pending hours. Separately fetched files/API responses can differ if source
data changes between requests or a Worker publishes corrections. Export does
not promise a cross-request snapshot or replace wallet/ledger reconciliation.
See [rollup semantics](21_USAGE_ROLLUPS.md).

## Spreadsheet and privacy policy

Quoting alone does not prevent spreadsheet formulas. Text beginning with
`=`, `+`, `-`, `@` or full-width equivalents, including leading whitespace,
control/Unicode-format bypasses, receives a leading apostrophe; leading control
or format characters are also guarded. Safe Unicode, commas, quotes and
embedded newlines are preserved within quoted cells; persisted data is never
modified. [OWASP CSV Injection guidance](https://github.com/OWASP/www-community/blob/master/pages/attacks/CSV_Injection.md)
warns that spreadsheet products and save/reopen behavior differ. Import
untrusted name columns as **text**; no universal Excel/LibreOffice execution
safety or save/reopen certification is claimed. Numeric precision also requires
text import in spreadsheets, despite exact source bytes.

Exports include no prompts, response bodies, provider/API secrets, fingerprints,
trace IDs or provider evidence payloads. Activity uses safe request metadata;
daily uses aggregate counters. Dashboard downloads use selected filters, not
loaded table pages or chart DOM. Pending download is disabled; range/tenant
change or unmount aborts it and cannot download stale tenant data. Error states
offer a smaller-range retry and never synthesize a CSV.

## Acceptance evidence

Backlog criteria: “Filters/UTC/totals match API, spreadsheet injection escaped,
size/page bound, other-tenant data absent. Contract/security tests.”

| Criterion | Tests |
| --- | --- |
| Filters/UTC/totals match | `Csv_activity_matches_paged_api_and_utc_filters_without_other_tenant_rows` independently parses CSV and compares paged API IDs/order, nullable tokens, charges, offsets and microsecond edges; pins 3500 micro-USD / 120 input tokens. `Csv_daily_matches_api_lag_and_original_day_after_refund_publication` compares the snapshot API before/after a real Worker refund publication. |
| Spreadsheet escaping | `Csv_escapes_formula_prefixes_and_preserves_quoted_cell_boundaries` covers 17 unsafe-prefix/quote/newline cases with an independent CSV parser, safe Unicode and unchanged source; integer precision and unknowns have separate assertions. |
| Size/page bound | Exact 1/5000 rows and cap+1, exact 4 MiB UTF-8 bytes and byte+1, invalid kinds/bounds, header-only empty output; real HTTP rejects `limit`/`cursor` and returns `413` without an attachment. |
| Tenant/security | `Csv_http_enforces_session_current_project_grants_bounds_and_safe_download_headers` uses real sessions, PostgreSQL authorization, allowed BillingViewer/granted Developer, denied outsider/unscoped Developer, foreign IDs and revoked grants; asserts no secret/payload/foreign request data. |
| Failure/cancellation | Service rejects pre-cancellation and propagates database failure; `Csv_deadline_cancels_pending_io_and_returns_problem_without_partial_download` exercises the actual 30-second HTTP deadline; `Csv_client_disconnect_cancels_pending_io_without_a_download` checks aborted I/O. |
| Browser/wire | Six new Playwright cases check actual downloaded bytes, active filters, daily refund/net, `413` retry, `403`/`504` errors, invalid range and tenant-switch abort. Published OpenAPI and Node schema tests pin binary media, auth, bounds and errors; compile-only cases enforce `Blob` result and reject unsupported export kinds. |

## Validation record — 2026-10-11

| Command | Result |
| --- | --- |
| `dotnet restore UZLLM.slnx` | PASS. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS: 0 errors; existing AppHost `ASPIRE010` warning remains. |
| `dotnet test tests/Contract/UZLLM.Gateway.ContractTests --configuration Release --filter 'FullyQualifiedName~UsageCsvContractTests\|FullyQualifiedName~UsageExportHttpContractTests\|FullyQualifiedName~Published_usage_export' --logger trx --results-directory artifacts/test-results/usage005-contract` | PASS: 32 export service/HTTP/OpenAPI cases. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/usage005-final` | PASS: 631/631 across 16 test projects, 0 failures/skips; includes 191 PostgreSQL integration cases, 136 Gateway contract cases and 2 Aspire tests. |
| `npm run api:generate --prefix frontend/apps` and `npm run api:check --prefix frontend/apps` | PASS: versioned documents, client types and serializer examples match. |
| `npm run api:test --prefix frontend/apps` | PASS: 7 schema/wire cases. |
| `npm run api:types --prefix frontend/apps` and `npm run build --prefix frontend/apps` | PASS: typed binary transport, dashboard and admin. |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | PASS: 36 passed, 1 existing Aspire-URL-only browser smoke skipped. Includes all six new CSV browser cases. |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: 0 production vulnerabilities; no dependency upgrades. |
| `git diff --check` and export-document local link check | PASS. |

The initial full .NET/browser checks were incorrectly overlapped: AppHost's
`frontend-install` runs `npm ci`, while Vite held its Windows native binding.
AppHost logs report `EPERM` unlinking rolldown; Playwright then lost its package.
This environmental failed run is retained in `artifacts/test-results/usage005`.
After browser shutdown, `npm ci` restored dependencies; frontend and
.NET/AppHost validation were rerun **sequentially**, not by weakening assertions.
Do not run AppHost installation against an active Vite/browser test workspace.

TRX files are local ignored evidence. No coverage percentage, executed mutation
score or universal spreadsheet safety claim is made. Optional static
source/test pairing could not run because its Python runtime was unavailable;
inline assertion/gap review and actual tests were performed. Existing
development-tool advisory handling remains in [API validation](api/VALIDATION.md).
Local disposable fixtures do not pass two-host load/restore or live
merchant/provider launch gates in [production readiness](18_PRODUCTION_READINESS.md).
