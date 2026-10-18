# Enterprise Identity — SAML and SCIM Specification

## 1. Status, authority and scope

**ENTERPRISE-001 · P2 · Draft for review · 2026-10-17.** This is a design
deliverable, not implemented authentication. The user's Continue confirms the
three policy choices in the [readiness handoff](16_IMPLEMENTATION_BACKLOG.md#enterprise-001-readiness-review--2026-10-17).
It does not pre-approve all design details below. Specification approval is
still required before this task is Completed or follow-on work is promoted.

Confirmed scope:

- SAML 2.0 browser SSO and SCIM 2.0 **Users only**; first reference IdP is
  Microsoft Entra ID acting as SAML IdP and outbound provisioning client.
- Organization opt-in, **optional**, not enforced SSO; enterprise membership
  must be provisioned and explicitly linked. No JIT account/membership creation
  from a login assertion, automatic email merge, or implicit domain discovery.
- Deprovision tenant membership, project grants and all Gateway keys created
  by that member **in that organization**. Do not suspend a global account,
  other tenants or erase financial/audit history. Workloads using those keys stop.

Out of scope: OIDC, IdP-initiated SSO, SAML single logout/artifact binding,
Groups/role sync, SCIM Bulk, mandatory SSO, domain claiming, service identities/
key transfer, enterprise billing/SLA, and enabling production traffic. Optional
SSO is not a promise that local-password login is disabled. No charge/plan change
is proposed; enterprise price, support and contractual terms need separate
business approval. P0 OPS-006/007 and LAUNCH-003 gates remain unchanged.

Any later feature build stays **disabled by default** behind a deployment
switch. Synthetic acceptance environments may opt in; production exposure
requires completed qualification and explicit activation approval. Creating a
Draft/Enabled connection does not override that deployment gate.

Deliberate trade-offs for review: optional SSO is not enterprise password
enforcement; explicit account linking adds Owner/user onboarding steps; Users-only
provisioning does not automatically synchronize project access. Conservative
reactivation needs Owner intervention and creator-key revocation can interrupt
workloads. Do not advertise a turnkey mandatory-SSO/offboarding SLA from this
first profile. Mandatory SSO, service identities and automatic restoration
would need separate product/security decisions and implementation tasks.

Keep [ADR-025](15_ARCHITECTURE_DECISIONS.md#adr-025--secure-browser-sessions-and-operator-boundary),
FR-001–005/020–028/130–135, NFR-040–046/051/064/066 and the
[domain/financial invariants](06_DOMAIN_AND_BUSINESS_LOGIC.md#12-corrected-ownership-and-state-model).
The proposed requirements/ADR extension here become binding only on review
approval. Current [API artifacts](api/README.md) remain the implemented wire
authority; none of the routes below exist yet.

## 2. Repository evidence and architectural fit

Inspected on clean `main` at `5bd4b1a`:

| Existing component | Verified behavior | Consequence for this proposal |
|---|---|---|
| [Identity contracts](../src/Modules/Identity/Contracts/IdentityContracts.cs), [entities](../src/BuildingBlocks/UZLLM.Persistence/IdentityEntities.cs) | Global password accounts and opaque sessions; no external subject or tenant-authentication context. | Add explicit external binding and session provenance, not a second user directory or email login shortcut. |
| [Session middleware](../src/Modules/Identity/Infrastructure/IdentityServiceCollectionExtensions.cs) | Builds global account/operator claims; strict cookies and CSRF. | A SAML login must not issue today's unrestricted global session. |
| [Organization routes](../src/Modules/Organizations/Infrastructure/OrganizationServiceCollectionExtensions.cs) | List/create takes an account ID; existing permissions do not represent federation scope. | Filter discovery and deny global operations as well as tenant/project reads. A route-prefix check alone is insufficient. |
| [Team lifecycle](../src/Modules/Organizations/Application/TeamService.cs), [store](../src/Modules/Organizations/Infrastructure/PostgreSqlTeamStore.cs) | Organization lock, last-owner/role protection, tenant membership/grant revocation and atomic audit. | Reuse contracts/transaction ownership; add SCIM authority and key revocation fencing. |
| [ApiKeys service](../src/Modules/ApiKeys/Application/ApiKeyServices.cs), [store](../src/Modules/ApiKeys/Infrastructure/PostgreSqlApiKeyStore.cs) | Creator recorded; authentication checks key/project, not creator membership. Create authorization precedes its transaction. | Explicit lifecycle-revocation marker and transaction-time rechecks are required; changing membership alone is insufficient. |
| [Financial admission](../src/Modules/Billing/Infrastructure/PostgreSqlFinancialStore.cs), [attempt dispatch](../src/Modules/Usage/Application/UsageService.cs) | Authoritative DB admission; durable dispatch precedes upstream call. | Fence admission and each attempt; keep existing settlement/recovery after deprovision. |
| [Audit contract](../src/Modules/Audit/Contracts/AuditContracts.cs), [entity](../src/BuildingBlocks/UZLLM.Persistence/AuditEntities.cs) | Requires a real ActorAccountId/account FK. | Add an explicit SCIM machine-actor variant; do not invent an account to impersonate. |
| [Operator MFA integration regression](../tests/Integration/UZLLM.Persistence.IntegrationTests/OperatorMfaIntegrationTests.cs) | Confirmation, cross-session/time-step replay and independent audited reset. | External MFA/roles never substitute for the local operator boundary. |

The conceptual `external_auth_ref` in Database Design is not implemented.
Use the existing modular monolith/three hosts/shared PostgreSQL coordinator;
no identity microservice, message broker or distributed financial transaction.

## 3. Proposed requirements and ADR-025 extension

These IDs are reserved **proposals**, not additional implemented FRs:

| Proposed P2 requirement | Observable contract |
|---|---|
| FR-140 · Tenant federation | At most one enabled Entra connection per organization; explicit optional enablement, local-owner recovery and versioned trust configuration. |
| FR-141 · External identity binding | Immutable `(connection, issuer, Entra object ID)`; provision first, then deliberate local-account/SAML dual proof. Email/name are not identity keys. |
| FR-142 · Scoped SSO session | SAML proves access only to the selected organization; cannot enumerate/use other memberships, create organizations, manage a global account or become an operator. |
| FR-143 · Users provisioning | Tenant-token-authenticated, bounded SCIM discovery/query/create/update/deactivate/delete with truthful schema and replay-safe effects. |
| FR-144 · Least-privilege roles | New linked member starts ReadOnly without grants; only existing local role authorities can promote/grant. SCIM/SAML cannot set roles, project access or operator status. |
| FR-145 · Deprovision barrier | Atomic tenant access/key withdrawal and audit; post-barrier new admission/attempts denied; already dispatched work finishes accounting. |
| FR-146 · Lifecycle recovery | Directory reactivation never resurrects keys/grants/old privilege; local revocation cannot be undone by a delayed SCIM update. |
| FR-147 · Enterprise evidence | Offline adversarial/DB/browser fixtures plus separately recorded Entra interoperability, rotation and incident evidence before enterprise activation. |

Proposed ADR-025 extension: retain opaque server-backed sessions, distinct CSRF
and local operator MFA. Add an immutable session authentication origin and
optional tenant/connection/binding generation. All application authorization
must consume that scope. Do not replace local sessions with browser SAML/JWT
storage or trust claims for current permissions. No accepted ADR is changed
by publication of this draft.

## 4. Boundaries and proposed persistence

| Owner | Responsibilities / public contracts | Must not own |
|---|---|---|
| Identity Domain/Application/Contracts | Connection/binding/login state, normalized validated SAML result, scoped session creation/validation, explicit link/recovery policy. | Provider SDKs, ledger or tenant-role claims from an IdP. |
| Identity Infrastructure | Maintained SAML library behind a narrow validator; XML/crypto, pinned trust, token hashing, PostgreSQL stores. Select supported .NET 10 library/license after hostile-fixture evaluation; never implement XML signatures by hand. | Tenant billing decisions or direct foreign-module stores. |
| Organizations | SCIM Users/lifecycle, membership/role/project-grant rules; existing Identity/Audit contracts and its own narrow credential-revocation port. | Password/global-account suspension, role sync or a cyclic ApiKeys project reference. |
| ApiKeys | Tenant/creator revocation operation, terminal lifecycle marker, reactivation/rotation denial and admission credential validity. | SCIM HTTP, SAML or wallet mutation. |
| Management | Future SAML/control/SCIM HTTP translation, auth scheme selection, scoped authorization, no-store/CSRF/body/edge bounds. | Domain invariants in handlers. |
| Gateway / Usage / Billing | Admission and dispatch authorization barriers; existing usage, holds, settlement and debt. | SAML parsing or network calls inside transactions. |
| Worker / Audit / Frontend | Expiry cleanup/retryable alerts; immutable minimal lifecycle audit; tenant settings/link/sign-in UI. | Async revocation as the authoritative security barrier. |

Preserve the actual assembly dependency graph: Organizations already references
Identity; ApiKeys references Projects which references Organizations. Therefore
do **not** add Identity→Organizations or Organizations→ApiKeys references.
Identity defines a narrow tenant-membership resolution/link port in its own
Contracts; Organizations defines its own creator-key revocation port. Thin
Management host adapters implement those ports by delegating to Organizations/
ApiKeys public contracts within the same scoped transaction. Business policy
stays in the owning Application/Domain; endpoints/adapters add no role/financial
rules. Gateway similarly composes narrow admission/dispatch authorization
ports. Architecture tests must prove acyclic references and no foreign
Infrastructure access; do not widen frozen exceptions or introduce a new service.

Proposed tables, implemented only by later explicit Migrator tasks:

- `iam.federation_connection`: organization FK, fixed issuer/tenant/SSO URL,
  SP entity/ACS identities, `Draft/Enabled/Disabled`, version, verified-test
  metadata; unique enabled organization. Versioned signing certificates are
  bounded child rows (thumbprint, public cert, validity/retirement); any SP
  signing private key is encrypted via the established secret infrastructure.
- `org.scim_user`: organization/connection composite FK, server UUID, immutable
  `externalId` (Entra object GUID), raw profile, normalized indexed `userName`,
  desired `active`, version, deleted timestamp and local-block/generation state.
  Partial unique `(connection, externalId)` and `(connection, normalizedUserName)`
  for non-deleted records. Email is neither globally unique here nor an account FK.
- `iam.external_identity`: composite tenant/connection/SCIM-resource FKs,
  issuer + canonical object GUID, AccountId FK, lifecycle generation, link time.
  Only one live account binding per external subject and one live binding per
  organization/account; never cascade-delete account/audit/financial records.
- `iam.federation_flow`: random request ID, hash of correlation/RelayState,
  connection version, purpose `Login/Link/Test`, permitted relative return path,
  optional initiator account/session hash, deadlines, state
  (`Pending`, `Validated`, `Consumed`) and hashed completion receipt. Separate
  unique response/assertion replay hashes with expiry and connection namespace.
  Owner link authorizations are separate bounded one-use records keyed by
  connection/SCIM-user/AccountId, with version/expiry and authorizing Owner;
  one live authorization per tuple, no account-discovery or bearer-link secret.
- `iam.session` extension: `LocalPassword/Federated`, nullable organization,
  connection, binding/generation and expiry. CHECK requires federation scope
  together; legacy rows backfill `LocalPassword`. Index scoped revocation reads.
- `iam.scim_credential`: organization/connection FK, public ID, random-secret
  keyed fingerprint/key version, expiry, generation and revocation. Show once;
  no recoverable bearer plaintext. Unique credential ID/fingerprint namespace;
  purpose-separated hashing keys, never reuse Gateway-key authentication.
- ApiKeys lifecycle-revocation evidence: key FK + organization/member/binding
  generation, immutable reason/time. Unique key revocation; index creator plus
  project/organization lookup. Existing key generation history is retained.
- Audit extension: actor kind `Account/ScimCredential`, nullable existing
  account FK and nullable composite tenant/SCIM-credential FK, CHECK enforcing
  exactly the matching variant. Existing rows retain their account IDs and
  default Account kind without rewriting history. Credential evidence is
  retained after revocation, never cascade-deleted; audit remains append-only.

Configuration, credentials, binding and lifecycle mutations require current
authorization/version checks and audit in the shared PostgreSQL transaction.
Unique constraints back every race-sensitive identity claim. ACS atomically
inserts replay markers and marks a flow Validated with a hashed receipt;
Finish atomically consumes that receipt/flow and creates the session/binding.
Only minimal validated subject/context is retained, never raw XML. Audit
failure rolls back the corresponding security mutation. Cleanup never removes
unexpired replay protection, and a Validated flow retains the original deadline.
No migration or direct DDL is included in this task.
Future Down migrations must refuse to discard populated bindings, lifecycle
revocations or machine-actor audit evidence; do not erase that data to make a
downgrade pass. Hosts still never apply migrations and runtime roles remain DDL-denied.

The internal validated-proof contract carries only connection/version,
request/response/assertion IDs, issuer, canonical Entra object GUID, NameID,
AuthnInstant/session expiry and validated signing-certificate fingerprint.
It is produced only by the trusted validator; no unvalidated XML/SDK principal
crosses that boundary. Application resolution adds the scoped binding/AccountId,
never an assertion-supplied UZLLM account ID or role.

## 5. Connection setup, linking and session semantics

1. A verified **local-password Owner**, with password re-proof within five
   minutes, configures a Draft connection. Retain at least one local,
   non-SCIM-managed Owner. Local Owners cannot be adopted as managed members
   or promoted from managed members in the initial profile; return `409`.
   Bootstrap/recovery therefore never depends on a working IdP. Admins cannot
   change trust or provision credentials; operators are not tenant Owners by fiat.
2. Pin the exact Entra tenant issuer, SSO endpoint and public signing certs;
   upload bounded metadata/certificates, not arbitrary fetch URLs. Verify
   tenant ID, HTTPS host/path and SP/ACS identities against fixed deployment
   origins; metadata never supplies arbitrary redirects or remote XML keys.
   First profile is the public Entra cloud with tenant-GUID-specific
   `login.microsoftonline.com/{tenantGuid}/saml2` and exact
   `https://sts.windows.net/{tenantGuid}/` issuer. Sovereign-cloud and common/
   organizations endpoints need a separately reviewed profile, not a URL override.
3. Perform a correlated Test flow without creating a customer session/binding.
   Enable only against the tested unchanged configuration version. This proves
   configuration/signature handling, not a full provisioning qualification.
4. SCIM creates a directory resource, **not** a global account or usable
   membership. User separately registers/verifies a local account using existing
   flows, or signs in to an existing account. Owner issues a one-use 10-minute
   link authorization naming that exact verified AccountId and SCIM user;
   unbound records are visible only to the tenant Owner.
   The user supplies their own AccountId from their authenticated session;
   no global email/account-search endpoint is introduced. An Owner may share a
   dashboard `/sign-in/sso/{connectionId}` URL: the connection ID is a routing
   identifier, never an authorization secret, and login still needs browser proof.
5. That account, in a local session with CSRF and fresh password proof,
   deliberately starts Link. After SAML proof of the pre-provisioned immutable
   object ID, a same-origin confirmation rechecks the same local session,
   one-use authorization, active/unblocked directory state and connection version. Atomically
   bind and create ReadOnly membership with no grants. Reject automatic adoption
   of an existing manual membership; no implicit email/account merge. Local
   operator accounts cannot be linked; granting operator access to an account
   with a live enterprise binding is likewise denied until explicit unlinking
   under a separately approved recovery procedure. Emails are descriptive,
   not binding proof.
6. Subsequent Login can only resolve an existing live binding and active member.
   Mint a **tenant-scoped** opaque session (maximum eight hours, capped by a
   supplied IdP session expiry). Fresh login replaces the browser session token;
   it does not upgrade/preserve a global session. UI warns before replacement.
   Signing in locally is required to switch to global account context.

Use Entra's signed `objectidentifier` claim as the immutable subject, matching
SCIM `externalId <- objectId`; retain NameID for protocol validation, **not**
as an assumed object ID. UPN/email/NameID may differ or change. Validate one
well-formed object GUID and configured issuer, not a caller-selected claim.
Microsoft documents the object identifier independently from pairwise NameID
in its [SAML protocol](https://learn.microsoft.com/en-us/entra/identity-platform/single-sign-on-saml-protocol).

Federated sessions return `isOperator=false`; deny admin/MFA, password/email
changes, account linking, organization creation and invitation acceptance.
List organizations returns only the bound tenant. Project-ID-only APIs resolve
the tenant before authorization; IDs in body/query, downloads/CSV, BYOK,
usage, wallet and all read/mutation services obey the same scope. Route filters
alone cannot guard direct application calls: an explicit authentication-context
contract must reach authorization services, default-denying unknown scope.
Local sessions retain current membership-based authorization, including active
SCIM-managed memberships. Deactivation denies that tenant even on local sessions;
other tenants and the global account remain usable.

Connection disablement revokes scoped sessions/pending flows immediately and
rejects new SSO, but does not deactivate directory members or revoke their
Gateway keys by itself. SCIM may continue deprovisioning while SSO is disabled
using a separately active credential. Rotating issuer/tenant is replacement,
not retargeting bindings; disable and explicitly re-provision/re-link. No
automatic recovery login, emailed impersonation link or external-MFA operator grant.

## 6. SAML browser and cryptographic profile

Proposed SP-initiated Redirect AuthnRequest / POST Response profile:

- Server-generated random XML-safe request ID, five-minute flow TTL, opaque
  RelayState at most 80 bytes bound to one flow/browser/connection/version.
  Relative dashboard return paths are server-allowlisted; never trust a URL
  supplied by an assertion or arbitrary RelayState.
  Request persistent NameID; validate a nonempty persistent NameID in the
  signed assertion without treating that pairwise value as the Entra object ID.
- A separate short-lived `Secure; HttpOnly; SameSite=None` **correlation-only**
  cookie permits the IdP cross-site POST. It contains no application session
  privilege. Existing session/CSRF cookies remain `SameSite=Strict`.
  Reject absent correlation; do not degrade to state-only acceptance. One
  outstanding flow per browser is sufficient initially; starting another
  cancels the earlier one. Retain correlation between ACS and Finish; clear
  it only on final consumption, cancellation or expiry.
  Use host-only `__Host-` cookie names (Path=/, no Domain), at least 256 bits
  of randomness, and independent cookie names/secrets from existing sessions.
- ACS alone bypasses browser CSRF headers, replacing them with correlation,
  signature, issuer, recipient/audience and InResponseTo validation. It does
  not depend on a Strict session cookie arriving on the cross-site POST.
  ACS returns a no-store 303 to a same-origin finish page; a hash-bound,
  one-use completion receipt is in a Secure/HttpOnly/SameSite=Strict cookie,
  never a URL/query. ACS marks Validated, not a logged-in application session.
  Finish POST needs exact same-origin Origin, flow-start CSRF and browser
  correlation; Link/Test also need the original local session/CSRF/password-proof
  context. Test records only version-bound test success, never a customer login.
  Login rotates session and CSRF.
- Accept one successful response and one signed bearer assertion, RSA-SHA256
  or stronger explicitly allowlisted equivalent; initial RSA keys at least
  2048 bits. Trust pinned currently valid keys only; reject SHA-1/unsigned,
  duplicate IDs, wrapping, external references, DTD/entities/XInclude and
  extra/encrypted assertions outside the supported profile. Signed requests
  use a configured SP key; never accept a certificate just because it appears
  in KeyInfo. Use locally bundled schema validation and a maintained library.
- Validate the exact signed assertion used for identity: issuer, audience,
  SubjectConfirmation bearer/Recipient/InResponseTo/expiry, Conditions and
  AuthnStatement. Require response Destination/InResponseTo to match flow;
  if response is signed, validate it too. Unknown mandatory conditions fail.
  No unsolicited response, artifact resolution or claim-based tenant routing.
- Allow at most 120 seconds clock skew. Require new IssueInstant within the
  five-minute request window plus skew; honor assertion/confirmation expiry
  without assuming the IdP issues five-minute assertions. Keep replay hashes
  through the latest accepted assertion expiry plus skew; reject validity
  windows exceeding two hours. PostgreSQL unique insertion wins across nodes.
  SAML login must not be treated as recent local operator MFA.

These are a deliberately bounded product profile of the
[OASIS browser SSO profile §4.1](https://docs.oasis-open.org/security/saml/v2.0/saml-profiles-2.0-os.pdf),
not a claim to support every SAML binding. Hostile XML and signature fixture
review must follow [OWASP SAML guidance](https://cheatsheetseries.owasp.org/cheatsheets/SAML_Security_Cheat_Sheet.html).
Pin up to two explicitly approved IdP signing certs for rollover, record actor,
reason and validity, test the new version before retirement, and support
emergency removal/disable. Never auto-trust remote metadata or block an auth
transaction on metadata/CRL fetching; certificate compromise disables the connection.

## 7. Proposed HTTP contracts — not implemented OpenAPI

All routes are on Management behind TLS, not Gateway. Browser/control routes
keep current JSON errors, `no-store`, CSRF on authenticated mutations and
401/403/404 distinctions. IDs are UUIDs. Mutations require expected config/
resource version (`If-Match`) on Owner updates to existing versioned resources;
stale versions return 412. Flow requests use one-use state instead; SCIM
If-Match remains optional as specified below. Do not generate a
client advertising these routes until their implementation task.

### Owner control and browser federation

Prefix `M = /management/v1/organizations/{organizationId}/identity`:

| Proposed route | Contract / authority |
|---|---|
| `GET M/connection` | Owner-only safe config/version/status, cert fingerprints, last test; no token/private key. |
| `PUT M/connection` | Local Owner + CSRF + fresh password; expected version (create `If-None-Match: *`), issuer/tenant/SSO URL and bounded certs; 201/200; forces Draft, invalidates pending flows. |
| `POST M/connection/test` | Same local Owner proof; starts Test for exact version; 200 `{redirectUrl, expiresAt}` pointing only to pinned IdP. |
| `POST M/connection/enable` / `disable` | Same proof + 8–500 character reason; tested version needed for enable, 204 or 409. Disable invalidates flows/scoped sessions atomically. |
| `POST M/scim-credentials` | Same proof; expiry at most 90 days, one 256-bit random token shown once, 201 + metadata. Caller request UUID deduplicates issuance; replay 409, never returns plaintext again. |
| `DELETE M/scim-credentials/{id}` | Same proof/reason; 204, effective on next authenticated SCIM request; at most two active credentials for rotation. |
| `GET M/users` | Owner-only bounded provisioning metadata: SCIM ID, profile, desired/effective state, linked account and block reason; no raw assertion/token. |
| `POST M/users/{id}/link-authorizations` | Local Owner proof; exact existing verified AccountId, 10-minute one-use authorization returned only as metadata (no reusable URL secret); 201/409. |
| `POST M/users/{id}/resume` | Local Owner proof/reason/current version after directory reactivation; releases local block only, ReadOnly/no grants; no old key/session resurrection. |
| `GET /management/v1/auth/saml/challenge` | No login or tenant discovery; no-store same-origin bootstrap returns a random five-minute flow-start CSRF token paired with a Strict readable cookie. No permissive CORS. |
| `POST /management/v1/auth/saml/{connectionId}/start` | `purpose=Login` or `Link`; same-origin Origin and header/cookie flow-start CSRF; 200 `{redirectUrl, expiresAt}` to pinned IdP. Link additionally requires local session/CSRF/password and matching authorization. Disabled/unknown share generic errors. |
| `POST /management/v1/auth/saml/{connectionId}/acs` | Form SAMLResponse + RelayState only; correlation/signature rules above; no application auth from unvalidated fields. |
| `POST /management/v1/auth/saml/finish` | One-use cookie receipt and same-origin flow proof; creates scoped Login, confirms Link or records Test only; 204 then UI navigation. Invalid/expired proof 401; state conflict 409. |

SSO-004 implements only the Owner-initiated Test branch of shared ACS/Finish
and metadata, with other purposes denied. SSO-005 extends those same handlers
with Login/Link after SCIM writes and scoped authorization are ready; it does
not add duplicate routes or require an unfinished Login endpoint to test trust.
The frontend performs an explicit top-level navigation to the returned
Redirect-binding URL; API fetch must not rely on following a cross-origin 302.
That temporary AuthnRequest/RelayState URL is never stored or logged; it is not
a reusable application session. ACS alone uses a 303 back to the same-origin
finish page, with no assertion or completion secret in that URL.

Public SP metadata is fixed server-generated identifiers/bindings/public certs
at `/management/v1/auth/saml/{connectionId}/metadata`; no tenant member data,
issuer fetch proxy or secrets. Expose only intended exact SAML/SCIM routes
through edge; no new `/management/*` wildcard proxy. Current edge already has
a broad `/management/v1/` route with 5 MiB global body allowance and a separate
8 KiB password-auth matcher; new path-specific SAML bounds/deadlines must take
precedence, not inherit those generic values. SCIM's non-v1 path needs its own
bounded route allowlist. Do not alter payment ingress/replay rules.
Body fields with global
account or tenant overrides are rejected. Owner re-proof uses existing password
hasher with abuse bounds; it is not a new global operator-MFA alternative.
Each sensitive Owner mutation and Link start carries `password` in its bounded
JSON body and checks the active verified local account's current hash; no
reusable proof token is returned. For Link/Test, retain only proof time/session
binding in the flow and require it to remain within five minutes at Finish.
Neither the plaintext password nor its login request is persisted or logged.

### SCIM service provider

Base `S = /management/scim/v2/{connectionId}`. Only a dedicated scoped SCIM
Bearer credential is accepted, independent of cookies, Gateway keys and operator
tokens. Credential connection/organization must match route and all queries.
No CORS/browser-cookie authentication. `application/scim+json`, TLS, no-store;
derive Locations from configured external origin, never untrusted Host headers.

| Route | Supported behavior |
|---|---|
| `GET S/ServiceProviderConfig`, `/Schemas`, `/ResourceTypes` | Authenticated truthful Users-only discovery; patch/filter/ETag supported; bulk/sort/password changes false. No Groups type. |
| `GET S/Users`, `GET S/Users/{id}` | Bounded list/detail including inactive users, excluding deleted; ListResponse with totalResults/startIndex/itemsPerPage/Resources. Missing or other-tenant ID 404. |
| `POST S/Users` | 201 + Location/ETag and immutable server ID; duplicate live userName/externalId 409 `uniqueness`, no duplicate effects. No global account/member activation. |
| `PATCH S/Users/{id}` | SCIM PatchOp, atomic Add/Replace/Remove for supported mutable fields, case-insensitive op/attribute names; 200 resource + ETag. Pathless object Add/Replace supported. |
| `PUT S/Users/{id}` | Full replacement of supported mutable profile; required userName/externalId/active remain valid; preserves server ID and binding. 200 resource. No implicit identity retarget. |
| `DELETE S/Users/{id}` | 204 after atomic deprovision + tombstone; later requests to deleted ID 404. Repeats have no further effects. |

Required create/profile: core User schema, `externalId` Entra object GUID,
nonempty `userName` (max 320), explicit boolean `active`. Optional displayName/
name parts max 200, up to five bounded emails; no password storage. Store raw
supplied strings; separate normalized lookup keys implement case-insensitive
userName uniqueness. Require immutable externalId on update; email/UPN changes
never rebind or change global account email/verification. Ignore supplied
read-only id/meta; unsupported role/group/extension writes fail explicitly and
cannot elevate access. Configure Entra mappings to this supported schema,
including `externalId <- objectId`; do not use its example mailNickname mapping.

Filter grammar initially supports a single `eq` on `userName`, `externalId`,
`id`, or `emails[type eq "work"].value`; safe parser/parameters, no arbitrary
SQL. All lookup keys used for uniqueness can be queried. Invalid/compound
filters 400 `invalidFilter`, not an empty success. Pagination startIndex >=1,
default count 100, cap 200 (count=0 returns count only), deterministic ID order;
attributes/excludedAttributes project only supported fields, preserving required
IDs/schema metadata. No claim of cross-page snapshot consistency during edits.
PATCH supports name/displayName/userName/active and emails (whole collection
or work-email valuePath); required field removal or subject replacement returns
400 `mutability`/`invalidValue` and rolls back the whole patch.

SCIM error envelope has Error schema, **string** status and safe detail;
unknown routes/Groups 404, disallowed methods 405, wrong media 415, bounds 413,
credential 401 with WWW-Authenticate, wrong token scope 403, invalid syntax/value
400, uniqueness 409, stale If-Match 412, throttled 429 + Retry-After, dependency
unavailable 503. Never emit a dashboard login redirect or ASP.NET HTML error.
Strong ETag/version comparison when supplied; do not require Entra to send
If-Match. Serialize unconditional writes and use the conservative recovery
barrier below because SCIM does not prove causal order of those writes.
SCIM create duplicates deliberately return 409, not a fabricated replayed
201; clients recover by querying their stable externalId/userName. DELETE
retries return 404 after the first 204 while preserving idempotent effects.

Protocol envelope/lifecycle behavior follows
[RFC 7644 §§3–4](https://www.rfc-editor.org/rfc/rfc7644.html);
schema/attribute metadata follows
[RFC 7643](https://www.rfc-editor.org/rfc/rfc7643.html).
Entra is the **client of these SaaS endpoints**, not the unrelated Entra
inbound Graph SCIM API. Its [outbound provisioning guide](https://learn.microsoft.com/en-us/entra/identity/app-provisioning/use-scim-to-provision-users-and-groups)
provides the reference request shapes; Users-only, mapping and actual response
compatibility still require fixture/live qualification.

## 8. Lifecycle, concurrency and financial boundaries

Directory desired state and effective tenant authorization are independent:

The revocation guarantee starts when UZLLM commits the validated lifecycle
change, not when an administrator disables a person in Entra. Provisioning
delivery may lag or stop; monitor last successful provisioning/failed callbacks
and provide immediate local revocation. No instantaneous IdP-to-Gateway SLA
or complete directory-sync proof is inferred from an HTTP 2xx response.

| Event | Directory state | Effective access |
|---|---|---|
| Create active user | Active, unlinked | None until explicit link; then ReadOnly/no grants. |
| Profile rename | Existing | Unchanged immutable binding; not account-email update. |
| `active=false` | Inactive | Revoke member/grants/scoped sessions/flows and tenant creator keys; set local recovery block. |
| Repeated deactivate | Inactive | No repeated audit mutation/key revocation; success reflects existing state. |
| `active=true` after inactive | Active | Still locally blocked until Owner resume; never restore privilege/keys. |
| Owner resume | Must be Active | ReadOnly/no grants; fresh SSO required. New keys only after an authorized grant; old keys stay terminal. |
| Manual Team revoke | Preserve directory desired state | Set local block; SCIM profile/active writes cannot undo it. Team/invitation routes cannot bypass this. |
| Delete | Tombstoned, hidden | Same revocation effects; no financial/history deletion. |

Recreate after delete may reuse userName with a **new server resource ID**;
it must not automatically reuse the old account binding, grants or credentials.
Tombstone subject history remains security/audit evidence, not a live uniqueness
conflict. Explicit new link authorization/dual proof is required. Initial
profile forbids SCIM-managed Owner promotion/adoption, so an IdP cannot remove
the recovery Owner or defeat last-owner protection. Admin/Developer privilege
is granted only locally and is not restored by reactivation.
For an unlinked resource, Owner resume clears only the local block; it cannot
create a membership/account or bypass the later explicit Link ceremony.

### Exact transactional barriers (proposed, not existing behavior)

Use the existing organization row as the lifecycle fence: **shared row lock**
for enterprise admission/dispatch and authorized key/membership-changing use
cases, **exclusive row lock** for deprovision, matching existing Team locking.
Acquire it before mutable authorization reads and other write locks. Shared
readers can run concurrently; hold it only for short DB work. No network/stream
await while holding the fence. Consistent order: organization, membership/
binding, existing financial wallet/credential/budget/key locks in their existing
order where relevant, session/attempt changes, audit. Lifecycle commands do
not acquire wallet locks; never reverse existing financial lock ordering.
Every path that can regrant access/create/reactivate/rotate keys or mutate
managed membership must participate and recheck current state **inside** its
transaction. Index/bound creator-key scans; no async partially revoked success.
Connection/token changes and Link/Login/Test Finish also use this fence;
after acquiring it, revalidate connection version/status, flow/binding
generation and current SCIM credential where applicable. Pre-transaction
authentication alone cannot authorize a mutation after token revocation or
connection disablement has committed. Only Test may operate on a Draft
connection; Login/Link require Enabled. Connection replacement and credential
revoke take the exclusive fence, so stale in-flight writes cannot restore access.

Deprovision transaction: authenticate current SCIM token/version; lock
organization exclusively; re-read scoped resource/binding; set directory/block
generation and revoke membership/grants; terminally revoke all matching
creator keys using project→organization join (including rotated generations);
invalidate scoped sessions/flows; append minimal audit and transactional outbox
notification; commit, then return success. Audit or DB failure rolls back all
effects and returns retryable 503, never 2xx with partial withdrawal. Already
revoked keys remain revoked; status/rotate endpoints must reject resurrection
even by a different authorized manager. This creator rule applies only to
members adopted into this enterprise lifecycle, not every manual Team revoke.

Financial admission obtains the shared fence before live key/lifecycle checks
and the existing reservation transaction. Each **new attempt**, including
failover, obtains/rechecks that fence when persisting Dispatched. If deprovision
commits first, deny new admission/dispatch and finalize unused holds through
existing no-dispatch release. If Dispatched commits first, the operation is
already authorized in flight; its eventual HTTP send may race with deprovision
after that commit. Do not promise physical recall of a network packet or zero
provider cost after deactivation. No provider HTTP call is inside the transaction.

Existing output/unknown usage continues through evidence, settlement and
reconciliation even for revoked keys/members; no fallback after partial output,
no refund/release solely due to identity revocation, no rewriting posted balance,
ledger, FX, debt, budgets or financial retention. Duplicate SCIM delivery never
credits/debits a wallet. API-key revocation is authoritative PostgreSQL state;
Redis invalidation/alerts are advisory post-commit side effects.
BYOK credentials remain organization-owned resources with their existing grants;
this lifecycle does not delete shared provider credentials or pretend to revoke
a secret at the upstream provider. Operators/Owners must separately rotate
upstream credentials a departed person knew, under the existing secret runbook.

No SCIM ordering guarantee is invented: absent If-Match, a delayed `active=true`
can change desired state but cannot remove a local recovery block. An Owner's
explicit resume can race with newer IdP intent; process a later deactivation
again and retain audit. If strict causal synchronization is later required,
define an IdP-supported version/ordering contract separately.

## 9. Threats, bounds and operations

| Threat / failure | Required control and evidence |
|---|---|
| Tenant IdP impersonates a global multi-tenant account | Federated session scoped to connection/tenant; cross-tenant ID/discovery/download/direct-service denial fixtures. |
| Reused email/UPN or malicious SCIM account adoption | Explicit AccountId-targeted authorization + local password and signed immutable subject proof; no implicit merge/JIT. |
| XML wrapping/XXE/key substitution/replay/login CSRF | Library validation, pinned trust, bounded parser, correlated single-use PostgreSQL flow/replay consumption across two nodes. |
| IdP role/MFA claims grant operator access | Never derive roles/operator/recent MFA from assertion or SCIM; test linked operator rejection and admin endpoints. |
| Leaked SCIM token | Hashed tenant token, expiry/rotation/revoke, no Gateway/admin scope; no token in telemetry, URL or browser persistence. |
| Trust config takeover/metadata SSRF | Local Owner/fresh proof/CSRF/version + audit; bounded upload, no arbitrary fetch/KeyInfo dereference, exact approved Entra endpoints. |
| Deprovision races with key issue/rotation/reserve/failover | Shared/exclusive tenant fence, fresh reads, terminal key marker, DB race/deadlock/rollback tests. |
| IdP/DB/Redis outage or bad clock | Fail new SSO/SCIM operations closed; existing local recovery access works subject to normal dependencies; financial finalization remains independent. No cached grant fallback. |
| Stale reactivation or connection replacement | Local recovery block/generation, no restored credentials; new issuer requires explicit reprovision/relink. |

Proposed operator-configured hard bounds: SAML form 256 KiB total / 128 KiB
decoded XML / depth 32; metadata 128 KiB; SCIM body 32 KiB, max 20 PatchOp
operations; profile fields bounded above. Reject duplicate critical fields and
oversize input before parsing. Limit start/ACS/finish to 30 attempts per source
IP/minute and 10 per connection/browser flow/minute; SCIM 120 requests per
credential/minute and concurrency 4, plus an unauthenticated IP backstop.
Use trusted edge client-IP policy, hashed identifiers and distributed limits;
Redis limiter loss returns 503 without auth mutation. Tune only from qualified
evidence, not unrestricted per-tenant settings. HTTP deadlines 10 seconds,
five-minute browser flow, bounded DB lock wait 5 seconds; limiter/lock failure
must remain distinguishable from invalid proof. Keep live in-flight financial
recovery independent from these new admission checks.

Audit connection/cert/token changes, link authorization/consumption, effective
membership/key withdrawal, local resume and access denials. Record org,
connection/resource IDs, local actor **or SCIM credential identity**, reason,
version/generation and request ID; a provisioning token is not a fictitious
account actor. Existing Audit contract needs an additive machine-actor
representation rather than using Owner/operator ID. Repeated no-op reads/
PATCH attempts use bounded metrics/structured security events, not duplicate
successful mutation audit entries. Do not log assertions, RelayState, NameID,
raw profile emails, bearer tokens, password proofs or XML/SCIM bodies.

Metrics: bounded result/reason/flow-type counters, latency, replay denials,
deprovision failures, inactive-member key rejects, expiring trust/token inventory
and cleanup/outbox age; tenant/user/cert/request IDs belong in protected
diagnostics, not metric labels. Alert on invalid-signature spikes, revocation
failures, cert/token expiry and repeated blocked reactivation. Provide disable/
token-revoke/local-owner recovery and key-impact runbooks before activation.
Retention of identity/profile/audit/tombstone data needs approved legal schedule;
do not retain raw assertions as ordinary diagnostics. Restore quarantined data,
revoke restored sessions/tokens/flows and reconcile lifecycle revocations before
exposure; do not resurrect offboarded access from a backup.

## 10. Required acceptance scenarios — designed, not executed

All fixtures below are **future work**; today's existing tests do not prove them.

| ID | Observable acceptance / required test type |
|---|---|
| ENT-01 | Explicit link succeeds only after provision, matching authorized AccountId, local password/CSRF and SAML object ID; email match alone/unknown subject/JIT denied. Unit + real HTTP/PG. |
| ENT-02 | SAML session cannot list/read/mutate other tenants or global account/create-org/invite/admin/MFA, even when its AccountId has those local privileges. Include project-ID/query/body/export/direct-service paths. Contract + PG. |
| ENT-03 | Valid signed assertion; wrong key/issuer/audience/destination/recipient/object ID; duplicate ID/wrapping/XXE/oversize/SHA-1/extra assertion/unsigned failures never create session. Offline hostile fixtures. |
| ENT-04 | Wrong/missing browser correlation/RelayState/InResponseTo, unsolicited reply, clock boundaries, consumed receipt and replay across two nodes fail; exactly one flow/session commit, audit rollback. PG + browser. |
| ENT-05 | Entra-shaped Users create/get/filter/pagination/empty list/PatchOp/PUT/discovery, schema/media/error/ETag parity; Groups/roles unsupported truthfully. Contract fixtures from official examples. |
| ENT-06 | Duplicate create 409; PATCH atomic rollback/no-op/retry; deactivate twice; delete then 404; deleted username recreated with fresh unbound identity. PG races. |
| ENT-07 | Token expired/revoked/wrong tenant, malformed filter/injection/oversize/wrong auth scheme and Redis outage fail without lifecycle mutation/data leakage. Contract + Redis/PG. |
| ENT-08 | Two organizations share global account: deactivate A removes only A membership/grants/creator keys/scoped sessions, not B/global password access/financial history; rotated old keys stay denied. PG + HTTP. |
| ENT-09 | Race deprovision against key create/rotate/re-enable, invitation accept, local role/grant/resume and admission/each failover dispatch. Whichever fence commits first determines allowed effects; no surviving post-barrier key/grant. PG two-node. |
| ENT-10 | Deactivate during SSE/disconnect/provider timeout and failed settlement: no new attempt, partial output never retried, verified/unknown usage reconciles; ledger/holds/debt exact and no extra refund. Real Gateway/PG/provider fixture. |
| ENT-11 | Deactivate→delayed reactivate/manual Team revoke→SCIM active: local block persists; explicit Owner resume gives ReadOnly/no grants, fresh session/new keys only. Managed Owner adoption/promotion denied. PG. |
| ENT-12 | Trust enable/test-version mismatch, cert overlap/expiry/emergency disable, token rotate/revoke and DB/audit/cancellation/unknown commit recovery are fail-closed and observable; secrets absent. Unit + PG/HTTP. |
| ENT-13 | Dashboard settings/link/sign-in/disabled/recovery/error and tenant-switch flows; show-once token not persisted, keyboard/focus and uz/ru/en; real backend, not mocked routes alone. Browser + real stack. |
| ENT-14 | Exact reference Entra tenant/app mappings, Users provision/deactivate/reactivate/delete, signed login/cert rollover and token rotation; independent security review, restore/offboarding rehearsal and incident owner evidence. External qualification, never fabricated by fixtures. |

Before building fixtures, replace illustrative GUIDs/users/keys with synthetic
test identities, record source URL and review date, and avoid copying production
assertions/secrets. Test behavior, not only XML/JSON string snapshots. Offline
RSA signatures and disposable PG/Redis/two-process fixtures do not require
live merchant/IdP credentials; ENT-14 does require a reference tenant later.

## 11. Proposed follow-on task register

All tasks below are **P2 / Proposed / not approved to execute**. Promotion
requires approval of this specification plus explicit feature scheduling while
P0 gates remain open. The listed order is dependency-safe; SSO-003 could run
after SSO-001 independently of SCIM-001, but default execution stays sequential.
Each task ends with tests, exact evidence/docs, generated API/client parity
when routes change, task-only commit and user approval before the next task.

| ID / objective | Dependencies; requirements | Likely modules/files | Acceptance / required tests / completion definition |
|---|---|---|---|
| SSO-001 · Persist federation identity, flows and scoped session model | Approved ENTERPRISE-001; FR-140–142, ADR-025 | Identity/Organizations Contracts; Identity Domain/Infrastructure; Persistence entities/config/migrations; Audit machine-actor contract | Explicit migration/backfill/constraints and additive contracts/ports, no auth routes. ENT-01/02/04 schema, uniqueness/rollback and runtime-DDL denial tests; architecture tests reject cycles/foreign Infrastructure access. Complete only with migration downgrade/data-protection evidence. |
| SSO-002 · Enforce tenant-scoped authentication context | SSO-001; FR-142/144, NFR-044 | Identity middleware; Organizations/Projects authorization; Management global/project-ID/read/export/admin routes | Existing local sessions unchanged; seeded Federated sessions default-denied globally/outside tenant in both HTTP and application calls; ENT-02/08, existing tenant/operator suite. Complete only with enumerated endpoint/service permission matrix, not a single route filter. |
| SCIM-001 · Add atomic member/key deprovision and dispatch fence | SSO-001, SSO-002; FR-145–146, FR-130–133 | Organizations/ApiKeys/Identity/Audit contracts; thin host port adapters; shared Persistence; Billing admission and Usage dispatch | No SCIM endpoint yet. Terminal creator-key evidence and lifecycle barriers, no cyclic module reference/network transaction or rewritten financial rules. ENT-08–11 race/SSE/recovery/deadlock/rollback tests; measure same-tenant shared-lock contention. Complete only when all three inference operations and failover obey fencing. |
| SSO-003 · Validate bounded SAML profile offline | SSO-001; FR-141/147, NFR-042/046 | Identity Infrastructure validator/library; Unit/Contract synthetic fixture corpus | Evaluate maintained .NET 10 library/license; narrow normalized contract, no endpoint. ENT-03/04 crypto/parser/correlation/clock fixtures including library hostile cases. Complete only with reviewed trust/profile support matrix and no external XML/key fetching. |
| SSO-004 · Expose Owner trust and provisioning-token controls | SSO-002, SSO-003, SCIM-001; FR-140/143/147 | Identity/Organizations Application; Management; edge; api/client artifacts | Draft/test/enable/disable, safe read/token create/revoke and fresh password proof/versioned audit. ENT-07/12 plus exact edge/body/CSRF/secret/cache checks. Test flow cannot mint membership/session. Complete with token/trust rotation runbook and generated contract parity. |
| SCIM-002 · Expose authenticated Users discovery and reads | SSO-004; FR-143, NFR-044/046 | Organizations Contracts/Infrastructure; Management SCIM auth/limits/edge | Users-only metadata/list/detail/filter/projection/pagination and SCIM error envelope; no mutations yet. ENT-05/07 scope, query/injection/count/media fixtures and Redis loss. Complete with explicit supported-schema/discovery contract and no browser/Gateway-token acceptance. |
| SCIM-003 · Expose Users writes and conservative restoration | SCIM-001, SCIM-002; FR-143–146 | Organizations SCIM orchestration; Identity/ApiKeys/Audit contracts; Management | POST/PATCH/PUT/DELETE invoke existing atomic lifecycle, desired/effective state and tombstones. ENT-05/06/08–12 real PG retries/races/ETag/no-op/audit failures. Complete with synthetic Entra request/response corpus and no financial mutation. |
| SSO-005 · Expose explicit linking and scoped browser login | SSO-002, SSO-003, SSO-004, SCIM-003; FR-141–142/144 | Identity/Organizations Application; Management SAML routes/cookies; edge; api/client | Owner link authorization/users/resume plus start/ACS/finish/metadata, no JIT/implicit adoption. ENT-01–04/11/12 real HTTP/PG multi-process replay and real cross-site cookie tests. Complete only when invalidation/deprovision prevents stale finish and no SAML session becomes global/operator. |
| FRONTEND-011 · Add enterprise settings and deliberate account linking | SSO-005, SCIM-003, FRONTEND-010, DX-002; FR-140–147, ADR-016 | dashboard settings/auth, shared api-client, translations, browser specs | Owner safe connection/profile/credential views, key-impact warning, link/SSO/recovery/no-project-grant states, no mandatory-SSO control. ENT-13 accessible/localized and real-stack flow; use the preserved root mockup's visual style with improved UX/accessibility, not a root mockup rewrite. Complete with screenshots and no tokens/assertions in browser persistence/cache. |
| ENTERPRISE-003 · Qualify first Entra profile and operations | FRONTEND-011 (transitively all SSO/SCIM tasks above); FR-147, NFR-043–046/064/066 | docs/enterprise validation; deployment tests/runbooks; reference IdP configuration | ENT-01–14 evidence for exact deployed revision, cert/token rotation/offboarding/restore and security review. Offline qualification may pass separately, but task remains Blocked if live Entra/owner evidence missing. Complete only with documented residual risks and explicit enterprise activation approval; never passes unrelated paid-launch gates. |

Completion of ENTERPRISE-001 means an **approved specification and approved
decomposition**, not completion of any row above. First proposed implementation
would be SSO-001 only after approval/promotion; no later P2 feature is selected
by this document.

## 12. External prerequisites and review decisions

Not required to draft/test protocol fixtures: live IdP credentials, merchant
accounts, or paid-traffic authorization. Required before enterprise activation:

- Reference Entra tenant/app administrator, application assignment and explicit
  mappings (`externalId <- objectId`, signed objectidentifier claim), trusted
  certs, HTTPS callback origin/DNS and test users; verify the actual outbound
  provisioning client profile rather than assuming broad SCIM certification.
- Signed enterprise support/privacy/identity-profile retention terms, named
  tenant IdP and local recovery Owners, rotation/offboarding/incident ownership,
  confirmed acceptance of creator-key workload interruption and optional SSO.
- ENT-14 interoperability/security/restore evidence plus all applicable
  [paid-launch gates](18_PRODUCTION_READINESS.md). No SAML/SCIM certification,
  mandatory-login enforcement or enterprise SLA claim from this specification.

Review explicitly: (a) local Owner-controlled link authorization + dual proof,
(b) tenant-only SSO sessions and local-only operator boundary, (c) managed
ReadOnly/no grants and no managed Owner in this first profile, (d) conservative
reactivation/terminal keys, (e) bounded wire/crypto profile and ten proposed
implementation tasks. These details remain **proposed until user review**.

## 13. Specification validation and limitations

Validation on Windows/PowerShell, .NET 10 and Docker Desktop, 2026-10-18,
against implementation revision `5bd4b1a` with only these six documentation
paths changed:

| Check | Actual result |
|---|---|
| `dotnet restore UZLLM.slnx` | PASS, exit 0. |
| `dotnet build UZLLM.slnx --configuration Release --no-restore` | PASS, exit 0; zero errors, one existing ASPIRE010 warning (`AspireUseCliBundle=false`); no warning suppression/config change. |
| `dotnet test UZLLM.slnx --configuration Release --no-build --logger trx --results-directory artifacts/test-results/enterprise-001-20261018` | PASS, exit 0; **Passed: 1028, Failed: 0, Skipped: 0**, sixteen assemblies. PostgreSQL 234/234 (16 minutes 12 seconds). All sixteen final TRX reports independently checked for unique assembly coverage, outcome and matching totals at 2026-10-18T06:48:31Z. |
| PowerShell relative-link/heading/task-register check | PASS: 76 local links/anchors; ten unique dependency-ordered proposed tasks, eight proposed FRs and fourteen acceptance scenarios. Other task/history rows unchanged. Requirements/ADR references are not treated as task dependencies. |
| Task scope and whitespace | Exactly six documentation paths, no implementation/configuration/migration/generated-client edits; `git diff --check` PASS. Staged whitespace/scope reviewed before commit. |

The 2026-10-17 initial baseline attempt has no captured overall test exit/result
after the terminal session expired across the interruption; it is **not**
claimed as a passing full run. The 2026-10-18 rerun records TRX results under
the ignored local artifact directory above. Do not treat partial report counts
as a final baseline or substitute the prior REFUND-002 run for current evidence.

No SAML/SCIM behavior has been implemented or tested by this documentation
task; ENT-01–14 are designs, not passing feature tests. No frontend/browser,
live IdP, protocol interoperability or production qualification is claimed.
The task remains In Progress pending specification approval; no proposed
implementation task or next P2 specification is started.

Sources reviewed 2026-10-17: linked OASIS SAML browser profile, IETF SCIM core/
protocol RFCs, OWASP SAML guidance, Microsoft Entra SAML protocol and outbound
SCIM guide. Local policy choices, bounds, session isolation, key barrier and
task decomposition are this proposal, not vendor guarantees. Live compatibility
and library-specific cryptographic support remain unverified until their tasks.
