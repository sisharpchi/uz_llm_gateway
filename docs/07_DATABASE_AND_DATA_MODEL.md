# Database and Data Model

## 1. Schema groups

Recommended PostgreSQL schemas:

```text
iam
org
gateway
catalog
billing
payment
usage
ops
audit
```

---

## 2. Core tables

### `iam.user`
- id
- email
- password_hash / external_auth_ref
- status
- created_at

### `org.organization`
- id
- name
- status
- created_at

### `org.member`
- organization_id
- account_id (FK to `iam.user.id`)
- role
- status
- created_at

### `gateway.project`
- id
- organization_id
- name
- status
- settings_json
- created_at
- archived_at nullable

`org.member` has a composite primary key of `(organization_id, account_id)`.
`gateway.project` has a foreign key to `org.organization`, and project names
are unique within their organization. Later tables that carry both project and
organization use the project scope as a tenant guard.

### `gateway.api_key`
- id
- project_id
- name
- key_prefix
- secret_fingerprint
- status
- expires_at
- all_time_limit
- recurring_limit
- recurring_period
- created_by
- created_at

Never store plaintext gateway secret.

---

## 3. Provider and catalog

### `catalog.provider`
- id
- code
- name
- status

### `catalog.model`
- id
- canonical_code
- display_name
- context_length
- max_output_tokens
- capabilities_json
- status

### `catalog.provider_model`
- id
- provider_id
- model_id
- upstream_model_code
- endpoint/base_url_ref
- status
- capabilities_override_json

### `catalog.model_price`
- id
- provider_model_id
- valid_from
- valid_to
- input_price_micro_usd_per_million
- output_price_micro_usd_per_million
- cached_input_price...
- extra_pricing_json

### `gateway.provider_credential`
- id
- organization_id nullable
- provider_id
- credential_type (`Platform`, `BYOK`)
- encrypted_secret
- kms_key_version
- status
- allowed_models_json or normalized child table
- spend_limit
- created_at

---

## 4. Billing

### `billing.wallet`
- organization_id PK
- posted_balance_micro_usd
- reserved_balance_micro_usd
- version

`version` supports optimistic concurrency if selected.

### `billing.ledger_entry`
- id
- organization_id
- type
- amount_micro_usd
- reference_type
- reference_id
- occurred_at
- metadata_json

Possible types:
- TopUp
- UsageCharge
- Refund
- AdjustmentCredit
- AdjustmentDebit
- PromotionalCredit

Reservations can be modeled separately.

### `billing.reservation`
- id
- organization_id
- project_id
- api_key_id
- request_id
- amount_micro_usd
- status
- settled_amount_micro_usd nullable
- expires_at
- created_at
- settled_at

Unique:
- request_id

---

## 5. Payments

### `payment.payment_intent`
- id
- organization_id
- provider (`Payme`, `Click`)
- amount_uzs
- status
- external_transaction_id
- idempotency_key
- fx_rate_snapshot
- credit_amount_micro_usd
- created_at
- paid_at
- canceled_at

### `payment.callback_log`
- id
- payment_intent_id nullable
- provider
- external_request_id
- request_hash
- response_code
- received_at

Use retention policy because callback payload can include sensitive data.

---

## 6. Usage

### `usage.request`
Partition candidate when traffic grows.

Fields:
- id
- organization_id
- project_id
- api_key_id
- canonical_model_id
- provider_model_id
- provider_credential_id
- started_at
- completed_at
- status
- http_status
- is_stream
- route_strategy
- fallback_count
- provider_request_id
- trace_id

### `usage.metering`
- request_id PK/FK
- input_tokens
- output_tokens
- cached_input_tokens
- reasoning_tokens nullable
- provider_cost_micro_usd
- platform_fee_micro_usd
- customer_charge_micro_usd
- price_version_id
- usage_source (`Provider`, `Estimated`, `Reconciled`)

### `usage.performance`
- request_id
- gateway_overhead_ms
- upstream_total_ms
- time_to_first_token_ms
- output_tokens_per_second

### Optional payload table

`usage.payload`

Keep separate so metadata retention can differ from prompt/response retention.

- request_id
- encrypted_request_payload
- encrypted_response_payload
- expires_at

---

## 7. Alerts

### `ops.alert_rule`
- id
- organization_id
- project_id nullable
- type
- threshold
- channel
- status

### `ops.alert_event`
- id
- rule_id
- dedupe_key
- status
- triggered_at
- delivered_at

### `ops.notification_destination`
- organization_id
- type (`Telegram`, `Webhook`, future Email)
- encrypted/config data
- status

---

## 8. Audit

### `audit.audit_event`
- id
- organization_id
- user_id
- action
- resource_type
- resource_id
- ip
- metadata_json
- occurred_at

Examples:
- api_key.created
- api_key.rotated
- byok.created
- project.archived
- ledger.adjusted
- payment.refunded

---

## 9. Indexes

Critical examples:

```text
gateway.api_key(secret_fingerprint)
usage.request(project_id, started_at desc)
usage.request(api_key_id, started_at desc)
usage.request(provider_model_id, started_at desc)
billing.reservation(request_id) UNIQUE
payment.payment_intent(provider, external_transaction_id) UNIQUE when available
audit.audit_event(organization_id, occurred_at desc)
```

---

## 10. Partitioning

Do not partition everything on day one.

Likely future partition candidates:
- `usage.request`
- `usage.performance`
- `audit.audit_event`
- callback/raw event tables

Partition by month after volume justifies it.

---

## 11. Data consistency

### Strong consistency
- wallet;
- reservation;
- settlement;
- payment credit;
- API key status during authentication.

### Eventual consistency acceptable
- dashboards;
- aggregate charts;
- provider performance scoring;
- alerts;
- exports.

---

## 12. Completion tables and constraints

Add `usage.attempt`, `usage.evidence`, `billing.settlement`,
`billing.budget_policy`, `billing.budget_bucket`, `billing.reservation_budget`,
`billing.recovery_debt`, `billing.debt_entry`, `ops.outbox`, `ops.consumer_inbox`,
and `ops.job`. A request has ordered attempts; evidence is append-only; a
reservation has at most one settlement.

Use composite tenant foreign keys where a row contains organization, project,
or key identifiers. Require unique operation identities for payment credit,
reversal, reservation, settlement, and `(consumer, event_id)`. Require
non-overlapping effective price intervals for a provider mapping.

`billing.wallet`, applicable budget buckets, and state rows are locked in a
stable operation-first, wallet-before-budget order. Runtime roles may append
ledger/audit/evidence records but not update or delete them. Keep financial
history unpartitioned; request, attempt, performance, callback, and audit
telemetry become monthly partition candidates only after measured volume.
