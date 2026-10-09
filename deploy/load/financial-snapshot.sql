-- Read-only OPS-006 evidence. Invoke with psql -v org_id=<isolated tenant UUID>.
-- Run before and after the 60-second load phase against the same database.
WITH tenant AS (SELECT :'org_id'::uuid AS id),
reservations AS (
  SELECT COUNT(*) FILTER (WHERE status = 'Reserved') AS open_count,
         COALESCE(SUM(amount_micro_usd) FILTER (WHERE status = 'Reserved'), 0) AS held,
         COUNT(*) FILTER (WHERE status = 'Settled') AS settled_count,
         COALESCE(SUM(captured_micro_usd) FILTER (WHERE status = 'Settled'), 0) AS captured
  FROM billing.reservation WHERE organization_id = (SELECT id FROM tenant)
),
ledger AS (
  SELECT COALESCE(SUM(amount_micro_usd), 0) AS total,
         COUNT(*) FILTER (WHERE type = 'UsageCharge') AS charge_count,
         COALESCE(SUM(amount_micro_usd) FILTER (WHERE type = 'UsageCharge'), 0) AS charged
  FROM billing.ledger_entry WHERE organization_id = (SELECT id FROM tenant)
)
SELECT json_build_object(
  'organizationId', wallet.organization_id,
  'posted', wallet.posted_balance_micro_usd::text,
  'reserved', wallet.reserved_balance_micro_usd::text,
  'ledgerSum', ledger.total::text,
  'openReservations', reservations.open_count,
  'reservedSum', reservations.held::text,
  'settledCount', reservations.settled_count,
  'capturedSum', reservations.captured::text,
  'usageChargeCount', ledger.charge_count,
  'usageChargeSum', ledger.charged::text
)
FROM billing.wallet AS wallet CROSS JOIN reservations CROSS JOIN ledger
WHERE wallet.organization_id = (SELECT id FROM tenant);
