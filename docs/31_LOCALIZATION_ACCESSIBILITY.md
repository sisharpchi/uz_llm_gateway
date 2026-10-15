# FRONTEND-010 — Localized and Accessible Customer Flows

## Scope and architecture

Customer dashboard, auth/recovery, onboarding and invitation acceptance support
English (initial default), Uzbek Latin and Russian. This is the production app
under `frontend/apps/dashboard`, not the separate root mockup. The existing
navy/mint design, module boundaries and generated API client are preserved.
Admin translation is outside this customer-flow task; no API, database,
migration, server configuration or package-version changes are required.

870 English source-message keys index an Uzbek/Russian catalog. React
renders escaped strings and named interpolation parameters; there is no DOM
rewriter, HTML injection or external translation service. Navigation, forms,
dialogs, application-owned validation, empty/error/loading/success states, financial explanations
and closed display enums use the catalog. User-defined names, IDs, provider
names, model codes, secrets, retained bodies, CSV and executable JSON/curl
examples remain opaque. Translation changes labels, never wire values.

`LocaleStore` uses a small SPA subscription, without remounting scoped editors
or changing query keys. Only `uzllm.ui.locale.v1` and
`uzllm.ui.timezone.v1` are persisted, as browser-local **display preferences**,
not account policy. Validate stored values; unavailable storage still permits
in-tab changes. Cross-tab changes update open copy without extending a secret's
existing lifetime. No API call saves language/timezone; no secret enters storage.

## Financial and time invariants

- USD micro-units and UZS tiyin remain exact decimal strings/`BigInt` through
  formatting. Preserve two-place wallet display truncation, except nonzero
  sub-cent amounts use six places rather than misleadingly displaying zero; usage,
  budgets and BYOK use six places, catalog prices retain sub-micro precision.
  Currency units stay explicit; an unknown charge remains unknown, not zero.
- Native localized labels have explicit canonical option values. In particular
  `Monthly`, `price`, `Payme`, `LowBalance`, role codes and execution modes are
  never translated in requests. Existing numeric budget precision guards remain.
- Inputs retain their documented canonical grammar: whole UZS; USD with a dot,
  no grouping/exponent; date filters and expiry inputs explicitly UTC. A local
  display timezone does not move analytics/financial UTC windows or change FX.
- Some Chromium builds claim Uzbek support but return root-locale separators
  and `M10` month names. Pin Latin Uzbek month names, nonbreaking-space grouping
  and comma display decimals with a small formatter; use native Intl for
  English/Russian. This is display-only, without a polyfill dependency.

## Accessibility contract

Document language follows selection. Named native selectors, inputs, buttons,
headings and status/alert regions expose accessible names. A global skip link
targets a focusable main; route changes focus content, including replacement of
an asynchronous loading main, without stealing focus from an active input.
Native modal dialogs keep background controls inert, have labelled headings,
start on the safe dismiss action, close on Escape and restore connected trigger
focus. Show-once secrets/payloads retain their existing clear-on-close/scope rules.

Long localized text and exact monetary values wrap; mobile navigation and wide
activity tables keep their intentional local scrolling. Large-text and reduced
motion checks preserve usable controls. Automated accessible-name/focus/browser
evidence is not a claim of a manual NVDA/VoiceOver audit or universal WCAG certification.

## Validation evidence

Validated 2026-10-16: repository Release baseline, source-catalog/placeholder
checks, browser regressions and disposable real TLS-edge/API/PostgreSQL/Redis/
Worker/payment/provider flow. Screenshots were reviewed after private dialogs
closed. The final evidence below supersedes failed intermediate runs.

Acceptance maps to ADR-016 and Frontend Architecture §10. The previous backlog
reference to NFR-083 was incorrect: that requirement governs migration
versioning, not accessibility. User approval authorizes this P1 task while
OPS-006 still lacks separate staging hosts; no production gate is passed here.

| Acceptance criterion | Evidence |
| --- | --- |
| Supported customer screens localized | `localization.spec.ts` traverses all 12 dashboard pages in all three languages; Uzbek/Russian auth/recovery, onboarding and invitation/error paths, plus existing English auth coverage. Catalog tests check 870 two-language messages, placeholder parity, hard-coded JSX/attributes, literal translation keys and validation/action feedback coverage. |
| Financial units exact and readable | Int64 maximum, negative/sub-cent/zero amounts, tiyin fractions, six-place budgets, sub-micro catalog prices and fractional markup; no monetary Number coercion. Locale changes preserve canonical Monthly/price/Payme values and budget/input precision guards. |
| Keyboard and accessible names | Named controls on every supported screen, document language, main focus, safe modal dismissal/focus restoration, reduced motion, mobile/200% text and no page-level overflow. Native date-picker/autofill/browser validation UI remains controlled by the browser/OS language; application copy is localized. |
| Security and state isolation | Opaque HTML-like names render as text; locale changes retain drafts without remounting, a cross-tab switch does not reissue/show a closed secret, invalid/unavailable storage fails safely, preferences do not write APIs or alter UTC query windows. Existing role/tenant/late-secret regressions remain mandatory. |
| Real service flow | `zzzz-localization-real-stack.spec.ts`: Uzbek register → Worker verification mail → login/organization/project → canonical Monthly budget → show-once key → exact UZS quote/top-up → synthetic authenticated Payme callbacks → Russian/UTC persistence → real fixture inference/settlement/activity → localized Owner search on real Team API. No browser route mocks or live payments/providers. |

Initial runs exposed duplicate whitespace from source-text extraction, a catalog
test that asserted before the new tenant's query completed, and a strict
`getByLabel` test locator that included native option text. Fix the actual copy
and wait for the intended query; use the combobox's computed accessible name,
without weakening authorization or secret assertions. Chromium Uzbek fallback
and sub-cent visibility were corrected and regression-tested. Final copy uses
accounting-specific Uzbek reconciliation terminology. Failed runs do not count.

The root mockup's dark/minimalist direction is retained without importing its
sample balances, floating-point conversions or simulated traffic. UI/UX and
React/TypeScript skills informed visible focus, text reflow, safe modal actions
and retained component identity; no new UI/state/i18n framework was introduced.

### Commands and limitations

Windows/PowerShell, .NET SDK 10.0.401, Docker Desktop; synthetic identities,
merchant/provider credentials and isolated test databases only. The final
implementation passes the checks below; these are not paid-launch approval.

| Repository-root command | Result |
| --- | --- |
| `npm ci --prefix frontend/apps` | PASS |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin TypeScript + Vite |
| `npm run api:check --prefix frontend/apps` | PASS: schema/client/serializer examples match, no wire changes |
| `npm run api:test --prefix frontend/apps` | PASS: 12, zero failures/skips |
| `npm run api:types --prefix frontend/apps` | PASS |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | PASS: final 108, zero failures; one pre-existing Aspire-URL-only skip, not acceptance evidence |
| Same browser command with `CI=true` | PASS locally with bundled Chromium: 108, zero failures, same one pre-existing skip; wired into the existing CI job, not a hosted-CI result |
| Focused localization Playwright specs | PASS: 16; final assertions also included in full suite |
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:acceptance .` | PASS: final UI image rebuilt |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: final UI image, 5/5, zero failures/skips |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS: zero errors, existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/frontend010-baseline` | PASS: 800 across 16 projects, zero failures/skips; includes 218 PostgreSQL/Redis, 198 Gateway, 15 architecture and 2 Aspire cases |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production advisories |
| Full `npm audit --prefix frontend/apps` | Existing dev-only source-map-js GHSA-68fv-2mgg-jv7q (high), exit 1; no dependency upgrade bundled |
| Changed-document file links | PASS; anchors not evaluated |
| `git diff --check` / `git diff --cached --check` | PASS |

The larger bilingual catalog produces a 590.52 kB host JS bundle (176.94 kB
gzip) and Vite's >500 kB warning; it is not a frontend/load-performance
qualification. The unchanged Docker build-only native-binding installation
re-resolves Vite 8.3.4 versus the host lock's 8.3.1. No reproducible release-build,
hosted CI, manual assistive-technology, live merchant/provider, two-node or
HA/PITR qualification is claimed. OPS-006 and external paid-launch gates remain
blocked independently of this task.

Final disposable real-stack evidence: `uzllmacc52818b9a`, completed
`2026-10-15T21:35:24.200Z`. It identifies base revision
`834cce656d2f616c6aece85cd5e48477477a1a6e` plus this tested working diff, not a
deployed release. The harness removed its own services and temporary credentials;
existing local PostgreSQL/pgAdmin were preserved. No shared migration was applied.
Private dialogs are closed in inspected screenshots; all metadata is synthetic:
[Uzbek desktop](assets/frontend-010/uz-desktop.png),
[Russian desktop](assets/frontend-010/ru-desktop.png),
[Russian mobile / 200% text](assets/frontend-010/ru-mobile.png),
[Real settled activity / UTC](assets/frontend-010/real-localized-activity.png).
