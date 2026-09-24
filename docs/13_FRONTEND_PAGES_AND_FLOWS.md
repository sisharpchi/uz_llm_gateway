# Frontend Pages and User Flows

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
- delete.

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
- custom base URL where supported;
- allow models;
- spend limit.

Actions:
- test;
- disable;
- edit;
- delete.

---

## 13. Routing [P1]

Project defaults:

```text
Routing strategy:
( ) Auto
( ) Cheapest
( ) Lowest latency
( ) Highest throughput
```

Controls:
- provider allowlist;
- provider denylist;
- provider priority;
- fallback enabled;
- fallback models.

Show warning that restrictive routing can reduce availability.

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

---

## 15. Alerts [P1]

Rules:
- low wallet;
- budget 80%;
- budget 100%;
- error-rate spike;
- provider outage.

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

Members:
- name/email;
- role;
- project access;
- status.

Actions:
- invite;
- change role;
- remove.

---

## 17. Organization Settings

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
Disable provider/model, maintenance notice.

### Audit
Sensitive action trail.

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
| Catalog | models, mappings, capability/pricing summary | Catalog |
| Activity/detail | cursor activity, request/attempt/evidence state | Usage |
| Billing | wallet, quote, payment status/history, ledger/debt | Billing, Payments |
| Operator console | providers, prices, payments, ledger, incidents | Admin/Ops |

P0 onboarding is: register and verify email → create project → obtain UZS quote
and top up → create API key → send first managed request. Billing and request
views must show pending evidence, pending settlement, and a spending hold/debt
when present. P1/P2 controls are not displayed as active P0 options.
