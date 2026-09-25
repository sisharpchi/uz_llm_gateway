import { useEffect, useState, type FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { management, type Session } from '@uzllm/api-client';
import './style.css';

function AdminApp() {
  const [session, setSession] = useState<Session | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  useEffect(() => { management.session().then(setSession).catch(() => setSession(null)).finally(() => setLoading(false)); }, []);
  async function login(event: FormEvent) { event.preventDefault(); setError('');
    try { await management.login(email, password); setSession(await management.session()); setPassword(''); }
    catch { setError('Sign-in failed. Check your credentials.'); } }
  if (loading) return <main className="center" role="status">Checking operator session…</main>;
  if (!session) return <main className="center"><form onSubmit={login} className="card"><span className="eyebrow">UZLLM · OPERATOR</span><h1>Sign in</h1>
    <label>Email<input type="email" value={email} onChange={event => setEmail(event.target.value)} required /></label>
    <label>Password<input type="password" value={password} onChange={event => setPassword(event.target.value)} required /></label>
    {error && <p role="alert" className="error">{error}</p>}<button>Sign in</button></form></main>;
  if (!session.isOperator) return <main className="center"><div className="card"><span className="eyebrow">ACCESS DENIED</span><h1>Operator access required</h1><p>This account cannot use the operator console.</p>
    <button onClick={async () => { await management.logout(); setSession(null); }}>Sign out</button></div></main>;
  return <div className="shell"><aside><strong>UZLLM <small>OPERATOR</small></strong><p>Restricted control plane</p></aside><main><span className="eyebrow">SIGNED IN AS {session.email}</span><h1>Operations</h1>
    <div className="card"><h2>Console foundation</h2><p>Provider, pricing, payment, ledger, incident and audit controls arrive in ADMIN-001. No privileged action is exposed before server-side operator authorization and recent MFA are enforced.</p></div>
    <button onClick={async () => { await management.logout(); setSession(null); }}>Sign out</button></main></div>;
}

createRoot(document.getElementById('root')!).render(<AdminApp />);
