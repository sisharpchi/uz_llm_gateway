import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { management, usdFromMicro, uzsFromTiyin, type AdminPayment, type AdminPaymentCase,
  type AdminRefund, type AdminFinancialRisk } from '@uzllm/api-client';
import './FinancialOperations.css';

const short = (id: string) => id.slice(0, 8);
const date = (value: string) => new Date(value).toLocaleString();

function paymentState(payment: AdminPayment) {
  if (payment.reconciliationStatus === 'Open' && payment.hasReversal) return ['Disputed · reversed', 'danger'];
  if (payment.reconciliationStatus === 'Open') return ['Disputed', 'warning'];
  if (payment.hasReversal) return ['Reversed', 'danger'];
  if (payment.hasCredit) return ['Credited', 'success'];
  if (payment.localStatus === 'Paid') return ['Credit pending', 'warning'];
  return [payment.localStatus === 'Canceled' || payment.localStatus === 'Expired' ? payment.localStatus : 'Pending', 'neutral'];
}

export function FinancialOperations({ refreshToken }: { refreshToken: number }) {
  const [filter, setFilter] = useState(''); const [applied, setApplied] = useState('');
  const [payments, setPayments] = useState<AdminPayment[]>([]);
  const [cases, setCases] = useState<AdminPaymentCase[]>([]);
  const [refunds, setRefunds] = useState<AdminRefund[]>([]);
  const [risk, setRisk] = useState<AdminFinancialRisk | null>(null);
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  const [caseReason, setCaseReason] = useState(''); const [reference, setReference] = useState('');
  const [settlementId, setSettlementId] = useState(''); const [amount, setAmount] = useState('');
  const [refundReason, setRefundReason] = useState('');
  const attempt = useRef<{ fingerprint: string; key: string } | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const [p, c, r, f] = await Promise.all([management.adminPayments(applied || undefined),
        management.adminPaymentCases(), management.adminRefunds(applied || undefined), management.adminFinancialRisk()]);
      setPayments(p); setCases(c); setRefunds(r); setRisk(f);
    } catch { setError('Financial data could not be loaded. Check recent MFA and retry.'); }
    finally { setLoading(false); }
  }, [applied]);
  useEffect(() => { void load(); }, [load, refreshToken]);

  async function resolve(id: string) {
    if (caseReason.trim().length < 8 || !reference.trim()) {
      setError('Enter an investigation reason (at least 8 characters) and a resolution reference.'); return;
    }
    if (!window.confirm('Close this case? Resolution does not post or reverse money.')) return;
    setBusy(true); setError(''); setNotice('');
    try { await management.adminResolvePaymentCase(id, caseReason.trim(), reference.trim());
      setNotice('Case resolved and audited; wallet unchanged.'); await load(); }
    catch { setError('Resolution failed. Verify evidence and your MFA session.'); }
    finally { setBusy(false); }
  }
  async function refund(event: FormEvent) {
    event.preventDefault();
    if (!/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(settlementId.trim()) || !/^[1-9][0-9]*$/.test(amount)
      || BigInt(amount) > 9_223_372_036_854_775_807n
      || refundReason.trim().length < 8) { setError('Enter a settlement UUID, positive whole micro-USD amount, and reason.'); return; }
    if (!window.confirm('Credit the settled charge to the customer wallet? This is not a cash payout.')) return;
    const fingerprint = `${settlementId.trim()}|${amount}|${refundReason.trim()}`;
    if (attempt.current?.fingerprint !== fingerprint) attempt.current = { fingerprint, key: crypto.randomUUID() };
    setBusy(true); setError(''); setNotice('');
    try { const result = await management.adminCreateRefund(settlementId.trim(), attempt.current.key, amount, refundReason.trim());
      setNotice(result.duplicate ? 'Existing refund returned; no duplicate credit.' : 'Wallet-credit refund posted and audited.');
      attempt.current = null; setSettlementId(''); setAmount(''); setRefundReason(''); await load(); }
    catch { setError('Refund not confirmed. Keep the same values to retry with the same key; inspect the ledger first.'); }
    finally { setBusy(false); }
  }

  return <>
    <div className="intro"><strong>Financial operations</strong><p>Local callback evidence is not an independent merchant statement. Investigate cases before closure; refunds are wallet credits, never cash payouts.</p></div>
    <section className="panel filter"><label>Organization ID (payments and refunds only)<input value={filter} onChange={event => setFilter(event.target.value)} placeholder="UUID" /></label><button className="secondary" onClick={() => setApplied(filter.trim())}>Apply filter</button></section>
    {error && <div role="alert" className="banner error">{error}</div>}{notice && <div role="status" className="banner success">{notice}</div>}
    {loading && <p role="status">Loading financial evidence…</p>}
    <section className="panel"><h2>Payments</h2><div className="table-wrap"><table><thead><tr><th>Payment</th><th>Provider</th><th>State</th><th>Observation</th><th>Amount</th><th>Wallet credit</th></tr></thead><tbody>{payments.map(item => { const [label, tone] = paymentState(item); return <tr key={item.id}><td><code>{short(item.id)}</code><small>{short(item.organizationId)} · {date(item.createdAt)}</small></td><td>{item.provider}</td><td><span className={`financial-status ${tone}`}>{label}</span><small>Local: {item.localStatus}{item.reconciliationReason ? ` · ${item.reconciliationReason}` : ''}</small></td><td>{item.providerObservation}<small>{item.callbackCount} callbacks; statement review is separate</small></td><td>{uzsFromTiyin(item.amountTiyin)}</td><td>{item.hasCredit ? usdFromMicro(item.creditMicroUsd) : 'Not credited'}</td></tr>; })}</tbody></table></div>{!loading && !payments.length && <p className="empty">No payments found.</p>}</section>
    <section className="panel"><h2>Reconciliation cases</h2><p>Closing a case never changes the wallet. Compare the merchant statement and immutable ledger first.</p><div className="table-wrap"><table><thead><tr><th>Case</th><th>Payment / provider</th><th>Reason</th><th>Status</th><th>Action</th></tr></thead><tbody>{cases.map(item => <tr key={item.id}><td><code>{short(item.id)}</code><small>{date(item.createdAt)}</small></td><td>{item.intentId ? short(item.intentId) : 'Unmatched'} · {item.provider ?? 'Unknown'}</td><td>{item.reason}</td><td><span className={`financial-status ${item.status === 'Open' ? 'warning' : 'neutral'}`}>{item.status}</span>{item.resolutionReference && <small>{item.resolutionReference}</small>}</td><td>{item.status === 'Open' && <button className="secondary" disabled={busy} onClick={() => resolve(item.id)}>Resolve</button>}</td></tr>)}</tbody></table></div>{!loading && !cases.length && <p className="empty">No reconciliation cases.</p>}<div className="finance-fields"><label>Investigation reason<input value={caseReason} onChange={event => setCaseReason(event.target.value)} placeholder="Merchant statement and ledger checked" /></label><label>Resolution reference<input value={reference} onChange={event => setReference(event.target.value)} placeholder="Statement row or incident ID" /></label></div></section>
    <section className="panel"><h2>Recovery debt and exposure</h2><p>Authoritative read as of {risk ? date(risk.dataAsOf) : '—'}. Use the organization, settlement or reservation reference when escalating; these are not automatic write actions.</p><div className="finance-risk-grid"><div><strong>Spending holds</strong>{risk?.debt.length ? risk.debt.map(item => <p key={item.organizationId}><code>{short(item.organizationId)}</code> · {usdFromMicro(item.outstandingMicroUsd)} · {item.spendingHeld ? 'Held' : 'Monitoring'}</p>) : <p>No recovery debt.</p>}</div><div><strong>Provider exposure</strong>{risk?.exposure.length ? risk.exposure.map(item => <p key={item.settlementId}><code>{short(item.settlementId)}</code> · {usdFromMicro(item.platformExposureMicroUsd)} exposure{item.unresolvedUsage ? ' · usage unresolved' : ''}</p>) : <p>No exposed settlements.</p>}</div><div><strong>Expired reservations</strong>{risk?.pending.length ? risk.pending.map(item => <p key={item.reservationId}><code>{short(item.reservationId)}</code> · {item.state} · {usdFromMicro(item.heldMicroUsd)} held</p>) : <p>No expired holds.</p>}</div></div></section>
    <div className="form-grid"><section className="panel"><h2>Wallet-credit refunds</h2><p>Immutable counter-entries of settled inference charges.</p><div className="table-wrap"><table><thead><tr><th>Created</th><th>Settlement</th><th>Organization</th><th>Credit</th></tr></thead><tbody>{refunds.map(item => <tr key={item.id}><td>{date(item.createdAt)}</td><td><code>{short(item.settlementId)}</code></td><td><code>{short(item.organizationId)}</code></td><td>{usdFromMicro(item.amountMicroUsd)}</td></tr>)}</tbody></table></div>{!loading && !refunds.length && <p className="empty">No wallet-credit refunds.</p>}</section><form className="panel" onSubmit={refund}><h2>Credit a settled charge</h2><label>Settlement ID<input value={settlementId} onChange={event => setSettlementId(event.target.value)} required placeholder="UUID" /></label><label>Amount (micro-USD)<input inputMode="numeric" value={amount} onChange={event => setAmount(event.target.value)} required placeholder="1000000 = $1" /></label><label>Auditable reason<input value={refundReason} onChange={event => setRefundReason(event.target.value)} required minLength={8} /></label><button disabled={busy}>Issue wallet credit</button></form></div>
  </>;
}
