import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { management, usdFromMicro, uzsFromTiyin, type Session, type AdminAccount,
  type AdminOrganization, type AdminProvider, type AdminPrice, type AdminLedgerEntry,
  type AdminPayment, type AdminControl, type AdminAudit } from '@uzllm/api-client';
import './style.css';

type View = 'incidents' | 'catalog' | 'payments' | 'ledger' | 'search' | 'audit';
const views: { id: View; label: string }[] = [
  { id: 'incidents', label: 'Incident controls' }, { id: 'catalog', label: 'Provider catalog' },
  { id: 'payments', label: 'Payments' }, { id: 'ledger', label: 'Ledger' },
  { id: 'search', label: 'Account search' }, { id: 'audit', label: 'Audit trail' }
];
const date = (value: string | null | undefined) => value ? new Date(value).toLocaleString() : '—';
const short = (id: string) => id.slice(0, 8);

function AdminApp() {
  const [session, setSession] = useState<Session | null>(null);
  const [booting, setBooting] = useState(true); const [recentMfa, setRecentMfa] = useState(false);
  const [email, setEmail] = useState(''); const [password, setPassword] = useState('');
  const [mfaPassword, setMfaPassword] = useState(''); const [mfaCode, setMfaCode] = useState('');
  const [mfaSecret, setMfaSecret] = useState(''); const [view, setView] = useState<View>('incidents');
  const [error, setError] = useState(''); const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState(''); const [form, setForm] = useState<Record<string, string>>({});
  const [organizationId, setOrganizationId] = useState(''); const [appliedOrganizationId, setAppliedOrganizationId] = useState(''); const [search, setSearch] = useState('');
  const [controls, setControls] = useState<AdminControl[]>([]); const [providers, setProviders] = useState<AdminProvider[]>([]);
  const [prices, setPrices] = useState<AdminPrice[]>([]); const [selectedMapping, setSelectedMapping] = useState('');
  const [payments, setPayments] = useState<AdminPayment[]>([]); const [ledger, setLedger] = useState<AdminLedgerEntry[]>([]);
  const [accounts, setAccounts] = useState<AdminAccount[]>([]); const [organizations, setOrganizations] = useState<AdminOrganization[]>([]);
  const [audit, setAudit] = useState<AdminAudit[]>([]);

  useEffect(() => { management.session().then(async value => {
    setSession(value); if (value.isOperator) setRecentMfa((await management.operatorAccess()).recentMfa);
  }).catch(() => setSession(null)).finally(() => setBooting(false)); }, []);
  const refresh = useCallback(async (target: View) => {
    if (target === 'incidents') setControls(await management.adminControls());
    if (target === 'catalog') setProviders(await management.adminProviders());
    if (target === 'payments') setPayments(await management.adminPayments(appliedOrganizationId || undefined));
    if (target === 'ledger' && appliedOrganizationId) setLedger(await management.adminLedger(appliedOrganizationId));
    if (target === 'audit') setAudit(await management.adminAudit());
  }, [appliedOrganizationId]);
  useEffect(() => { if (recentMfa) refresh(view).catch(() => setError('Operator data could not be loaded.')); }, [recentMfa, view, refresh]);
  async function perform(action: () => Promise<unknown>, success: string) {
    setBusy(true); setError(''); setNotice('');
    try { await action(); setNotice(success); await refresh(view); }
    catch { setError('Operation failed. Check values and your recent MFA session.'); }
    finally { setBusy(false); }
  }
  function ensureReason() { if (reason.trim().length < 8) { setError('A reason of at least 8 characters is required.'); return false; } return true; }
  function field(name: string, label: string, type = 'text') { return <label key={name}>{label}<input type={type} required value={form[name] || ''} onChange={e => setForm(current => ({ ...current, [name]: e.target.value }))} /></label>; }
  async function login(event: FormEvent) { event.preventDefault(); setError(''); try {
    await management.login(email, password); const value = await management.session(); setSession(value); setPassword('');
    if (value.isOperator) setRecentMfa((await management.operatorAccess()).recentMfa);
  } catch { setError('Sign-in failed. Check your credentials.'); } }
  async function verifyMfa(event: FormEvent) { event.preventDefault(); setError(''); try {
    await management.verifyOperatorMfa(mfaCode); setMfaCode(''); setMfaSecret(''); setRecentMfa(true);
  } catch { setError('Code was not accepted. Try a current authenticator code.'); } }
  async function enrollMfa(event: FormEvent) { event.preventDefault(); setError(''); try {
    const result = await management.enrollOperatorMfa(mfaPassword); setMfaSecret(result.sharedSecret); setMfaPassword('');
  } catch { setError('Enrollment was denied. MFA may already be configured, or the password is incorrect.'); } }
  async function signOut() { await management.logout(); setSession(null); setRecentMfa(false); }
  async function setStatus(kind: 'providers' | 'models' | 'mappings' | 'credentials' | 'controls', id: string, enabled: boolean) {
    if (!ensureReason() || !window.confirm(`${enabled ? 'Enable' : 'Disable'} ${id}?`)) return;
    await perform(() => management.adminSetStatus(kind, id, enabled, reason.trim()), 'Status updated and audited.');
  }
  async function create(kind: 'provider' | 'model' | 'mapping' | 'credential' | 'price', event: FormEvent) {
    event.preventDefault(); if (!ensureReason()) return;
    const action = {
      provider: () => management.adminCreateProvider(form.code, form.name, reason),
      model: () => management.adminCreateModel(form.code, form.name, Number(form.contextLength), Number(form.maxOutputTokens), ['Text'], reason),
      mapping: () => management.adminCreateMapping(form.providerId, form.modelId, form.upstreamModelCode, reason),
      credential: () => management.adminCreateCredential(form.providerId, form.secret, reason),
      price: () => management.adminSchedulePrice(form.mappingId, new Date(form.effectiveFrom).toISOString(), Number(form.inputPrice), Number(form.outputPrice), reason)
    }[kind];
    await perform(action, `${kind} created and audited.`); setForm(current => ({ ...current, secret: '' }));
  }
  async function runSearch(event: FormEvent) { event.preventDefault(); setError(''); if (search.trim().length < 2) return;
    try { const [people, orgs] = await Promise.all([management.adminAccounts(search), management.adminOrganizations(search)]);
      setAccounts(people); setOrganizations(orgs); } catch { setError('Search failed.'); } }

  if (booting) return <main className="center" role="status">Checking operator session…</main>;
  if (!session) return <main className="center"><form onSubmit={login} className="auth-card"><span className="eyebrow">UZLLM / OPERATOR</span><h1>Control plane</h1><p>Sign in with your operator account.</p><label>Email<input type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} required /></label><label>Password<input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required /></label>{error && <p role="alert" className="error">{error}</p>}<button>Sign in</button></form></main>;
  if (!session.isOperator) return <main className="center"><section className="auth-card"><span className="eyebrow">ACCESS DENIED</span><h1>Operator access required</h1><p>This account has no operator grant.</p><button onClick={signOut}>Sign out</button></section></main>;
  if (!recentMfa) return <main className="center"><section className="auth-card"><span className="eyebrow">STEP-UP AUTHENTICATION</span><h1>Verify it's you</h1><p>Operator access requires a current authenticator code and expires after 15 minutes.</p><form onSubmit={verifyMfa}><label>Authenticator code<input inputMode="numeric" autoComplete="one-time-code" value={mfaCode} onChange={e => setMfaCode(e.target.value)} required /></label><button>Verify code</button></form><details><summary>First-time MFA setup</summary><p>Use this only after an administrator grants operator access.</p><form onSubmit={enrollMfa}><label>Confirm password<input type="password" autoComplete="current-password" value={mfaPassword} onChange={e => setMfaPassword(e.target.value)} required /></label><button className="secondary">Enroll authenticator</button></form>{mfaSecret && <div className="secret"><strong>Save this secret in your authenticator now.</strong><code>{mfaSecret}</code><p>It will not be shown again. Then enter a code above.</p></div>}</details>{error && <p role="alert" className="error">{error}</p>}<button className="quiet" onClick={signOut}>Sign out</button></section></main>;

  return <div className="shell"><aside className="sidebar"><div className="brand"><span className="brand-mark">U</span><div><strong>UZLLM</strong><small>OPERATOR CONSOLE</small></div></div><div className="side-caption">WORKSPACE</div><nav aria-label="Operator sections">{views.map(item => <button key={item.id} className={view === item.id ? 'nav active' : 'nav'} onClick={() => { setView(item.id); setError(''); setNotice(''); }}>{item.label}</button>)}</nav><div className="sidebar-foot"><span className="live-dot" /> Recent MFA verified<small>{session.email}</small><button className="quiet" onClick={signOut}>Sign out ↗</button></div></aside>
    <main className="content"><header className="topbar"><div><span className="eyebrow">OPERATIONS / {view.toUpperCase()}</span><h1>{views.find(item => item.id === view)?.label}</h1></div><button className="secondary" onClick={() => refresh(view).catch(() => setError('Refresh failed.'))}>Refresh ↻</button></header>{error && <div role="alert" className="banner error">{error}</div>}{notice && <div role="status" className="banner success">{notice}</div>}
      {view === 'incidents' && <><div className="intro"><strong>Incident response</strong><p>Switches affect new managed inference and new top-up checkout only. In-flight requests and verified payment callbacks still finalize.</p></div><section className="panel"><div className="panel-heading"><h2>Platform controls</h2><span className="pill">AUTHORITATIVE · POSTGRESQL</span></div><label className="reason">Reason for next change<input value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. Provider incident INC-248" /></label>{controls.map(item => <div className="row" key={item.feature}><div><strong>{item.feature === 'ManagedTraffic' ? 'Managed API traffic' : 'New top-ups'}</strong><small>Updated {date(item.updatedAt)}</small></div><div className="row-actions"><span className={item.enabled ? 'status enabled' : 'status paused'}>{item.enabled ? 'Enabled' : 'Paused'}</span><button disabled={busy} className={item.enabled ? 'danger' : 'secondary'} onClick={() => setStatus('controls', item.feature, !item.enabled)}>{item.enabled ? 'Pause' : 'Resume'}</button></div></div>)}</section></>}
      {view === 'catalog' && <><div className="intro"><strong>Provider configuration</strong><p>Provider secrets remain encrypted and are never returned here. Disabling a provider or mapping removes it from new routing decisions.</p></div><section className="panel"><label className="reason">Required change reason<input value={reason} onChange={e => setReason(e.target.value)} placeholder="Change ticket or incident, at least 8 characters" /></label></section><section className="panel"><div className="panel-heading"><h2>Providers</h2><span className="pill">{providers.length} configured</span></div>{providers.map(provider => <div className="provider" key={provider.id}><div className="row"><div><strong>{provider.name}</strong><small>{provider.code} · {short(provider.id)}</small></div><div className="row-actions"><span className="status">{provider.status}</span><button disabled={busy} className="secondary" onClick={() => setStatus('providers', provider.id, provider.status !== 'Active')}>{provider.status === 'Active' ? 'Disable' : 'Enable'}</button></div></div>{provider.mappings.map(mapping => <div className="subrow" key={mapping.id}><div><strong>{mapping.modelCode}</strong><small>Upstream {mapping.upstreamModelCode} · {short(mapping.id)}</small></div><div className="row-actions"><button className="quiet" onClick={async () => { setSelectedMapping(mapping.id); setPrices(await management.adminPrices(mapping.id)); }}>Prices</button><button disabled={busy} className="secondary" onClick={() => setStatus('mappings', mapping.id, mapping.status !== 'Active')}>{mapping.status === 'Active' ? 'Disable' : 'Enable'}</button></div></div>)}{provider.credentials.map(credential => <div className="subrow" key={credential.id}><div><strong>Credential · {short(credential.id)}</strong><small>Key {credential.keyVersion} · {date(credential.createdAt)}</small></div><div className="row-actions"><span className="status">{credential.status}</span><button disabled={busy} className="secondary" onClick={() => setStatus('credentials', credential.id, credential.status !== 'Active')}>{credential.status === 'Active' ? 'Disable' : 'Enable'}</button></div></div>)}</div>)}{providers.length === 0 && <p className="empty">No providers configured.</p>}</section>{selectedMapping && <section className="panel"><h2>Price history · {short(selectedMapping)}</h2><div className="table-wrap"><table><thead><tr><th>From</th><th>To</th><th>Input / 1M</th><th>Output / 1M</th></tr></thead><tbody>{prices.map(price => <tr key={price.id}><td>{date(price.effectiveFrom)}</td><td>{date(price.effectiveTo)}</td><td>{price.inputPriceMicroUsdPerMillion}</td><td>{price.outputPriceMicroUsdPerMillion}</td></tr>)}</tbody></table></div></section>}
        <div className="form-grid"><form className="panel" onSubmit={e => create('provider', e)}><h2>Add provider</h2>{field('code', 'Code')}{field('name', 'Name')}<button disabled={busy}>Add provider</button></form><form className="panel" onSubmit={e => create('model', e)}><h2>Add canonical model</h2>{field('code', 'Code')}{field('name', 'Display name')}{field('contextLength', 'Context tokens', 'number')}{field('maxOutputTokens', 'Max output tokens', 'number')}<button disabled={busy}>Add model</button></form><form className="panel" onSubmit={e => create('mapping', e)}><h2>Add mapping</h2>{field('providerId', 'Provider ID')}{field('modelId', 'Model ID')}{field('upstreamModelCode', 'Upstream model code')}<button disabled={busy}>Add mapping</button></form><form className="panel" onSubmit={e => create('credential', e)}><h2>Provision credential</h2>{field('providerId', 'Provider ID')}{field('secret', 'Provider secret', 'password')}<button disabled={busy}>Store encrypted</button></form><form className="panel" onSubmit={e => create('price', e)}><h2>Schedule future price</h2>{field('mappingId', 'Mapping ID')}{field('effectiveFrom', 'Effective from', 'datetime-local')}{field('inputPrice', 'Input micro-USD / 1M', 'number')}{field('outputPrice', 'Output micro-USD / 1M', 'number')}<button disabled={busy}>Schedule price</button></form></div></>}
      {view === 'payments' && <><div className="intro"><strong>Payment evidence</strong><p>“Verified callback seen” means a signed callback was accepted locally. It is not independent provider confirmation. Reconciliation cases require investigation.</p></div><section className="panel filter"><label>Organization ID (optional)<input value={organizationId} onChange={e => setOrganizationId(e.target.value)} placeholder="UUID" /></label><button className="secondary" onClick={() => setAppliedOrganizationId(organizationId)}>Apply filter</button></section><section className="panel table-wrap"><table><thead><tr><th>Payment</th><th>Organization</th><th>Provider</th><th>Local state</th><th>Observation</th><th>Amount</th><th>Credit</th><th>Case</th></tr></thead><tbody>{payments.map(item => <tr key={item.id}><td><code>{short(item.id)}</code><small>{date(item.createdAt)}</small></td><td><code>{short(item.organizationId)}</code></td><td>{item.provider}</td><td>{item.localStatus}</td><td>{item.providerObservation}<small>{item.callbackCount} callbacks</small></td><td>{uzsFromTiyin(item.amountTiyin)}</td><td>{item.hasCredit ? usdFromMicro(item.creditMicroUsd) : 'Not credited'}</td><td>{item.hasReversal ? 'Reversed' : item.reconciliationReason || '—'}</td></tr>)}</tbody></table>{payments.length === 0 && <p className="empty">No payment intents found.</p>}</section></>}
      {view === 'ledger' && <><div className="intro"><strong>Immutable financial record</strong><p>Read-only ledger. Corrections need a separate audited workflow; entries are never edited.</p></div><section className="panel filter"><label>Organization ID<input value={organizationId} onChange={e => setOrganizationId(e.target.value)} placeholder="UUID" /></label><button className="secondary" onClick={() => setAppliedOrganizationId(organizationId)}>Load ledger</button></section><section className="panel table-wrap"><table><thead><tr><th>Occurred</th><th>Type</th><th>Amount</th><th>Reference</th></tr></thead><tbody>{ledger.map(item => <tr key={item.id}><td>{date(item.occurredAt)}</td><td>{item.type}</td><td>{usdFromMicro(item.amountMicroUsd)}</td><td>{item.referenceType} · <code>{short(item.referenceId)}</code></td></tr>)}</tbody></table>{ledger.length === 0 && <p className="empty">Select an organization.</p>}</section></>}
      {view === 'search' && <><div className="intro"><strong>Support lookup</strong><p>Search accounts by email and organizations by name. Secrets are never exposed.</p></div><form className="panel filter" onSubmit={runSearch}><label>Search<input value={search} onChange={e => setSearch(e.target.value)} minLength={2} placeholder="Email or organization" required /></label><button>Search</button></form><div className="form-grid"><section className="panel"><h2>Accounts</h2>{accounts.map(item => <div className="row" key={item.id}><div><strong>{item.email}</strong><small>{short(item.id)} · {item.status} · {item.emailVerified ? 'Verified' : 'Unverified'}</small></div>{item.isOperator && <span className="pill">OPERATOR</span>}</div>)}</section><section className="panel"><h2>Organizations</h2>{organizations.map(item => <div className="row" key={item.id}><div><strong>{item.name}</strong><small>{item.id} · {item.status}</small></div><span>{item.postedBalanceMicroUsd ? usdFromMicro(item.postedBalanceMicroUsd) : 'No wallet'}</span></div>)}</section></div></>}
      {view === 'audit' && <><div className="intro"><strong>Operator actions</strong><p>Append-only audit records the actor, resource and timestamp of control-plane mutations.</p></div><section className="panel table-wrap"><table><thead><tr><th>Occurred</th><th>Actor</th><th>Action</th><th>Resource</th></tr></thead><tbody>{audit.map(item => <tr key={item.id}><td>{date(item.occurredAt)}</td><td><code>{short(item.actorAccountId)}</code></td><td>{item.action}</td><td>{item.resourceType} · {item.resourceId ? short(item.resourceId) : 'global'}</td></tr>)}</tbody></table>{audit.length === 0 && <p className="empty">No audit events found.</p>}</section></>}
      {view === 'catalog' && <section className="panel"><h2>Canonical model controls</h2>
        {Array.from(new Map(providers.flatMap(provider => provider.mappings)
          .map(mapping => [mapping.modelId, mapping] as const)).values()).map(model =>
          <div className="row" key={model.modelId}><div><strong>{model.modelCode}</strong><small>{model.modelStatus} · {short(model.modelId)}</small></div>
            <button disabled={busy} className="secondary" onClick={() => setStatus('models', model.modelId, model.modelStatus !== 'Active')}>
              {model.modelStatus === 'Active' ? 'Disable model' : 'Enable model'}</button></div>)}</section>}
    </main></div>;
}

createRoot(document.getElementById('root')!).render(<AdminApp />);
