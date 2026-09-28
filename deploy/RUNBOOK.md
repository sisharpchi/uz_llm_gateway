# Production deployment and recovery

`deploy/compose.production.yml` is a two-node reference: run `gateway-a`,
`management-a`, `worker-a`, and `edge` on node A; run the `-b` services and
`edge` on node B. A redundant external L7 load balancer terminates or passes
TLS to both edges. It checks `/edge/health`; separately poll each private
`https://<node>:<gateway-or-management-port>/health/ready` and withdraw an
unready node. A one-host Compose run is a functional drill, **not HA**.

## Prerequisites and secrets

- Managed PostgreSQL with HA, synchronous durability appropriate to financial
  commits, WAL/PITR retention and tested backups; separate migrator and runtime
  roles. Redis must require TLS/auth and allow `INFO SERVER` to the Gateway
  principal for fail-closed restart detection. Confirm provider service RPO/RTO
  contracts before claiming the target RPO ≤5 minutes / RTO ≤30 minutes.
- A private, shared, durable key-ring filesystem mounted at
  `UZLLM_KEYRING_DIR` on both nodes, writable only by app UID 1654. Back it up
  with the database. Supply a PFX with private key for encryption; preserve old
  decryption certs during rotation until all protected MFA records and pending
  identity-email outbox payloads have migrated. Worker and Management must use
  the same ring and application name.
- Keep two separately enrolled operators available. A lost TOTP device can be
  reset only by a different operator who has verified MFA within 15 minutes:
  `POST /management/v1/admin/operators/{accountId}/mfa/reset` with CSRF proof
  and an incident reason. The target's sessions are revoked and re-enrollment
  needs their password. There is no self-service or unaudited database reset;
  agree an out-of-band, two-person break-glass recovery before paid launch.
- `UZLLM_CERT_DIR` contains `public.crt`, `public.key`, `internal-ca.crt`,
  `gateway.pfx` (SAN `gateway.internal`), `management.pfx` (SAN
  `management.internal`), and `keyring.pfx`. Both edge replicas verify the
  internal CA. Firewall backend ports to edge/node peers only. Renew before
  expiry; never commit certs or passwords.
- `UZLLM_SECRET_DIR` contains one UTF-8 file per Compose secret source in the
  manifest: runtime and migrator PostgreSQL DSNs, Redis DSN, API-key HMAC,
  provider KEK/version, certificate passwords, and merchant secrets. Compose
  maps each to its .NET configuration name under `/run/secrets`. Secret files
  and backups must be restricted to operators. All DB/Redis DSNs must enable
  TLS and validate the server certificate; never set trust-server-certificate.
- Set `UZLLM_PUBLIC_BASE_URL`, payment fee values, the three directories above,
  and the four `UZLLM_GATEWAY_*` / `UZLLM_MANAGEMENT_*` address and bind values.
  Remote upstream addresses include port, e.g. `node-b.internal:8445`. The
  default loopback binds are for one-host drills; production binds private IPs.
- Both merchant callbacks use exact HTTPS POST routes through edge:
  `/payments/payme/callback` and `/payments/click/callback`. Nginx buffers and
  caps each request at 32 KiB, bounds upstream timeouts, and never retries an
  upstream POST. Unknown `/payments/` paths are not served by the dashboard.
  Payme's Basic credential and CLICK's form signature are checked by Management;
  neither callback uses a browser session or CSRF token. Exercise the edge
  fixtures with `node --test deploy/tests/payment-edge.test.mjs` after building
  `uzllm-edge:ops002`; the test uses disposable Docker containers/certificates.
- CLICK is **default-deny** at both edges. After merchant onboarding, obtain
  CLICK's trusted callback source ranges from the merchant and install a
  protected file of `allow <verified CIDR>;` lines ending with `deny all;`.
  Set `UZLLM_CLICK_ALLOWLIST_FILE` to that file on **both** nodes. Do not use
  `allow all` or a public-wide CIDR. Its signed payload does not cover the
  reversal `error` field, so a signature alone is not sufficient ingress
  authentication. With the default file, Payme remains routable but CLICK
  requests receive 403; do not advertise CLICK checkout until this gate passes.
- The edge ignores client-supplied `X-Forwarded-For` by default. If an L7 load
  balancer fronts it, ensure the LB **overwrites** inbound `X-Forwarded-For`,
  then provide a reviewed `UZLLM_TRUSTED_PROXY_REALIP_FILE` containing only
  exact private LB CIDRs, `real_ip_header X-Forwarded-For;`, and
  `real_ip_recursive on;`. Do not trust a public CIDR. An L4 source-preserving
  LB needs no real-IP override. Verify the observed source against the merchant
  test callback before enabling CLICK, and firewall backend ports to the edge.
- Public auth and invitation POSTs have an 8 KiB edge/body cap and a shared
  edge client-IP zone (30/minute, burst ten). Management also uses Redis-backed
  per-operation IP and normalized-account limits; it returns 503 while Redis is
  unavailable rather than accepting unmetered attempts. Keep Management ports
  private: its direct-connection IP bucket is a high-volume backstop because
  proxied requests share the edge source IP. Monitor sustained 429/503 counts,
  especially after edge failover; never disable limits to restore login.
- Set `UZLLM_SMTP_HOST`, `UZLLM_SMTP_PORT` (default 587), and
  `UZLLM_SMTP_FROM`. Put SMTP username/password in
  `UZLLM_SECRET_DIR/Email__Username` and `Email__Password`. Worker requires
  STARTTLS and refuses startup without sender configuration. Verify real SMTP
  delivery, SPF/DKIM/DMARC and mailbox placement before customer launch.
  Passwords and one-time tokens must never appear in logs or traces.
- Set `UZLLM_OPERATIONS_ALERT_EMAIL` to an owned on-call mailbox before starting
  Worker. Trigger a test operational alert in staging and verify mailbox receipt,
  routing, human acknowledgement and escalation coverage; a configured address
  alone does not prove the operational launch gate.

The Payme/CLICK production merchant IDs, keys, callback IP policy, and
merchant-account verification remain external launch prerequisites. Do not
publish checkout until an actual merchant sandbox/live handshake is signed off.
Identity email is at-least-once: the inbox suppresses replays after recorded
delivery, but SMTP cannot guarantee exactly once if Worker crashes after the
remote server accepts a message and before recording completion. Such a retry
uses the same token and deterministic Message-ID. After token expiry Worker
skips delivery and marks the event complete; inspect dead-letter/outbox backlog
for persistent transport failures.

For operational incidents, inspect `GET /management/v1/admin/work/alerts?limit=50`
with a recently MFA-verified operator session. It shows alert identity, kind,
severity and delivery state without details. `Pending` means not yet confirmed
sent; `DeadLettered` requires immediate manual escalation. Correlate its
`notificationEventId` with `GET /management/v1/admin/work/dead-letters?limit=50`.
The dead-letter response contains only work
identity, type, attempts, terminal time and sanitized failure kind—not payloads.
Claims stop at `maxAttempts`; a final-attempt crash is marked `LeaseExpired` on
the next poll. Investigate the associated domain state and external provider
receipt before any replay: a remote send may have succeeded before the Worker
crashed. Do not blindly reset `attempt_count` or clear `dead_lettered_at` in
PostgreSQL. Operator alerts are sent to the configured mailbox with a stable
Message-ID. The inbox prevents a second send on normal replay, but SMTP cannot
guarantee exactly once after remote acceptance followed by Worker crash.

## Release sequence

1. Pin image digests and run CI gates: restore/build/all tests, frontend build,
   container builds, Nginx/Compose syntax, migration up/down on a disposable
   database, financial recovery test, and staged load drill. A failed gate
   blocks release. Back up first; inspect pending evidence and payment cases.
2. Apply migrations **once** with the separate migrator secret:
   `docker compose -f deploy/compose.production.yml --profile migration run --rm migrator`.
   Hosts never auto-migrate. Check backward compatibility before rolling out.
3. For releases adding outbox event types (including `team.email.invitation`),
   roll **both Worker replicas first**, then enable the corresponding Management
   routes. An old Worker can claim an unknown event and exhaust its retries.
   On A run `docker compose -f deploy/compose.production.yml up -d --no-deps worker-a`;
   on B run the equivalent for `worker-b`. Confirm both Workers are healthy and
   run the image containing the new handler. Then run
   `docker compose -f deploy/compose.production.yml up -d --no-deps gateway-a management-a edge`
   on A and the matching `-b` services on B. External LB must target both edges; configure
   `UZLLM_GATEWAY_A/B` and `UZLLM_MANAGEMENT_A/B` to the private node DNS+ports.
   Check each `/health/ready` and edge `/edge/health` before receiving traffic.
4. Roll one node at a time. Drain streams before replacing Gateway; do not
   replay a POST automatically after a lost response. Nginx buffering is off
   for SSE and upstream retries are disabled for writes. A client retry must
   use its idempotency key and may receive a conflict until original finalizes.
5. Watch 5xx, TTFT, active streams, admission failures, financial failures,
   payment mismatches, outbox backlog and provider health. Revert compatible
   application images if thresholds regress; do not blindly reverse a data
   migration after new writes.

## Backup, restore, and financial recovery

Use a protected `PGSERVICEFILE` with `uzllm_backup` and `uzllm_restore` entries;
the former is read-only, the latter points **only** to a pre-created empty
scratch DB named `uzllm_restore_*`. `UZLLM_BACKUP_DIR=/protected/backups
deploy/backup.sh` makes a checksumed custom-format logical backup. This is
supplemental; scheduled managed PITR/WAL shipping is what can meet the RPO.
Set `UZLLM_BACKUP_FILE` and `UZLLM_RESTORE_DB`, then run
`deploy/restore-drill.sh`. It verifies the checksum, rejects a non-scratch
target, restores, checks schema and wallet/reservation invariants, and reports
`PendingEvidence` separately from `PendingSettlement`.

After failover, keep new managed admission closed until PostgreSQL and Redis
readiness pass (and Redis's max-lease recovery horizon ends). Restart workers;
their leased jobs reconcile known verified evidence into one settlement.
Unknown evidence remains held until its reconciliation deadline, then is
released **without claiming zero upstream usage**. Late verified usage becomes
platform exposure, not a surprise customer debit. Reconcile payment provider
state and callback receipts before reopening top-ups. Never replay ledger
entries or payment callbacks by hand. Record restore start/end, latest
recoverable WAL timestamp, observed RPO/RTO and any unresolved exposure.

## Staged load gate

For a reproducible local rehearsal, create a **disposable** PostgreSQL database
named `uzllm_load_*` and a Redis instance, apply migrations, then run
`dotnet run --project tests/Load/UZLLM.LoadSeed` with
`UZLLM_LOAD_SEED_ALLOW=isolated-test-db`, its migrator PostgreSQL DSN and
test-only API-key HMAC/provider KEK environment values. The seeder refuses
other database names, credits the wallet through the real ledger service, and
prints a test API key once. Configure both Gateway replicas with the same
test keys, DB/Redis, and `Providers:OpenAI:BaseUrl` pointing to the fixture's
HTTPS `/v1/` URL; keep the fixture certificate trusted only in staging. The
Gateway admission limiter intentionally stays closed for its configured
maximum lease horizon after a new Redis epoch; wait for that recovery window
before measuring throughput. Never use this seeder against customer data.

Run `deploy/load/streaming.js` with k6 against **both** Gateway nodes behind
the edge and `deploy/load/deterministic-upstream.mjs` on a staging TLS hostname
(set `UZLLM_FIXTURE_CERT`/`UZLLM_FIXTURE_KEY`, then run it with Node 22), funded test tenant,
test API key and active catalog/price. Set `UZLLM_LOAD_URL`,
`UZLLM_LOAD_API_KEY`, `UZLLM_LOAD_MODEL`, and a fresh eight-hex-digit
`UZLLM_LOAD_RUN_ID` for every run; the test tenant needs at least 600
RPM and 320 concurrent key/project slots with provider timeout >30 seconds.
Run `k6 run deploy/load/streaming.js`. It submits 10 new requests/second with
200+ concurrent streams, unique idempotency keys, and verifies SSE termination.
Confirm `uzllm.gateway.active_streams` reaches at least 200 across both nodes;
the HTTP client alone cannot prove the server-side concurrency level.
Firewall the unauthenticated fixture to staging Gateway nodes only; never
register it as a production provider mapping.
Measure gateway-only overhead using matched upstream first-byte timestamps:
p50 <25 ms, p95 <100 ms, p99 <250 ms are initial NFR-010 targets; record actual
numbers and investigate misses. Also stop one node, Redis and PostgreSQL in
turn and verify fail-closed admission, no double charge and worker recovery.
Staged results and managed-service SLA evidence must be attached to the release
record; configuration alone is not proof of production availability.
