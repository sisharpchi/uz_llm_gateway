import { useEffect, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { ApiError, management, type Session } from '@uzllm/api-client';
import { ActionFeedback, LoadError, useLifecycleAction } from './LifecycleControls';
import './TeamPage.css';

export function AcceptInvitationPage() {
  const session = useQuery({ queryKey: ['session'], queryFn: management.session, staleTime: 0 });
  useEffect(() => {
    // Current invitation email carries a token, not a link. Accept pasted
    // tokens only; never forward accidental URL tokens to login/register.
    const clean = () => {
      const url = new URL(window.location.href);
      if (url.searchParams.has('token') || new URLSearchParams(url.hash.slice(1)).has('token')) {
        url.searchParams.delete('token'); url.hash = '';
        window.history.replaceState(window.history.state, '', url.pathname + url.search);
      }
    };
    clean(); window.addEventListener('hashchange', clean);
    return () => window.removeEventListener('hashchange', clean);
  }, []);
  const waiting = session.isPending || (!session.isFetchedAfterMount && session.isFetching);
  return <main className="team-accept"><span className="eyebrow">WORKSPACE INVITATION</span><h1>Accept an invitation</h1>
    <p>Use the one-time token from your invitation email. Sign in with that exact, verified email address first.</p>
    {waiting ? <p role="status">Checking your session…</p> : session.isError ?
      session.error instanceof ApiError && session.error.status === 401 ? <section className="panel stack"><h2>Sign in to accept</h2>
        <p>Your token is not saved or forwarded. After signing in, return here and paste it from your email.</p><Link className="button primary" to="/login">Sign in</Link><Link to="/register">Create an account</Link></section>
        : <LoadError error={session.error} retry={() => void session.refetch()} />
      : !session.data.emailVerified ? <section className="panel"><h2>Verify your email first</h2><p>Signed in as {session.data.email}. Verification must match the invited account.</p><Link to="/verify-email">Verify email</Link></section>
        : <AcceptForm key={session.data.accountId} session={session.data} />}
  </main>;
}

function AcceptForm({ session }: { session: Session }) {
  const [token, setToken] = useState(''); const [done, setDone] = useState(false);
  const action = useLifecycleAction(); const client = useQueryClient();
  async function submit(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      if (!/^[A-Za-z0-9_-]{43}$/.test(token.trim())) throw new Error('Paste the complete 43-character invitation token from your email.');
      try { return await management.acceptInvitation(token.trim()); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 404)
          throw new Error('This invitation is invalid, expired, already used, revoked, or does not match your verified email. Ask an Owner/Admin for a new invitation.');
        throw cause;
      }
    }, () => { setToken(''); setDone(true); }, 'Invitation accepted. Your workspace access has been updated.', () => {
      void client.invalidateQueries({ queryKey: ['organizations'] });
      void client.invalidateQueries({ queryKey: ['projects'] });
    });
  }
  return <section className="panel stack"><h2>Signed in as {session.email}</h2>
    {done ? <><ActionFeedback {...action} /><p>Choose the organization in the workspace selector. Developer and Read-only roles still need explicit project grants from an Owner/Admin.</p>
      <Link className="button primary" to="/">Open workspace</Link></> : <form className="stack" onSubmit={submit} aria-label="Accept invitation">
      <label>Invitation token<input type="password" value={token} maxLength={43} minLength={43} required autoComplete="off" spellCheck={false}
        disabled={action.busy} onChange={event => setToken(event.target.value)} /></label>
      <p className="muted">Acceptance happens only when you confirm. Tokens expire after seven days and cannot be replayed. Nothing is stored in browser storage or the query cache.</p>
      <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Accept invitation</button>
    </form>}
    <Link to="/">Cancel and return to workspace</Link>
  </section>;
}
