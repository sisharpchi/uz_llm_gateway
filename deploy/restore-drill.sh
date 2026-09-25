#!/bin/sh
set -eu

: "${PGSERVICEFILE:?Set PGSERVICEFILE to a protected pg_service.conf}"
: "${UZLLM_RESTORE_DB:?Set UZLLM_RESTORE_DB to an isolated scratch database name}"
: "${UZLLM_BACKUP_FILE:?Set UZLLM_BACKUP_FILE to a verified custom-format dump}"
case "$UZLLM_RESTORE_DB" in
  uzllm_restore_*) ;;
  *) echo 'Refusing restore: target must begin uzllm_restore_' >&2; exit 2 ;;
esac
test -f "$PGSERVICEFILE"
test -s "$UZLLM_BACKUP_FILE"
test -f "$UZLLM_BACKUP_FILE.sha256"
sha256sum -c "$UZLLM_BACKUP_FILE.sha256"
actual_db=$(psql 'service=uzllm_restore' -Atqc 'SELECT current_database()')
test "$actual_db" = "$UZLLM_RESTORE_DB" || {
  echo 'Refusing restore: service points to a different database' >&2; exit 2;
}

# This command is intentionally destructive only for an explicitly named
# isolated scratch database, never for the production service.
pg_restore --dbname=service=uzllm_restore --clean --if-exists --no-owner \
  --no-privileges --exit-on-error "$UZLLM_BACKUP_FILE"

psql 'service=uzllm_restore' -v ON_ERROR_STOP=1 <<'SQL'
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM billing.wallet WHERE posted_balance_micro_usd < reserved_balance_micro_usd
    OR reserved_balance_micro_usd < 0) THEN
    RAISE EXCEPTION 'Restored wallet invariant failed';
  END IF;
  IF EXISTS (SELECT 1 FROM billing.reservation r
    LEFT JOIN billing.settlement s ON s.reservation_id = r.id
    WHERE (r.status IN ('Settled', 'Released') AND s.id IS NULL)
       OR (r.status = 'Reserved' AND s.id IS NOT NULL)) THEN
    RAISE EXCEPTION 'Restored reservation/settlement invariant failed';
  END IF;
END $$;
SELECT financial_state, count(*) FROM usage.request
 WHERE financial_state IN ('PendingEvidence', 'PendingSettlement')
 GROUP BY financial_state ORDER BY financial_state;
SQL
echo 'Restore drill complete; inspect pending evidence separately from pending settlement.'
