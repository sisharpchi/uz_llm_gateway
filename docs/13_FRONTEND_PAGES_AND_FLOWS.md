# Frontend Pages and User Flows

## Account verification and recovery

The customer dashboard has `/register`, `/verify-email`, `/recover`, and
`/reset-password` routes. Verification/reset emails include a one-time token
and, when the public dashboard URL is configured, a link with that token in a
fragment. Opening a link pre-fills the token but never consumes it until the
user confirms; the fragment is removed from browser history immediately.
Registration and recovery show the same accepted state regardless of whether
an account exists. Invalid, expired and replayed tokens share one safe retry
message; 429 shows a neutral wait message. Successful reset clears the token
and password from the form, then links to sign-in. These flows use the real
Identity API, not the root `frontend/` mockup's sample state.

## 1. Onboarding

### Page: Welcome / Create organization

Goal:
- create first org;
- explain 3-step setup.

Wizard:

```text
1. Create project
2. Top up or add BYOK
3. Create API key
4. Send first request
```

Provide copyable curl/.NET/JS examples.

---

## 2. Organization Overview

Widgets:
- Available balance
- Spend today
- Spend this month
- Requests
- Error rate
- Top model
- Top project
- Recent activity

Charts:
- cost over time;
- requests over time;
- model cost distribution.

Actions:
- Top up
- Create project
- Create API key

---

## 3. Projects

Table:
- Name
- Environment/tag
- Spend
- Requests
- Error rate
- Active keys
- Status

Actions:
- Create
- Open
- Archive

---

## 4. Project Overview

Cards:
- project spend;
- request count;
- tokens;
- errors;
- latency.

Sections:
- cost timeline;
- model breakdown;
- provider breakdown;
- key breakdown;
- recent requests.

---

## 5. API Keys

Table:
- Name
- Masked key
- Status
- Created
- Expires
- Spend
- Limit
- Last used

Create dialog:
- name;
- expiry;
- all-time limit;
- recurring limit;
- allowed models/providers P1.

Result:
- show key once;
- copy button;
- warning.

Actions:
- disable/enable;
- rotate;
- edit limits;
- edit model/provider permissions.

FRONTEND-006 implements create with optional UTC expiry, show-once copy,
confirmation-gated rotate/disable/enable, permissions and separate request/spend
limits. Key deletion, renaming and changing an existing expiry are not exposed
by the current backend and are not simulated in the UI. Disable preserves
history (FR-022). Last-used timestamps and nonexistent summary fields are not
fabricated. Key usage reads the existing scoped 30-day summary with unsettled
counts and aggregation freshness. See [implemented lifecycle scope](26_CUSTOMER_LIFECYCLE.md).

---

## 6. Model Catalog

Search/filter:
- provider;
- capability;
- price;
- context length;
- status.

Card/table:
- model name;
- providers;
- input price;
- output price;
- context;
- tags: Vision / Tools / Reasoning / etc.

Detail:
- price by provider;
- capabilities;
- availability;
- sample request.

---

## 7. Playground [P1]

This page remains aspirational. DX-003 delivers [server/terminal SDK examples](36_SDK_EXAMPLES.md)
instead of its optional browser playground; it does not collect API secrets in
the dashboard or mark this page implemented.

Inputs:
- model;
- routing;
- messages;
- temperature;
- max output;
- streaming toggle.

Output:
- response;
- provider;
- tokens;
- latency;
- cost;
- request ID.

Useful for onboarding and provider debugging.

---

## 8. Activity

High-value page.

Columns:
- Time
- Status
- Project
- API key
- Model
- Selected model when explicit cross-model fallback changed it
- Provider
- Input tokens
- Output tokens
- Cost
- Duration

Filters:
- date range;
- status;
- key;
- model;
- provider;
- streaming;
- request ID search.

Click row -> Request Detail.

---

## 9. Request Detail

Sections:

### Overview
- status;
- request ID;
- trace ID;
- timestamp;
- key;
- project.

### Routing
- requested model;
- selected model and each attempt's model (when different);
- selected provider;
- fallback attempts;
- routing strategy.

### Usage
- input;
- output;
- cached;
- cost breakdown.

### Performance
- total duration;
- TTFT;
- output TPS.

### Error
If failed:
- normalized category;
- safe provider response;
- retry/fallback history.

### Payload
Only if organization enabled payload retention.

---

## 10. Analytics

Tabs:
- Cost
- Requests
- Tokens
- Errors
- Performance

Breakdown selectors:
- Model
- Provider
- API key
- Project

Date:
- 24h
- 7d
- 30d
- custom

---

## 11. Billing / Wallet

Header:
- current credit;
- UZS equivalent if shown;
- low balance threshold.

Top-up:
- amount presets;
- custom UZS amount;
- Payme;
- CLICK.

Payment history:
- date;
- provider;
- UZS amount;
- credited amount;
- state;
- reference.

The implemented dashboard derives pending, credited, reversed and under-review
labels from tenant-scoped wallet/case flags, not from a payment-status string
alone. It shows recovery debt/spending hold and immutable wallet-credit refunds;
operator-only merchant statement evidence and case details stay in the admin app.

Ledger:
- top-up;
- usage;
- adjustment;
- refund.

---

## 12. Provider Keys / BYOK [P1]

List:
- provider;
- masked key;
- status;
- allowed models;
- spend;
- last used.

Create:
- provider;
- key;
- name;
- explicit allowed projects.

`BYOK-001` initially exposed fixed official OpenAI/Anthropic endpoints and
organization credential-to-project grants; `BYOK-003` adds Google/Gemini and
DeepSeek parity. `FRONTEND-008` exposes the organization-scoped production UI at
`/organizations/:orgId/provider-keys`. Owner/Admin read/write existing encrypted
credentials through masked metadata, explicit project grants, model restrictions,
exact lifetime external caps, test, rename/rotate, disable and crypto-shred delete.
Secret input is always masked, cleared on submit/close, and never returned or
placed in Query/Mutation cache, storage or URLs. See [UX contract and evidence](29_BYOK_ROUTING_UX.md).
Do not show a custom base URL yet: P2 `PROVIDER-010` is a
[draft operator-approved private endpoint design](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md),
not an implemented or approved self-service URL feature. Backend endpoint,
credential, pricing and tenant-safe read contracts must precede its UI.
Model restrictions,
spend displays were backend-gated until `BYOK-002`; its masked restriction and
external spend fields are now shown by these controls.
Last-used remains unavailable and must stay hidden. The existing root
`frontend/` mockup remains visual reference only; production UI belongs in
`frontend/apps` and must follow the backlog's UI/UX note.

Actions:
- test;
- disable;
- edit;
- delete.

---

## 13. Routing [P1]

`FRONTEND-008` exposes `/organizations/:orgId/routing?project=:projectId`.
The existing project settings editor configures strategy (priority/price/latency/
throughput/auto), hard model/provider allowlists and metadata-only environment.
Budgets remain on their separate existing APIs. There is no persisted project
provider priority/denylist, credential default or fallback toggle in this contract.

A request-only builder previews Managed (default), explicit BYOK, or explicitly
consented Hybrid. It can override strategy, pin provider/model, and explicitly
name up to two model alternatives. Nothing here auto-saves consent or sends
inference. BYOK remains primary; Hybrid managed capacity is conditional on safe
pre-execution rejection, wallet hold and operator controls. Missing grant, disabled
key, exhausted estimated cap or known policy conflict blocks the example.
Exact endpoint ranking, key permissions and live health/quota are not exposed:
preview is a configuration explanation, never an admission/availability guarantee.
Restrictive routing can reduce availability; unknown outcomes/partial output are
never replayed. See [implemented boundaries](29_BYOK_ROUTING_UX.md).

The [approved ROUTING-007 specification](40_ADVANCED_ROUTING_CACHE_SPEC.md) designs future Owner
weight/affinity/cache controls and Activity explanations. These APIs/data and
screens are not implemented; project policy and financial/read-model work must
precede UI. Preserve mockup direction with improved accessibility and explicitly
explain cache retention, zero-fee hit versus required admission headroom, and
the difference between a configuration preview and an actual selected route.

---

## 14. Limits

Project:
- monthly budget;
- RPM;
- concurrency.

API key:
- all-time;
- daily/weekly/monthly;
- expiry.

Progress bars:
- current use;
- threshold;
- reset date.

FRONTEND-006 supports project and key Lifetime/Daily/Weekly/Monthly budgets.
Current-window captured spend and holds are shown separately; UTC reset is
server-provided. Zero is a hard deny-new-spend cap, not unlimited. RPM/concurrency
blank fields inherit finite operator ceilings; configured and effective values
are distinguished. Budget updates require billing permission, independent of key
management. No budget deletion or new per-request cost ceiling is introduced.

---

## 15. Alerts [P1]

FRONTEND-009 exposes `/organizations/:orgId/alerts`: organization-wide rules,
destinations and the latest 100 delivery events. Project selector does not filter
these reads. Owner/Admin manage; BillingViewer can read; Developer/ReadOnly
cannot. Telegram connects through an expiring private-chat challenge, never a
submitted chat ID. Webhook secret is shown once; no fake test-send/verification.

Rules:
- low wallet;
- budget 80%;
- budget 100%;
- error-rate spike;
- provider outage remains an operator alert, not a customer rule type.

Destination:
- Telegram;
- webhook.

Rule list:
- scope;
- type;
- threshold;
- last triggered;
- status.

---

## 16. Team [P1]

Implemented by FRONTEND-007 at `/organizations/:orgId/team`:

- Search email/role/status; show joined date, active/revoked membership and
  explicit project grants. No display-name contract is invented.
- Owner/Admin invite permitted roles; invitation list shows Pending/Expired/
  Accepted/Revoked and UTC expiry, not delivery confirmation or raw tokens.
- Confirm role changes and soft membership revocation. Admin cannot manage
  Owner/Admin; the last active Owner cannot be demoted/revoked.
- Grant/remove one project at a time for Developer/ReadOnly. Role changes to
  organization-wide permissions clear grants; returning needs fresh grants.
- `/accept-invitation`: sign in with the verified invited email, paste the
  emailed one-use token and explicitly accept. Invalid/expired/replayed/
  revoked/mismatched tokens share the same error. No URL token forwarding,
  invitation cancel/resend or automatic acceptance is implemented.

Every operation rechecks server permissions. See [scope and real API evidence](27_TEAM_MEMBERSHIP.md).

---

## 17. Organization Settings

FRONTEND-009 implements payload/privacy controls separately at
`/organizations/:orgId/privacy?project=:projectId`, for current Owners only.
Explicit encrypted opt-in is bounded to one hour–seven days; opt-out confirms
irreversible project payload deletion. The private inspector requires an explicit
request UUID read, displays expiry and clears bodies on close/scope change/local
expiry. Activity/financial/audit metadata remain separate. Organization language,
timezone, legal schedules and upstream ZDR are not invented by this screen.

- name;
- language;
- timezone;
- default currency display;
- retention/logging;
- privacy;
- danger zone.

---

## 18. Admin Console

### Organizations
Account state, balance, traffic, flags.

### Providers
Provider health, credentials, capacity.

### Models
Catalog + mapping management.

### Pricing
Versioned prices.

### Payments
Reconciliation/status.

### Ledger
Read-only inspection + controlled adjustment workflow.

### Incidents
Disable provider/model/mapping/credential, pause managed admission or new top-ups.
Current P0 switches do not publish a customer-facing maintenance notice;
that presentation remains follow-up work.

### Audit
Sensitive action trail.

The P0 console lives in `frontend/apps/admin` and uses the Management API,
not the root `frontend/` design mockup's sample data. Account/organization
search, provider/mapping/credential controls, future-effective price history,
payment evidence, reconciliation cases, wallet-credit refund actions, recovery
debt/exposure, read-only ledger, incident switches, and audit are live.
Discretionary ledger adjustment, cash payout and independent live merchant-state
lookup remain later work; the Payments page labels callback evidence honestly.

---

## 19. First-use UX success criterion

A new developer should reach a successful request with minimal steps:

```text
Register
 -> create project
 -> top up or BYOK
 -> create key
 -> copy example
 -> receive first model response
```

Every extra mandatory configuration step reduces activation.

---

## 20. P0 page contract matrix

| Page | APIs/data | Dependency |
|---|---|---|
| Onboarding | auth, organization, project, payment intent, key create | Identity, Projects, Payments |
| Overview | wallet, summaries, recent activity | Billing, Usage rollups |
| Projects/keys | scoped project and key metadata, holds/caps | Projects, ApiKeys, Billing |
| Catalog | `GET /management/v1/organizations/{orgId}/projects/{projectId}/catalog/models`; published mappings, effective customer rates, capabilities and selected-model quickstart | Catalog, Billing fee policy, Projects authorization |
| Activity/detail | cursor activity, request/attempt/evidence state | Usage |
| Billing | wallet, quote, payment status/history, ledger/debt | Billing, Payments |
| Operator console | providers, prices, payments, ledger, incidents | Admin/Ops |

P0 onboarding is: register and verify email → create project → obtain UZS quote
and top up → create API key → send first managed request. Billing and request
views must show pending evidence, pending settlement, and a spending hold/debt
when present. P1/P2 controls are not displayed as active P0 options.

## 21. Customer localization and accessibility

`FRONTEND-010` covers the implemented customer pages above plus auth/recovery,
onboarding and invitation acceptance in English, Uzbek Latin and Russian.
Language/timezone preferences are browser-local presentation only. Team search
accepts localized role/status labels as well as canonical codes. User names,
secrets and executable request examples are not translated. Display UTC/local
timestamps without changing UTC filter, expiry or budget inputs; keep localized
option labels separate from canonical API values. Native dialogs have safe
initial focus, Escape dismissal and focus restoration. See [scope and evidence](31_LOCALIZATION_ACCESSIBILITY.md).
