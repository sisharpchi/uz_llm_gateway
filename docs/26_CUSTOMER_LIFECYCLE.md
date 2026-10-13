# Customer lifecycle controls — FRONTEND-006

## Scope and architecture

FR-010–013/020–028/100–104; dependencies APIKEYS-004, LIMITS-002 and
PROJECTS-002. Explicit user selection authorizes this P1 delivery while OPS-006
remains externally blocked. This is not paid-launch or separate-host qualification.

The deployed React dashboard, not the root mockup, now supports:

- Projects: create, open, terminal archive with confirmation; selected-project
  routing strategy, metadata-only environment and hard model/provider allowlists.
- Keys: create with optional future UTC expiry, show-once copy with clipboard
  failure feedback, immediate rotation, disable/enable with confirmation and
  preserved identity/history; allow/deny model/provider policies.
- Project/key request limits: configured overrides, effective values and operator
  ceilings. Blank inherits finite bounds, never unlimited.
- Project/key financial budgets: Lifetime/Daily/Weekly/Monthly, exact USD input
  with six decimal places, captured/held progress and server UTC reset windows.
  Zero blocks new spend; installing a policy can conflict with active holds.
- Key usage: existing 30-day scoped summary, requests/tokens/net charge, unsettled
  count and aggregation freshness; project activity link for further filtering.

`ProjectsPage`/`KeysPage` compose shared policy, dialog and limit/budget controls.
The shared client adds typed wrappers for existing archive, rotation, status and
budget endpoints. No backend wire/schema/configuration or migration changes.
Server Domain/Application rules, tenant authorization and CSRF remain authoritative.
No financial state is computed or persisted by the browser.

## Safety and UX

Existing dark navy/mint product styling and the root mockup's compact security
callouts informed the layout. The mockup remains unchanged. Native labeled
dialogs, cancel-first destructive actions, keyboard focus, 44px actions,
responsive controls and reduced-motion compatibility preserve accessibility.
Environment is actual metadata (or explicitly untagged), never a fake Production
label. No fabricated traffic, last-used time or balance is displayed.

Null allowlists and deliberate empty deny-all lists have separate controls.
Strategy overrides never widen project/key restrictions or enable BYOK/Hybrid.
Each settings/policy save replaces its documented group, last-commit-wins;
request limits, budgets and key creation are independent operations, not one
atomic wizard. Configure new keys before putting their secret into service.

Secrets live only in the scoped page's ephemeral show-once state. Imperative
commands avoid TanStack Query/Mutation caches. Closing/navigating/switching scope
destroys them; late responses cannot reopen dialogs in another scope. Completed
commands still invalidate only their scope-keyed metadata, so returning
to a closed editor loads the updated settings instead of seeding an old draft.
No secret-bearing result is passed to that invalidation callback. In-flight
commands may still commit on the server: network failures explicitly instruct
refresh before retry. Creation and rotation are never automatically retried.

The existing budget wire contract uses numeric Int64 micro-USD. Browser input
is converted with BigInt, then accepted only up to `Number.MAX_SAFE_INTEGER`
micro-USD. Unsafe responses disable money display/editing rather than round them.
This is an exact-browser limitation, not a server financial ceiling; larger
values need an exact-integer API client. Wallet/analytics decimal-string contracts
are unchanged. Existing holds and counters are never reset by UI commands.

Permission failures are explicit per operation. Current key listing requires
ManageApiKeys (Owner/Admin or granted Developer), settings writes ManageProjects,
budget reads ReadBilling and writes ManageBilling. A role downgrade is enforced
by the API even when a previously authorized screen is still open. Read denial
is not presented as an empty list; unsafe secret issuance is disabled on key-read
failure. Status, expiry and archived-project states are distinct.

No key deletion/rename/existing-expiry edit, budget removal, price-class taxonomy,
team screen, BYOK/routing preview or localization is invented here. They require
their documented contracts/tasks; team APIs are used only for test fixture setup.

## Acceptance and validation

### Automated evidence

`dashboard/tests/lifecycle.spec.ts` has 11 focused browser cases covering
null/deny-all policy replacement, exact budgets/zero/overflow, finite inherited
limits, key expiry, show-once storage boundaries, rotation/disable/enable,
clipboard failure, cancel-first keyboard dialogs, role/read/conflict errors,
late tenant/project secret responses, late settings-save cache invalidation,
stale key-editor hiding after read denial, and desktop/mobile layout. Existing tenant/secret assertions now use exact text
locators because action buttons intentionally include their target key's name.

`dashboard/tests/real-stack.spec.ts` extends LAUNCH-002 without route interception:
owner settings/limit/budget writes persist through PostgreSQL, a zero key budget
rejects inference with 402, creation/rotation issue show-once secrets, the old
secret fails with 401, disable/enable changes authentication, and archive is
terminal. A granted Developer is downgraded to ReadOnly while the screen is
open; create/status/permission/limit writes visibly fail with 403 and stored
values remain unchanged. An outsider cannot read settings/key limits. Worker
SMTP invitation is test setup only, not a team-screen implementation.

### Commands — 2026-10-13

Windows/PowerShell, .NET 10.0.401, Docker 29.7.2. All infrastructure is disposable
and identities/provider/merchant keys are synthetic; no shared database, live
provider/merchant or paid traffic was used.

| Repository-root command | Result |
| --- | --- |
| `npm ci --prefix frontend/apps` | PASS; pre-existing development advisory below |
| `npm run build --prefix frontend/apps` | PASS; dashboard/admin strict TypeScript and production bundles |
| `npm run api:check --prefix frontend/apps` | PASS; no OpenAPI/example/client drift; no regeneration needed |
| `npm run api:test --prefix frontend/apps` | PASS: 10 schema/contract tests |
| `npm run api:types --prefix frontend/apps` | PASS; includes invalid status/window compile fixtures |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- lifecycle.spec.ts --workers=1` | PASS: 11, zero failures/skips |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | 47 PASS, one pre-existing Aspire-URL-only skip (URLs unset) |
| `node deploy/tests/run-launch-acceptance.mjs --host-publish` | PASS: real-stack lifecycle/launch acceptance, 34.5s scenario |
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:acceptance .` | PASS; rebuilt after final UI race correction |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: final real-stack scenario, 30.4s; loaded key-usage data verified |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production dependency vulnerabilities |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; zero errors, existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/frontend006` | PASS: 732 tests across 16 projects, zero failures/skips; includes all 200 PostgreSQL/Redis, 182 Gateway, 15 architecture and 2 Aspire cases |
| `git diff --check` / `git diff --cached --check` | PASS |

Initial browser runs caught ambiguous locators, a mobile shell min-width problem
and stale draft seeding after a late save. All were corrected with regression
coverage; failed runs are not passing acceptance evidence.

The final real-stack summary in `artifacts/frontend-006/real-stack/summary.json`
records pre-commit HEAD `9353530c63edeb8096e727c255d2f75b573a4ea2` plus this task's
working-tree implementation, `disposable-local-docker`, and completion timestamp
`2026-10-13T11:33:28.521Z`. Edge image ID:
`sha256:3771ca454d97a0cbfcfcf0570f5a045c14ed170be0bed416c464a447a7cb3949`.
No hosted-CI run or production gate is claimed. The existing Dockerfile's
build-only binding install re-resolved Vite to 8.3.4 inside the cached image;
the lockfile-based host build used 8.3.1. That pre-existing release reproducibility
limitation is unchanged and must be accounted for by artifact pinning at launch.

The pre-existing dev-only source-map-js high advisory GHSA-68fv-2mgg-jv7q remains;
no dependency upgrade is bundled. Python was unavailable for the UI skill's
optional CLI; its accessibility/layout guidance and existing mockup were used
directly. No automated screen-reader, full localization, coverage or mutation
score is claimed. OPS-006 and external launch gates remain independent blockers.

### Visual review

Captured and visually inspected synthetic fixtures (no secrets):
[project desktop](assets/frontend-006/projects-desktop.png),
[key mobile](assets/frontend-006/keys-mobile.png).
Additional desktop/mobile and real API screenshots are retained locally under
`artifacts/frontend-006/`; real-stack screenshots are captured only after secret
dialogs have closed. CI retains real-stack artifacts through the existing
LAUNCH-002 upload step.
