# Database and Data Model

The [approved ENTERPRISE-002 §7](45_INVOICE_SLA_REPORTING_SPEC.md#7-ownership-persistence-and-transaction-boundaries)
proposes immutable statement/source/revision, operational SLI coverage/report
and separately approved fiscal/claim evidence. Snapshot row membership is
explicit, not MAX(UUID)/timestamp as a commit watermark. No such tables or
migrations are created here; financial ledger/reservations remain authoritative.

The [approved DX-004 §6](44_MANAGEMENT_AUTOMATION_API_SPEC.md#6-ownership-persistence-and-transaction-fences)
proposes Identity-owned HMAC-only automation credentials, tenant-bound grants,
append-only safe command receipts and account/member/org invalidation epochs.
These tables/columns do not exist yet. Synchronous epoch fences and atomic
credential/receipt/audit commit are future implementation requirements, not
Redis financial state, a new database or permission to create migrations now.

The [approved SECURITY-007 §8](43_GUARDRAIL_PII_POLICY_SPEC.md#8-ownership-persistence-and-ux)
proposes tenant-bound guardrail policy/version/receipt and expiring metadata-only
decision records, plus durable request capture prohibition. No migration is
created here. Consent/configuration/audit must share a transaction; diagnostic
matches must not become an indefinite payload archive under financial LegalHold.

Proposed operation-feature profiles and bounded request metadata are described
in [approved GATEWAY-008 §4/8](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md). This specification adds
no schema today. Any approved implementation must preserve immutable model/
mapping/version references and tenant-filtered reads, using the existing
migrator; no new tool/conversation or financial-state store is proposed.

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

P1 `org.invitation` stores organization, normalized email, non-owner role,
inviter, SHA-256 token hash, expiry and accepted/revoked timestamps. A partial
unique index allows at most one pending invitation per organization/email;
the token hash is globally unique. `org.project_grant` has composite FKs to
`org.member(organization_id, account_id)` and
`gateway.project(organization_id, id)` to prevent cross-tenant grants. Revoking
membership deletes its grants; role changes to a non-project-scoped role also
clear grants. Member role/status checks constrain stored values.

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

`PROJECTS-002` stores the typed routing/environment/allowlist group under
`settings_json.defaults`, preserving sibling JSON groups by atomic JSONB update.
No new financial state or schema migration; legacy `{}` inherits unchanged defaults.
See [project defaults](25_PROJECT_DEFAULTS.md).

### `gateway.api_key`

`LIMITS-002` adds nullable integer `requests_per_minute` and `concurrency_limit`
on both `gateway.project` and `gateway.api_key`. NULL inherits bounded operator
defaults. CHECK constraints require RPM 1–1,000,000 and concurrency 1–10,000 when
present. Existing PK/FKs support the joined tenant-scoped admission read; no
additional table/index is needed. Whole-policy UPDATE and audit commit together.
Down refuses to discard overrides. See [tenant limits](24_TENANT_LIMITS.md).

- id
- project_id
- name
- key_prefix
- secret_fingerprint
- fingerprint_key_version (positive integer; legacy rows backfill to 1)
- status
- expires_at
- all_time_limit
- recurring_limit
- recurring_period
- created_by
- created_at

Never store plaintext gateway secret.

`APIKEYS-004` adds nullable PostgreSQL `text[]` columns `allowed_models`,
`denied_models`, `allowed_providers`, `denied_providers` on `gateway.api_key`.
Legacy rows remain unrestricted (NULL); an empty allow array denies all.
One UPDATE replaces the whole policy with its audit in the same transaction.
Authentication uses untracked authoritative reads, not a Redis permission
cache. No new table/index/FK is required: lookup is by existing key ID/prefix;
catalog codes may be preconfigured before publication. Secret rotation updates
only secret-generation fields. Downgrade refuses to discard existing restrictions.

The fingerprint version selects the HMAC key used to verify that row. The
active version is used for new keys and secret rotation; retaining older HMAC
keys permits staged verification without recovering customer secrets.

`gateway.api_key.generation` identifies the active secret generation. The
append-only identity/history table `gateway.api_key_generation` records each
generation's unique public prefix, activation and revocation timestamps. On
rotation, the key row's fingerprint/prefix/generation changes in the same
transaction that closes the old generation, inserts the new generation and
writes audit. Existing usage, budget and request FKs retain the same API-key ID.
The migration backfills generation 1 for pre-existing keys; downgrade refuses
keys with rotated history.

---

## 3. Provider and catalog

### `catalog.provider`
- id
- code
- name
- status

### `catalog.provider_data_policy`

`CATALOG-003` adds separate append-only `catalog.provider_data_policy` versions:
`id`, `provider_id` (FK to provider), retention/training/residency classifications,
`regions` (bounded text array), `scope`, `conditions`, `source_url`,
`source_observed_at`, `published_at`, and `published_by_account_id` (FK to user).
Unique `(provider_id, source_observed_at)` supports the newest-observation read;
publication locks the provider row and requires observation time to advance.
The publication and audit share one transaction. UPDATE/DELETE are rejected;
downgrade refuses populated evidence. No provider-policy facts are backfilled.
See [migration and disclosure semantics](35_PROVIDER_DATA_POLICIES.md).

### `catalog.model`

`GATEWAY-007`: embedding-only models use `capabilities_json:["Embeddings"]`
and `max_output_tokens:0` with positive per-input context. The versioned
`AddEmbeddingModelLimits` constraint permits zero only for that capability set
and preserves all legacy positive-output rows; no price/history is rewritten.
Embedding mappings require input-only effective prices (output zero, cached null,
no extra dimensions). [Migration and accounting boundary](34_EMBEDDINGS_API.md).

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

Custom endpoint registry/version/qualification, private catalog ownership and
immutable credential binding are **not implemented** by these generic columns.
`EndpointReference` is not currently resolved by adapters. See the
[approved PROVIDER-010 persistence design](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md#7-transactions-ownership-and-race-boundaries);
it needs separate approved feature tasks and explicit Migrator changes. Do not
put tenant URLs into the existing mapping field and infer secure execution.

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

### Discretionary credit evidence (`REFUND-002`)

`billing.wallet_adjustment` owns immutable organization, maker account,
positive micro-USD amount, normalized key/reason and creation timestamp.
`(organization_id, adjustment_key)` is unique. `billing.wallet_adjustment_approval`
owns the checker, approval reason/time and unique request ID; a composite FK
binds request/organization/maker and a CHECK rejects maker = checker.
Both account references restrict deletion. UPDATE/DELETE triggers and a
populated-history downgrade guard protect both tables. Status is derived from
approval existence; pending requests neither reserve nor credit money.

Wallet-first row locking serializes request-key creation and approval with
other financial operations. Approval, `AdjustmentCredit` ledger (reference
`wallet_adjustment`, request ID), existing debt recovery and audit use the same
PostgreSQL transaction. Unique ledger reference and approval request constraints
backstop replay; Redis and external transports are not involved.

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

`USAGE-004` adds derived `usage.hourly_rollup` (organization/UTC hour/project/
key/requested model/selected provider/execution state/stream grain),
`usage.rollup_bucket` (publication timestamp) and `usage.rollup_queue`
(request-keyed transactional invalidations). PostgreSQL view
`usage.rollup_source` reads verified evidence and settled gross/refund amounts.
Queue indexes cover oldest-first batching and tenant/hour backlog; the hourly
composite PK serves tenant/time reads. Empty provider UUID is an unrouted
dimension, not a provider FK. Source request FKs remain the tenant authority;
rollup rows reference their tenant/hour bucket. Full-hour publication and queue
acknowledgement share one transaction; no financial row is mutated. These are
rebuildable read models, not event sourcing or new financial history.
See [publication, backfill and freshness](21_USAGE_ROLLUPS.md).

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

`PRIVACY-001` implements this as a one-to-one, tenant-bound row with separate
wrapped data keys and key-version fields for request and response. The FK uses
`(request_id, organization_id, project_id)` so a payload cannot be attached to
another tenant's request. An expiry index supports bounded Worker deletion.
`usage.payload_retention_policy` is keyed by organization/project and stores
the explicit opt-in, 60–10080 minute lifetime, and updater metadata. No row
means retention disabled. Payload deletion does not remove `usage.request`,
usage evidence, wallet entries, or audit history.

---

## 7. Alerts

### `ops.alert_rule`
- `id`, `organization_id`, optional `project_id`, optional `budget_policy_id`,
  `destination_id`, `type`, `threshold`, `enabled`, `armed`, `episode`,
  `last_window_start`, `last_triggered_at`, `next_evaluation_at`.
- Types are `LowBalance` (micro-USD available wallet), `BudgetWarning` (basis
  points of captured plus reserved current-window budget), and `ErrorSpike`
  (basis points of completed 5-minute requests, minimum 20 samples).
- Composite tenant FKs bind project, budget policy, and destination to the
  same organization. Worker locks due rules with `SKIP LOCKED`.

### `ops.alert_event`
- `id`, `rule_id`, `episode`, `observed_value`, `status`, `triggered_at`,
  `delivered_at`. Unique `(rule_id, episode)` suppresses persistent breaches;
  a recovered metric or new budget window rearms a rule.

### `ops.notification_destination`
- One destination per organization/type: Telegram stores AES-GCM
  `encrypted_chat_id` and `key_version`; Webhook stores a validated HTTPS
  `endpoint_url`, AES-GCM `encrypted_webhook_secret`, and
  `webhook_key_version`. A type check makes those column sets mutually
  exclusive. Authenticated data binds organization, destination, and key
  version. Telegram is `Verified` only after private-chat proof; a webhook is
  `Active` after URL/DNS validation, without implying domain ownership.
  Tenant-scoped rules reference destinations through a composite FK.
- `ops.telegram_link_challenge` stores only the SHA-256 hash of a 10-minute,
  account-bound one-time `/start` token; expired challenges are pruned by Worker.
  Future Email destinations belong to later tasks.

---

## 8. Audit

### `audit.audit_event`
- id
- organization_id (nullable only for global operator actions)
- account_id (FK to `iam.user.id`)
- action
- resource_type
- resource_id
- ip
- metadata_json
- occurred_at

Audit rows are append-only: the runtime database role cannot update or delete
them. Tenant actions retain an organization FK; global operator actions have
`organization_id = NULL` and retain the actor FK. The descending
`(organization_id, occurred_at)` index supports scoped investigation. The
`ops.platform_control` table holds authoritative `ManagedTraffic` and `TopUps`
switches, seeded enabled by migration. A missing row or database failure fails
new admission closed; neither switch cancels in-flight settlement or callbacks.

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

`ops.outbox` and `ops.job` use `lease_owner`, `lease_expires_at`, and
`attempt_count` as a fenced claim identity. Renew/complete/fail updates require
the same owner and attempt before lease expiry. Claims require
`attempt_count < max_attempts`; an expired final attempt transitions to
`dead_lettered_at` rather than being reclaimed indefinitely. Partial indexes
on exhausted active leases keep that sweep bounded. `ops.consumer_inbox`
deduplicates committed consumer effects independently of lease retries.
`ops.operational_alert.notification_event_id` identifies the unique outbox
event sent to the on-call channel; nullable legacy alerts remain readable.
`notified_at` and the consumer inbox are committed together after transport
success. A dead-lettered notification remains visible through the operator
alert status and correlated dead-letter metadata. Neither column proves SMTP
exactly-once delivery across a crash after remote acceptance.

Use composite tenant foreign keys where a row contains organization, project,
or key identifiers. Require unique operation identities for payment credit,
reversal, reservation, settlement, and `(consumer, event_id)`. Require
non-overlapping effective price intervals for a provider mapping.

`billing.wallet`, applicable budget buckets, and state rows are locked in a
stable operation-first, wallet-before-budget order. Runtime roles may append
ledger/audit/evidence records but not update or delete them. Keep financial
history unpartitioned; request, attempt, performance, callback, and audit
telemetry become monthly partition candidates only after measured volume.

Recurring budgets use `billing.budget_policy.period` (`Lifetime`, `Daily`,
`Weekly`, `Monthly`) and `billing.budget_bucket(policy_id, window_start)` as a
composite key. The UTC window is half-open; weeks begin Monday. Each
`billing.reservation_budget` stores the admission-time `window_start` and has
a composite FK to that exact bucket. Settlement never moves an old hold into
the then-current window. A populated recurring history cannot be downgraded
to the former single-bucket schema.
