import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, management, type Organization, type Project, type RetainedPayload } from '@uzllm/api-client';
import { ActionFeedback, LifecycleDialog, LoadError, useLifecycleAction } from './LifecycleControls';
import './Lifecycle.css';
import './AlertsPrivacy.css';

export function PrivacyPage({ accountId, organization, project }: { accountId: string; organization: Organization; project: Project }) {
  const client = useQueryClient(); const key = ['payload-policy', accountId, organization.id, project.id];
  const policy = useQuery({ queryKey: key, queryFn: ({ signal }) => management.payloadPolicy(organization.id, project.id, signal), staleTime: 0 });
  const [dialog, setDialog] = useState<'policy' | 'payload' | null>(null);
  const ready = policy.isSuccess && policy.isFetchedAfterMount;
  return <div className="lifecycle-page privacy-page"><div className="page-heading"><span className="eyebrow">OWNER-ONLY CONTROLS · {organization.name} / {project.name}</span><h1>Privacy</h1><p>Retain less. Keep financial evidence separate.</p></div>
    <p className="lifecycle-callout">Payload retention is off by default. This project-only setting does not change provider data policies, billing, permissions, or imply zero data retention upstream.</p>
    {policy.isError ? <section className="panel stack"><h2>Owner access required</h2><LoadError error={policy.error} retry={() => void policy.refetch()} /><p>Only a current organization Owner can read/change retention or retrieve a body. Admin is not sufficient.</p></section> : !ready ? <p role="status">Loading project retention policy…</p> : <>
      <section className="panel stack"><div className="section-title"><div><span className="eyebrow">01 · PROJECT PAYLOAD POLICY</span><h2>{policy.data.enabled ? 'Retention enabled' : 'Retention disabled'}</h2></div><span className="tag">{policy.data.enabled ? 'Explicit opt-in' : 'Default-safe'}</span></div>
        <p><strong>{policy.data.retentionMinutes} minutes</strong> from capture when enabled · 60–10080 minutes permitted (one hour–seven days).</p>
        <p className="muted">Request and bounded complete response bodies are encrypted separately from activity metadata. Shortening the duration also shortens existing records. Expired payload reads are rejected immediately; Worker deletes expired rows in bounded sweeps every five minutes.</p>
        <div className="row-actions"><button className="button primary" onClick={() => setDialog('policy')}>Configure retention</button><button className="button secondary" onClick={() => void policy.refetch()}>Refresh policy</button></div>
      </section>
      <section className="panel stack"><span className="eyebrow">02 · EXPLICIT, AUDITED ACCESS</span><h2>Inspect a retained payload</h2><p className="muted">Ordinary Activity stays payload-free. Paste a request ID from this project to perform one owner-authorized, audited read. No automatic body fetch, query cache, browser storage or export.</p>
        <button className="button secondary" disabled={!policy.data.enabled} onClick={() => setDialog('payload')}>Open payload inspector</button>{!policy.data.enabled && <p>Enable retention explicitly before capturing new requests. Old bodies cannot be reconstructed.</p>}
      </section>
      {dialog === 'policy' && <PolicyDialog orgId={organization.id} project={project} enabled={policy.data.enabled} minutes={policy.data.retentionMinutes} close={() => setDialog(null)} refresh={() => void client.invalidateQueries({ queryKey: key })} />}
      {dialog === 'payload' && policy.data.enabled && <PayloadDialog orgId={organization.id} projectId={project.id} close={() => setDialog(null)} />}
    </>}
    <section className="panel stack"><span className="eyebrow">03 · WHAT THIS DOES NOT DELETE</span><h2>Independent retention classes</h2><p>Opt-out deletes stored payload rows for this project transactionally. It does not erase activity, usage evidence, financial ledger, audit history or provider-side data.</p><p className="muted">Diagnostics and operational metadata follow a separately approved schedule; financial/audit records remain on legal hold. Existing backups may contain old payload bytes until their approved expiry. Restores must stay quarantined until scrubbed. These are not customer-configurable deletion promises.</p></section>
  </div>;
}

function PolicyDialog({ orgId, project, enabled, minutes, close, refresh }: { orgId: string; project: Project; enabled: boolean; minutes: number; close: () => void; refresh: () => void }) {
  const [nextEnabled, setEnabled] = useState(enabled); const [duration, setDuration] = useState(String(minutes)); const [consent, setConsent] = useState(false); const action = useLifecycleAction();
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => { cancel.current?.focus(); }, []);
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(() => {
      const value = Number(duration);
      if (!/^\d+$/.test(duration) || !Number.isInteger(value) || value < 60 || value > 10080) throw new Error('Retention must be a whole number from 60 to 10080 minutes.');
      if (!consent) throw new Error('Confirm the retention and deletion effects before saving.');
      return management.setPayloadPolicy(orgId, project.id, nextEnabled, value);
    }, close, '', refresh);
  }
  return <LifecycleDialog title="Configure project retention" close={close}><form className="stack" onSubmit={save}><p>Applies only to {project.name}. No retroactive capture. If encryption is unavailable, enabling fails closed.</p>
    <label>Payload storage<select value={nextEnabled ? 'on' : 'off'} onChange={event => { setEnabled(event.target.value === 'on'); setConsent(false); }}><option value="off">Disabled · delete stored project payloads</option><option value="on">Enabled · explicit encrypted opt-in</option></select></label>
    <label>Retention duration (minutes)<input type="number" required min={60} max={10080} step={1} value={duration} onChange={event => { setDuration(event.target.value); setConsent(false); }} /></label>
    <p className={nextEnabled ? 'notice' : 'error'}>{nextEnabled ? 'Future request bodies will be stored encrypted until expiry. Shortening also reduces existing expiry times.' : 'Saving Disabled deletes all stored payloads for this project. This cannot be undone; metadata and financial history remain.'}</p>
    <label className="privacy-consent"><input type="checkbox" required checked={consent} onChange={event => setConsent(event.target.checked)} />I understand this project’s storage and deletion effects.</label>
    <ActionFeedback {...action} /><div className="row-actions"><button ref={cancel} type="button" autoFocus className="button secondary" onClick={close}>Cancel</button><button className={`button ${nextEnabled ? 'primary' : 'danger'}`} disabled={action.busy || !consent}>Save retention policy</button></div>
  </form></LifecycleDialog>;
}

function PayloadDialog({ orgId, projectId, close }: { orgId: string; projectId: string; close: () => void }) {
  const [requestId, setRequest] = useState(''); const [payload, setPayload] = useState<RetainedPayload | null>(null); const [expired, setExpired] = useState(false);
  const action = useLifecycleAction(); const abort = useRef<AbortController | null>(null);
  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => {
    if (!payload) return;
    const timer = window.setInterval(() => { if (Date.parse(payload.expiresAt) <= Date.now()) { setPayload(null); setExpired(true); } }, 250);
    return () => window.clearInterval(timer);
  }, [payload]);
  async function inspect(event: FormEvent) {
    event.preventDefault(); setPayload(null); setExpired(false); const id = requestId.trim(); setRequest('');
    await action.run(async () => {
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) throw new Error('Enter a valid request UUID from this project’s Activity.');
      abort.current?.abort(); abort.current = new AbortController();
      try { return await management.retainedPayload(orgId, projectId, id, abort.current.signal); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 404) throw new Error('Payload unavailable: never captured, expired, opted out, or outside this project. No body was returned.');
        throw cause;
      }
    }, value => { if (Date.parse(value.expiresAt) <= Date.now()) setExpired(true); else setPayload(value); }, '');
  }
  return <LifecycleDialog title="Retained payload · private" close={close}><form className="stack" onSubmit={inspect}>
    <p>Owner-only, audited access. Closing, switching scope or local expiry clears this view. Do not share private prompt/response content.</p>
    <label>Request UUID<input required autoComplete="off" spellCheck={false} value={requestId} onChange={event => setRequest(event.target.value)} /></label><button className="button secondary" disabled={action.busy}>Read retained payload</button><ActionFeedback {...action} />
    {expired && <p role="status">Payload expired. Its body has been cleared from this view.</p>}
    {payload && <section className="stack" aria-label="Private payload contents"><p>Request {payload.requestId} · expires <time dateTime={payload.expiresAt}>{new Date(payload.expiresAt).toLocaleString()}</time></p><h3>Retained request</h3><pre className="private-payload">{payload.request}</pre><h3>Retained response</h3>{payload.response === null ? <p>No complete response was retained. Interrupted or oversized responses may be absent; this does not imply zero provider cost.</p> : <pre className="private-payload">{payload.response}</pre>}</section>}
    <button type="button" autoFocus className="button secondary" onClick={close}>Close and clear payload</button>
  </form></LifecycleDialog>;
}
