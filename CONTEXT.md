# UZLLM Financial Language

The billing context distinguishes temporary spending capacity from posted
credit and records who bears an uncertain or excess provider charge.

## Language

**Posted balance**:
The organization's credit balance after immutable ledger entries, before
subtracting active reservations.
_Avoid_: Available balance

**Reservation**:
A temporary hold against an organization's posted credit and every applicable
hard budget for one logical inference request.
_Avoid_: Charge, debit

**Settlement**:
The one terminal accounting result for a reservation, capturing no more than
its hold and releasing the remainder.
_Avoid_: Payment, usage evidence

**Recovery debt**:
Reversed credit that could not be recovered from available credit while active
reservations remained protected.
_Avoid_: Negative wallet balance

**Platform exposure**:
Provider cost that cannot be charged to the customer under the request's
reservation or an unresolved-usage outcome.
_Avoid_: Customer debt
