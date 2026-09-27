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
  payloads. Define a data-subject and breach-response owner.

## External / business prerequisites (never code-task completion)

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
