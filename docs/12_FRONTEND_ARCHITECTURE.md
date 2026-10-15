# Frontend Architecture

## 1. Recommended stack

Recommended default:

- React
- TypeScript
- Vite
- TanStack Query
- React Router if SPA
- React Hook Form + Zod
- TanStack Table
- a chart library suitable for time-series analytics
- component system such as shadcn/ui/Radix-based approach
- i18n ready from day one

React/TypeScript/Vite is the accepted implementation baseline for this MVP.

---

## 2. Frontend applications

Recommended to keep two logical areas:

### Customer dashboard

```text
app.example.uz
```

### Admin/operator console

```text
admin.example.uz
```

They may share a monorepo/design system.

---

## 3. Suggested monorepo

```text
frontend/
  apps/
    dashboard/
    admin/

  packages/
    ui/
    api-client/
    auth/
    charts/
    i18n/
    types/
    config/
```

---

## 4. Route structure

```text
/login
/register

/:orgId/
  overview
  projects
  billing
  team
  settings

/:orgId/projects/:projectId/
  overview
  api-keys
  models
  provider-keys
  activity
  analytics
  routing
  limits
  alerts
  settings
```

Admin:

```text
/admin/
  organizations
  users
  providers
  models
  pricing
  payments
  ledger
  incidents
  audit
```

The current customer dashboard uses `/organizations/:orgId/models?project=:projectId`
for the catalog, matching its shared project selector. The selected project's
ID is sent to the scoped Management catalog read endpoint; no project or
provider data is inferred from the route alone.

---

## 5. State strategy

### Server state
Use TanStack Query:
- projects;
- usage;
- payments;
- keys metadata;
- model catalog.

### Local UI state
Use component state or lightweight store for:
- sidebar;
- filters;
- temporary wizard state.

Avoid putting all API data into one global Redux-like store without a reason.

---

## 6. API client

Generate or maintain typed management API client from OpenAPI.

Inference playground may use the public Gateway API through backend-safe patterns.

Never expose privileged management secrets in the browser.

---

## 7. Security in UI

- Never redisplay full API key after create/rotate.
- Copy key inside one-time modal.
- Warn user that closing loses plaintext.
- Mask BYOK credentials.
- Re-auth / confirmation for sensitive operations if needed.
- Do not store gateway API key plaintext in localStorage as part of dashboard operation.

---

## 8. Multi-tenant context

Global context bar:

```text
Organization selector
Project selector
Date range
```

Every project page should make current scope visually obvious to prevent editing production while thinking user is in staging.

---

## 9. Analytics UI principles

Dashboard must answer quickly:

1. How much balance is left?
2. How much did we spend?
3. Which project/key/model caused it?
4. Are requests failing?
5. Which provider is slow/unhealthy?

Use drill-down:

```text
Overview chart
  -> click model
  -> filtered Activity
  -> individual Request Detail
```

---

## 10. i18n

Design for:
- Uzbek Latin;
- Russian;
- English.

Use catalog-backed source-message keys, not untranslated long UI copy.
`FRONTEND-010` implements customer/auth/onboarding/invitation flows with browser-local
language/timezone preferences; see [scope and verification](31_LOCALIZATION_ACCESSIBILITY.md).
Raw names, secrets, model/provider identifiers and API/CSV/request examples are
not translated. Localized option labels retain explicit canonical wire values.

Formatting:
- UZS;
- USD;
- local time / UTC toggle;
- token/count abbreviations.

Display money from exact micro-unit strings/BigInt; preserve existing precision
guards and canonical input formats. The timezone toggle changes displayed
timestamps only, never budget windows, UTC filters, expiry inputs or FX.
Use an explicit Latin Uzbek formatting fallback where browser Intl data is
incomplete. Language/timezone are not server-side financial or security policy.

---

## 11. Error UX

Management API errors:
- inline validation;
- toast for transient action failure;
- dedicated empty/error states.

Gateway activity:
- show normalized error category;
- show provider-safe message;
- show request ID;
- avoid leaking upstream secret details.

---

## 12. Realtime/near realtime

MVP can poll:
- balance;
- recent activity;
- status.

Later:
- SSE/WebSocket for live activity.

Do not add realtime infrastructure before necessary.

---

## 13. Design system entities

Reusable components:
- StatCard
- CostBadge
- ProviderBadge
- ModelBadge
- StatusBadge
- UsageChart
- SpendProgress
- KeyMaskedValue
- CopySecretDialog
- RequestStatus
- EmptyState
- ErrorState
- ConfirmationDialog
- DataTable
- FilterBar
- DateRangePicker

---

## 14. Implementation baseline

The selected implementation baseline is React, TypeScript, Vite, npm
workspaces, TanStack Query, React Router, and a generated Management API client.
Management authentication uses secure browser sessions; browser code never
stores provider or gateway secrets. Query keys include organization/project
scope. Monetary micro-unit values are displayed from decimal strings, never
JavaScript floating point.

Customer and operator applications remain separate under `frontend/apps`.
P0 exposes only the managed-credit flow; BYOK, advanced routing, alerts, team,
and payload retention controls remain hidden until their backend tasks complete.
FOUNDATION-002 will narrow the current `packages` ignore rule before workspace
source is added.

The root `frontend/` mock prototype remains untouched. The production dashboard
and operator application are npm workspaces under `frontend/apps/`, sharing
the DX-002 OpenAPI-derived wire types and typed transport in
`frontend/packages/api-client/`. Operator controls are a separate application.
Build and browser test commands are in `frontend/apps/README.md`.

FRONTEND-006 adds lifecycle controls at
`/organizations/:orgId/projects?project=:projectId` and
`/organizations/:orgId/keys?project=:projectId`. Settings, permissions, request
limits and financial budgets use existing separate APIs, not a combined atomic
save. Queries include organization/project/key scope. Scoped components unmount
on selection changes; late commands cannot reveal an old scope's secret or
write its feedback into the new scope. Creation/rotation use imperative calls
and ephemeral local secret state, never Query/Mutation caches or browser storage.
Budget Int64 JSON numbers outside JavaScript's safe integer range are explicitly
refused, never rounded. See [lifecycle evidence and boundaries](26_CUSTOMER_LIFECYCLE.md).

FRONTEND-007 exposes the organization-scoped Team page and standalone
`/accept-invitation` page. Team reads include account and organization in their
query keys; current Owner/Admin membership gates metadata and controls. Scoped
unmounts discard drafts/feedback, and committed commands refresh only their
scope. Invitation tokens stay in masked ephemeral form state, never Query/
Mutation caches, browser storage or sign-in URLs. Commands remain independently
authorized by Management; UI restrictions are not security boundaries. See
[team scope and validation](27_TEAM_MEMBERSHIP.md).

FRONTEND-008 adds Provider keys and Routing routes. BYOK metadata queries include
account and organization; current failed reads mask cached controls/data. Routing
adds project scope, reuses the project settings editor and never persists execution
mode/credential/fallback consent. Secret commands are imperative and ephemeral,
not TanStack mutations; submitted inputs clear before awaiting I/O. External caps
use exact BigInt/string micro-units up to Int64. See [scope and evidence](29_BYOK_ROUTING_UX.md).

FRONTEND-009 adds organization-scoped Alerts and project-scoped Privacy routes.
Alert metadata queries include account/organization; current read failures hide
cached controls. Role metadata gates mutations; every server operation still
authorizes independently. Payload bodies, one-time webhook secrets and Telegram
links use imperative calls and ephemeral dialog state, never Query/Mutation
caches, storage or dashboard URL parameters (Telegram's external start link
necessarily carries its one-use challenge). Payload reads are cancelled on unmount and
cleared at local expiry; the backend separately rejects expired reads. Only
Owners can access retention. See [scope and evidence](30_ALERTS_PRIVACY_UX.md).
