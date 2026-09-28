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

`BYOK-002` uses a separately versioned BYOK fee policy. Its markup basis
points represent a **platform fee on estimated provider cost**, not a managed
resale charge; fixed fee is additive. A zero policy permits requests without
wallet credit. The PostgreSQL reservation holds the wallet fee (or the larger
managed ceiling for explicit Hybrid fallback) and separately holds one
credential's conservative external provider-cost estimate. Lifetime spend
cap admission atomically checks `external_spent + external_reserved + new_hold`.
Finalization debits only the wallet platform fee for BYOK, records external
provider spend on the credential/settlement, and releases both holds in one
transaction. A rejected BYOK attempt followed by managed execution uses the
managed charge and releases the unused external hold. Unknown provider usage
retains holds through reconciliation; late verified BYOK usage becomes an
append-only external-spend adjustment, never platform exposure. The catalog
price is a cost estimate, not the customer's provider invoice; upstream
discounts, tax, and provider-side usage differences require external account
reconciliation before a cap can be marketed as a hard provider-bill limit.

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

`PAYMENT-002` records operator-transcribed merchant statement rows with source
reference, source-file SHA-256, row reference, transaction ID, normalized
status, amount and observation time. The source digest is provenance metadata,
**not** proof the uploaded values match the merchant file; live merchant
verification remains a launch gate. Rows are append-only and keyed by merchant,
source and row. A conflicting replay is rejected. Different rows for one
external transaction, missing local payments, amount/status disagreement and
unapplied reversals open deduplicated operator cases. A paid local intent with
no independent observation after 12 hours is also queued by the Worker.
Only one case per mismatch is open at a time; new evidence after closure can
open a fresh case without erasing the previous resolution.
Case closure requires recent operator MFA, a reason and an evidence reference;
it records audit and resolves the alert but never mutates the wallet. Any
financial correction must use its separately authorized ledger/payment path.

---

## 12. FX

Pick one controlled FX source/policy.

Store:
- rate value;
- rate source;
- effective timestamp.

Never retroactively change prior credit.

`ADMIN-002` publishes FX snapshots through recent-MFA operator access with a
mandatory audit reason. `observed_at` is the effective start; the next snapshot
closes that implicit half-open window. Publication must advance the global
effective time, while `payment.fx_quote` and `payment_intent` retain their
original snapshot, rate and USD credit. The source and commercial rate need
finance approval outside the repository. Managed/BYOK inference fee policies
are separately versioned by policy code. PostgreSQL excludes overlapping
half-open windows; scheduling a later version may shorten only the future
end of its predecessor, never its fee values or elapsed effective period.

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

For a recurring budget, TX-02 creates/holds the bucket for the request's UTC
admission day, Monday-based week, or calendar month. All applicable project
and key policies must pass in the same transaction. TX-05 always updates the
bucket identified by the reservation, even after a window rolls over; it does
not spend the new window for an earlier request. Installing a new policy while
matching reservations are active is rejected instead of silently omitting
those holds. Existing policy limits may be lowered below spent-plus-held to
block future admissions, without invalidating existing reservations.

For a dispatched request, expiration is a review trigger, not permission to
release money. Persist evidence before settlement. Reconcile unknown evidence
for 24 hours after its deadline; charge verified usage, release the balance, and
record unresolved exposure for the platform. Known evidence remains pending
until settled.

`BILLING-003` adds a bounded Worker sweep over expired, still-reserved holds,
including holds whose original leased reconciliation job dead-lettered. It
reuses the row-locked settlement path; an attempt that reached a provider but
has no usage evidence is recorded as `Unknown` even if its execution is already
terminal. Unknown evidence stays held until the later of its evidence deadline
and 24 hours after reservation expiry. A failed calculation/settlement or an
overdue pending result schedules one deduplicated operator alert. The alert
resolves after a committed finalization event; the operator financial-risk read
shows pending holds, exposure and recovery debt without request payloads.

On a confirmed payment reversal, recover `min(reversal, AvailableBalance)`,
preserve active reservations, record the balance as recovery debt, and block new
managed admission. Future top-ups or releases repay debt through explicit ledger
entries. Provider reversal, usage refund, and discretionary cash refund are
different operations.

`REFUND-001` permits an operator to refund a prior `Settled` inference charge
to the same organization's USD-credit wallet only. The sum of partial refunds
cannot exceed `settlement.charged_micro_usd` (the actual wallet charge, not
provider cost or uncapped price). The organization-scoped refund key is
idempotent; replay with changed terms conflicts. PostgreSQL serializes on the
wallet, writes an append-only `billing.settlement_refund` row and `Refund`
ledger counter-entry, then applies any existing recovery debt and audit in one
transaction. An active reservation remains held. Refunds do not retroactively
lower historical project/key spend buckets or send UZS to a card; cash payout
and discretionary adjustments require separate policy and tasks.

P0 payment work includes Payme `GetStatement` support and provider-specific
callback fixtures. Public CLICK protocol examples from its official integration
repository are sufficient for automated implementation acceptance. Active
merchant sandbox/live protocol verification remains a paid-launch prerequisite.

### P0 implementation and launch controls

`Payments__FeeBasisPoints` and `Payments__FixedFeeTiyin` define the local
top-up fee; no default commercial rate is assumed. Configure
`Payments__Payme__MerchantId`, `Payments__Payme__Key`,
`Payments__Click__MerchantId`, `Payments__Click__ServiceId`, and
`Payments__Click__SecretKey` through secret management. A controlled operator
must publish a `billing.fx_rate_snapshot` before quotes can be issued; quotes
reject snapshots older than 24 hours and expire after 30 minutes. Do not store
merchant secrets in `appsettings*.json` or `.env.example`.

`payment.fx_quote` and `payment.callback_log` are append-only. Each intent
copies its quote's exact UZS amount, fee, FX and USD credit; one quote and one
organization/idempotency key bind at most one intent. The provider transaction
identity is unique within merchant scope. A verified completion updates intent,
top-up ledger, wallet/debt and outbox in one PostgreSQL transaction. A verified
reversal applies the billing recovery-debt rules in one transaction. The worker
expires unbound intents; stale bound transactions become deduplicated manual
reconciliation cases with an operational alert, never an inferred zero-charge
or automatic credit. Payme `GetStatement` exposes local transaction history;
it is **not** independent provider-side settlement evidence.

Public-spec tests exercise Payme lifecycle/replay and CLICK's documented
signature field order, Prepare/Complete, invalid signature/amount and reversal.
Before accepting live money, obtain merchant credentials and sandbox fixtures,
verify Payme/CLICK callbacks and checkout links against the actual merchant
accounts, compare provider settlement reports with local intents/ledger, and
configure a trusted ingress source policy for CLICK callbacks. CLICK's public
Shop API signature does not cover every callback field (notably `error`), so
signature validation alone is insufficient to authenticate a reversal from an
untrusted network source. This is an external launch gate, not a blocker for
public-protocol implementation and automated validation.

Protocol references: [Payme CreateTransaction](https://developer.help.paycom.uz/metody-merchant-api/createtransaction/),
[GetStatement](https://developer.help.paycom.uz/metody-merchant-api/getstatement/),
[CancelTransaction](https://developer.help.paycom.uz/metody-merchant-api/canceltransaction/),
and the [official CLICK integration examples](https://github.com/click-llc/click-integration-php/blob/master/README.md).
