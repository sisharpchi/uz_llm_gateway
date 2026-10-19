# Alerts and privacy UX — FRONTEND-009

## Scope and authority

FR-091/110–115, NFR-041–045/050–053/064/070, ADR-011/017/025.
OPS-003, OPS-004 and DX-002 are completed dependencies; PRIVACY-001 and
NOTIFY-002/003 supply the existing behavior. Explicit user selection authorizes
this P1 task while OPS-006 remains externally blocked. No launch gate changes.
The root mockup stays untouched; its alert configuration grouping informs the
navy/mint production pages, not its unsafe chat-ID entry or simulated test-send.
UI skill guidance is applied to readable labels, keyboard dialogs, 44px actions,
mobile wrapping, scoped states and honest acknowledgements, not a new UI stack.

Routes: `/organizations/:orgId/alerts` (organization-wide regardless of project
selection) and `/organizations/:orgId/privacy?project=:projectId` (one project).
Notifications owns rules/destinations/delivery evidence; Usage owns retention and
encrypted bodies. No financial, provider, worker-delivery or schema changes.

## Alert controls

- Owner/Admin manage; BillingViewer reads. Developer/ReadOnly cannot read alerts.
  Current membership metadata gates actions, but Management always reauthorizes.
  Failed current reads mask cached configuration and unmount private dialogs.
- LowBalance uses available wallet micro-USD; input supports six USD decimals
  with exact BigInt conversion and rejects unsafe JSON integers. BudgetWarning
  uses an existing project budget (not key budget), captured + held use and
  0.01–100% mapped to integer basis points. ErrorSpike supports org/project,
  five-minute completed requests and at least 20 samples. No spend mutation.
- Pause/enable and destination disable require confirmation. Enabling rearms a
  rule and can retrigger an existing breach. Recovery/new budget windows rearm
  deduplicated episodes. Disabling a channel does not silently delete rules.
- Telegram issues a ten-minute private-chat link, never accepts a chat ID. The
  official HTTPS `t.me` link is checked before navigation, expires visibly and
  is hidden when expired/closed. Closing does not revoke the issued challenge.
- Webhook registration validates public HTTPS/DNS, not ownership or successful
  delivery. The signing secret is shown once in ephemeral dialog state; closing
  hides it. Refresh then disable before replacement; no automatic write retry.
- New additive `GET .../alerts/events` returns latest 100 newest-first events
  with billing-read permission, tenant join, cancellation and no-store. Safe
  fields only; no chat ID, URL, transport error, body, signature or secret.
  Pending means no recorded acknowledgement (including work needing support),
  Delivered means transport accepted, Suppressed means delivery stopped. None
  proves human receipt or exactly-once delivery. Operator retry/dead-letter and
  provider-outage alerts stay outside the customer API.

## Payload controls and privacy boundaries

- Only current organization Owners can read/change project policy or retrieve
  a body; Admin is insufficient. Disabled by default, 60–10080 integer minutes.
  Explicit consent confirms storage/deletion effects; shortening also reduces
  existing expiries. Missing encryption returns an honest service failure.
- Opt-out deletes project payload rows transactionally. Activity, usage evidence,
  ledger and audit remain. No retroactive capture, metadata erasure, upstream ZDR
  or existing-backup byte-erasure promise. Financial/audit legal hold, approved
  operational schedules and quarantined restore scrub remain distinct.
- SECURITY-009 subsequently makes policy, shortening/opt-out effects and audit
  atomic. Its [rollback/cancellation evidence](46_RETENTION_AUDIT_ATOMICITY.md)
  supplements this task's historical UI coverage; no new screen or guardrail.
- Private inspector requires a pasted project request UUID and an explicit,
  audited read. Ordinary Activity never fetches bodies. Render as escaped text;
  nullable response explains incomplete/oversized output without implying zero
  provider cost. Show expiry; clear local body at expiry, before another read,
  on close, scope/unmount, and abort pending reads on unmount. Backend independently
  rejects expired/out-of-scope reads immediately; Worker sweeps expired rows.
- Secret/link/body commands are imperative, never Query/Mutation cached or written
  to storage, dashboard URL queries, telemetry or downloads. Telegram's external
  one-use `t.me` start link necessarily carries its challenge. Only scoped metadata
  uses TanStack Query.
  Late responses cannot reopen closed or different-scope dialogs. Browser memory
  is not a cryptographic erasure guarantee; downloaded screenshots/private tabs
  remain the user's responsibility.

## Configuration and external prerequisites

No migrations, dependency upgrades or production configuration changes.
Existing PayloadSecrets keys are separate from provider secrets. Existing
Telegram bot/webhook/chat key configuration and OutboundWebhooks encryption
keys must be provisioned by the operator; unconfigured channels report failure,
never simulated success. The disposable acceptance harness generates a separate
synthetic webhook key, uses public example.com only for DNS registration and
keeps its ErrorSpike below minimum sample size, then pauses/disables it: no
remote webhook POST or live bot message is sent.

Live bot/receiver ownership and delivery, provider/merchant onboarding, approved
retention/backup/legal schedules and distinct staging hosts are external gates.
See [production readiness](18_PRODUCTION_READINESS.md).

## Validation evidence

Validation: 2026-10-16, Windows/PowerShell, .NET SDK 10.0.401, Docker Desktop,
synthetic disposable identities/keys only. No intermediate run or historical
Completed label is production-launch evidence.

| Acceptance | Regression evidence |
| --- | --- |
| Delivery visible, opt-out understood | Browser creates exact threshold rules, confirms pause/disable and displays Pending/Delivered/Suppressed explanations. PostgreSQL endpoint test checks newest 100, ordering, anonymous/tenant/Owner/Admin/BillingViewer/Developer/ReadOnly and secret-free/no-store output. Existing delivery fixtures retain actual retry/suppression/replay coverage. |
| Owner-only retention and expiry | Browser opt-in/opt-out consent, role denial including Admin, explicit body read, null response, local expiry clearing, 403/404/503 and pending read scope cancellation. Existing PostgreSQL retention tests cover encrypted capture, shortening, opt-out, expiry and Worker deletion. |
| Secret lifecycle | Webhook show-once and late closed-command test; Telegram expiry/unconfigured channel; no storage/URL; current denial masks cached controls, uncertain writes never retry. |
| Real API/DB vertical | `zzz-alerts-privacy-real-stack.spec.ts`: register/Worker mail/session → owner UI opt-in → fixture BYOK inference → retained encrypted body read → guarded disposable expiry/404 → second inference → opt-out/404 with metadata retained → webhook DNS/create/show-once → non-triggering ErrorSpike → pause/disable → stranger 401/403 and invited Admin privacy denial. No browser route mocks or production clock override. |

The expiry fixture requires a harness-only opt-in flag, `uzllmacc` project
prefix, dedicated `uzllm_acceptance` database and exact validated UUIDs. It
changes dates on only the created synthetic payload. No runtime backdoor or
shared-database write is added. Actual delivery state transitions are proved
by existing PostgreSQL/synthetic transport tests, not claimed as live delivery
from the real-stack empty delivery history.

Initial focused browser run exposed mobile initial focus on a retention field;
fixed to Cancel without weakening assertions. Final review added a failed
project-list regression: loading/error no longer pretends a project is absent
or renders cached privacy controls; one shared error is shown, not duplicated.
Failed runs are not passing evidence. The final suite includes all 19 new
browser cases, not just the initial 16/18-case focused runs.

| Repository-root command | Result |
| --- | --- |
| `npm ci --prefix frontend/apps` | PASS |
| `npm run api:generate --prefix frontend/apps` | PASS; additive customer delivery route/DTO, generated TypeScript and serializer example |
| `npm run api:check --prefix frontend/apps` | PASS; schema/client/example drift absent |
| `npm run api:test --prefix frontend/apps` | PASS: 12 |
| `npm run api:types --prefix frontend/apps` | PASS; no-secret delivery and numeric-retention negative fixtures |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin TypeScript + Vite |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | PASS: 92, zero failures; one pre-existing Aspire-URL-only skip, not acceptance evidence |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- alerts-privacy.spec.ts --workers=1` | PASS: final 19, zero failures/skips; includes strengthened HTML-as-text and landscape/200% text assertions |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests --configuration Release --filter FullyQualifiedName~CustomerAlertIntegrationTests --logger trx --results-directory artifacts/frontend-009-focused` | PASS: 8, zero failures/skips |
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:acceptance .` | PASS; rebuilt after final UI read-error guard |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: final image run 4/4, zero failures/skips; `uzllmacc4f690db0`, completed `2026-10-15T20:01:09.514Z` |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; zero errors, existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/frontend009-baseline` | PASS: 800 across 16 projects, zero failures/skips; includes 218 PostgreSQL/Redis, 198 Gateway, 15 architecture and 2 Aspire cases |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production advisories |
| Changed-document local file-link check | PASS; anchors not evaluated |
| `git diff --check` / `git diff --cached --check` | PASS |

The first harness run rebuilt all application and edge images; final no-build
qualification uses the unchanged backend images and rebuilt final UI. Artifacts
identify pre-commit base `b660017bf13ee3d862dfe9a0109cc48fe15bfe11` plus this
tested working diff, not a production release revision. Disposable resources
are removed by the harness, without disturbing the existing local PostgreSQL/
pgAdmin. Screenshots are inspected, synthetic metadata only, with private
dialogs closed: [Alerts](assets/frontend-009/alerts-desktop.png),
[Privacy mobile](assets/frontend-009/privacy-mobile.png),
[Real-stack alerts](assets/frontend-009/real-alerts.png),
[Real-stack privacy](assets/frontend-009/real-privacy.png).

Normal npm audit still reports existing dev-only source-map-js
GHSA-68fv-2mgg-jv7q (high); no upgrade is bundled. The unchanged edge build-only
native-binding installation re-resolves Vite 8.3.4 versus host lock 8.3.1; this
is not a reproducible-release-build claim. Python 3.12 UI search was available
for this task; navy/mint consistency and accessibility guidance were used,
not its unrelated marketing-layout recommendation. No live channel, hosted CI,
provider/merchant, HA/PITR or paid-launch qualification is claimed.
