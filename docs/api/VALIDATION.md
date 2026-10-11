# DX-002 — Repository validation

Date: 2026-10-11. Scope: versioned current contracts and client drift, not a
production launch qualification. See [artifact guide](README.md).

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore --no-incremental` | PASS; existing ASPIRE010 CLI-bundle warning only |
| Full solution tests (command below) | PASS: 577 passed, 0 failed, 0 skipped across 16 projects; includes 175 PostgreSQL/Redis cases and 2 AppHost cases |
| `dotnet test UZLLM.slnx --configuration Release --no-build --list-tests` | PASS; all 12 new OpenAPI cases discovered |
| `npm run api:generate --prefix frontend/apps` | PASS; actual host documents and serializer fixtures regenerated |
| `npm run api:check --prefix frontend/apps` | PASS; both documents, both client types and examples match |
| `npm run api:test --prefix frontend/apps` | PASS: 5 tests |
| `npm run api:types --prefix frontend/apps` | PASS: negative type fixtures and positive nullable/string cases |
| `npm run build --prefix frontend/apps` | PASS: dashboard and admin |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: 0 vulnerabilities |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | PASS: 29 passed, 1 existing Aspire-URL-dependent smoke skipped |
| `git diff --cached --check` | PASS |

Full solution command (TRX files remain ignored local evidence):

```sh
dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/dx002
```

## Acceptance evidence

- `Published_examples_use_real_parser_writer_and_management_serialization` plus
  `serializer-derived examples match their actual status and media-type schema`
  cover real JSON output, errors, null unknown usage, money beyond JS safe integer
  range and top-up 201/200 replay distinctions.
- `Stream_example_preserves_done_and_safe_error_envelopes` compares successful SSE
  output with the published example and checks that interruption has no DONE.
- `Documented_request_rejections_are_enforced_by_real_parser` checks eight parser
  rejection cases; `gateway request example and rejection boundaries agree with
  the bounded chat contract` verifies their structural schema constraints.
- `session CSRF operator and idempotency requirements stay explicit` checks the
  public/session/operator surfaces and required proof/correlation headers.
- `drift comparison detects schema and client changes without treating CRLF as
  drift`, the CLI regeneration comparison and CI workflow enforce checked-in
  document/example/type synchronization. Compile-only type cases reject missing
  replay proof, unsupported routes/methods and numeric billing money.

## Limits

The default eight-worker browser run suffered Chrome/navigation timeouts; the
one-worker run passed without changing assertions or increasing timeouts. The
Aspire browser smoke needs `UZLLM_ASPIRE_DASHBOARD_URL` and was not claimed here.
The optional static source-pairing analyzer could not run because Python is not
on PATH; tests and manual bounded assertion review were performed independently.

The full development-dependency audit still reports the pre-existing
`source-map-js` high-severity advisory GHSA-68fv-2mgg-jv7q in both app toolchains.
It is not part of the production dependency graph; this contract task does not
silently apply unrelated dependency upgrades or claim the full audit is clean.

No provider/payment commercial credentials, live merchant verification, two-node
load/failover evidence, or production activation is implied. Those gates remain
in `../18_PRODUCTION_READINESS.md`; future Responses/embeddings and SDK automation
remain separate backlog tasks. Runtime wire/financial behavior is unchanged.
