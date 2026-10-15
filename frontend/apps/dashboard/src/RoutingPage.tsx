import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { management, type ByokCredential, type CustomerCatalog, type Organization, type Project, type ProjectSettings } from '@uzllm/api-client';
import { LoadError } from './LifecycleControls';
import { ProjectSettingsEditor } from './ProjectsPage';
import { byokKeysQuery } from './ProviderKeysPage';

const strategies = [ ['priority', 'Deterministic priority'], ['price', 'Lowest price'], ['latency', 'Lowest latency'], ['throughput', 'Highest throughput'], ['auto', 'Auto'] ];

export function RoutingPage({ organization, project, accountId }: { organization: Organization; project?: Project; accountId: string }) {
  if (!project || project.status !== 0) return <section className="panel"><h1>Routing</h1><p>Select an active project before configuring routing.</p></section>;
  return <ProjectRouting key={project.id} organization={organization} project={project} accountId={accountId} />;
}

function ProjectRouting({ organization, project, accountId }: { organization: Organization; project: Project; accountId: string }) {
  const settings = useQuery({ queryKey: ['project-settings', organization.id, project.id], queryFn: ({ signal }) => management.projectSettings(organization.id, project.id, signal), staleTime: 0 });
  const catalog = useQuery({ queryKey: ['route-catalog', accountId, organization.id, project.id], queryFn: ({ signal }) => management.catalog(organization.id, project.id, signal), staleTime: 0 });
  const keys = useQuery({ queryKey: byokKeysQuery(accountId, organization.id), queryFn: ({ signal }) => management.byokKeys(organization.id, signal), staleTime: 0 });
  const waiting = settings.isPending || catalog.isPending || (!settings.isFetchedAfterMount && settings.isFetching) || (!catalog.isFetchedAfterMount && catalog.isFetching);
  return <div className="lifecycle-page provider-page"><div className="page-heading"><span className="eyebrow">ROUTING & EXECUTION · {project.name}</span><h1>Routing</h1><p>Configure defaults. Preview explicit requests. Keep hard restrictions in control.</p></div>
    <p className="lifecycle-callout">Managed is always the default. Strategy is configurable; model/provider permissions, budgets, wallet and operator limits are mandatory. No provider key or fallback consent is stored in project defaults.</p>
    <ProjectSettingsEditor organizationId={organization.id} projectId={project.id} />
    {waiting ? <p role="status">Loading preview configuration…</p> : settings.isError ? <LoadError error={settings.error} retry={() => void settings.refetch()} /> : catalog.isError ? <LoadError error={catalog.error} retry={() => void catalog.refetch()} />
      : <RequestPreview key={`${settings.data.routing}:${JSON.stringify(settings.data.allowedModels)}:${JSON.stringify(settings.data.allowedProviders)}`} settings={settings.data} catalog={catalog.data}
        credentials={keys.isError || !keys.isFetchedAfterMount ? [] : keys.data ?? []} keysUnavailable={keys.isError || keys.isPending || !keys.isFetchedAfterMount} projectId={project.id} />}
    {keys.isError && <p className="notice">BYOK metadata unavailable: only Owner/Admin can inspect credentials. Managed preview remains available. Refresh after access changes.</p>}
    <div className="row-actions"><Link className="button secondary" to={`/organizations/${organization.id}/provider-keys?project=${project.id}`}>Manage provider keys</Link><Link className="button secondary" to={`/organizations/${organization.id}/projects?project=${project.id}`}>Project limits & budgets</Link>
      <button className="button secondary" onClick={() => { void settings.refetch(); void catalog.refetch(); void keys.refetch(); }}>Refresh preview data</button></div>
  </div>;
}

function RequestPreview({ settings, catalog, credentials, keysUnavailable, projectId }: { settings: ProjectSettings; catalog: CustomerCatalog; credentials: ByokCredential[]; keysUnavailable: boolean; projectId: string }) {
  const [mode, setMode] = useState<'managed' | 'byok' | 'hybrid'>('managed');
  const [keyId, setKeyId] = useState(''); const [modelId, setModelId] = useState(''); const [pin, setPin] = useState('');
  const [strategy, setStrategy] = useState(''); const [consent, setConsent] = useState(false);
  const [fallback1, setFallback1] = useState(''); const [fallback2, setFallback2] = useState('');
  const selected = credentials.find(key => key.id === keyId);
  const model = catalog.models.find(value => value.id === modelId);
  const allows = (values: string[] | null | undefined, value: string) => values == null || values.includes(value);
  const fallbacks = [fallback1, fallback2].filter(Boolean);
  const models = model ? [model.id, ...fallbacks] : [];
  const issues: string[] = [];
  if (!model) issues.push('Select a published model. An empty catalog may indicate project restrictions or unavailable pricing.');
  if (new Set(models).size !== models.length) issues.push('Fallback models must be unique and different from the primary model.');
  for (const code of models) {
    const entry = catalog.models.find(value => value.id === code);
    if (!entry || !allows(settings.allowedModels, code)) issues.push(`Model ${code} is not available under the current project restrictions.`);
    else if (!entry.providers.some(provider => allows(settings.allowedProviders, provider.provider) && (code !== model?.id || !pin || provider.provider === pin)))
      issues.push(`No permitted catalog provider for ${code}. A provider pin cannot bypass restrictions.`);
  }
  if (mode !== 'managed') {
    if (keysUnavailable || !selected) issues.push('Select a currently readable provider key; unavailable metadata never changes the request to Managed.');
    else {
      if (selected.status !== 'Active') issues.push('This credential is disabled.');
      if (!selected.projectIds.includes(projectId)) issues.push('This project has no explicit grant for the credential.');
      if (model && selected.allowedModels !== null && !selected.allowedModels.includes(model.id)) issues.push('The credential does not allow this canonical model.');
      if (!allows(settings.allowedProviders, selected.providerCode) || (pin && pin !== selected.providerCode)) issues.push('The BYOK provider conflicts with the pin or project provider restrictions.');
      if (model && !model.providers.some(provider => provider.provider === selected.providerCode)) issues.push('The BYOK provider has no published mapping for this model.');
      if (selected.spendLimitMicroUsd !== null && BigInt(selected.externalSpentMicroUsd) + BigInt(selected.externalReservedMicroUsd) >= BigInt(selected.spendLimitMicroUsd))
        issues.push('Estimated external spend plus holds has reached the lifetime cap. Gateway admission remains authoritative.');
    }
  }
  if (mode === 'byok' && fallbacks.length) issues.push('Cross-model fallback requires explicit Hybrid managed consent, not BYOK-only execution.');
  if (mode === 'hybrid' && !consent) issues.push('Hybrid requires explicit managed-fallback consent.');
  const extension = { ...(strategy ? { routing: strategy } : {}), ...(mode !== 'managed' ? { provider_key_id: keyId, allow_managed_fallback: mode === 'hybrid' && consent } : {}), ...(fallbacks.length ? { fallback_models: fallbacks } : {}) };
  const request = { model: `${pin ? `${pin}/` : ''}${modelId}`, messages: [{ role: 'user', content: 'Hello' }], stream: true, ...(Object.keys(extension).length ? { uzllm: extension } : {}) };
  return <section className="panel stack" aria-label="Request preview"><div className="section-title"><div><span className="eyebrow">REQUEST-ONLY · NEVER AUTO-SAVED</span><h2>Execution preview</h2></div><span className="tag">{mode === 'managed' ? 'Managed' : mode === 'byok' ? 'BYOK' : 'Hybrid'}</span></div>
    <p className="muted">Configuration example only. No inference is sent. Catalog snapshot: {new Date(catalog.dataAsOf).toLocaleString()}. Live eligibility and exact endpoint ranking are not exposed by this API.</p>
    <div className="control-pair"><label>Execution mode<select value={mode} onChange={event => { setMode(event.target.value as typeof mode); setConsent(false); }}><option value="managed">Managed (default)</option><option value="byok">BYOK only (explicit)</option><option value="hybrid">Hybrid (explicit opt-in)</option></select></label>
      <label>Request routing override<select value={strategy} onChange={event => setStrategy(event.target.value)}><option value="">Inherit project ({settings.routing ?? 'priority'})</option>{strategies.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label></div>
    <p className="muted">Effective strategy: <strong>{strategy || settings.routing || 'priority'}</strong> · {strategy ? 'explicit request override' : 'project default / deterministic inheritance'}. BYOK stays first regardless of managed strategy. No configurable multi-key ordering.</p>
    <div className="control-pair"><label>Primary model<select aria-label="Primary model" value={modelId} onChange={event => { setModelId(event.target.value); setPin(''); }}><option value="">Choose a canonical model</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.displayName} ({value.id})</option>)}</select></label>
      <label>Provider pin<select value={pin} onChange={event => setPin(event.target.value)}><option value="">No pin — eligible providers</option>{model?.providers.map(value => <option key={value.mappingId} value={value.provider}>{value.providerName}</option>)}</select></label></div>
    {mode !== 'managed' && <label>Request provider key<select value={keyId} disabled={keysUnavailable} onChange={event => setKeyId(event.target.value)}><option value="">Choose an explicit credential</option>{credentials.map(key => <option key={key.id} value={key.id}>{key.name} · {key.providerCode} · {key.maskedKey} · {key.status}</option>)}</select></label>}
    {mode === 'hybrid' && <label className="consent-label"><input type="checkbox" checked={consent} onChange={event => setConsent(event.target.checked)} />I explicitly allow managed fallback and its full wallet reservation.</label>}
    <details><summary>Optional request-only model fallback</summary><p className="muted">Up to two explicit canonical alternatives; same-model candidates are considered before these alternatives. The provider pin constrains the primary model only; explicit alternatives may use other permitted providers. No project default enables this.</p><div className="control-pair">
      <label>First fallback model<select value={fallback1} onChange={event => setFallback1(event.target.value)}><option value="">None</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.id}</option>)}</select></label>
      <label>Second fallback model<select value={fallback2} onChange={event => setFallback2(event.target.value)}><option value="">None</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.id}</option>)}</select></label></div></details>
    <div className="route-preview"><h3>Candidate sequence — conditional, not a health guarantee</h3>
      {issues.length ? <div role="alert" className="error"><strong>Preview blocked</strong><ul>{issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul></div> : <>
        <ol className="route-steps">{mode !== 'managed' && <li><strong>BYOK primary</strong> · {selected!.providerCode} / {modelId} · {selected!.maskedKey}. Explicit key only.</li>}
          {mode !== 'byok' && <li><strong>{mode === 'hybrid' ? 'Managed fallback' : 'Managed primary → eligible same-model failover'}</strong> · {modelId}, {pin || 'eligible provider'}; strategy {strategy || settings.routing || 'priority'}. {mode === 'hybrid' ? 'Only after an eligible verified pre-execution rejection, with explicit consent and wallet hold.' : 'Provider/model pinning can remove same-model failover; Gateway applies the bounded attempt policy.'}</li>}
          {fallbacks.map(code => <li key={code}><strong>Managed model alternative</strong> · {code}, only if an attempt slot and eligible rejection permit it.</li>)}</ol>
        <pre className="route-example" aria-label="Chat request JSON">{JSON.stringify(request, null, 2)}</pre></>}
      <p className="muted">Never replay after partial output, authentication failure, timeout or unknown execution. An unavailable/ungranted/capped BYOK key fails admission, not a silent Managed switch. Managed pause may remove Hybrid fallback. Project/key policies, budget and wallet holds, provider capability/health/quota and operator limits are rechecked by Gateway. This preview has no API-key permission context and cannot authorize or guarantee a request.</p>
    </div>
  </section>;
}
