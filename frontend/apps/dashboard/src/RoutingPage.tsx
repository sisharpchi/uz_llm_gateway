import { formatDateTime } from './LocaleFormatting';
import { t } from './I18n';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { management, type ByokCredential, type CustomerCatalog, type Organization, type Project, type ProjectSettings } from '@uzllm/api-client';
import { LoadError } from './LifecycleControls';
import { ProjectSettingsEditor } from './ProjectsPage';
import { byokKeysQuery } from './ProviderKeysPage';

const strategies = [ ['priority', 'Deterministic priority'], ['price', 'Lowest price'], ['latency', 'Lowest latency'], ['throughput', 'Highest throughput'], ['auto', 'Auto'] ];

export function RoutingPage({ organization, project, accountId }: { organization: Organization; project?: Project; accountId: string }) {
  if (!project || project.status !== 0) return <section className="panel"><h1>{t("Routing")}</h1><p>{t("Select an active project before configuring routing.")}</p></section>;
  return <ProjectRouting key={project.id} organization={organization} project={project} accountId={accountId} />;
}

function ProjectRouting({ organization, project, accountId }: { organization: Organization; project: Project; accountId: string }) {
  const settings = useQuery({ queryKey: ['project-settings', organization.id, project.id], queryFn: ({ signal }) => management.projectSettings(organization.id, project.id, signal), staleTime: 0 });
  const catalog = useQuery({ queryKey: ['route-catalog', accountId, organization.id, project.id], queryFn: ({ signal }) => management.catalog(organization.id, project.id, signal), staleTime: 0 });
  const keys = useQuery({ queryKey: byokKeysQuery(accountId, organization.id), queryFn: ({ signal }) => management.byokKeys(organization.id, signal), staleTime: 0 });
  const waiting = settings.isPending || catalog.isPending || (!settings.isFetchedAfterMount && settings.isFetching) || (!catalog.isFetchedAfterMount && catalog.isFetching);
  return <div className="lifecycle-page provider-page"><div className="page-heading"><span className="eyebrow">{t("ROUTING & EXECUTION · ")}{project.name}</span><h1>{t("Routing")}</h1><p>{t("Configure defaults. Preview explicit requests. Keep hard restrictions in control.")}</p></div>
    <p className="lifecycle-callout">{t("Managed is always the default. Strategy is configurable; model/provider permissions, budgets, wallet and operator limits are mandatory. No provider key or fallback consent is stored in project defaults.")}</p>
    <ProjectSettingsEditor organizationId={organization.id} projectId={project.id} />
    {waiting ? <p role="status">{t("Loading preview configuration…")}</p> : settings.isError ? <LoadError error={settings.error} retry={() => void settings.refetch()} /> : catalog.isError ? <LoadError error={catalog.error} retry={() => void catalog.refetch()} />
      : <RequestPreview key={`${settings.data.routing}:${JSON.stringify(settings.data.allowedModels)}:${JSON.stringify(settings.data.allowedProviders)}`} settings={settings.data} catalog={catalog.data}
        credentials={keys.isError || !keys.isFetchedAfterMount ? [] : keys.data ?? []} keysUnavailable={keys.isError || keys.isPending || !keys.isFetchedAfterMount} projectId={project.id} />}
    {keys.isError && <p className="notice">{t("BYOK metadata unavailable: only Owner/Admin can inspect credentials. Managed preview remains available. Refresh after access changes.")}</p>}
    <div className="row-actions"><Link className="button secondary" to={`/organizations/${organization.id}/provider-keys?project=${project.id}`}>{t("Manage provider keys")}</Link><Link className="button secondary" to={`/organizations/${organization.id}/projects?project=${project.id}`}>{t("Project limits & budgets")}</Link>
      <button className="button secondary" onClick={() => { void settings.refetch(); void catalog.refetch(); void keys.refetch(); }}>{t("Refresh preview data")}</button></div>
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
    if (!entry || !allows(settings.allowedModels, code)) issues.push(t('Model {p0} is not available under the current project restrictions.', { p0: code }));
    else if (!entry.providers.some(provider => allows(settings.allowedProviders, provider.provider) && (code !== model?.id || !pin || provider.provider === pin)))
      issues.push(t('No permitted catalog provider for {p0}. A provider pin cannot bypass restrictions.', { p0: code }));
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
  return <section className="panel stack" aria-label={t("Request preview")}><div className="section-title"><div><span className="eyebrow">{t("REQUEST-ONLY · NEVER AUTO-SAVED")}</span><h2>{t("Execution preview")}</h2></div><span className="tag">{mode === 'managed' ? t("Managed") : mode === 'byok' ? 'BYOK' : 'Hybrid'}</span></div>
    <p className="muted">{t("Configuration example only. No inference is sent. Catalog snapshot: ")}{formatDateTime(new Date(catalog.dataAsOf))}{t(". Live eligibility and exact endpoint ranking are not exposed by this API.")}</p>
    <div className="control-pair"><label>{t("Execution mode")}<select value={mode} onChange={event => { setMode(event.target.value as typeof mode); setConsent(false); }}><option value="managed">{t("Managed (default)")}</option><option value="byok">{t("BYOK only (explicit)")}</option><option value="hybrid">{t("Hybrid (explicit opt-in)")}</option></select></label>
      <label>{t("Request routing override")}<select value={strategy} onChange={event => setStrategy(event.target.value)}><option value="">{t("Inherit project (")}{settings.routing ?? t("priority")})</option>{strategies.map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></label></div>
    <p className="muted">{t("Effective strategy: ")}<strong>{strategy || settings.routing || t("priority")}</strong> · {strategy ? t("explicit request override") : t("project default / deterministic inheritance")}{t(". BYOK stays first regardless of managed strategy. No configurable multi-key ordering.")}</p>
    <div className="control-pair"><label>{t("Primary model")}<select aria-label={t("Primary model")} value={modelId} onChange={event => { setModelId(event.target.value); setPin(''); }}><option value="">{t("Choose a canonical model")}</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.displayName} ({value.id})</option>)}</select></label>
      <label>{t("Provider pin")}<select value={pin} onChange={event => setPin(event.target.value)}><option value="">{t("No pin — eligible providers")}</option>{model?.providers.map(value => <option key={value.mappingId} value={value.provider}>{value.providerName}</option>)}</select></label></div>
    {mode !== 'managed' && <label>{t("Request provider key")}<select value={keyId} disabled={keysUnavailable} onChange={event => setKeyId(event.target.value)}><option value="">{t("Choose an explicit credential")}</option>{credentials.map(key => <option key={key.id} value={key.id}>{key.name} · {key.providerCode} · {key.maskedKey} · {t(key.status)}</option>)}</select></label>}
    {mode === 'hybrid' && <label className="consent-label"><input type="checkbox" checked={consent} onChange={event => setConsent(event.target.checked)} />{t("I explicitly allow managed fallback and its full wallet reservation.")}</label>}
    <details><summary>{t("Optional request-only model fallback")}</summary><p className="muted">{t("Up to two explicit canonical alternatives; same-model candidates are considered before these alternatives. The provider pin constrains the primary model only; explicit alternatives may use other permitted providers. No project default enables this.")}</p><div className="control-pair">
      <label>{t("First fallback model")}<select value={fallback1} onChange={event => setFallback1(event.target.value)}><option value="">{t("None")}</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.id}</option>)}</select></label>
      <label>{t("Second fallback model")}<select value={fallback2} onChange={event => setFallback2(event.target.value)}><option value="">{t("None")}</option>{catalog.models.map(value => <option key={value.id} value={value.id}>{value.id}</option>)}</select></label></div></details>
    <div className="route-preview"><h3>{t("Candidate sequence — conditional, not a health guarantee")}</h3>
      {issues.length ? <div role="alert" className="error"><strong>{t("Preview blocked")}</strong><ul>{issues.map((issue, index) => <li key={index}>{t(issue)}</li>)}</ul></div> : <>
        <ol className="route-steps">{mode !== 'managed' && <li><strong>{t("BYOK primary")}</strong> · {selected!.providerCode} / {modelId} · {selected!.maskedKey}{t(". Explicit key only.")}</li>}
          {mode !== 'byok' && <li><strong>{mode === 'hybrid' ? t("Managed fallback") : t("Managed primary → eligible same-model failover")}</strong> · {modelId}, {pin || t("eligible provider")}{t("; strategy ")}{strategy || settings.routing || t("priority")}. {mode === 'hybrid' ? t("Only after an eligible verified pre-execution rejection, with explicit consent and wallet hold.") : t("Provider/model pinning can remove same-model failover; Gateway applies the bounded attempt policy.")}</li>}
          {fallbacks.map(code => <li key={code}><strong>{t("Managed model alternative")}</strong> · {code}{t(", only if an attempt slot and eligible rejection permit it.")}</li>)}</ol>
        <pre className="route-example" aria-label={t("Chat request JSON")}>{JSON.stringify(request, null, 2)}</pre></>}
      <p className="muted">{t("Never replay after partial output, authentication failure, timeout or unknown execution. An unavailable/ungranted/capped BYOK key fails admission, not a silent Managed switch. Managed pause may remove Hybrid fallback. Project/key policies, budget and wallet holds, provider capability/health/quota and operator limits are rechecked by Gateway. This preview has no API-key permission context and cannot authorize or guarantee a request.")}</p>
    </div>
  </section>;
}
