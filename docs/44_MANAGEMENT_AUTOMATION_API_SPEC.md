# Management Automation and API Lifecycle Specification

## 1. Status, authority and bounded decision

**DX-004 · Approved specification · 2026-10-18 · Specification only.**
DX-002 supplies implemented versioned OpenAPI/example/client drift checks, not
headless credentials. This document adds no token, endpoint, schema, UI or SDK
behavior. The user's Continue approves the profile and follow-on design in
draft `0116885`; specification approval does not schedule implementation,
promote feature tasks or activate automation.

Authority: FR-002/004/011/020–027/090–096 and the P2 management API item define
identity, tenancy, keys and useful reads. NFR-042–046/050–051/084 constrain
secrets, authorization, bounded work, privacy and lifecycle. ADR-004/011/017/
022/024/025 preserve wire, retention, financial and browser/operator boundaries.
The P2 item means **headless customer automation**, not that today's working
Management API is missing. Keep its existing cookie/CSRF contract unchanged.

Approved first-profile design: an expiring **Owner-sponsored, read-only automation
credential**, bound to one organization, its issuing local account, a finite
project allowlist and explicit scopes. It supports CI inventory and bounded
usage reporting, not infrastructure provisioning. No organization-independent
service account, OAuth authorization server, JWT exchange, refresh token,
client-credentials grant, human password in CI or implicit browser login.

Exclude headless writes to Gateway keys, projects, routing, budgets, wallet,
payments/refunds, team, provider/BYOK credentials, payload controls, guardrails,
notifications and all operator resources. Broader provisioning and independent
workload identity need their own scoped design; do not add a wildcard now.

## 2. Verified baseline and integration gaps

| Inspected source / evidence | Actual behavior and design implication |
|---|---|
| `src/Modules/Identity/Infrastructure/IdentityServiceCollectionExtensions.cs`; Identity contracts/service | Management middleware authenticates opaque cookies and sets local account claims; mutation filters validate cookie/header CSRF. Operator proof is separate. There is no automation identity, generic password reauthentication contract or bearer policy. Do not globally add bearer authentication to existing routes. |
| `src/Modules/Organizations/Application/OrganizationService.cs`; `TeamService.cs` | Role/project authorization exists; team mutations serialize under an organization lock. An Owner currently has broad permissions, so passing the sponsor's account ID alone cannot enforce a credential's narrower scopes. Role regrant needs a non-resurrection fence. |
| Identity/Organizations `.csproj`; architecture tests | Organizations already references Identity, not vice versa; each module's Contracts folders share its assembly. Adding an Identity reference back to Organizations/Projects for automation would introduce a cycle. Use a narrow Identity-owned authority port implemented by Organizations, not a new infrastructure exception. |
| `src/Modules/Identity/Infrastructure/PostgreSqlIdentityStore.cs` | Password recovery atomically changes the password and revokes sessions, not future automation credentials. Existing account/member rows have no dedicated automation invalidation epochs. Add explicit hooks rather than infer security changes from `UpdatedAt`. |
| `src/Modules/ApiKeys/Application/ApiKeyServices.cs`; Contracts | Gateway keys are inference credentials with show-once issue/rotate, HMAC fingerprints, generation and reversible Active/Disabled status. These are not Management tokens; list authorization currently requires ManageApiKeys. A new read scope must not accidentally inherit create/rotate rights. |
| `src/UZLLM.Management.Api/UsageReadEndpoints.cs`; Usage Contracts/Application | Existing usage reads enforce tenant/project roles at HTTP; `IUsageReadService` takes organization/filter, not an authenticated actor. An automation application facade must authorize and pin the project before every call. Activity defaults to 50, maximum 100; UTC windows default to 30 days, maximum 90. |
| `src/UZLLM.Management.Api/Catalog/CustomerCatalogService.cs` | Customer catalog returns published managed chat pricing, not live provider quota/health or a promise that every mapping is allowed for a key. Reuse pricing calculations, not an inference authorization bypass or an unbounded list. |
| `src/UZLLM.Management.Api/OpenApi/ManagementOpenApi.cs`; [artifact guide](api/README.md) | Version 1.0.0 describes session-backed routes, endpoint-specific errors and no-store. Generator rejects undocumented operations. Future automation must declare its own security scheme/errors and regenerate examples/client in the implementing task. Do not add fictitious live operations to today's artifact. |
| Existing Identity/Team/ApiKeys unit, PostgreSQL and OpenAPI wire tests | These test current roles, rotation, recovery and serializer parity. They do not test automation issuance, scopes, epochs, replay or restore revocation. Passing the baseline is not new feature acceptance. |

## 3. Permission and authentication contract

Credential permissions are the intersection of an active verified local sponsor
who remains Owner, active organization, immutable project allowlist, exact
scope, endpoint operation allowlist and current resource ownership/status.
Neither a scope nor an Owner claim bypasses a restriction. Unknown scope,
duplicate scope/project, empty list, `*`, null-as-unrestricted, another tenant's
project and a future/unlisted operation are rejected, never silently broadened.

| Scope | First allowed operations | Explicit exclusions |
|---|---|---|
| `projects.read` | Safe identity/name/status/environment for an allowlisted project. | Raw settings JSON, creation/archive, budgets, routing writes. |
| `api_keys.read` | Paginated key metadata: ID, name, prefix, status, expiration, generation. | Secret/fingerprint, credential material, issue/rotate/enable/disable/permissions writes. |
| `catalog.read` | Paginated published customer model/mapping/capability/rate metadata for one active allowlisted project. | Provider credentials/internal costs, live probes or promise of inference eligibility. |
| `usage.read` | Project-pinned Activity and summary using current UTC, token, pending, charged/refunded/net and freshness semantics. | Raw payloads, request detail/evidence/provider-cost/exposure views, CSV/export, organization-wide reads, ledger and wallet. |

Allow 1–4 scopes and 1–100 projects per credential. No scope implies another.
An archived project may expose status through `projects.read`; its catalog and
key inventory return 404. Historical usage remains readable while the project
belongs to the allowlist and organization. New projects are never auto-granted.
Credentials cannot issue or inspect other automation credentials.

Use a typed immutable `AutomationAccessContext` with credential ID/generation,
sponsor ID, organization, projects, scopes and epoch snapshots. The future
Identity verifier constructs it from trusted storage, not request claims.
Make construction internal to Identity, with no public writable properties or
HTTP deserialization; never accept a caller-created context as a DTO/proof.
Application facades require it and reauthorize each operation; do not synthesize
a browser principal, generic Owner actor, operator claim or `Guid.Empty` actor.
Existing browser service contracts remain usable only after the facade has
checked the narrower context. HTTP-only scope checking is insufficient.

Exactly one `Authorization: Bearer <token>` header on the new machine route
family; TLS only. Reject multiple/comma-joined headers, session/CSRF cookies
on machine routes, query/body credentials and bearer on lifecycle routes. No
cookie fallback after invalid bearer. A Management token cannot authenticate
Gateway inference, provider callbacks or admin routes; a Gateway key cannot
authenticate automation. Existing browser routes do not gain bearer access.
Do not enable credentialed cross-origin browser access to machine routes.

## 4. Credential lifecycle and safe recovery

Proposed shape: `uzllm_mgmt_<selector>_<secret>`; selector is 16 random bytes
encoded as 22 unpadded base64url characters, secret is 32 random bytes encoded
as 43 characters. Both are CSPRNG-generated anew on rotation. The durable
credential UUID is separate from the selector. Exact token length is 77 ASCII
characters; parse fixed offsets, not underscore splitting (base64url itself
may contain underscores). Reject padding and noncanonical encodings.
Store only HMAC-SHA256 of the
complete token with a dedicated, versioned Management-only key ring; bind the
fingerprint input to a fixed `management-automation-v1` domain separator.
The exact input is UTF-8 `management-automation-v1`, one NUL byte, then the
UTF-8 complete token; no locale normalization or trimming of secret material.
Constant-time comparison, unique selector, bounded parser before DB lookup;
never reuse Gateway fingerprint keys, provider encryption or Data Protection
material. Unknown key version fails closed; no HMAC fallback or plaintext.
Distribute old/new verification versions to every Management node before
changing the active issuance version; retain every version referenced by usable
credentials and both versions through rollback. Revoke/reissue affected tokens
before removal; changing the active version is not credential revocation.

Creation requires the issuing account's active verified **local** browser
session, current Owner role, CSRF and fresh password verification in that
request. A minimal future Identity verification contract must verify the
existing password without issuing another session; do not call login as a
reauthentication shortcut. Federated-only issuance is unsupported until a
separately approved tenant-bound step-up exists. Operator MFA is not customer
Owner proof. Password is never stored in receipts, telemetry or request logs.
Perform the bounded password check before opening lifecycle locks; an internal,
request-local proof binds the session/account and account security epoch, which
must be rechecked under the transaction fence before any mutation. It is not a
client-supplied reusable reauthentication token.

Default expiry seven days from issuance; explicit expiry must be more than
one hour and at most 30 days ahead. Enforce UTC half-open validity `now <
expiresAt`. Maximum 20 unexpired, unrevoked credentials per organization;
invalidated-epoch credentials do not consume that limit. Names are trimmed
2–80 characters; list metadata excludes secrets and password proofs.

Only the sponsor may create/rotate its own credential. Any current local Owner
may list/revoke credentials in the organization through browser CSRF; rotate
requires fresh password proof, revoke does not. Revoke is irreversible and
idempotent; no enable endpoint. Rotation requires active epochs, exact expected
generation and a non-expired/non-revoked credential. It immediately replaces
the selector/fingerprint, increments generation, preserves scopes/projects and
the original expiry, and has no overlap. Narrow/widen/extend means revoke and
explicitly issue a new credential; never renew implicitly on use.

Password reset/account suspension increments an Identity-owned security epoch;
Owner demotion/member revocation increments that member's Organizations-owned
delegation epoch; organization suspension increments its automation epoch.
Credential creation snapshots all three. Every use compares them and current
statuses. Reverification, reactivation, regrant and token rotation cannot revive
old credentials. Ordinary logout revokes the browser session, **not** independent
automation credentials; UI must state this and offer explicit revocation.
Any later account-disable/password-change/admin-recovery path must advance the
same account epoch. Current code has no such automation hooks yet.

Revocation/rotation applies to reads authorized after its commit. A read that
already acquired its authorization fence may finish; delivered bytes cannot
be recalled. Keep read materialization bounded and finish before releasing that
fence. Do not promise mid-response cancellation of information already sent.

## 5. Proposed routes, DTOs, errors and idempotency

All routes here are **unimplemented proposals**, not current OpenAPI paths.

Browser base `B = /management/v1/organizations/{organizationId}/automation-credentials`:

| Method / suffix | Proof and result |
|---|---|
| `GET B` | Owner session; paginated metadata, 200. |
| `POST B` | Sponsor session + CSRF + password; name/scopes/projectIds/optional expiresAt; 201 metadata plus show-once `secret`. |
| `POST B/{credentialId}/rotate` | Sponsor session + CSRF + password + expectedGeneration; 200 replacement metadata and show-once secret. |
| `POST B/{credentialId}/revoke` | Owner session + CSRF + expectedGeneration; 200 safe metadata including revokedAt. No secret. |

Machine base `M = /management/v1/automation/organizations/{organizationId}/projects/{projectId}`:

| GET suffix | Required scope / safe response |
|---|---|
| `M` | `projects.read`; project identity, name, status, environment. |
| `M/api-keys` | `api_keys.read`; bounded key metadata page. |
| `M/catalog/models` | `catalog.read`; bounded published catalog page with dated pricing/fee information. |
| `M/usage/activity` | `usage.read`; current safe `UsageActivityPage` shape. |
| `M/usage/summary` | `usage.read`; current `UsageSummary` shape, restricted to path project. |

Machine clients cannot supply an overriding projectId/organizationId query.
Unknown/duplicate query fields are 400. Usage retains supported UTC/status/key/
model/provider/stream/request filters; validate API-key/request IDs belong to
the path project before query. Global model/provider IDs only narrow that
project's data, never grant access to another project. Reject pagination fields
on summary. Keep unknown
usage null/pending, amounts integer **micro-USD strings**, never floats or
provider-spend substitution. Summary is not an invoice or live wallet balance.

Lists default 50/max 100 with keyset ordering (metadata by createdAt then ID;
catalog uses flat mapping rows by canonical code then mapping ID, with model
and pricing metadata; Activity preserves startedAt/ID).
Opaque cursor <=512 bytes, signed and bound to credential/generation, org,
project, endpoint, normalized filters and sort version; Activity wraps the
existing <=128-character position inside this scope envelope. Cursor is not
authorization, expires after 15 minutes and becomes invalid on rotation.
Use versioned, domain-separated cursor signing material, not token fingerprints
or reversible session cookies; only normalized IDs/window bounds/filter digest
and position in the envelope, never free-text request content or payload.
Changed scope/filter/project or invalid cursor is 400, never a broader query.
No snapshot guarantee across pages; repeat GET may observe new committed data.
Apply tenant/project filters and keyset limits in the datastore, not after an
organization-wide materialization followed by in-memory filtering/pagination.
Default usage window 30/max 90 days; explicitly fix `to` for repeatable ranges.
On the first Activity page, freeze the effective from/to in the cursor; later
pages inherit those bounds when omitted, rather than recomputing `now` and
breaking cursor validation. Explicit filters must match the bound query.
Responses <=4 MiB, request bodies <=8 KiB, 10-second total read deadline;
oversize 413/no partial page, timeout 504/no partial JSON. No downloads/jobs/SSE.

Successful bodies and errors always use `Cache-Control: no-store`; machine
responses carry `X-Request-Id`. New machine/lifecycle errors use Problem Details
with safe stable `code` and requestId extensions, not provider text:

| Status | Meaning / client behavior |
|---|---|
| 400 | malformed/unknown field, invalid scope/expiry/cursor or mixed credentials; correct request. |
| 401 | missing credential uses `authentication_required`; presented invalid/expired/revoked/epoch-invalid token uses generic `invalid_token`. Bearer challenge, no token/tenant disclosure. Missing browser session stays the existing 401 contract. |
| 403 | authenticated but missing scope or local Owner/password/CSRF proof; machine `insufficient_scope` challenge. No resource discovery. |
| 404 | resource absent, different organization/project or outside credential project allowlist; no existence oracle. |
| 409 | idempotency conflict, already-issued secret, stale generation or active credential limit; inspect safe metadata, do not blindly retry. |
| 413/429/503/504 | bounded size/rate/dependency/deadline rejection; 429 and temporary 503 provide bounded Retry-After seconds. |

Existing endpoint-specific errors are unchanged. New family DTOs, operation
IDs, optional fields, enums and auth challenges must be serializer-tested in
DX-002 artifacts when implemented, not copied as a universal browser contract.
Missing bearer uses a plain `WWW-Authenticate: Bearer realm="uzllm-management"`
challenge without an error parameter; a presented invalid token adds
`error="invalid_token"`. Missing scope uses 403 with `error="insufficient_scope"`
and the required fixed scope, never an echoed untrusted value. Other statuses
do not attach a misleading authentication challenge.

Lifecycle POST requires UUID `Idempotency-Key`; browser supplies a stable
operation UUID before sending. Unique identity is `(organizationId, actorId,
operation, key)`. Hash a canonical, versioned safe command: resource/expected
generation, normalized scopes/projects/name and requested expiry (use a stable
`default` marker before first execution). **Exclude password, cookies and secrets**.
Use SHA-256 over the versioned fixed-property-order UTF-8 JSON command, scope
codes sorted ordinally, UUID project IDs sorted in canonical lowercase form
and UTC expiry normalized to one round-trip format. Clients do not supply the
digest; equivalent ordering cannot turn a retry into another command.
Different normalized content under the same identity is 409 `idempotency_conflict`.
Reauthorize current session/role/tenant before receipt lookup; creation/rotation
also verify fresh password even for a retry. Receipt existence is not authority.

Create/rotate commit metadata, fingerprint/generation, immutable audit and
receipt in one PostgreSQL transaction; return plaintext only after commit.
Duplicate create/rotate returns 409 `secret_already_issued` with safe credential
ID, committed generation and operation ID, **never** stored/reconstructed secret
or another mutation. If response is lost, browser lists metadata, then rotates
with the committed generation and a new key, or revokes. Revoke same-key retry
returns original safe 200 metadata after reauthorization; different-key revoke
of an already revoked credential, with matching generation, returns current safe
200 without another credential mutation. Record a new no-op audit/receipt for
that new operation identity; same-key replay appends neither again.
Expected generation mismatch on a non-replay command is 409, including a revoke
racing rotation; refresh metadata and explicitly retry with new intent.

Keep minimal command receipts as append-only tombstones under the current audit
legal hold; no 24-hour expiry permits delayed reissuance. A signed archive policy
may later compact them only while preserving operation uniqueness/denial.
No response bodies or proof values are stored. Concurrent identical commands
have one winner; loser waits for the unique transaction and reads its receipt.
Audit/receipt failure rolls everything back; failed attempts may be retried.
Inference ADR-022's 24-hour duplicate window stays separate and unchanged.
GETs have no idempotency receipt and may retry 429/503/504 with capped backoff,
jitter, Retry-After and at most two retries; clients cannot retry 401/403/404
as another identity or automatically regenerate secrets after 409.
Keep edge/proxy automatic write retries disabled as in current
`deploy/nginx/edge.conf`; only the caller may repeat a lifecycle command with
its original operation key. Examples do not follow redirects with credentials.

## 6. Ownership, persistence and transaction fences

Identity owns credential lifecycle/cryptography/authentication in its existing
Domain/Application/Infrastructure/Contracts folders, plus a typed automation
access contract; no new generic security microservice. Organizations owns
membership/organization invalidation and organization-scoped grants. Projects
owns project lifecycle/tenant ownership. Projects, ApiKeys,
Catalog and Usage own their read projections/calculations. Management owns
route/auth selection, DTO validation and error mapping; its separate
`Automation/Application` facade composes authorized reads, never controller
business rules. Identity's scope/epoch policy and module-owned query filters
remain enforceable without HTTP. Audit owns append-only
events. Worker owns no issuance/permission decision and may only perform
approved metadata minimization. No provider adapter, Billing or Redis owns
credential truth. No new CQRS framework or broad module-access exception.

Preserve the current project graph: define a minimal
`IAutomationOrganizationAuthority` port and safe snapshot in Identity.Contracts;
Organizations implements current Owner/org/member-epoch and project-ownership
checks with its existing stores/project-grant SQL, participating in the caller's
transaction. Identity references no Organizations/Projects implementation or
assembly. Organizations already references Identity and needs no reverse
Projects dependency. The host composes this port and module read contracts;
do not leak module entities or SQL into endpoint handlers. Enforce dependency
and direct-application authorization tests before exposing routes.

Proposed persistence, **not migrations in DX-004**:

- `iam.automation_credential`: UUID, org/sponsor, name, selector, HMAC/key version,
  positive generation, issued/expires/revoked timestamps, three epoch snapshots.
  Unique selector; indexes `(organization_id, created_at, id)` and sponsor/status;
  checks enforce positive versions, validity and irreversible revocation.
- Child project grants and scope rows: PK `(credential_id, project_id)` and
  `(credential_id, scope)`; composite org/credential/project FKs prevent mixed
  tenant grants. Sponsor has a composite FK to `org.member`; never cascade away
  audit/operation identity when a membership is revoked.
- `iam.automation_command_receipt`: unique `(organization_id, actor_id,
  operation, idempotency_key)`, safe command digest/version, credential/generation,
  result code, operation ID/time and safe revoked timestamp; no secret/proof/body.
  Append-only runtime permission, tenant FKs and a unique audit-event FK linking
  the winning operation to its immutable audit event.
- Dedicated increasing epochs on `iam.account`, `org.member`, `org.organization`;
  never timestamps as epochs, no reset/decrement on restore or role regrant.

Lifecycle transactions lock account, organization, then credential in that order;
If actor and sponsor differ, lock both accounts in UUID order before org.
Issuance serializes the per-org active count. Team changes already lock org:
increment the target member epoch in that same transaction, without acquiring
account locks afterwards. Password reset/suspension takes account lock and bumps
its epoch in its existing transaction; org suspension bumps org epoch under its
lock. No outbox-only asynchronous revocation window. Publish only minimal
cross-module contracts and enlist them in the shared scoped context/coordinator;
do not start nested independent transactions inside an already enlisted one.

Machine reads use a short read transaction: resolve candidate, acquire shared
account -> organization -> credential locks, recheck token/generation/epochs/
statuses/expiry and scoped resource, materialize the bounded read, then commit
before response I/O. Shared locks must conflict with their lifecycle updates.
At time of authorization, role/grant changes committed earlier cannot be missed;
concurrent revocation waits for bounded admitted reads. A query begun before
revocation may finish, never a stale cached admission after commit. Enforce the
10-second deadline/cancellation, dispose transaction on every failure; no lock
is held while sending HTTP bytes or calling providers. This boundary may read
existing financial projections but never mutates ledger, reservation, settlement,
budget buckets or charge. Automation has no per-request fee in this profile.

## 7. Abuse, operations, retention and restore

Dedicated shared Redis limits before reads: 60 requests/minute/credential,
300/minute/org; concurrency four/credential and 20/org, 15-second leases with
no renewal beyond the 10-second request deadline. Unknown/bad tokens share
60/minute/trusted-source-IP preauthentication limit, not attacker-chosen selector
buckets. Create/rotate/password proofs: ten/minute/account plus org limit.
All are proposed ceilings, operator may lower, never bypass deployment caps.
Redis loss fails automation reads/create/rotate closed with 503, no unbounded
local fallback. Existing browser-based emergency revoke must remain usable with
PostgreSQL available and no dependency on the new automation Redis limiter.
PG loss fails closed; no cached Owner/scope/epoch authority. Release leases and
locks on client cancellation; if lifecycle commit already succeeded, receipt
and metadata survive even if the response cannot be delivered.

Audit issuance/rotation/revoke and invalidation in their mutation transaction,
using actual sponsor/Owner account IDs and safe credential/operation/generation/
scope/project metadata. Automation read/security telemetry identifies actor kind
`Automation`, credential ID/generation, operation, correlation ID and outcome,
not browser impersonation; do not write an immutable audit event for every GET.
Mask Authorization/passwords/selectors, cookies and query content everywhere;
no credential ID, org, raw URL or cursor in metric labels. Metrics use fixed
route/scope/outcome labels: auth denials, scope denials, 429s, DB/Redis faults,
query/deadline latency, cursor rejects and lifecycle replay/conflict. Alert on
denial spikes, receipt/audit rollback and unavailable verification dependencies.

Use existing approved diagnostic/operational retention for bounded access
events; do not invent a new legal duration. Credential/receipt/audit identity
stays under legal hold; expired token fingerprints need not stay usable.
Quarantined backup restore must revoke **all** restored automation credentials
before edge exposure (atomic epoch bump for every restored organization), then
validate current memberships and rotate suspect fingerprint keys. Restoring an
older epoch alone can resurrect stolen tokens: no claim of safety without the
scrub drill. No restoration can recreate a secret from a fingerprint.

## 8. Versioning, compatibility and customer UX

Keep Gateway `/v1` and Management `/management/v1`. Explicit new automation
routes/security schemes are additive; they do not relax existing cookie/CSRF,
operator-MFA or error behavior. Document OpenAPI `info.version` by semantic
contract revision; it is not an auth protocol or permission to break a path.
Optional additive response fields are allowed; request validation stays strict.
Scope semantics, token audience, units, required fields, enum removals and error
meaning changes are breaking. New scopes never attach themselves to old tokens.

Breaking changes need a new versioned path, migration guide, dated notice and
**at least 180 days** overlap before sunset under NFR-084/docs11. Communicate via
published changelog and affected account notification; include scope/token/client
migration, replacement examples, rollback and explicit dates in UTC. Proposed
responses on deprecated operations use RFC 9745 `Deprecation: @<unix-seconds>`,
`Link: <guide-url>; rel="deprecation"`, and RFC 8594 `Sunset: <HTTP-date>`.
Sunset must not precede the notice/overlap window. Token expiry is **not** API
deprecation. Existing v1 headers are not claimed implemented by this design.
Emergency retirement needs the existing incident decision/customer notice;
credential compromise may require immediate revocation with no 180-day grace.

Future Owner settings UI distinguishes Management automation from Gateway and
provider keys. Show immutable scopes/projects/sponsor/expiry/generation; masked
metadata, show-once copy with clear loss recovery, immediate cutover and expiry
warnings; no token localStorage/sessionStorage, query URL, analytics or screenshots.
Organization switch clears secret/dialog state. Non-Owner controls stay absent
but backend denial remains authoritative. Preserve Uzbek/Russian/English and
accessible focus/error handling; use the repository mockup as a visual baseline
and improve hierarchy/spacing/states, not a new ad-hoc style. No playground or
browser-run bearer requests; examples are terminal/server only with redacted env
variables and TLS origin validation. No automation UI is implemented today.

## 9. Designed acceptance scenarios — not executed feature tests

| Case | Required observable result / fixture |
|---|---|
| AUT-001 | Token families/audiences cannot cross Gateway/browser/admin/callback boundaries; reject mixed/multiple/query credentials. HTTP/native contract fixtures. |
| AUT-002 | Owner/local/verified/session/CSRF/password proofs required for issue/rotate; Admin/developer/federated-only or bearer denied. Tenant HTTP and pure policy tests. |
| AUT-003 | Every scope is independent; unknown/empty/duplicate/wildcard grants rejected; direct application invocation cannot escape scope. Unit and PG/HTTP tests. |
| AUT-004 | Org/path/project/key/request/cursor tampering never reveals another project's data; summary totals use exactly one authorized project. Two-tenant PG tests. |
| AUT-005 | CSPRNG shape, HMAC domain/key version, constant-time compare and no recoverable secret/proof in DB/log/audit/fixtures. Cryptography and content-sink tests. |
| AUT-006 | UTC TTL boundary, 20-credential race and archived/new-project rules hold; no implicit expiry extension. Fake-clock unit and parallel PG tests. |
| AUT-007 | Two concurrent identical issue commands create one credential/audit/receipt; replay gives safe 409, changed command gives conflict; no plaintext replay. PG rollback/race tests. |
| AUT-008 | Rotation expected-generation CAS, immediate cutover, concurrent rotate/revoke and lost-response recovery preserve old expiry/scopes; only one replacement generation. PG/HTTP tests. |
| AUT-009 | Duplicate revoke is safe, irreversible and tenant-authorized; browser revoke works through automation Redis outage. PG/Redis/HTTP tests. |
| AUT-010 | Password reset, account/org suspend or Owner demotion/revocation fences tokens; regrant/reactivation cannot resurrect them. Existing recovery/Team plus epoch transaction tests. |
| AUT-011 | Authorization/revocation races obey shared lock order and deadline; reads begun after commit reject old credentials on both nodes, already-admitted reads may finish. PG and distinct-node tests. |
| AUT-012 | Audit/receipt failure, cancellation and crash around commit roll back all or yield one durable recoverable outcome; no nested independent commit. Fault-injection PG tests. |
| AUT-013 | Activity/summary keep UTC/filter/freshness/pending/string-cost/refund semantics; no financial writes, requests, holds or provider I/O from reads. Read-model/ledger/dispatch spy tests. |
| AUT-014 | Signed keyset cursor, changed filters/scope/generation, 100-row/4-MiB/90-day bounds, 10-second timeout and cancellation hold. Unit/PG/HTTP tests. |
| AUT-015 | Shared rate/concurrency caps across nodes, unknown-token source limiting, Redis/PG outage and lease cleanup fail closed without blocking emergency revoke. Redis/fault/load tests. |
| AUT-016 | Exact generated OpenAPI security, DTOs, statuses/no-store/challenges/examples and client drift match actual routes; server examples redact credentials and cap retries. Wire/CLI tests. |
| AUT-017 | Deprecation/Sunset dates/guide/180-day notice and new-version fixtures preserve old v1 auth/error/units; incident exception documented, no implicit old-token scopes. Contract compatibility tests. |
| AUT-018 | Metadata-only telemetry, legal-hold receipts, quarantine restore epoch scrub and key-ring rotation/rollback reject all restored old tokens without touching financial history. PG/restore/content-sink tests. |
| AUT-019 | Show-once, loss/rotate/revoke UX, role denial, tenant switching and three locales work against real API; screenshot has no secret. Browser/real-edge tests. |
| AUT-020 | Feature-off default, staged rollout/rollback, bounded read load/lock waits and alerts pass; absent distinct-host evidence is Blocked, not a production pass. Load/fault/runbook review. |

## 10. Approved follow-on design — unscheduled, execution approval required

All eight rows are **approved P2 design**, not executable backlog additions.
Separate execution approval must explicitly promote bounded implementation tasks.
Table order is the recommended sequence. Each completion requires listed
criteria/tests, synchronized contracts/docs, Release baseline and focused commit.

| Proposed task | Objective / likely files | Dependencies / references | Acceptance, tests and completion |
|---|---|---|---|
| AUTOMATION-001 | Typed Identity automation contracts/domain, safe commands/scopes and credential/grant/receipt/epoch schema; Identity/Organizations/Persistence/Migrator. | Approved DX-004, DX-002, ARCH-001; §3–6, NFR-042/044/084, ADR-025. | AUT-003/005/006 schema/crypto/unit/migration/architecture tests: tenant FKs, unique identity/selector, no plaintext, expiry/immutable grants and append-only receipts. No public auth/issuance yet. |
| AUTOMATION-002 | Synchronous invalidation epochs and transaction-safe authorization fences; Identity recovery/store, Organizations Team/suspension contracts, Persistence. | AUTOMATION-001; §4/6/7, FR-004/135, ADR-024/025. | AUT-010–012/018 PG race/reset/regrant/rollback tests, stable account->org->credential order, no nested transaction or async revocation window; quarantine scrub design executable locally. No token routes yet. |
| AUTOMATION-003 | Owner browser lifecycle with fresh local password proof, generation CAS, receipt/audit atomicity and safe replay; Identity/Management/Audit. | AUTOMATION-002, SECURITY-003; §4–6, FR-002, NFR-045. | AUT-002/006–009/012 PG/HTTP proof, concurrency, lost-response, audit rollback and stale replay fixtures. All create/rotate/revoke DTOs/no-store/generated artifacts updated in same task; no machine access yet. |
| AUTOMATION-004 | Explicit machine auth context, route-only verifier, fail-closed limits/dependencies and read-fence facade; Identity/Management/Redis/Observability. | AUTOMATION-003, SECURITY-002; §3/6/7, NFR-043/044/046. | AUT-001/003–005/010/011/015 unit/HTTP/PG/Redis tests; do not attach bearer to browser/admin routes. Feature off, no data read endpoints yet; emergency browser revoke remains usable. |
| AUTOMATION-005 | Bounded project/key/catalog/usage projections and five scoped reads; existing modules/Management/API artifacts. | AUTOMATION-004, USAGE-004, APIKEYS-004, DX-001; §3/5/6, FR-011/090–096. | AUT-003/004/006/013/014/016 PG/wire tests prove filtering before queries, typed application auth, cursors/bounds and unchanged accounting. No secret/detail/export/write expansion. |
| DX-005 | Terminal/server automation quickstart, exact auth/retry/status examples and version migration/deprecation fixtures; docs/api/examples/client tests. | AUTOMATION-005, DX-003; §5/8, NFR-084. | AUT-007/008/016/017 offline and real TLS-edge examples, generated artifact parity, no cookies/password in CI; v1 compatibility and lifecycle recovery documented/tested. No new runtime endpoints. |
| FRONTEND-016 | Owner automation settings/show-once/recovery/revoke UX; dashboard/shared client. | DX-005, FRONTEND-010; §8, ADR-016/025. | AUT-002/008/009/019 localized accessible browser/real API tests/screenshots; mockup-aligned improved design, no browser bearer storage/execution or leaked screenshot secret. |
| OPS-012 | Disabled-default rollout/rotation/revoke/restore runbooks and optional automation qualification; deploy/Observability/tests/Load. | FRONTEND-016, OPS-006; §6–9, NFR-046/060–073/084. | AUT-011/015/018/020 distinct-host latency/lock/limit/failure/restore evidence, alert ownership and rollback review. Missing staging stays Blocked; no launch activation from local Aspire/fixtures. |

After contracts exist, UI copy and CLI documentation can be prepared independently
but cannot claim tested API behavior until reads/lifecycle exist. SAML/SCIM,
custom providers, strict privacy, cache, guardrails and invoice/SLA are not
dependencies for this local read-only profile. Combining later features requires
their own authorization/data-policy review. Any future headless **write** needs
new per-operation scope, hard restriction precedence, optimistic preconditions,
atomic receipt/audit semantics and secret/financial recovery design first.

## 11. References, validation and product review

Official references reviewed 2026-10-18: [RFC 6750](https://www.rfc-editor.org/rfc/rfc6750)
defines HTTP Bearer use, TLS protection and challenge errors; adopting its header
scheme does not require implementing OAuth issuance. [RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)
defines the structured-date Deprecation field and documentation link;
[RFC 8594](https://www.rfc-editor.org/rfc/rfc8594) defines Sunset using HTTP-date.
The read-only profile, epochs, TTL, scope/rate limits, receipts and 180-day policy
are repository proposals/policies, **not** limits mandated by these RFCs.

Fresh draft validation on 2026-10-18, Windows/.NET 10 and Docker disposable
PostgreSQL 17/Redis 8. Runtime/test/dependency inputs remain at `13fe40c`:

| Check | Result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0, projects up to date. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0, zero errors; existing ASPIRE010 CLI-bundle warning (2m29s). |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/dx-004-20261018` | PASS, exit 0. Sixteen Completed TRX reports: 1028 passed, zero failed/skipped/aborted; PostgreSQL 234/234 (15m45s for that assembly). |
| Static document checks | PASS: 149 local Markdown targets/16 linked anchors across thirteen paths; balanced fences/tables, four independent scopes/five read routes, 20 unique designed cases and eight collision-free/unpromoted task IDs. Proposed dependencies are ordered, with nine completed existing prerequisites; OPS-012 retains the blocked distinct-host OPS-006 gate. Token-length arithmetic checked. |
| Scope/history | Thirteen documentation-only paths; other backlog/history rows and launch gates unchanged. No runtime/test/frontend/configuration/dependency/generated-contract edit. |
| `git diff --check` and staged scope/whitespace review | PASS; task-only documentation changes. |

TRX stays in ignored local artifacts, not Git. No AUT feature fixture, new
OpenAPI operation, browser flow, migration, live credential or production
qualification is claimed. Designed AUT-001–020 are acceptance plans, not tests
executed by the unchanged baseline.

At the draft handoff, review of the read-only first profile, Owner/local
sponsorship, finite project/scope grants, seven/30-day expiry, immediate
rotation/no secret replay, epoch revocation/restore rule, bounded reads and
eight-task decomposition was pending. Approval-only closeout follows; feature
execution and task promotion remain separate.

### Approval-only closeout — 2026-10-18

The user's Continue directly answers the review request for `0116885`. It
approves the read-only first profile and four independent scopes, local Owner
sponsorship, finite project grants, credential/expiry/rotation/revocation and
safe replay contracts, synchronous epoch/restore fences, versioning/deprecation,
20 designed cases and eight-task decomposition. This satisfies DX-004's
specification review, not implemented automation, feature-task scheduling,
credential issuance, legal approval or production activation. All eight
follow-ons remain unscheduled and require separate execution approval/promotion.

The prior sixteen TRX reports were re-read: **1028 passed, zero failed/skipped/
aborted**, including PostgreSQL 234. Comparison to tested `13fe40c` confirms
unchanged runtime, test, configuration and dependency inputs. No new restore/
build/test run or AUT feature acceptance is claimed for this approval-only
closeout. Fresh static checks verify 149 local Markdown targets/16 linked
anchors, balanced fences/tables, four independent scopes/five proposed reads,
20 unchanged designed cases and eight unchanged dependency-ordered follow-on
rows. Token-length arithmetic is rechecked. Only thirteen documentation paths
change; other backlog/history rows and launch gates remain unchanged.
Whitespace and staged scope checks pass. OPS-006 still needs unavailable
distinct staging hosts; SECURITY-009's separate retention/audit risk remains
unfixed and unpromoted. Stop before ENTERPRISE-002 or any feature execution
without separate user approval.
