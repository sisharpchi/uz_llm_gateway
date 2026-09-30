import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ApiError, management, usageUsdFromMicro, type CatalogModel, type CustomerCatalog } from '@uzllm/api-client';
import './CatalogPage.css';

const gatewayBaseUrl = import.meta.env.VITE_GATEWAY_BASE_URL?.replace(/\/$/, '')
  ?? (import.meta.env.DEV ? 'http://localhost:5214' : window.location.origin);

export function CatalogPage({ organizationId, projectId }: { organizationId: string; projectId?: string }) {
  const catalog = useQuery({ queryKey: ['catalog', organizationId, projectId],
    queryFn: () => management.catalog(organizationId, projectId!), enabled: !!projectId,
    refetchInterval: 60_000 });
  const [search, setSearch] = useState('');
  const [provider, setProvider] = useState('all');
  const [capability, setCapability] = useState('all');
  const [maxInputUsd, setMaxInputUsd] = useState('');
  const [minContext, setMinContext] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const providers = useMemo(() => [...new Set(catalog.data?.models.flatMap(model => model.providers.map(item => item.provider)) ?? [])].sort(), [catalog.data]);
  const capabilities = useMemo(() => [...new Set(catalog.data?.models.flatMap(model => model.providers.flatMap(item => item.capabilities)) ?? [])].sort(), [catalog.data]);
  const visible = catalog.data?.models.filter(model =>
    `${model.id} ${model.displayName}`.toLowerCase().includes(search.trim().toLowerCase())
    && (!minContext || model.contextLength >= Number(minContext))
    && model.providers.some(item => (provider === 'all' || item.provider === provider)
      && (capability === 'all' || item.capabilities.includes(capability))
      && (!maxInputUsd || Number(item.inputPriceMicroUsdPerMillion) / 1_000_000 <= Number(maxInputUsd)))) ?? [];
  const selected = visible.find(model => model.id === selectedId) ?? visible[0];

  return <><div className="page-heading"><span className="eyebrow">PUBLISHED MANAGED CATALOG</span><h1>Models</h1>
    <p>Compare current provider prices and capabilities, then make your first real chat request.</p></div>
    {!projectId ? <section className="panel"><h2>Create a project first</h2><p>Model discovery is scoped to an active project.</p></section>
      : catalog.isPending ? <p role="status">Loading published models…</p>
      : catalog.isError ? <p role="alert" className="error">{catalog.error instanceof ApiError && catalog.error.status === 403
        ? 'You do not have access to this project.' : catalog.error instanceof ApiError && catalog.error.status === 404
          ? 'This project is not available.' : 'Model catalog or managed pricing is unavailable. Try again later.'}</p>
      : <><div className="catalog-filters">
          <label>Search models<input value={search} onChange={event => setSearch(event.target.value)} placeholder="Model name or ID" /></label>
          <label>Provider<select value={provider} onChange={event => setProvider(event.target.value)}><option value="all">All providers</option>{providers.map(item => <option key={item} value={item}>{item}</option>)}</select></label>
          <label>Capability<select value={capability} onChange={event => setCapability(event.target.value)}><option value="all">All capabilities</option>{capabilities.map(item => <option key={item} value={item}>{item}</option>)}</select></label>
          <label>Max input price · USD / 1M<input type="number" min="0" step="0.01" value={maxInputUsd} onChange={event => setMaxInputUsd(event.target.value)} placeholder="Any price" /></label>
          <label>Min context tokens<input type="number" min="0" step="1" value={minContext} onChange={event => setMinContext(event.target.value)} placeholder="Any context" /></label>
        </div>
        <p className="catalog-meta">Prices as of {new Date(catalog.data.dataAsOf).toLocaleString()} · USD per 1M tokens · published status is not a live health guarantee.</p>
        <div className="catalog-layout"><section className="catalog-results" aria-label="Model results">
          {visible.length ? visible.map(model => <button key={model.id} className={`catalog-card ${selected?.id === model.id ? 'selected' : ''}`}
            onClick={() => setSelectedId(model.id)} aria-pressed={selected?.id === model.id}>
            <span className="catalog-card-head"><strong>{model.displayName}</strong><span className="tag">{model.status}</span></span>
            <code>{model.id}</code><span className="catalog-card-foot">{model.providers.length} provider{model.providers.length === 1 ? '' : 's'} · {model.contextLength.toLocaleString()} context</span>
          </button>) : <div className="panel">No published models match these filters.</div>}
        </section><section className="panel catalog-detail" aria-label="Model detail">
          {selected ? <ModelDetail model={selected} catalog={catalog.data} providerFilter={provider} capabilityFilter={capability} maxInputUsd={maxInputUsd} />
            : <><h2>No model selected</h2><p>Adjust filters or ask an operator to publish an active model, mapping, price and managed fee policy.</p></>}
        </section></div></>}
  </>;
}

function ModelDetail({ model, catalog, providerFilter, capabilityFilter, maxInputUsd }: { model: CatalogModel; catalog: CustomerCatalog; providerFilter: string; capabilityFilter: string; maxInputUsd: string }) {
  const [copied, setCopied] = useState(false);
  const request = quickstart(model.id);
  return <><span className="eyebrow">MODEL DETAIL</span><h2>{model.displayName}</h2><code className="catalog-model-id">{model.id}</code>
    <div className="catalog-statline"><span>Context <strong>{model.contextLength.toLocaleString()}</strong></span><span>Max output <strong>{model.maxOutputTokens.toLocaleString()}</strong></span></div>
    <div className="catalog-tags">{model.capabilities.map(item => <span className="tag" key={item}>{item}</span>)}</div>
    <h3>Published provider prices</h3><div className="catalog-price-list">{model.providers.filter(item =>
      (providerFilter === 'all' || item.provider === providerFilter)
      && (capabilityFilter === 'all' || item.capabilities.includes(capabilityFilter))
      && (!maxInputUsd || Number(item.inputPriceMicroUsdPerMillion) / 1_000_000 <= Number(maxInputUsd))).map(item => <div key={item.priceVersionId} className="catalog-price-row">
      <strong>{item.providerName}</strong><span>Input {rateUsd(item.inputPriceMicroUsdPerMillion)} / 1M</span><span>Output {rateUsd(item.outputPriceMicroUsdPerMillion)} / 1M</span>
      {item.cachedInputPriceMicroUsdPerMillion && <span>Cached input {rateUsd(item.cachedInputPriceMicroUsdPerMillion)} / 1M</span>}
      <small>Effective {new Date(item.priceEffectiveFrom).toLocaleString()} · {item.capabilities.join(', ')}</small>
    </div>)}</div>
    <p className="catalog-meta">Rates include {catalog.markupBasisPoints / 100}% managed markup. A fixed {usageUsdFromMicro(catalog.fixedFeeMicroUsdPerRequest)} is added per request. Actual charge depends on the chosen provider and measured tokens; wallet admission may reserve more. BYOK uses a separate fee policy.</p>
    <h3>Quickstart · chat completions</h3><p className="muted">Create an API key for this project and fund your wallet first. Replace only the placeholder key; never paste a live key into this page.</p>
    <pre className="code-example"><code>{request}</code></pre>
    <button className="button secondary" onClick={async () => { await navigator.clipboard.writeText(request); setCopied(true); }}>Copy request</button>
    {copied && <span role="status" className="catalog-copied">Copied without an API secret.</span>}
  </>;
}

function rateUsd(microUsd: string): string {
  const [units, fraction = ''] = microUsd.split('.');
  const scale = 6 + fraction.length;
  const divisor = 10n ** BigInt(scale);
  const magnitude = BigInt(units + fraction);
  const whole = (magnitude / divisor).toLocaleString('en-US');
  const tail = (magnitude % divisor).toString().padStart(scale, '0').replace(/0+$/, '');
  return `$${whole}.${tail.padEnd(2, '0')}`;
}

export function quickstart(modelId: string): string {
  const body = JSON.stringify({ model: modelId, messages: [{ role: 'user', content: 'Hello from UZLLM' }],
    max_completion_tokens: 100, stream: false });
  return `curl -sS '${gatewayBaseUrl}/v1/chat/completions' -H 'Authorization: Bearer YOUR_API_KEY' -H 'Content-Type: application/json' -d '${body}'`;
}
