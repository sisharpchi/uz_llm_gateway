import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { management, usageUsdFromMicro, type ApiKey, type Project } from '@uzllm/api-client';
import { ActionFeedback, AllowlistField, allowlistValue, codeListDraft, LifecycleDialog, LoadError, parseCodes, ShowOnceSecret, useLifecycleAction } from './LifecycleControls';
import { BudgetsEditor, LimitsEditor } from './LimitsEditor';
import './Lifecycle.css';

export function KeysPage({ organizationId, project }: { organizationId: string; project?: Project }) {
  if (!project) return <section className="panel"><h1>Choose a project first</h1><p>Create a project before managing API keys.</p></section>;
  if (project.status !== 0) return <section className="panel"><h1>Archived project</h1><p>New keys and changes are unavailable. Existing usage and financial history remain available.</p></section>;
  return <ProjectKeys organizationId={organizationId} project={project} />;
}

function ProjectKeys({ organizationId, project }: { organizationId: string; project: Project }) {
  const client = useQueryClient(); const action = useLifecycleAction();
  const [name, setName] = useState(''); const [expires, setExpires] = useState(''); const [secret, setSecret] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null); const [filter, setFilter] = useState('');
  const [confirmation, setConfirmation] = useState<{ apiKey: ApiKey; kind: 'rotate' | 'disable' | 'enable' } | null>(null);
  const queryKey = ['keys', organizationId, project.id];
  const keys = useQuery({ queryKey, queryFn: () => management.keys(project.id), staleTime: 0, refetchOnWindowFocus: false });
  const selectedKey = keys.data?.find(value => value.id === selected);
  async function create(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      const expiry = expires ? new Date(expires + 'Z') : null;
      if (expiry && (!Number.isFinite(expiry.getTime()) || expiry.getTime() <= Date.now())) throw new Error('Expiry must be a future UTC date and time.');
      return management.createKey(project.id, name.trim(), expiry?.toISOString() ?? null);
    }, issued => { setSecret(issued.secret); setName(''); setExpires(''); }, 'Key created. Copy the show-once secret.',
      () => { void client.invalidateQueries({ queryKey }); });
  }
  const filtered = keys.data?.filter(value => `${value.name} ${value.prefix}`.toLowerCase().includes(filter.toLowerCase())) ?? [];
  return <div className="lifecycle-page"><div className="page-heading"><span className="eyebrow">PROJECT ACCESS · {project.name}</span><h1>API keys</h1>
    <p>Issue credentials, narrow permissions and control spend without losing history.</p></div>
    <p className="lifecycle-callout">Secrets appear once, at creation or rotation. Only masked prefixes are listed. Disable revokes new admission; it does not delete history or cancel already admitted work.</p>
    <div className="lifecycle-overview"><section className="panel"><div className="section-title"><h2>Keys</h2><span className="tag">{keys.data?.length ?? '—'} total</span></div>
      <label className="stack">Search keys<input type="search" value={filter} onChange={event => setFilter(event.target.value)} placeholder="Name or public prefix" /></label>
      {keys.isPending ? <p role="status">Loading API keys…</p> : keys.isError ? <LoadError error={keys.error} retry={() => void keys.refetch()} /> : filtered.length ?
        <ul className="row-list key-list">{filtered.map(key => {
          const expired = key.expiresAt != null && Date.parse(key.expiresAt) <= Date.now();
          return <li key={key.id}><div><strong>{key.name}</strong><code className="key-prefix">{key.prefix}••••••••</code>
            <small>Created {new Date(key.createdAt).toLocaleDateString()} · {key.expiresAt ? `Expires ${new Date(key.expiresAt).toISOString()} (UTC)` : 'No expiry'}</small></div>
            <div className="row-actions"><span className={`tag ${key.status === 0 && !expired ? 'status-active' : ''}`}>{key.status === 1 ? 'Disabled' : expired ? 'Expired' : 'Active'}</span>
              <button className="button secondary" aria-expanded={selected === key.id} onClick={() => setSelected(selected === key.id ? null : key.id)}>Configure {key.name}</button>
              <button className="text-button" disabled={expired} onClick={() => setConfirmation({ apiKey: key, kind: key.status === 0 ? 'disable' : 'enable' })}>{key.status === 0 ? 'Disable' : 'Enable'} {key.name}</button>
              <button className="text-button" disabled={key.status !== 0 || expired} onClick={() => setConfirmation({ apiKey: key, kind: 'rotate' })}>Rotate {key.name}</button></div></li>;
        })}</ul> : <p className="muted">{keys.data?.length ? 'No matching keys.' : 'No API keys for this project.'}</p>}</section>
      <form onSubmit={create} className="panel stack"><span className="eyebrow">SHOW-ONCE CREDENTIAL</span><h2>Create an API key</h2>
        <label>Key name<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required /></label>
        <label>Expiry (UTC, optional)<input type="datetime-local" value={expires} onChange={event => setExpires(event.target.value)} /></label>
        <ActionFeedback {...action} /><button className="button primary" disabled={action.busy || keys.isError || keys.isPending}>Create key</button>
        <small className="muted">New keys inherit project restrictions. Configure permissions and budgets before using the secret. Those are separate API operations.</small>
      </form></div>
    {!keys.isError && selectedKey && <KeyControls key={selectedKey.id} organizationId={organizationId} projectId={project.id} apiKey={selectedKey} />}
    {secret && <ShowOnceSecret secret={secret} close={() => setSecret(null)} />}
    {confirmation && <KeyActionDialog key={`${confirmation.apiKey.id}:${confirmation.kind}`} {...confirmation} projectId={project.id} organizationId={organizationId}
      close={() => setConfirmation(null)} issued={value => { setConfirmation(null); setSecret(value); }} />}
  </div>;
}

function KeyActionDialog({ apiKey, kind, organizationId, projectId, close, issued }: {
  apiKey: ApiKey; kind: 'rotate' | 'disable' | 'enable'; organizationId: string; projectId: string; close: () => void; issued: (secret: string) => void;
}) {
  const action = useLifecycleAction(); const client = useQueryClient();
  const title = kind === 'rotate' ? 'Rotate' : kind === 'disable' ? 'Disable' : 'Enable';
  async function confirm() {
    await action.run(async () => kind === 'rotate' ? management.rotateKey(apiKey.id) : management.setKeyStatus(apiKey.id, kind === 'disable' ? 1 : 0), result => {
      if (result) issued(result.secret); else close();
    }, 'Key updated.', () => { void client.invalidateQueries({ queryKey: ['keys', organizationId, projectId] }); });
  }
  return <LifecycleDialog title={`${title} ${apiKey.name}?`} close={close}>
    <p>{kind === 'rotate' ? 'The old secret stops working immediately after commit, with no overlap. Update your application with the show-once replacement. Identity, permissions and budgets remain unchanged.'
      : kind === 'disable' ? 'New requests using this key will be denied. History and already admitted requests remain intact. You can enable this key later.'
        : 'This key will admit new requests again, subject to expiry, project restrictions, budgets and limits.'}</p>
    <ActionFeedback {...action} /><div className="row-actions"><button autoFocus className="button secondary" onClick={close}>Cancel</button>
      <button className={`button ${kind === 'enable' ? 'primary' : 'danger'}`} disabled={action.busy} onClick={() => void confirm()}>Confirm {kind}</button></div>
  </LifecycleDialog>;
}

function KeyControls({ organizationId, projectId, apiKey }: { organizationId: string; projectId: string; apiKey: ApiKey }) {
  const [tab, setTab] = useState<'permissions' | 'limits' | 'usage'>('permissions');
  return <section className="project-controls"><span className="eyebrow">KEY CONTROLS</span><h2>{apiKey.name}</h2>
    <div className="lifecycle-tabs" aria-label="Key controls">{(['permissions', 'limits', 'usage'] as const).map(value => <button key={value} className="button secondary" aria-pressed={tab === value} onClick={() => setTab(value)}>
      {value === 'permissions' ? 'Model & provider permissions' : value === 'limits' ? 'Limits & budgets' : 'Key usage'}</button>)}</div>
    {tab === 'permissions' ? <KeyPermissionsEditor organizationId={organizationId} projectId={projectId} apiKey={apiKey} />
      : tab === 'limits' ? <div className="stack"><LimitsEditor organizationId={organizationId} projectId={projectId} apiKeyId={apiKey.id} />
        <BudgetsEditor organizationId={organizationId} projectId={projectId} apiKeyId={apiKey.id} /></div>
        : <KeyUsage organizationId={organizationId} projectId={projectId} apiKeyId={apiKey.id} />}
  </section>;
}

function KeyPermissionsEditor({ organizationId, projectId, apiKey }: { organizationId: string; projectId: string; apiKey: ApiKey }) {
  const [models, setModels] = useState(codeListDraft(apiKey.permissions?.allowedModels));
  const [providers, setProviders] = useState(codeListDraft(apiKey.permissions?.allowedProviders));
  const [deniedModels, setDeniedModels] = useState(apiKey.permissions?.deniedModels?.join('\n') ?? '');
  const [deniedProviders, setDeniedProviders] = useState(apiKey.permissions?.deniedProviders?.join('\n') ?? '');
  const action = useLifecycleAction(); const client = useQueryClient();
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(() => management.setKeyPermissions(apiKey.id, { allowedModels: allowlistValue(models), allowedProviders: allowlistValue(providers),
      deniedModels: parseCodes(deniedModels), deniedProviders: parseCodes(deniedProviders) }),
    undefined, 'Key permissions saved. Deny rules always win.', () => { void client.invalidateQueries({ queryKey: ['keys', organizationId, projectId] }); });
  }
  return <form className="panel stack lifecycle-section" onSubmit={save} aria-label="Key permissions"><h3>Model & provider permissions</h3>
    <p className="muted">Deny wins over allow. Both key and project policies must permit a route, including fallback. Updates affect future admissions, not active streams.</p>
    <div className="control-pair"><AllowlistField label="Allowed models" value={models} change={setModels} /><AllowlistField label="Allowed providers" value={providers} change={setProviders} /></div>
    <div className="control-pair"><label>Denied model codes<textarea rows={3} maxLength={25856} value={deniedModels} onChange={event => setDeniedModels(event.target.value)} placeholder="One canonical code per line; blank denies none" /></label>
      <label>Denied provider codes<textarea rows={3} maxLength={25856} value={deniedProviders} onChange={event => setDeniedProviders(event.target.value)} placeholder="e.g. anthropic" /></label></div>
    <small className="muted">Saving replaces all four lists, last-commit-wins. Price-class restrictions are not supported.</small>
    <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Save key permissions</button>
  </form>;
}

function KeyUsage({ organizationId, projectId, apiKeyId }: { organizationId: string; projectId: string; apiKeyId: string }) {
  const query = useQuery({ queryKey: ['key-usage', organizationId, projectId, apiKeyId], queryFn: () => management.usageSummary(organizationId, { projectId, apiKeyId }), refetchInterval: 15_000 });
  return <section className="panel lifecycle-section"><h3>Key usage · last 30 days</h3><p className="muted">Financial budgets use their own UTC windows; analytics never replaces authoritative admission checks.</p>
    {query.isPending ? <p role="status">Loading key usage…</p> : query.isError ? <LoadError error={query.error} retry={() => void query.refetch()} />
      : <div className="budget-grid"><div className="budget-card"><span>Requests</span><strong>{query.data.requestCount}</strong></div>
        <div className="budget-card"><span>Tokens · input / output</span><strong>{query.data.inputTokens} / {query.data.outputTokens}</strong></div>
        <div className="budget-card"><span>Net charge · USD</span><strong>{usageUsdFromMicro(query.data.netChargedMicroUsd)}</strong></div></div>}
    {query.data && <p className="muted">As of {new Date(query.data.dataAsOf).toISOString()} · {query.data.pendingCount} requests unsettled · {query.data.aggregation.pendingBucketCount} aggregation buckets pending. Unknown usage is not zero.</p>}
    <Link className="button secondary" to={`/organizations/${organizationId}/activity?project=${projectId}`}>Open project activity and filter by key</Link>
  </section>;
}
