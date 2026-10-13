# Team membership and invitations — FRONTEND-007

## Scope and ownership

FR-004–005 and ADR-025; dependency DX-002. Explicit user approval selects this
P1 task while OPS-006 remains externally blocked. This is not paid-launch or
separate-host qualification. The deployed dashboard changes, not the root
`frontend/` mockup or operator application.

`TeamPage` uses typed wrappers over the existing Management Team endpoints.
Organization Application/Infrastructure remains authoritative for membership,
transactional audit, last-owner protection and project authorization. No backend
wire/schema changes, migration, production configuration or financial state is
introduced. The acceptance harness adds only a guarded disposable test fixture.

## Delivered behavior

- Organization-level `/organizations/:orgId/team`: searchable email/role/status,
  joined dates, explicit grant counts, safe invitation metadata and freshness.
- Owner/Admin invite permitted roles. Owner cannot be invited directly; an
  existing member can be promoted by an Owner. Admin cannot invite, change or
  revoke Owner/Admin. Last active Owner demotion/revocation is disabled and
  remains protected by PostgreSQL even for forged requests.
- Cancel-first confirmation dialogs for role changes, soft revocation and each
  project grant/removal. Revocation retains history and removes grants.
  Developer/ReadOnly require explicit grants; switching to an organization-wide
  role clears grants. Returning requires granting projects again.
- Invitations show Pending/Expired/Accepted/Revoked with UTC expiry. Pending
  means issued, not delivered. No cancel/resend endpoint exists; expired records
  may be replaced by a fresh invite, while pending duplicates conflict.
- Standalone `/accept-invitation`: verified matching session, masked manual
  token entry and explicit acceptance. Current Worker mail contains a token,
  not an acceptance URL. Anonymous users receive sign-in/register links; service
  errors are not misreported as signed-out sessions. Invalid, expired, revoked,
  mismatched and replayed tokens uniformly receive the API's 404 explanation.

## Safety and UX

Account/organization-keyed reads and scoped components prevent tenant draft or
feedback bleed. Read denial hides member/invitation data and controls, rather
than showing an empty successful list. Live permission changes are enforced by
the API. Commands are imperative, pending-gated and never automatically retried;
network uncertainty requires refresh before retry. A committed late command
still invalidates its original scope without reopening another scope's form.

Tokens stay in ephemeral masked form state, never Query/Mutation caches or
browser storage. Success/unmount clears them. Accidental token query/hash values
are removed from the current URL and never forwarded to sign-in/register; do
not share token URLs. Backend/edge logs must still obey existing secret-redaction
policy. Acceptance does not guess an organization ID absent from the response:
the recipient opens the workspace selector after success.

The existing navy/mint mockup informed visual styling, with labeled native
dialogs, cancel-first focus, 44px actions, readable disabled controls, responsive
cards and reduced-motion compatibility. No fabricated member name, traffic or
delivery status is displayed.

## Validation evidence

The task's focused browser, real-stack and repository-wide regression checks
passed. This evidence qualifies the task, not production launch gates.

`dashboard/tests/team.spec.ts` covers Owner/Admin restrictions, invite and
role/grant commands, last-owner protection, cancel-first revocation, live 403/
409 feedback, late tenant-switch responses, acceptance session states,
token/URL/storage/replay boundaries, unchanged-data expiry polling, desktop,
mobile, landscape and large-text layout.

`dashboard/tests/team-real-stack.spec.ts` runs without route interception through
the real TLS edge, Management, Worker SMTP, PostgreSQL and Redis: verified
synthetic accounts, emailed invitations, actual expiry/reissue/accept/replay,
Owner/Admin authorization, cross-tenant denial, project grant/removal and soft
revocation. The expiry fixture is allowed only under `uzllmacc<8 hex>` Compose
projects, the dedicated `uzllm_acceptance` database and an explicit harness flag;
it updates exactly one UUID/email-validated synthetic, unaccepted invitation.
It changes no financial rows or runtime clock. Test pacing respects the real
edge's shared 30/minute auth/invitation limit, including invitation GETs.

### Commands — 2026-10-13

Windows/PowerShell, .NET SDK 10.0.401 and Docker. All accounts and upstream/
merchant credentials are synthetic; no live or shared database was used.

| Repository-root command | Result |
| --- | --- |
| `npm ci --prefix frontend/apps` | PASS; existing dev-only advisory below |
| `npm run build --prefix frontend/apps` | PASS; dashboard/admin strict TypeScript and production bundles |
| `npm run api:check --prefix frontend/apps` | PASS; existing artifacts/types/examples match; no regeneration needed |
| `npm run api:test --prefix frontend/apps` | PASS: 10 schema/contract tests |
| `npm run api:types --prefix frontend/apps` | PASS; Owner invitation and noncanonical role-label compile guards |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- team.spec.ts --workers=1` | PASS: 9 focused browser cases, zero failures/skips |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | 56 PASS, one existing Aspire-URL-only skip (URLs unset) |
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:acceptance .` | PASS; final UI rebuilt |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: 2 real-stack scenarios; Team 1.9m, total 2.4m |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production dependency vulnerabilities |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; zero errors, pre-existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/frontend007` | PASS: 732 tests across 16 projects; zero failures/skips, including 200 PostgreSQL/Redis, 182 Gateway, 40 Organizations, 15 architecture and 2 Aspire cases |
| `git diff --check` / `git diff --cached --check` | PASS |

Initial runs caught a fixture-read race and the real edge's shared throttle.
The fixture now waits for the first read and real-stack actions pace below the
documented rate without relaxing security or retrying writes. Failed runs are
not passing evidence. The final real-stack summary retained under
`artifacts/frontend-007/real-stack/summary.json` records pre-commit HEAD
`01236c823e0cda0785ea4c76f7bed588b4c972bb` plus this task's working tree,
`disposable-local-docker` and completion `2026-10-13T12:15:03.635Z`.
Edge image: `sha256:21c227718f912ba7460b32334132dec8d013596a978e11a8127341fc0fcf19d3`.

### Visual review and limitations

Captured and visually inspected synthetic, token-free
[desktop](assets/frontend-007/team-desktop.png) and
[mobile](assets/frontend-007/team-mobile.png) screens. Additional real Owner/
Admin screenshots are retained in `artifacts/frontend-007/real-stack/` after
token forms have closed. Existing CI acceptance artifacts include the new test.

The pre-existing dev-only source-map-js high advisory GHSA-68fv-2mgg-jv7q remains;
no dependency upgrade is bundled. The existing edge recipe's build-only binding
install resolves Vite 8.3.4 versus lockfile host Vite 8.3.1; release artifact
pinning remains necessary. Python was unavailable for the UI skill CLI, so its
layout/accessibility guidance and the repository mockup were applied directly.
No screen-reader, full localization, coverage/mutation score or hosted-CI run
is claimed. OPS-006 and live merchant/provider/infrastructure launch gates
remain independent blockers.
