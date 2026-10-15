# BYOK and routing UX — FRONTEND-008

## Scope and ownership

FR-055–068, NFR-041–044/046/080–084, ADR-010/013/024/025.
BYOK-003 and PROJECTS-002 are completed prerequisites. User-named P1 selection
is explicitly authorized while the unrelated OPS-006 two-host gate stays blocked.
The root mockup remains untouched: its dark panels, mint accents and routing
sequence informed the production React UI, with keyboard dialogs, confirmation,
scoped state and honest failure explanations replacing fabricated controls.

`/organizations/:orgId/provider-keys` owns organization credential metadata;
`/organizations/:orgId/routing?project=:projectId` owns project settings and a
request-only builder. Existing Provider services own all credential mutations;
existing Projects services own defaults/restrictions. Billing/ledger, adapters,
encryption and Gateway policy are unchanged. No migration/config/package upgrade.

The only new API is `GET .../provider-keys/providers`: current Owner/Admin tenant
authorization, no-store, active supported catalog `{id,code,name}` only. It closes
the registration UUID discovery gap without opening operator APIs or hard-coding
catalog identities. Listing does not certify mapped/priced models or live health.
Contracts and TypeScript are regenerated; unsupported/inactive choices are hidden
server-side, and create revalidates authoritative state.

## Credential UX and security

- Create, rename/rotate, explicit per-project grant/revoke, replace canonical
  model restrictions and optional exact lifetime external cap, test, disable,
  and irreversible crypto-shred delete. Creation and grants are independent:
  failed grants never imply credential rollback. Disabled keys cannot be edited,
  tested or newly granted; remove/delete remain available. No re-enable API.
- Secret input is password-only, never revealed/copied/returned. Local state
  clears before submit and on dialog/scope unmount. Commands are imperative,
  never Query/Mutation cached; no storage, URL or telemetry. Network ambiguity
  tells the user to refresh before retrying. Writes are never automatically retried.
- Metadata cache is account/organization scoped. A current read denial hides
  cached values and unmounts dialogs. Late committed commands invalidate only
  their original scope; cannot reopen a form or render feedback in another tenant.
  Provider keys are organization-scoped, so project selector changes do not turn
  a credential-create draft into an implicit grant to the newly selected project.
- External cap/spent/holds use decimal strings and BigInt, never floating point.
  Blank cap means null, zero blocks new spend; at most six USD decimals/Int64.
  BYOK allowlist is null or 1–100 codes, never an empty deny-all array. Server
  validates active provider mappings; model codes are not inferred from names.
- Valid authenticates a fixed model-list endpoint only; Invalid/Unavailable are
  explained without upstream payloads. Last test time is shown, last-used is not
  fabricated. Tests do not reset inference health or certify supplier invoices.

## Routing preview and non-goals

Reuse project settings editing; no second financial policy, per-request cost cap,
project credential default or automatic BYOK/Hybrid is added. Hard restrictions
always intersect API-key permissions and existing budgets/wallet/operator gates.
The builder starts Managed; switching into Hybrid resets and requires consent.
It emits explicit provider UUID, routing override/pin and optional ordered model
alternatives only in request JSON. It does not save these choices or send traffic.
The provider pin constrains the primary model; explicitly consented canonical
model alternatives may use other providers within project/key hard restrictions,
matching the current Gateway contract rather than implying a global provider lock.

Candidate steps explain BYOK first, then conditional managed same-model capacity,
then explicit managed model alternatives if an attempt slot remains. No multi-key
weights, custom endpoints, cache/timeout slider, guaranteed endpoint ordering or
live health prediction. Project catalog is a priced snapshot; key permissions,
request capabilities, quota, health, financial holds and operator pause are
rechecked by Gateway. Known disabled/ungranted/disallowed/capped key and consent
conflicts block the example; unavailable BYOK never silently becomes Managed.
Authentication, timeout/unknown execution and partial output never trigger replay.

## Validation evidence

Validation date: 2026-10-15, Windows/PowerShell, SDK 10.0.401, local Docker and
synthetic credentials only. Required repository checks passed; the completed
status is included in this task's dedicated local commit, not a launch approval.

| Acceptance | Observable evidence |
| --- | --- |
| Secret reveal prohibited | `byok-routing.spec.ts`: create/rotate password-only inputs, cleared submit/cancel, no storage/URL/plaintext display, read-denial masks cached controls, late tenant response cannot reopen UI. Real HTTP tests assert masked response and no secret; real-stack checks no-store. |
| Mode/order preview accurate | Managed inheritance and strategy override; BYOK first regardless of strategy; Hybrid requires fresh explicit consent; project change resets mode/override/consent. Pin affects primary only; explicit alternatives may use other permitted providers. Known model/provider/grant/cap conflicts block preview. |
| Failures explained | Valid/Invalid/Unavailable verification explanations; current 403 read denial; 400/403/409/429/503 and lost-write feedback without retry or retained input; irreversible disable/delete confirmations. |
| Real API flow | `zz-byok-real-stack.spec.ts`: real register/Worker mail/session → catalog identity → masked create → restrictions → grant → Gateway key → fixture chat/SSE → external spend 22 micro-USD, no external hold, wallet zero under synthetic zero fee → default/explicit mode preview → stranger 401/403 → rotate → disable/denied inference → delete/404. No browser route mocking or supplier verification override. |
| Minimal API authorization | Four-provider `ByokEndpointIntegrationTests`: anonymous/foreign tenant denied; Owner/Admin accepted; Developer/ReadOnly/BillingViewer denied; unsupported and inactive supported providers absent. |

| Repository-root command | Result |
| --- | --- |
| `npm ci --prefix frontend/apps` | PASS |
| `npm run api:generate --prefix frontend/apps` | PASS; additive provider-options route/DTO/client and serializer example |
| `npm run api:check --prefix frontend/apps` | PASS; no wire/client/example drift |
| `npm run api:test --prefix frontend/apps` | PASS: 11 schema/example tests |
| `npm run api:types --prefix frontend/apps` | PASS: includes exact-cap and no-secret negative fixtures |
| `npm run build --prefix frontend/apps` | PASS: dashboard/admin, TypeScript + Vite |
| `npm run test:e2e --workspace @uzllm/dashboard --prefix frontend/apps -- --workers=1` | 73 PASS, zero failures; one existing Aspire-URL-only skip (not acceptance evidence) |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --no-build --filter FullyQualifiedName~ByokEndpointIntegrationTests --logger trx --results-directory artifacts/test-results/frontend008-http-final` | PASS: 4, zero failures/skips, including final role/filter matrix |
| `node deploy/tests/run-launch-acceptance.mjs --no-build` | PASS: final rebuilt-edge run 3/3, zero failures/skips; disposable project `uzllmacc308daf9d`, completed `2026-10-15T07:06:16.850Z` |
| `dotnet restore UZLLM.slnx` | PASS |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS; zero errors, existing ASPIRE010 warning |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/frontend008-baseline` | PASS: 799 across 16 projects, zero failures/skips; includes 217 PostgreSQL/Redis, 198 Gateway, 15 architecture and 2 Aspire cases |
| `npm audit --omit=dev --prefix frontend/apps` | PASS: zero production advisories |
| Changed-document local file-link check | PASS; anchors not evaluated |
| `git diff --check` / `git diff --cached --check` | PASS |

Initial browser failures exposed exact-label selectors and a test race with
independent settings refetch (which deliberately resets request drafts); corrected
selectors and fresh scoped read, without increasing timeouts or weakening checks.
First real-stack run exposed the new spec taking the existing cold-Redis sentinel
before LAUNCH-002. Run the BYOK spec last; standalone bootstrap accepts only exact
`admission_unavailable` + Retry-After, proves external spend/hold remain zero, then
waits the fixture recovery horizon. No runtime retry/limiter relaxation added.
The failed runs are not passing evidence. Final edge is rebuilt after the primary-
pin clarification. Normal `npm audit` still reports the existing dev-only high
source-map-js GHSA-68fv-2mgg-jv7q; no dependency upgrade is bundled. Existing edge
build-only native-binding install re-resolves Vite 8.3.4 versus host lock 8.3.1;
the recipe is unchanged and this is not a reproducible-release-build claim.
Python UI-search CLI was unavailable; existing design and skill quick-reference
guidance were used without installing tools or claiming an automated design score.
Synthetic fixture checks do not qualify live suppliers, merchants, external secret
distribution, hosted CI or the blocked two-host reliability gate. Acceptance
artifacts identify the pre-commit base revision plus this tested working diff, not
a production release revision; failed traces contain synthetic data only and are
ignored, never committed.

Screenshots (synthetic metadata, no entered secrets):
[Provider keys](assets/frontend-008/provider-keys.png),
[Routing desktop](assets/frontend-008/routing-desktop.png),
[Routing mobile](assets/frontend-008/routing-mobile.png),
[Real-stack Hybrid preview](assets/frontend-008/real-routing.png).
