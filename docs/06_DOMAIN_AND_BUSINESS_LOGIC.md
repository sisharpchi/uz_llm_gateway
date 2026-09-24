# Domain and Business Logic

## 1. Main aggregates

### Organization
Owns:
- members;
- wallet;
- projects;
- billing policy.

### Project
Owns configuration scope:
- API keys;
- routing settings;
- project budgets;
- data/logging preferences.

### GatewayApiKey
Owns:
- secret identity;
- status;
- expiry;
- spend limit;
- permissions.

### Wallet
Represents current financial state derived from ledger/reservations.

### Payment
Represents external top-up lifecycle.

### Model
Canonical AI model identity.

### ProviderModel
A provider-specific endpoint capable of serving a canonical model.

### UsageRequest
Audit/metering representation of one inference attempt/request.

---

## 2. Important invariants

### Wallet invariants

```text
AvailableBalance >= 0
```

unless an explicit controlled credit facility exists.

```text
Available = PostedBalance - ActiveReservations
```

A managed inference request cannot be accepted when reservation would violate allowed balance/credit rules.

### Ledger invariant

Ledger rows are never edited to “fix balance”.

Corrections use compensating entries.

### Payment invariant

One external provider transaction can credit a wallet at most once.

### Settlement invariant

One reservation can transition:

```text
Reserved -> Settled
Reserved -> Released
```

not both multiple times.

### API key invariant

Disabled/expired key cannot create new gateway requests.

---

## 3. Request lifecycle state

Recommended conceptual states:

```text
Received
Authenticated
Validated
Reserved
Routed
UpstreamStarted
Streaming
Succeeded
Failed
Settled
Released
```

Do not require every transient state as a DB row update in the hot path; use a minimal persisted state model plus tracing.

---

## 4. Cost model

For token models:

```text
ProviderCost =
  input_tokens  * input_rate
+ output_tokens * output_rate
+ cached_tokens * cached_input_rate
+ provider-specific extras
```

Normalize rate into precise units, for example USD micro-dollars.

Then:

```text
CustomerInferenceCharge =
ProviderCost
+ PlatformMarkup
+ optional product-specific fees
```

Top-up/payment fee may be charged separately.

Keep these distinct:
- provider cost;
- gateway inference markup;
- payment fee;
- tax;
- promotional credit.

That makes margins auditable.

---

## 5. Currency logic

Recommended:

### External
Customer pays in UZS.

### Internal inference pricing
Keep normalized provider pricing in USD-equivalent micro units because upstream vendors usually publish USD pricing.

### Payment record
Store:

```text
paid_amount_uzs
fx_rate_snapshot
credited_usd_micro
payment_fee_uzs
provider_transaction_id
```

Never recompute old payment value using today's FX rate.

Dashboard may display:
- UZS estimated equivalent;
- base credit balance.

Product decision can later choose whether the public wallet itself is labeled UZS or USD credit.

---

## 6. Reservation logic

### Why reservation exists

Suppose:

```text
Balance = $1
50 concurrent requests
```

A simple `balance > 0` check allows overspend.

### Flow

```text
Estimate upper charge
       |
Reserve
       |
Call provider
       |
Get actual usage
       |
Capture actual
       |
Release remainder
```

If reliable max cost cannot be calculated, use:
- project/key max output;
- provider model max output;
- conservative estimate;
- configurable reservation ceiling.

---

## 7. Provider execution modes

### Managed
Use platform provider credential.

### BYOK
Use user's encrypted provider credential.

### Hybrid
Candidate order may be:

```text
1. healthy BYOK credential
2. another BYOK credential
3. managed provider capacity
```

subject to policy.

---

## 8. Failure classification

### Retry/fallback eligible
Usually:
- connection timeout;
- 429;
- selected 5xx;
- provider unavailable;
- capacity errors.

### Normally not blindly retryable
- invalid user request;
- authentication error caused by user's BYOK;
- unsupported parameter;
- context too large;
- policy rejection unless model fallback explicitly permits it.

### Mid-stream failure
Special case:
- client may already have partial output;
- some providers may still bill;
- automatic retry could duplicate content.

Therefore mid-stream retries should be conservative and policy-driven.

---

## 9. Budget hierarchy

Recommended evaluation:

```text
Organization wallet
   AND
Project budget
   AND
API key budget
   AND
Member budget (future)
```

A request is admitted only if all applicable hard gates pass.

---

## 10. Pricing versioning

Do not overwrite:

```text
gpt-x input = $X
```

Instead:

```text
PriceVersion
  valid_from
  valid_to
  input_rate
  output_rate
  cached_rate
```

Usage stores price version or enough immutable snapshot data to reproduce charge.

---

## 11. Model identity

Use canonical model:

```text
Model
  id = "gpt-x"
```

and mappings:

```text
ProviderModel
  provider = openai
  upstream_id = "gpt-x"

ProviderModel
  provider = another-cloud
  upstream_id = "openai/gpt-x"
```

This enables provider-level routing without changing the client-facing model.

---

## 12. Corrected ownership and state model

An Organization is the tenant and billing identity; it associates members,
projects, and a Wallet rather than making them one mutable aggregate. A Project
is the workload/configuration scope. `UsageRequest` is a logical customer
operation; each possible upstream execution is a separate Attempt.

Persist independent dimensions:

| Dimension | States |
|---|---|
| Execution | `Prepared`, `Dispatched`, `Succeeded`, `Failed`, `Canceled`, `OutcomeUnknown` |
| Delivery | `NotStarted`, `Partial`, `Completed`, `ClientDisconnected` |
| Financial | `Reserved`, `PendingEvidence`, `PendingSettlement`, `Settled`, `Released` |

An attempt that might have reached a provider is never automatically retried or
released solely because its lease expires. Known evidence awaits settlement;
unknown evidence is reconciled for a bounded window, then verified usage is
charged and unresolved cost is platform exposure.

Budget admission requires all applicable wallet, project, and key gates to hold
the reservation. A recovery debt records reversed credit that cannot be
recovered from `AvailableBalance`; it prevents new managed spending without
invalidating existing reservations.

Cached input and reasoning values must state whether they are subsets of input
or output before pricing; they must never be double-counted.
