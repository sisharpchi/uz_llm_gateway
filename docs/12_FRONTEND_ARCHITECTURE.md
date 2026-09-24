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

Do not hard-code long UI text into components.

Formatting:
- UZS;
- USD;
- local time / UTC toggle;
- token/count abbreviations.

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
