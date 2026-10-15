import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, management, usageUsdFromMicro, type ByokCredential, type ByokProvider, type Organization, type Project } from '@uzllm/api-client';
import { ActionFeedback, LifecycleDialog, LoadError, parseCodes, useLifecycleAction } from './LifecycleControls';
import './Lifecycle.css';
import './ProviderKeysPage.css';

export const byokKeysQuery = (accountId: string, orgId: string) => ['byok-keys', accountId, orgId];
const testExplanation = (status: string | null) => status === 'Valid' ? 'Valid — model-list authentication accepted; not a quota, model-access or billing guarantee.'
  : status === 'Invalid' ? 'Invalid — authentication rejected. Rotate the key or check its provider account.'
    : status === 'Unavailable' ? 'Unavailable — quota, service, network or verification deadline prevented a conclusive test. No automatic retry.'
      : 'Not tested — Active means enabled locally, not verified or healthy.';

async function byokCommand<T>(command: () => Promise<T>) {
  try { return await command(); }
  catch (cause) {
    if (cause instanceof ApiError && cause.status === 409)
      throw new Error('The credential changed during this operation or is disabled. Refresh before retrying; no automatic retry was made.');
    throw cause;
  }
}

export function ProviderKeysPage({ organization, accountId, projects }: { organization: Organization; accountId: string; projects: Project[] }) {
  const client = useQueryClient(); const orgId = organization.id;
  const keys = useQuery({ queryKey: byokKeysQuery(accountId, orgId), queryFn: ({ signal }) => management.byokKeys(orgId, signal), staleTime: 0 });
  const options = useQuery({ queryKey: ['byok-providers', accountId, orgId], queryFn: ({ signal }) => management.byokProviders(orgId, signal), enabled: keys.isSuccess, staleTime: 0 });
  const [dialog, setDialog] = useState<{ kind: 'create' } | { kind: 'edit' | 'disable' | 'delete'; key: ByokCredential } | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const current = keys.data?.find(key => key.id === selected);
  function refresh() { void client.invalidateQueries({ queryKey: byokKeysQuery(accountId, orgId) }); }
  const waiting = keys.isPending || (!keys.isFetchedAfterMount && keys.isFetching);
  return <div className="lifecycle-page provider-page"><div className="page-heading"><span className="eyebrow">ORGANIZATION CREDENTIALS · {organization.name}</span>
    <h1>Provider keys</h1><p>Your providers. Your credentials. Explicit access for every project.</p></div>
    {waiting ? <p role="status">Loading provider keys…</p> : keys.isError ? <section className="panel stack"><h2>Provider key access unavailable</h2><LoadError error={keys.error} retry={refresh} /><p className="muted">Only current Owners and Admins may read or manage BYOK credentials.</p></section> : <>
      <p className="lifecycle-callout">Secrets are encrypted and never returned. Adding or granting a key does not change Managed execution: each request must explicitly select BYOK or Hybrid.</p>
      <section className="panel"><div className="section-title"><div><h2>Connected credentials</h2><span className="muted">{keys.data.length} keys · metadata only</span></div><div className="row-actions">
        <button className="button secondary" onClick={refresh}>Refresh keys</button><button className="button primary" onClick={() => setDialog({ kind: 'create' })}>Add provider key</button></div></div>
        {keys.data.length ? <ul className="row-list">{keys.data.map(key => <li key={key.id}><div><strong>{key.name}</strong><small>{key.providerCode} · <code>{key.maskedKey}</code> · {key.status}</small>
          <small>{testExplanation(key.lastTestStatus)}</small><small>{key.projectIds.length} explicit project grants</small></div><div className="row-actions">
          <button className="button secondary" onClick={() => setSelected(key.id)}>Configure {key.name}</button>
          <button className="text-button" disabled={key.status !== 'Active'} onClick={() => setDialog({ kind: 'edit', key })}>Edit {key.name}</button>
          <button className="text-button danger-text" disabled={key.status !== 'Active'} onClick={() => setDialog({ kind: 'disable', key })}>Disable {key.name}</button>
          <button className="text-button danger-text" onClick={() => setDialog({ kind: 'delete', key })}>Delete {key.name}</button></div></li>)}</ul>
          : <p className="muted">No provider keys. Add an active supported provider, then grant projects separately. Failed grant saves do not undo a created credential.</p>}</section>
      {current && <CredentialControls key={`${current.id}:${current.updatedAt}`} credential={current} projects={projects} refresh={refresh} />}
      {dialog && (dialog.kind === 'create' || dialog.kind === 'edit'
        ? <CredentialDialog key={dialog.kind === 'edit' ? dialog.key.id : 'new'} orgId={orgId} credential={dialog.kind === 'edit' ? dialog.key : undefined} options={options.data ?? []}
          optionsError={options.error} retryOptions={() => void options.refetch()} close={() => setDialog(null)} refresh={refresh} />
        : <ConfirmCredentialDialog orgId={orgId} credential={dialog.key} kind={dialog.kind} close={() => setDialog(null)} refresh={refresh} />)}
    </>}</div>;
}

function CredentialDialog({ orgId, credential, options, optionsError, retryOptions, close, refresh }: {
  orgId: string; credential?: ByokCredential; options: ByokProvider[]; optionsError: unknown; retryOptions: () => void; close: () => void; refresh: () => void;
}) {
  const [provider, setProvider] = useState(''); const [name, setName] = useState(credential?.name ?? ''); const [secret, setSecret] = useState('');
  const action = useLifecycleAction();
  async function save(event: FormEvent) {
    event.preventDefault();
    const submittedSecret = secret; setSecret(''); // Never retain submitted plaintext while awaiting the server.
    await action.run(() => byokCommand(() => credential
      ? management.updateByokKey(orgId, credential.id, name.trim(), submittedSecret || null)
      : management.createByokKey(orgId, provider, name.trim(), submittedSecret)), () => close(), '', refresh);
  }
  return <LifecycleDialog title={credential ? `Edit ${credential.name}` : 'Add provider key'} close={close}>
    <form className="stack" onSubmit={save}><p>Fixed official endpoints only. The secret stays masked, is cleared on submit/close, and cannot be retrieved. Re-enter it after a failed write; refresh first if the outcome is uncertain.</p>
      {!credential && (optionsError ? <LoadError error={optionsError} retry={retryOptions} /> : <label>Provider<select aria-label="Provider" required value={provider} onChange={event => setProvider(event.target.value)}><option value="">Choose an active provider</option>
        {options.map(option => <option value={option.id} key={option.id}>{option.name} ({option.code})</option>)}</select><small>No options? Ask an operator to activate a supported catalog provider.</small></label>)}
      {credential && <p className="muted">{credential.providerCode} · {credential.maskedKey}. Rotation resets the verification result.</p>}
      <label>Credential name<input required minLength={2} maxLength={120} value={name} onChange={event => setName(event.target.value)} /></label>
      <label>{credential ? 'Replacement provider secret (optional)' : 'Provider secret'}<input type="password" autoComplete="off" spellCheck={false} required={!credential} minLength={12} maxLength={4096} pattern="[!-~]{12,4096}" value={secret} onChange={event => setSecret(event.target.value)} /></label>
      <ActionFeedback {...action} /><div className="row-actions"><button type="button" autoFocus className="button secondary" onClick={close}>Cancel</button>
        <button className="button primary" disabled={action.busy || (!credential && (!options.length || !!optionsError))}>{action.busy ? 'Saving…' : 'Save credential'}</button></div></form>
  </LifecycleDialog>;
}

function ConfirmCredentialDialog({ orgId, credential, kind, close, refresh }: { orgId: string; credential: ByokCredential; kind: 'disable' | 'delete'; close: () => void; refresh: () => void }) {
  const action = useLifecycleAction();
  return <LifecycleDialog title={`${kind === 'delete' ? 'Delete' : 'Disable'} ${credential.name}?`} close={close}>
    <p>{kind === 'delete' ? 'Permanently crypto-shred this stored secret. Audit and financial history remain; this cannot be undone.' : 'Stop future use. There is no re-enable operation; add a new credential if needed.'} Existing financial holds are not erased and BYOK requests do not silently switch to Managed.</p>
    <ActionFeedback {...action} /><div className="row-actions"><button autoFocus className="button secondary" onClick={close}>Cancel</button><button className="button danger" disabled={action.busy}
      onClick={() => void action.run(() => byokCommand(() => kind === 'delete' ? management.deleteByokKey(orgId, credential.id) : management.disableByokKey(orgId, credential.id)), close, '', refresh)}>Confirm {kind}</button></div>
  </LifecycleDialog>;
}

function capToMicro(value: string): string | null {
  if (!value.trim()) return null;
  if (!/^\d+(\.\d{1,6})?$/.test(value)) throw new Error('Use a non-negative USD amount with up to six decimals; no exponent or commas.');
  const [whole, fraction = ''] = value.split('.'); const micro = BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
  if (micro > 9_223_372_036_854_775_807n) throw new Error('Amount exceeds the supported Int64 external-spend cap.');
  return micro.toString();
}
const capUsd = (value: string | null) => value === null ? '' : `${BigInt(value) / 1_000_000n}.${(BigInt(value) % 1_000_000n).toString().padStart(6, '0')}`;

function CredentialControls({ credential: key, projects, refresh }: { credential: ByokCredential; projects: Project[]; refresh: () => void }) {
  const action = useLifecycleAction(); const [test, setTest] = useState<string | null>(null);
  const [restricted, setRestricted] = useState(key.allowedModels !== null); const [models, setModels] = useState(key.allowedModels?.join('\n') ?? '');
  const [cap, setCap] = useState(capUsd(key.spendLimitMicroUsd)); const [project, setProject] = useState('');
  const [grant, setGrant] = useState<{ id: string; enabled: boolean } | null>(null);
  const active = key.status === 'Active'; const orgId = key.organizationId;
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(() => {
      const codes = restricted ? parseCodes(models) : null;
      if (codes && (!codes.length || codes.length > 100)) throw new Error('Choose 1–100 canonical models from this provider, or use Unrestricted. Empty lists are not supported for BYOK.');
      return byokCommand(() => management.restrictByokKey(orgId, key.id, codes, capToMicro(cap)));
    }, undefined, 'Restrictions saved. Existing holds remain; future admission rechecks all policies.', refresh);
  }
  return <section className="panel stack" aria-label={`Configure ${key.name}`}><div className="section-title"><div><span className="eyebrow">CREDENTIAL CONTROLS · {key.providerCode}</span><h2>{key.name}</h2><code>{key.maskedKey}</code></div><span className="tag">{key.status}</span></div>
    <div className="provider-stats"><div><small>Estimated external spend</small><strong>{usageUsdFromMicro(key.externalSpentMicroUsd)}</strong></div><div><small>External reserved</small><strong>{usageUsdFromMicro(key.externalReservedMicroUsd)}</strong></div><div><small>Lifetime external cap</small><strong>{key.spendLimitMicroUsd === null ? 'Uncapped' : usageUsdFromMicro(key.spendLimitMicroUsd)}</strong></div></div>
    <p className="muted">Supplier spend is catalog-estimated, not an invoice guarantee. Your wallet pays the published BYOK platform fee, not this external spend. Unknown usage can retain holds.</p>
    <div className="credential-test"><p role="status">{testExplanation(test ?? key.lastTestStatus)}<br /><small>{key.lastTestedAt ? `Last recorded: ${new Date(key.lastTestedAt).toLocaleString()}` : 'No recorded test time.'} Verification does not reset inference health.</small></p>
      <button className="button secondary" disabled={!active || action.busy} onClick={() => void action.run(() => byokCommand(() => management.testByokKey(orgId, key.id)), result => setTest(result.status), 'Verification finished.', refresh)}>Test credential</button></div>
    {!active && <p className="notice">Disabled. Editing/testing/new grants unavailable. Existing grants may be removed; deletion is still available.</p>}
    <form className="stack" onSubmit={save}><fieldset disabled={!active || action.busy}><legend>Model & spend restrictions</legend>
      <div className="control-pair"><div className="stack"><label>Model access<select value={restricted ? 'selected' : 'all'} onChange={event => setRestricted(event.target.value === 'selected')}><option value="all">Unrestricted provider models</option><option value="selected">Selected canonical models</option></select></label>
        {restricted && <label>Allowed canonical models<textarea required rows={3} maxLength={20100} value={models} onChange={event => setModels(event.target.value)} /></label>}</div>
        <label>Lifetime external cap (USD)<input inputMode="decimal" value={cap} onChange={event => setCap(event.target.value)} placeholder="Blank = uncapped; 0 = block new spend" /><small>Six decimal places. Blank resets to uncapped. Zero/lowering below spent + held blocks future admission, never removes holds.</small></label></div>
      <p className="muted">Use active canonical model codes for this provider. Project and API-key restrictions still intersect. Saving replaces this restriction group.</p><button className="button primary">Save restrictions</button></fieldset></form>
    <div className="stack"><h3>Explicit project access</h3><p className="muted">No grant means no use. Each change is independent; granting never enables BYOK by default.</p>
      <ul className="row-list">{key.projectIds.map(id => <li key={id}><strong>{projects.find(value => value.id === id)?.name ?? id}</strong><button className="button secondary" disabled={action.busy} onClick={() => setGrant({ id, enabled: false })}>Remove grant {projects.find(value => value.id === id)?.name ?? id}</button></li>)}</ul>
      {!key.projectIds.length && <p>No projects granted.</p>}<div className="control-pair"><label>Project to grant<select value={project} disabled={!active} onChange={event => setProject(event.target.value)}><option value="">Choose a project</option>{projects.filter(value => value.status === 0 && !key.projectIds.includes(value.id)).map(value => <option value={value.id} key={value.id}>{value.name}</option>)}</select></label>
        <button className="button secondary" disabled={!active || !project || action.busy} onClick={() => setGrant({ id: project, enabled: true })}>Grant project access</button></div></div>
    <ActionFeedback {...action} />
    {grant && <LifecycleDialog title={grant.enabled ? 'Grant project access?' : 'Remove project grant?'} close={() => setGrant(null)}><p>Change access for {projects.find(value => value.id === grant.id)?.name ?? grant.id} in this organization. This affects future admission only.</p><button autoFocus className="button secondary" onClick={() => setGrant(null)}>Cancel</button><button className="button primary" disabled={action.busy}
      onClick={() => void action.run(() => byokCommand(() => grant.enabled ? management.grantByokProject(orgId, key.id, grant.id) : management.revokeByokProject(orgId, key.id, grant.id)), () => setGrant(null), 'Project access updated.', refresh)}>Confirm grant change</button><ActionFeedback {...action} /></LifecycleDialog>}
  </section>;
}
