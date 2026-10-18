# Production Readiness and Paid-Launch Gates

This is an evidence checklist, not a claim that the service is live. A gate is
`Passed` only when its evidence is linked in a dated release manifest
(`LAUNCH-003`) for the exact deployed revisions and environment. Automated
fixtures, local drills and commercial approvals are distinct evidence types.
The current baseline is audited in [17](17_POST_IMPLEMENTATION_AUDIT.md).

## Repository-verified gates

| Gate | Required evidence before `Passed` |
|---|---|
| Functional | On a real staging stack: register/verify, create organization/project, issue a show-once key, fund through a payment callback, invoke `/v1/chat/completions` in non-streaming and SSE modes, see one settled charge, activity and balance. Verify authorized and denied tenant paths. |
| Financial | PostgreSQL-backed `FIN-001–006`, `PAY-001–003` and `IDEM-001` pass in current CI; concurrent admission cannot overspend; ledger/holds/posted balance reconcile; unknown usage and settlement failures have durable recovery evidence; wallet refunds are idempotent and counter-posted; pricing/FX versions are recorded. |
| Security | Authorization/tenant matrix, CSRF/session, show-once secret cache policy, MFA confirmation/replay, public auth throttling/body limits, BYOK and platform-secret encryption/rotation, operator audit and payment callback signatures/ingress checks pass adversarial tests. No secrets or retained prompts in ordinary telemetry. |
| Reliability | Two Gateway instances on **different nodes** behind the edge pass at least 10 RPS for 60 seconds and 200 simultaneous streams with measured TTFT/error/cancellation/settlement outcomes. PostgreSQL, Redis, Worker, provider and node faults are exercised; fail-closed admission and recovery are observed. |
| Payment | Payme and CLICK documented protocol fixtures pass replay, invalid amount/signature, cancellation/reversal and reconciliation tests. Edge exposes only intended callback paths with TLS and bounded bodies; CLICK stays default-deny until a merchant-verified source policy is installed. **Separately**, each live merchant performs a signed top-up and reversal with provider-side evidence and matching wallet ledger. |
| Provider | OpenAI and Anthropic live managed credentials, approved terms, quota/headroom, models and fee/FX policy are recorded. Measured provider usage/cost and gateway settlement are reconciled on representative streaming, timeout and cancellation cases; no fallback after output. Gemini/DeepSeek are qualified only if offered at launch. |
| Operational | Named on-call, dashboards for admission/latency/TTFT/stream disconnects/unknown usage/holds/debt/callback failures/outbox age, alert delivery with escalation, reconciler and retry/dead-letter controls, incident runbooks, migration rollback and backup/restore drill are demonstrated. |
| Legal/business | Signed-off UZS/USD FX source/cadence, fees, tax/fiscal treatment, refund terms, privacy/retention schedule, hosting region/residency, data processing terms and incident ownership. Software tests cannot certify these decisions. |
| Release | Pinned artifacts/config and schema version, successful migrations with runtime DDL denied, smoke/rollback plan, gate evidence links, approvers and unresolved-risk register. No gate may be marked passed from the `Completed` backlog label alone. |

## Required operational validation

If payload retention controls are offered at paid launch, close the commit-
before-audit risk recorded in [SECURITY-007 §2](43_GUARDRAIL_PII_POLICY_SPEC.md#2-verified-baseline-and-concrete-gap)
with transactional audit-failure/cancellation rollback evidence (proposed
SECURITY-009); current fixtures do not supply it. The specification does not
promote or complete that correction. Optional guardrails additionally need
consent/corpus/expected-miss, no-content telemetry, retention-race, financial,
load/failure/restore and rollout evidence. Input heuristics do not prove output
safety, lawful processing or upstream ZDR; no gate passes from this draft.

Optional gateway expansion in the [approved GATEWAY-008 specification](42_GATEWAY_CAPABILITY_EXPANSION_SPEC.md)
is not a new paid-launch prerequisite and does not pass any gate. If later
offered, each exact model/mapping/operation feature profile needs protocol,
usage/cost, cancellation/failure, tenant/security and load evidence plus live
provider/commercial qualification. New modality pricing must have a proven
reservation upper bound before activation; local token fixtures are insufficient.

- Preserve request IDs across edge, Gateway, provider attempt, usage evidence,
  ledger and Worker. Alert on unknown-cost aging, failed settlement, stale
  holds, platform exposure, callback mismatch, outbox retry ceiling, provider
  health and rejected managed admission.
- Demonstrate restore from a managed PostgreSQL backup with measured RPO/RTO,
  migration compatibility and ledger reconciliation. Test Redis loss/rejoin;
  Redis is never authoritative for money. Verify a Worker crash, duplicate
  delivery and slow-job lease renewal or safe re-claim.
- Use production-like seeded model prices, fees, FX and quotas for load/fault
  runs. Record throughput, p95/p99 latency, TTFT, concurrent streams,
  cancellation cleanup, wallet exposure, and error classification. A single
  local Compose host does not prove high availability.
- Set payload retention separately from financial/audit retention. Test
  expiry, opt-out deletion, log redaction and backup handling for deleted
  payloads. Define a data-subject and breach-response owner. For `PRIVACY-002`,
  attach the signed schedule reference and approved diagnostic/operational
  durations, evidence that Worker uses that config, and the quarantined
  restore/scrub drill from `deploy/PRIVACY-002-VALIDATION.md`. Audit and
  financial records remain on explicit legal hold pending an approved archive
  procedure; do not represent the 30/90-day defaults as legal approval.
- Attach the `SECURITY-006` staging rotation drill: both nodes read old/new
  provider and BYOK ciphertext, Management/Worker read old/new Data Protection
  payloads, provider version inventory reconciles to per-row audit events,
  started/completed key-ring audit pairs match XML, and rollback retains every
  decryptor. The local fixtures in `deploy/SECURITY-006-VALIDATION.md` do not
  replace this deployment evidence.

## External / business prerequisites (never code-task completion)

If discretionary wallet credits are exposed in the release, include the
`REFUND-002` independent-maker/checker, replay/concurrency, audit rollback and
hold/debt evidence in its Financial/Security gate review. The approved software
policy is credit-only with different recent-MFA approval for every amount;
it does not replace fiscal/legal approvals or authorize cash payouts/debits.
See [policy and repository evidence](37_WALLET_ADJUSTMENTS.md).

| Prerequisite | Owner evidence to attach before paid launch |
|---|---|
| Provider commercial access | Agreements, approved use/resale terms, production credentials, model availability, quotas and pricing evidence for each offered provider. |
| Payme and CLICK onboarding | Merchant IDs/secrets, callback URLs and any provider-specific allowlist/source requirements, provider sandbox/live acceptance records, settlement statements and chargeback contacts. Public protocol fixtures do not substitute for this. |
| FX, fees and fiscal policy | Named FX source and fallback, publication cadence, markup/tax/fiscal rules, rounding and refund conversion policy approved by finance/legal. |
| Infrastructure | Hosting region/data-residency decision, managed PostgreSQL HA/PITR and measured restore guarantee, managed Redis behavior, certificates/DNS, secret store/rotation ownership, SMTP and optional Telegram delivery. |
| Operations and law | Named incident/on-call owners, alert channels, retention/legal schedule, privacy and customer terms, refund approvals and payment reconciliation owner. |

If any mandatory external evidence is unavailable, mark its gate `Blocked`
and keep paid traffic disabled. A green repository CI run is not permission to
open managed public admission.

Local Aspire orchestration (`DEVEX-001`) is a developer convenience, not
evidence for the two-node reliability, live payment, or provider gates.

Enterprise SAML/SCIM is not required for the initial paid MVP and is not
implemented. [ENTERPRISE-001](38_ENTERPRISE_IDENTITY_SPEC.md) is an approved
specification, not a passed Security/Provider gate. If feature execution is later approved,
its enterprise activation additionally requires tenant-scoped authorization,
deprovision/key-race and financial-finalization fixtures, reference Entra
provisioning/login/cert-token rotation evidence, independent security review,
local recovery ownership and a quarantine restore/offboarding drill. Live IdP
onboarding is an external prerequisite for activation, not for writing its
specification or synthetic fixtures. None of these enterprise checks replaces
OPS-006/007 or existing merchant/provider/legal launch evidence.

Optional ZDR/regional processing is also not required for initial paid MVP and
is not implemented. [PRIVACY-003](39_PRIVACY_ELIGIBILITY_SPEC.md) is an approved
specification, not a passed privacy, Provider or Legal gate or feature-execution
approval.
Any future activation additionally needs scoped enforcement/content-sink and
revocation/restore fixtures, verified account/model/operation/endpoint evidence,
current provider exceptions, region qualification and named review/incident
owners. Public documentation and green CI cannot certify a real credential;
whole-platform residency and erasure of historical backups are not promised.
Absent evidence, strict routes stay disabled; the existing launch gates remain.

Additional project weights, affinity and gateway response caching in the
[approved ROUTING-007 specification](40_ADVANCED_ROUTING_CACHE_SPEC.md) are optional future work,
not implemented/qualified features. Existing price/performance routing remains
implemented. Fee/retention design approval does not authorize feature execution.
Any new cache activation still needs separately approved/promoted feature tasks,
tenant/epoch/dispatch/recovery tests, isolated encrypted body-store qualification
and the specified load/content-sink/rollback evidence. Cache hits cannot stand
in for provider throughput, live usage evidence or the 200-stream gate. No
current launch gate is passed by this specification or its unexecuted benchmark plan.

`LAUNCH-002` adds a disposable real-service acceptance stack to CI (see
`deploy/LAUNCH-002-VALIDATION.md`). It proves the repository vertical flow with
synthetic merchant/provider credentials, but does not replace the real staging
and external evidence required by the Functional, Payment and Provider gates.

Custom suppliers are optional P2 work, not required for initial paid MVP.
[PROVIDER-010](41_CUSTOM_PROVIDER_ENDPOINT_SPEC.md) is an approved threat/contract
specification, not feature-execution approval, implemented endpoint security or
provider qualification. Activation
would require pinned-transport/tenant/financial/rotation/fault fixtures, actual
egress/TLS and domain-control evidence, supplier commercial/data terms, model/
usage/price qualification and incident ownership. A fixed official verifier,
global BaseUrl override or synthetic public-address fixture is not this evidence.
Never waive existing launch gates or imply custom invoices are verified by CI.
