# PRIVACY-002 retention and restore validation

## Policy configuration

The Worker accepts `Retention__ScheduleReference`,
`Retention__DiagnosticDays` (default 30), `Retention__OperationalDays` (default
90), `Retention__AuditDisposition=LegalHold`, and
`Retention__FinancialDisposition=LegalHold`. Supply the reference only after
legal/security approval of the schedule; blank means non-payload minimization
is disabled and logged. A reference is a pointer to signed policy evidence,
not proof that an operator has signed it. Duration values must be 1–3650 days.
Payload opt-out and expiry continue independently. Audit and financial records
are not auto-purged; an approved archival/deletion procedure and backup expiry
policy remain external launch evidence.

## Quarantined PITR/backup restore drill

1. Stop or deny Gateway and Management traffic to the restored database. Do
   not run the normal Worker yet. Confirm the backup timestamp, schema version,
   restore owner, and target environment; never point this command at a live
   production database.
2. Apply only compatible migrations through the normal Migrator process. Run
   `dotnet run --project src/UZLLM.Migrator -- --scrub-restored-payloads
   <operator-account-id> --confirm-all-payloads` using an explicit
   `ConnectionStrings__Postgres` for the **isolated restored database**.
   This deliberately disables every restored payload opt-in and deletes every
   retained body, even those not yet expired. The account must have active
   operator access in the restored database. Capture the reported counts and
   immutable `Privacy.RestorePayloadScrub` audit event.
3. Verify `SELECT count(*) FROM usage.payload` returns zero and
   `SELECT count(*) FROM usage.payload_retention_policy WHERE enabled` returns
   zero. Start the Worker with the signed retention config, wait for the
   metadata sweep, and verify no overdue eligible trace/operational content
   remains. Reconcile wallet/ledger/outbox and run application smoke tests
   under `OPS-007` before reopening traffic. Inform customers that payload
   retention must be opted into again after a restore.
4. Record RPO/RTO and the actual managed backup/PITR expiry. Old immutable
   backup bytes may still contain deleted payloads until that expiry; protect
   backup access and never claim immediate physical deletion from backups.

The integration test `Restore_scrub_disables_restored_opt_ins_and_deletes_all_payload_before_reopening`
proves the transactional reset against PostgreSQL, while
`Sweep_is_bounded_and_reapplies_to_old_content_after_restore` simulates
reintroduced old operational content. Neither replaces a real managed restore
drill or signed retention approval.
