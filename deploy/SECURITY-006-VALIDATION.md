# SECURITY-006 rotation qualification

Local automated drill uses disposable PostgreSQL and temporary PFX/key-ring
fixtures; it is **not** proof that production secret distribution or live
certificate backup is complete.

Repository validation on 2026-10-02: `dotnet restore UZLLM.slnx` and
`dotnet build UZLLM.slnx --configuration Release --no-restore` passed with
zero build warnings/errors. `dotnet test UZLLM.slnx --configuration Release
--no-build` passed (including 172 PostgreSQL integration tests).
`docker compose -f deploy/compose.production.yml -f
deploy/compose.keyring-rotation.yml --profile rotation config --quiet`
passed using non-secret placeholder paths; the merged manifest retains all
Management/rotation secrets. The exact rotated-credential fixtures were also
run as a filtered 2/2 integration suite.

| Scenario | Evidence |
|---|---|
| Staged provider/BYOK KEK cutover, bounded batch, rollback | `ProviderSecretRotationIntegrationTests.Bounded_rotation_and_rollback_preserve_platform_and_tenant_secrets_with_audit` |
| Wrong old KEK, missing operator, tenant substitution | Same integration test; no row/audit mutation on rejection |
| Old MFA/email payload readable after certificate rollover and rollback | `OperatorKeyRingTests.Certificate_rotation_and_rollback_keep_old_operator_and_email_payloads_readable` |
| Missing previous PFX fails closed | `OperatorKeyRingTests.Certificate_rotation_rejects_missing_previous_private_key` |
| Session key creation requires MFA and records start/completion | `ProviderSecretRotationIntegrationTests.Operator_keyring_rotation_requires_mfa_and_records_start_and_completion` |
| Old KEK removed too early fails closed | `ProviderCredentialSecurityTests.Removing_an_old_provider_key_before_rewrap_fails_closed` |

Staging operations drill before launch:

1. Record revision, database backup/PITR point, key-ring backup, operator/change ticket and both node IDs; never copy key bytes into evidence.
2. Stage both KEKs/certificates, restart each Gateway/Management/Worker node, and prove old inference, MFA and queued email on both nodes.
3. Change active KEK; prove new credentials are tagged `v2` and old credentials still resolve; run one 1-row batch and then batches of at most 100, recording version-count SQL and audit IDs after each.
4. Create a new Data Protection key with the restricted rotation job; restart both Management/Worker replicas to refresh the shared ring, prove old/new protected payloads on each node, and match started/completed audit IDs to the new key ID.
5. Roll active versions/certificate back with **both** old and new decryption materials retained; repeat old/new reads. Restore the original active version after qualification. Confirm no secret appears in logs/traces.
6. Archive only version counts, audit IDs, elapsed time, node pass/fail and key-ring backup restore result. Do not claim completion if any node cannot read either generation or the audit has an unmatched started event.

Retirement of old KEKs/PFX files is a separate approved operation after zero
live references **and** backup-retention expiry; never infer safety from an
empty current-database inventory alone.
