import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, Navigate, Route, Routes, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ApiError, management, usdFromMicro, uzsFromTiyin,
  type Organization, type Project, type PaymentQuote, type CreatedTopUp } from '@uzllm/api-client';
import { ActivityPage, AnalyticsPage } from './UsagePages';

type Page = 'overview' | 'projects' | 'keys' | 'billing' | 'activity' | 'analytics';
const validPages: Page[] = ['overview', 'projects', 'keys', 'billing', 'activity', 'analytics'];

export function App() {
  return <Routes>
    <Route path="/login" element={<AuthPage key="login" kind="login" />} />
    <Route path="/register" element={<AuthPage key="register" kind="register" />} />
    <Route path="/verify-email" element={<AuthPage key="verify" kind="verify" />} />
    <Route path="/organizations/:orgId/:page" element={<WorkspaceGate />} />
    <Route path="*" element={<WorkspaceGate />} />
  </Routes>;
}

function WorkspaceGate() {
  const session = useQuery({ queryKey: ['session'], queryFn: management.session });
  const organizations = useQuery({ queryKey: ['organizations', session.data?.accountId],
    queryFn: management.organizations, enabled: !!session.data });
  const { orgId, page } = useParams();

  if (session.isPending) return <Loading />;
  if (session.isError) return <Navigate to="/login" replace />;
  if (organizations.isPending) return <Loading />;
  if (organizations.isError) return <ErrorState error={organizations.error} />;
  if (!orgId) return organizations.data.length
    ? <Navigate to={`/organizations/${organizations.data[0].id}/overview`} replace />
    : <Onboarding />;
  const current = organizations.data.find(org => org.id === orgId);
  if (!current) return <NotFound />;
  return <Dashboard organization={current} organizations={organizations.data}
    page={validPages.includes(page as Page) ? page as Page : 'overview'} />;
}

function AuthPage({ kind }: { kind: 'login' | 'register' | 'verify' }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [token, setToken] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [done, setDone] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setBusy(true);
    try {
      if (kind === 'register') { await management.register(email, password); setDone(true); }
      else if (kind === 'verify') { await management.verifyEmail(token); navigate('/login'); }
      else { await management.login(email, password); queryClient.removeQueries({ queryKey: ['session'] }); navigate('/'); }
    } catch (cause) { setError(message(cause)); } finally { setBusy(false); }
  }
  const title = kind === 'login' ? 'Sign in' : kind === 'register' ? 'Create your account' : 'Verify your email';
  return <div className="auth-layout">
    <div className="auth-intro"><span className="brand-mark">U</span><p className="eyebrow">UZLLM GATEWAY</p>
      <h1>One API.<br /><em>Every model.</em></h1><p>Build with confidence. Route requests, watch usage, and pay locally.</p></div>
    <main className="auth-card"><h2>{title}</h2>
      {done ? <div role="status" className="notice">Registration accepted. Check your email for the verification token, then verify before signing in.</div>
        : <form onSubmit={submit} className="stack">
          {kind === 'verify' ? <label>Verification token<input value={token} onChange={event => setToken(event.target.value)} required autoComplete="one-time-code" /></label>
            : <><label>Email<input type="email" value={email} onChange={event => setEmail(event.target.value)} required autoComplete="email" /></label>
              <label>Password<input type="password" value={password} onChange={event => setPassword(event.target.value)} required minLength={12} autoComplete={kind === 'login' ? 'current-password' : 'new-password'} /></label></>}
          {error && <p role="alert" className="error">{error}</p>}
          <button className="button primary" disabled={busy}>{busy ? 'Please wait…' : title}</button>
        </form>}
      <div className="auth-links"><Link to="/login">Sign in</Link><Link to="/register">Register</Link><Link to="/verify-email">Verify email</Link></div>
    </main>
  </div>;
}

function Onboarding() {
  const navigate = useNavigate();
  const client = useQueryClient();
  const [name, setName] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try { const org = await management.createOrganization(name);
      await client.invalidateQueries({ queryKey: ['organizations'] });
      navigate(`/organizations/${org.id}/overview`);
    } catch (cause) { setError(message(cause)); } finally { setBusy(false); }
  }
  return <main className="onboard"><span className="eyebrow">WELCOME TO UZLLM</span><h1>Start with your organization.</h1>
    <p>Create a workspace, then add a project, top up, and issue your first API key.</p>
    <form onSubmit={submit} className="panel stack"><label>Organization name<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required /></label>
      {error && <p role="alert" className="error">{error}</p>}<button disabled={busy} className="button primary">Create organization</button></form>
  </main>;
}

function Dashboard({ organization, organizations, page }: { organization: Organization; organizations: Organization[]; page: Page }) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const [search, setSearch] = useSearchParams();
  const projectId = search.get('project') ?? '';
  const projects = useQuery({ queryKey: ['projects', organization.id],
    queryFn: () => management.projects(organization.id) });
  const selectedProject = projects.data?.find(item => item.id === projectId) ?? projects.data?.[0];
  const wallet = useQuery({ queryKey: ['wallet', organization.id],
    queryFn: () => management.wallet(organization.id), refetchInterval: 15_000 });
  const pageUrl = (next: Page) => `/organizations/${organization.id}/${next}${selectedProject ? `?project=${selectedProject.id}` : ''}`;
  async function signOut() {
    await management.logout(); client.clear(); navigate('/login');
  }
  return <div className="app-shell">
    <a href="#main-content" className="skip-link">Skip to content</a>
    <aside className="sidebar"><Link to="/" className="brand"><span className="brand-mark">U</span><span>UZLLM <small>GATEWAY</small></span></Link>
      <div className="sidebar-label">WORKSPACE</div>
      <label className="select-label">Organization<select aria-label="Organization" value={organization.id} onChange={event => navigate(`/organizations/${event.target.value}/overview`)}>
        {organizations.map(org => <option key={org.id} value={org.id}>{org.name}</option>)}</select></label>
      <label className="select-label">Project<select aria-label="Project" value={selectedProject?.id ?? ''} disabled={!projects.data?.length}
        onChange={event => setSearch({ project: event.target.value })}>
        {!projects.data?.length && <option value="">No projects</option>}
        {projects.data?.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <nav aria-label="Main navigation" className="nav-list">{validPages.map(item => <Link key={item} to={pageUrl(item)} aria-current={page === item ? 'page' : undefined}>
        <span className="nav-icon">{item === 'overview' ? '◫' : item === 'projects' ? '▤' : item === 'keys' ? '⌘' : item === 'activity' ? '≋' : item === 'analytics' ? '▥' : '◈'}</span>{item === 'keys' ? 'API keys' : item[0].toUpperCase() + item.slice(1)}</Link>)}</nav>
      <div className="sidebar-bottom"><p>Managed credits</p><strong>{wallet.data ? usdFromMicro(wallet.data.availableBalanceMicroUsd) : '—'}</strong>
        <button onClick={signOut} className="text-button">Sign out</button></div>
    </aside>
    <div className="workspace"><header className="topbar"><div><span className="eyebrow">{organization.name}</span><span className="topbar-project">{selectedProject?.name ?? 'No project selected'}</span></div>
      <span className="environment">Production</span></header>
      <main id="main-content" className="content">
        {projects.isError && <ErrorState error={projects.error} />}
        {page === 'overview' && <Overview organization={organization} project={selectedProject} wallet={wallet.data?.availableBalanceMicroUsd} />}
        {page === 'projects' && <ProjectsPanel organization={organization} projects={projects.data ?? []} />}
        {page === 'keys' && <KeysPanel organizationId={organization.id} project={selectedProject} />}
        {page === 'billing' && <BillingPanel organizationId={organization.id} />}
        {page === 'activity' && <ActivityPage organizationId={organization.id} projectId={selectedProject?.id} />}
        {page === 'analytics' && <AnalyticsPage organizationId={organization.id} projectId={selectedProject?.id} />}
      </main>
    </div>
  </div>;
}

function Overview({ organization, project, wallet }: { organization: Organization; project?: Project; wallet?: string }) {
  return <><div className="page-heading"><span className="eyebrow">YOUR WORKSPACE</span><h1>Overview</h1><p>A clear starting point for your first live request.</p></div>
    <div className="summary-grid"><div className="panel metric"><span>Available balance</span><strong>{wallet ? usdFromMicro(wallet) : '—'}</strong><small>Managed credits · USD</small></div>
      <div className="panel metric"><span>Current project</span><strong>{project?.name ?? 'Not created'}</strong><small>{organization.name}</small></div></div>
    <section className="panel"><h2>Get started</h2><ol className="steps"><li>Create a project</li><li>Top up with Payme or CLICK</li><li>Create and securely copy an API key</li><li>Call <code>POST /v1/chat/completions</code></li></ol>
      <p className="muted">Activity and analytics show real gateway traffic once requests are made. No sample balance or fabricated traffic is shown here.</p>
      <pre className="code-example">{`curl https://api.example.uz/v1/chat/completions \\\n+  -H 'Authorization: Bearer YOUR_API_KEY' \\\n+  -H 'Content-Type: application/json' \\\n+  -d '{"model":"YOUR_MODEL_ID","messages":[{"role":"user","content":"Hello"}]}'`}</pre></section>
  </>;
}

function ProjectsPanel({ organization, projects }: { organization: Organization; projects: Project[] }) {
  const client = useQueryClient(); const [name, setName] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent) { event.preventDefault(); setBusy(true); setError('');
    try { await management.createProject(organization.id, name); setName(''); await client.invalidateQueries({ queryKey: ['projects', organization.id] }); }
    catch (cause) { setError(message(cause)); } finally { setBusy(false); } }
  return <><div className="page-heading"><span className="eyebrow">{organization.name}</span><h1>Projects</h1><p>Keep API keys and usage scoped to the correct environment.</p></div>
    <div className="two-column"><section className="panel"><h2>Your projects</h2>{projects.length ? <ul className="row-list">{projects.map(project =>
      <li key={project.id}><strong>{project.name}</strong><span className="tag">{project.status === 0 ? 'Active' : 'Archived'}</span></li>)}</ul> : <p className="muted">No projects yet.</p>}</section>
      <form onSubmit={submit} className="panel stack"><h2>Create a project</h2><label>Project name<input value={name} onChange={event => setName(event.target.value)} required minLength={2} maxLength={100} /></label>
        {error && <p role="alert" className="error">{error}</p>}<button disabled={busy} className="button primary">Create project</button></form></div></>;
}

function SecretDialog({ secret, onClose }: { secret: string; onClose: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const closedByUser = useRef(false);
  useEffect(() => { const node = ref.current; node?.showModal(); return () => node?.close(); }, []);
  return <dialog ref={ref} onCancel={() => { closedByUser.current = true; }}
    onClose={() => { if (closedByUser.current) onClose(); }} className="secret-dialog"><div className="stack"><span className="eyebrow">SHOW ONCE</span><h2>Your new API key</h2>
    <p>Copy this key now. It will never be shown again.</p><code className="secret-value">{secret}</code>
    <button className="button primary" onClick={() => navigator.clipboard.writeText(secret)}>Copy key</button>
    <button className="button secondary" onClick={() => { closedByUser.current = true; ref.current?.close(); }}>I saved it</button></div></dialog>;
}

function KeysPanel({ organizationId, project }: { organizationId: string; project?: Project }) {
  const client = useQueryClient(); const [name, setName] = useState(''); const [secret, setSecret] = useState<string | null>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const keys = useQuery({ queryKey: ['keys', organizationId, project?.id], queryFn: () => management.keys(project!.id), enabled: !!project });
  async function create(event: FormEvent) { event.preventDefault(); if (!project) return; setBusy(true); setError('');
    try { const issued = await management.createKey(project.id, name); setName(''); setSecret(issued.secret);
      await client.invalidateQueries({ queryKey: ['keys', organizationId, project.id] }); }
    catch (cause) { setError(message(cause)); } finally { setBusy(false); } }
  async function disable(id: string) { if (!project) return;
    try { await management.disableKey(id); await client.invalidateQueries({ queryKey: ['keys', organizationId, project.id] }); }
    catch (cause) { setError(message(cause)); } }
  if (!project) return <EmptyProject />;
  return <><div className="page-heading"><span className="eyebrow">{project.name}</span><h1>API keys</h1><p>Issue a key for this project. Secrets are visible only at creation.</p></div>
    <div className="two-column"><section className="panel"><h2>Keys</h2>{keys.isError && <ErrorState error={keys.error} />}
      {keys.data?.length ? <ul className="row-list">{keys.data.map(key => <li key={key.id}><div><strong>{key.name}</strong><small className="key-prefix">{key.prefix}••••••••</small></div>
        <div className="row-actions"><span className="tag">{key.status === 0 ? 'Active' : 'Disabled'}</span>{key.status === 0 && <button className="text-button" onClick={() => disable(key.id)}>Disable</button>}</div></li>)}</ul>
        : <p className="muted">No API keys for this project.</p>}</section>
      <form onSubmit={create} className="panel stack"><h2>Create an API key</h2><label>Key name<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required /></label>
        {error && <p role="alert" className="error">{error}</p>}<button disabled={busy} className="button primary">Create key</button><p className="muted">The secret is never stored by this browser.</p></form></div>
    {secret && <SecretDialog secret={secret} onClose={() => setSecret(null)} />}</>;
}

function BillingPanel({ organizationId }: { organizationId: string }) {
  const wallet = useQuery({ queryKey: ['wallet', organizationId], queryFn: () => management.wallet(organizationId), refetchInterval: 15_000 });
  const payments = useQuery({ queryKey: ['topups', organizationId], queryFn: () => management.topUps(organizationId), refetchInterval: 15_000 });
  const [amount, setAmount] = useState('100000'); const [provider, setProvider] = useState<'Payme' | 'Click'>('Payme');
  const [quote, setQuote] = useState<PaymentQuote | null>(null); const [topup, setTopup] = useState<CreatedTopUp | null>(null);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const paymentAttempt = useRef<{ quoteId: string; key: string } | null>(null);
  useEffect(() => { setQuote(null); setTopup(null); setError(''); paymentAttempt.current = null; }, [organizationId]);
  async function getQuote(event: FormEvent) { event.preventDefault(); setBusy(true); setError(''); setTopup(null);
    try { if (!/^\d+$/.test(amount) || BigInt(amount) < 100n) throw new Error('Enter at least 1 UZS.');
      const nextQuote = await management.quotes(organizationId, provider, amount);
      paymentAttempt.current = { quoteId: nextQuote.id, key: crypto.randomUUID() };
      setQuote(nextQuote); }
    catch (cause) { setError(message(cause)); } finally { setBusy(false); } }
  async function startPayment() { if (!quote) return; setBusy(true); setError('');
    try { if (paymentAttempt.current?.quoteId !== quote.id) paymentAttempt.current = { quoteId: quote.id, key: crypto.randomUUID() };
      const intent = await management.createTopUp(organizationId, quote.id, paymentAttempt.current.key);
      setTopup(intent); await payments.refetch(); }
    catch (cause) { setError(message(cause)); } finally { setBusy(false); } }
  return <><div className="page-heading"><span className="eyebrow">MANAGED CREDITS</span><h1>Billing</h1><p>Top up in UZS. Your locked quote determines the credited USD amount.</p></div>
    <div className="summary-grid"><div className="panel metric"><span>Available balance</span><strong>{wallet.data ? usdFromMicro(wallet.data.availableBalanceMicroUsd) : '—'}</strong><small>USD credits</small></div>
      <div className="panel metric"><span>Reserved balance</span><strong>{wallet.data ? usdFromMicro(wallet.data.reservedBalanceMicroUsd) : '—'}</strong><small>Active request holds</small></div></div>
    <div className="two-column"><form onSubmit={getQuote} className="panel stack"><h2>New top-up</h2><label>Amount (UZS)<input inputMode="numeric" pattern="[0-9]+" value={amount} onChange={event => { setAmount(event.target.value); setQuote(null); }} required /></label>
      <label>Payment provider<select value={provider} onChange={event => { setProvider(event.target.value as 'Payme' | 'Click'); setQuote(null); }}><option value="Payme">Payme</option><option value="Click">CLICK</option></select></label>
      <button disabled={busy} className="button secondary">Get quote</button>
      {quote && <div className="quote" role="status"><p>You pay <strong>{uzsFromTiyin(quote.amountTiyin)}</strong></p><p>Fee <strong>{uzsFromTiyin(quote.feeTiyin)}</strong></p><p>Credit <strong>{usdFromMicro(quote.creditMicroUsd)}</strong></p><small>Valid until {new Date(quote.expiresAt).toLocaleString()}</small>
        <button type="button" disabled={busy} onClick={startPayment} className="button primary">Continue to {provider}</button></div>}
      {topup && <div className="notice" role="status">Payment {topup.intent.status}. {topup.checkoutUrl ? <a href={topup.checkoutUrl} target="_blank" rel="noopener noreferrer">Open secure {provider} checkout ↗</a> : 'Checkout unavailable.'}</div>}
      {error && <p role="alert" className="error">{error}</p>}</form>
      <section className="panel"><h2>Payment history</h2>{payments.isError && <ErrorState error={payments.error} />}
        {payments.data?.length ? <ul className="row-list">{payments.data.map(item => <li key={item.id}><div><strong>{uzsFromTiyin(item.amountTiyin)}</strong><small>{item.provider} · {new Date(item.createdAt).toLocaleDateString()}</small></div><span className="tag">{item.status}</span></li>)}</ul>
          : <p className="muted">No payments yet. Balances update only after provider confirmation.</p>}</section></div></>;
}

function EmptyProject() { return <div className="panel"><h2>Create a project first</h2><p>API keys always belong to a project.</p><Link to="../projects" className="button primary">Go to projects</Link></div>; }
function Loading() { return <main className="loading" role="status">Loading your workspace…</main>; }
function NotFound() { return <main className="loading"><h1>Workspace not found</h1><Link to="/">Go home</Link></main>; }
function ErrorState({ error }: { error: unknown }) { return <p role="alert" className="error">{message(error)}</p>; }
function message(error: unknown): string {
  if (error instanceof ApiError) return error.status === 403 ? 'You do not have access to this workspace.'
    : error.status === 401 ? 'Sign in to continue.' : `Request failed (${error.status}). Please try again.`;
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.';
}
