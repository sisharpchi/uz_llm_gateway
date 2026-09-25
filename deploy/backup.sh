#!/bin/sh
set -eu
umask 077

# Supplemental logical backup. Managed PostgreSQL PITR/WAL retention is required
# to meet the five-minute RPO; a daily pg_dump alone does not meet it.
: "${PGSERVICEFILE:?Set PGSERVICEFILE to a protected pg_service.conf}"
: "${UZLLM_BACKUP_DIR:?Set UZLLM_BACKUP_DIR to an existing protected directory}"
test -d "$UZLLM_BACKUP_DIR"
test -f "$PGSERVICEFILE"

backup="$UZLLM_BACKUP_DIR/uzllm-$(date -u +%Y%m%dT%H%M%SZ)-$$.dump"
pg_dump --dbname=service=uzllm_backup --format=custom --no-owner --file="$backup"
test -s "$backup"
sha256sum "$backup" > "$backup.sha256"
printf 'Backup: %s\n' "$backup"
printf 'Checksum: %s\n' "$backup.sha256"
