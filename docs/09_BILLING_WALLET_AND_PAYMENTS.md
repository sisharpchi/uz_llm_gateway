# Billing, Wallet and Payments

## 1. Billing goals

- prevent overspend;
- keep every charge explainable;
- survive duplicate callbacks;
- support UZS payments;
- preserve upstream USD cost;
- make refunds/adjustments auditable.

---

## 2. Wallet model

Recommended conceptual values:

```text
PostedBalance
ReservedBalance
AvailableBalance = PostedBalance - ReservedBalance
```

Inference admission uses `AvailableBalance`.

Do not implement only:

```text
user.Balance -= cost;
```

without ledger/reservation history.

---

## 3. Ledger

Ledger is append-only.

Example:

| Event | Amount |
|---|---:|
| Top-up | +10.00 |
| Usage charge | -0.063 |
| Refund | +1.00 |
| Manual debit adjustment | -0.50 |

Reservation is temporary state and may be separate from posted ledger.

---

## 4. Inference billing sequence

```text
1. Resolve price
2. Estimate reservation
3. Atomically verify available balance
4. Create reservation
5. Send provider request
6. Collect authoritative/estimated usage
7. Calculate actual provider cost
8. Calculate platform charge
9. Settle reservation
10. Release unused amount
11. Persist usage snapshot
```

Settlement must be idempotent by `request_id` or `reservation_id`.

---

## 5. Reservation amount

Potential formula:

```text
max_estimated =
  estimated_input_cost
+ max_allowed_output_cost
+ safety_margin
```

For huge context/output values, cap by:
- API key max request cost;
- project policy;
- model/provider max.

Future feature:
`max_cost` request option.

---

## 6. Managed vs BYOK billing

### Managed
Customer charge affects UZLLM wallet.

### BYOK
Provider cost is paid by customer's provider account.

UZLLM may:
- charge no fee;
- charge platform fee;
- include BYOK in subscription.

Architecture must support a configurable billing policy.

Do not entangle routing adapter code with commercial pricing policy.

---

## 7. UZS top-up

Payment intent:

```text
Organization
Amount UZS
Provider
Status
FX snapshot
Expected credit
```

After confirmed payment:

```text
Payment -> Completed
Ledger -> TopUp
Wallet -> PostedBalance increased
```

in a single transaction where possible.

---

## 8. Payme

Design for current Merchant API lifecycle such as:
- CheckPerformTransaction
- CreateTransaction
- PerformTransaction
- CancelTransaction
- CheckTransaction

Requirements:
- validate merchant authentication;
- validate amount/order/payment state;
- duplicate calls return consistent result;
- external transaction ID uniqueness;
- cancellation creates appropriate compensating behavior;
- provider callback request/response trace for support.

---

## 9. CLICK

CLICK integration should isolate provider-specific `Prepare` / `Complete` or Merchant API behavior behind `IPaymentProvider`.

Example:

```csharp
public interface IPaymentProvider
{
    Task<PaymentInitResult> CreateAsync(...);
    Task<PaymentCallbackResult> HandleCallbackAsync(...);
    Task<PaymentStatusResult> GetStatusAsync(...);
}
```

Provider callbacks never directly manipulate arbitrary wallet fields; they call Billing application service.

---

## 10. Payment state machine

Recommended normalized state:

```text
Pending
Created
Paid
Canceled
Failed
Expired
Refunded
```

External provider states map into these normalized values.

---

## 11. Reconciliation

Background reconciliation should detect:
- callback missing but provider says paid;
- local says paid but credit missing;
- canceled/paid disagreement;
- duplicate external transaction IDs.

Financial incident => alert + manual review queue.

---

## 12. FX

Pick one controlled FX source/policy.

Store:
- rate value;
- rate source;
- effective timestamp.

Never retroactively change prior credit.

---

## 13. Revenue reporting

Separate:
- gross top-up;
- inference provider cost;
- platform markup;
- payment processing cost;
- promotional credits;
- refunds.

This allows actual gross margin calculation per:
- organization;
- model;
- provider;
- period.

---

## 14. Fraud/abuse controls [P1]

- payment velocity limits;
- suspicious account flags;
- top-up limits for new accounts;
- API consumption velocity anomaly;
- manual hold if necessary;
- IP/device/risk signals only when justified and privacy-compliant.

---

## 15. Normative transaction and recovery rules

All wallet, budget, payment, and settlement values use integer USD micro-units;
UZS principal uses integer tiyin. FX and rate calculations use fixed precision,
and historical values are never recomputed. Customer charge is rounded once at
the request boundary and cannot exceed its reservation; excess provider cost is
recorded as platform exposure.

| Transaction | Atomic effect |
|---|---|
| TX-02 | Idempotency claim, request, wallet/key/project holds, reservation |
| TX-04 | Usage evidence and durable finalization work |
| TX-05 | One ledger debit, wallet/budget update, settlement, full hold release |
| TX-06 | Verified payment state, one top-up credit, debt recovery, audit/outbox |
| TX-07 | Payment reversal, available-credit recovery, debt, spending hold |

TX-02 locks operation identity, wallet, then budget rows; no provider call is
made in the transaction. TX-05 captures and releases in one transaction; there
is no independent “release remainder” retry. A lost commit acknowledgement is
recovered by reading the unique operation identity before retrying.

For a dispatched request, expiration is a review trigger, not permission to
release money. Persist evidence before settlement. Reconcile unknown evidence
for 24 hours after its deadline; charge verified usage, release the balance, and
record unresolved exposure for the platform. Known evidence remains pending
until settled.

On a confirmed payment reversal, recover `min(reversal, AvailableBalance)`,
preserve active reservations, record the balance as recovery debt, and block new
managed admission. Future top-ups or releases repay debt through explicit ledger
entries. Provider reversal, usage refund, and discretionary cash refund are
different operations.

P0 payment work includes Payme `GetStatement` support and provider-specific
callback fixtures. CLICK implementation is accepted only against active merchant
protocol/signature fixtures supplied during merchant onboarding.
