import { useState } from 'react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { management, usageUsdFromMicro, type Project, type UsageFilters, type UsageRequestDetail } from '@uzllm/api-client';
import { UsageExportButton } from './UsageExportButton';

const startOfDay = (value: Date) => new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate()));
const today = () => startOfDay(new Date()).toISOString().slice(0, 10);
const daysAgo = (days: number) => startOfDay(new Date(Date.now() - days * 86_400_000)).toISOString().slice(0, 10);
const timestamp = (value: string) => new Date(value).toLocaleString();
const money = (value: string | null | undefined) => value == null ? 'Unknown / unsettled' : usageUsdFromMicro(value);
const analyticsRefresh = { refetchInterval: 10_000, refetchIntervalInBackground: false };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function useWindow(projectId?: string) {
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());
  const filters: UsageFilters = {
    from: from && !Number.isNaN(Date.parse(`${from}T00:00:00Z`)) ? new Date(`${from}T00:00:00Z`).toISOString() : undefined,
    to: to && !Number.isNaN(Date.parse(`${to}T00:00:00Z`)) ? new Date(new Date(`${to}T00:00:00Z`).getTime() + 86_400_000).toISOString() : undefined,
    projectId
  };
  return { from, to, setFrom, setTo, filters };
}

function WindowControls({ from, to, setFrom, setTo }: {
  from: string; to: string; setFrom: (value: string) => void; setTo: (value: string) => void;
}) {
  return <div className="usage-window"><label>From (UTC)<input type="date" value={from} max={to} onChange={event => setFrom(event.target.value)} /></label>
    <label>To (UTC)<input type="date" value={to} min={from} onChange={event => setTo(event.target.value)} /></label></div>;
}

export function ActivityPage({ organizationId, projectId, projects }: { organizationId: string; projectId?: string; projects: Project[] }) {
  const window = useWindow(projectId);
  const [search, setSearch] = useSearchParams();
  const [status, setStatus] = useState('');
  const [apiKeyId, setApiKeyId] = useState('');
  const [modelId, setModelId] = useState('');
  const [providerId, setProviderId] = useState('');
  const [stream, setStream] = useState('');
  const [requestId, setRequestId] = useState('');
  const [lookup, setLookup] = useState('');
  const windowDays = (new Date(`${window.to}T00:00:00Z`).getTime() - new Date(`${window.from}T00:00:00Z`).getTime()) / 86_400_000 + 1;
  const invalidWindow = window.from > window.to || !window.from || !window.to || !Number.isFinite(windowDays) || windowDays > 90;
  const keys = useQuery({ queryKey: ['activity-keys', projectId], queryFn: () => management.keys(projectId!), enabled: !!projectId });
  const models = useQuery({ queryKey: ['activity-models', organizationId, window.filters], queryFn: () => management.usageBreakdown(organizationId, 'model', window.filters), enabled: !invalidWindow });
  const providers = useQuery({ queryKey: ['activity-providers', organizationId, window.filters], queryFn: () => management.usageBreakdown(organizationId, 'provider', window.filters), enabled: !invalidWindow });
  const effectiveKey = projectId && keys.data?.some(key => key.id === apiKeyId) ? apiKeyId : undefined;
  const filters: UsageFilters = { ...window.filters, apiKeyId: effectiveKey, modelId: modelId || undefined,
    providerId: providerId || undefined, status: status || undefined, isStream: stream ? stream === 'true' : undefined,
    requestId: lookup || undefined };
  const activity = useInfiniteQuery({ queryKey: ['activity', organizationId, filters],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) => management.activity(organizationId, filters, 25, pageParam),
    getNextPageParam: page => page.nextCursor ?? undefined, enabled: !invalidWindow });
  const selected = search.get('request');
  const detail = useQuery({ queryKey: ['usage-detail', organizationId, selected],
    queryFn: () => management.usageDetail(organizationId, selected!), enabled: !!selected });
  const keySummary = useQuery({ queryKey: ['activity-key-summary', organizationId, filters],
    queryFn: () => management.usageSummary(organizationId, filters), enabled: !invalidWindow && !!effectiveKey });
  const rows = activity.data?.pages.flatMap(page => page.items) ?? [];
  function choose(id: string | null) {
    const next = new URLSearchParams(search);
    if (id) next.set('request', id); else next.delete('request');
    setSearch(next);
  }
  function changeProject(nextId: string) {
    const next = new URLSearchParams(search);
    if (nextId) next.set('project', nextId); else next.delete('project');
    next.delete('request'); setSearch(next);
    setApiKeyId(''); setModelId(''); setProviderId(''); setLookup(''); setRequestId('');
  }
  function findRequest() {
    const id = requestId.trim();
    if (id && !uuid.test(id)) return;
    setLookup(id); choose(id || null);
  }
  return <><div className="page-heading"><span className="eyebrow">INFERENCE AUDIT</span><h1>Activity</h1>
    <p>Every logical gateway request, its attempts, usage evidence, and final charge.</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window}
      setFrom={value => { window.setFrom(value); setModelId(''); setProviderId(''); choose(null); }}
      setTo={value => { window.setTo(value); setModelId(''); setProviderId(''); choose(null); }} />
      <label>Filter project<select value={projectId ?? ''} onChange={event => changeProject(event.target.value)}><option value="">All projects</option>
        {projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <label>API key<select value={effectiveKey ?? ''} disabled={!projectId || keys.isPending || keys.isError} onChange={event => setApiKeyId(event.target.value)}>
        <option value="">All keys{!projectId ? ' · select a project' : ''}</option>{keys.data?.map(key => <option key={key.id} value={key.id}>{key.name} · {key.prefix}</option>)}</select></label>
      <label>Model<select value={modelId} onChange={event => setModelId(event.target.value)}><option value="">All models</option>
        {models.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>Provider<select value={providerId} onChange={event => setProviderId(event.target.value)}><option value="">All providers</option>
        {providers.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>Status<select value={status} onChange={event => setStatus(event.target.value)}><option value="">All states</option>
        {['Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution'].map(value => <option key={value}>{value}</option>)}</select></label>
      <label>Delivery<select value={stream} onChange={event => setStream(event.target.value)}><option value="">All types</option><option value="true">Streaming</option><option value="false">Non-streaming</option></select></label>
      <form onSubmit={event => { event.preventDefault(); findRequest(); }} className="usage-lookup"><label>Exact request ID<input value={requestId} onChange={event => setRequestId(event.target.value)} placeholder="UUID" aria-invalid={!!requestId.trim() && !uuid.test(requestId.trim())} /></label>
        <button className="button secondary">Find</button></form></div>
    {invalidWindow && <p role="alert" className="error">Choose a valid UTC date range of at most 90 days.</p>}
    {!!requestId.trim() && !uuid.test(requestId.trim()) && <p role="alert" className="error">Request ID must be a UUID.</p>}
    {keys.isError && projectId && <p role="alert" className="error">API keys could not be loaded for this project.</p>}
    {activity.isError && <p role="alert" className="error">Activity could not be loaded. Check the filters and try again. <button className="text-button" onClick={() => activity.refetch()}>Retry</button></p>}
    {effectiveKey && keySummary.data && <div className="panel usage-key-summary" role="status"><strong>{keys.data?.find(key => key.id === effectiveKey)?.name} usage</strong>
      <span>{keySummary.data.requestCount.toLocaleString()} requests</span><span>{(keySummary.data.inputTokens + keySummary.data.outputTokens).toLocaleString()} verified tokens</span>
      <span>{usageUsdFromMicro(keySummary.data.chargedMicroUsd)} settled charge</span></div>}
    <UsageExportButton organizationId={organizationId} kind="activity" filters={filters} disabled={invalidWindow} />
    <div className="usage-layout"><section className="panel usage-table-panel" aria-label="Request activity">
      <div className="usage-section-head"><h2>Requests</h2><span className="muted">{activity.isFetching ? 'Updating…' : `${rows.length} loaded`}</span></div>
      {activity.isPending && !invalidWindow ? <p role="status" className="muted">Loading activity…</p>
        : activity.isError || invalidWindow ? null : rows.length === 0 ? <p className="muted">No requests match this window and filters.</p>
          : <div className="usage-table-scroll"><table className="usage-table"><thead><tr><th>When</th><th>Project / model</th><th>Key / provider</th><th>Execution / delivery</th><th>Input / output</th><th>Latency</th><th>Charge</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.requestId} className={selected === row.requestId ? 'selected' : ''}>
              <td><button className="usage-row-link" onClick={() => choose(row.requestId)} aria-label={`Inspect request ${row.requestId}`}>{timestamp(row.startedAt)}</button></td>
              <td><strong>{row.projectName}</strong><small>{row.modelCode}</small></td>
              <td><strong>{row.apiKeyName}</strong><small>{row.providerCode ?? 'Unrouted'}</small></td>
              <td><span className={`usage-state ${row.httpStatus && row.httpStatus >= 400 ? 'bad' : ''}`}>{row.executionState}</span><small>{row.deliveryState}{row.isStream ? ' · Stream' : ''}</small></td>
              <td>{row.inputTokens ?? 'Unknown'} / {row.outputTokens ?? 'Unknown'}</td>
              <td>{row.durationMs === null ? '—' : `${row.durationMs} ms`}</td><td>{money(row.chargedMicroUsd)}</td></tr>)}</tbody></table></div>}
      {activity.isFetchNextPageError && <p role="alert" className="error">More activity could not be loaded. Retry below.</p>}
      {activity.hasNextPage && <button className="button secondary usage-more" disabled={activity.isFetchingNextPage} onClick={() => activity.fetchNextPage()}>
        {activity.isFetchingNextPage ? 'Loading…' : 'Load more'}</button>}
    </section><aside className="panel usage-inspector" aria-label="Request details">
      <div className="usage-section-head"><h2>Request details</h2>{selected && <button className="text-button" onClick={() => choose(null)}>Close</button>}</div>
      {!selected ? <p className="muted">Select a request to inspect its safe metadata and provider attempts.</p>
        : detail.isPending ? <p role="status" className="muted">Loading request…</p>
          : detail.isError ? <p role="alert" className="error">This request is unavailable or outside your organization.</p>
            : <RequestInspector detail={detail.data} />}
    </aside></div></>;
}

function RequestInspector({ detail }: { detail: UsageRequestDetail }) {
  const request = detail.request;
  return <div className="usage-detail"><code className="usage-id">{request.requestId}</code>
    {(request.executionState === 'OutcomeUnknown' || detail.unresolvedUsage) && <p role="status" className="usage-warning">Outcome or provider usage is unknown. Reconciliation may change the final charge; this does not mean the provider billed zero.</p>}
    {(request.deliveryState !== 'Completed' && request.isStream) && <p role="status" className="usage-warning">Streaming delivery was partial or interrupted. Provider usage may differ from tokens delivered to the client.</p>}
    <dl><dt>Requested model</dt><dd>{request.modelCode}</dd><dt>Selected model</dt><dd>{request.selectedModelCode ?? (request.attemptCount ? 'Unknown' : 'Not selected')}</dd><dt>Project</dt><dd>{request.projectName}</dd>
      <dt>Key</dt><dd>{request.apiKeyName}</dd><dt>Execution</dt><dd>{request.executionState}</dd>
      <dt>Delivery</dt><dd>{request.deliveryState}{request.isStream ? ' · Streaming' : ''}</dd><dt>HTTP status</dt><dd>{request.httpStatus ?? 'Unknown'}</dd><dt>Financial</dt><dd>{request.financialState}</dd>
      <dt>Started / finished</dt><dd>{timestamp(request.startedAt)} / {request.completedAt ? timestamp(request.completedAt) : 'Unknown'}</dd>
      <dt>Duration</dt><dd>{request.durationMs == null ? 'Unknown' : `${request.durationMs} ms`}</dd>
      <dt>Input / output</dt><dd>{request.inputTokens ?? 'Unknown'} / {request.outputTokens ?? 'Unknown'}</dd>
      <dt>Customer charge</dt><dd>{money(detail.chargedMicroUsd)}</dd>
      <dt>Provider cost</dt><dd>{money(detail.providerCostMicroUsd)}</dd>
      <dt>External provider spend</dt><dd>{money(detail.externalProviderSpendMicroUsd)}</dd>
      <dt>Late provider spend</dt><dd>{money(detail.lateExternalSpendMicroUsd)}</dd>
      <dt>Platform exposure</dt><dd>{money(detail.platformExposureMicroUsd)}</dd>
      <dt>Routing strategy</dt><dd>{detail.routeStrategy ?? 'Unknown'}</dd>
      <dt>Trace</dt><dd><code>{detail.traceId ?? 'Unknown'}</code></dd></dl>
    <h3>Provider attempts</h3>{detail.attempts.length === 0 ? <p className="muted">No provider call was made.</p>
      : <ol className="usage-attempts">{detail.attempts.map(attempt => <li key={attempt.attemptId}>
        <strong>#{attempt.number} {attempt.providerCode}{attempt.modelCode ? ` · ${attempt.modelCode}` : ''}</strong><span>{attempt.executionState} · {timestamp(attempt.startedAt)}{attempt.completedAt ? ` → ${timestamp(attempt.completedAt)}` : ''}</span>
        <small>Upstream ID: {attempt.providerRequestId ?? 'Unavailable'}{attempt.errorCategory ? ` · ${attempt.errorCategory}` : ''}</small></li>)}</ol>}
    <h3>Usage evidence</h3>{detail.evidence.length === 0 ? <p className="muted">No provider usage evidence recorded yet.</p>
      : <ol className="usage-attempts">{detail.evidence.map(item => <li key={item.evidenceId}><strong>{item.state} · {item.source}</strong>
        <span>Input {item.inputTokens ?? 'Unknown'} · Output {item.outputTokens ?? 'Unknown'} · Cached {item.cachedInputTokens ?? 'Unknown'} · Reasoning {item.reasoningTokens ?? 'Unknown'}</span>
        <small>Captured {timestamp(item.capturedAt)}{item.reconcileAfter ? ` · Recheck after ${timestamp(item.reconcileAfter)}` : ''}</small></li>)}</ol>}
    <p className="muted">{detail.unresolvedUsage ? 'Reconciliation pending' : 'No unresolved usage flag'}</p>
    <p className="muted">Prompt and response bodies are not retained in this view.</p></div>;
}

export function AnalyticsPage({ organizationId, projectId }: { organizationId: string; projectId?: string }) {
  const window = useWindow(projectId);
  const filters = window.filters;
  const windowDays = (Date.parse(`${window.to}T00:00:00Z`) - Date.parse(`${window.from}T00:00:00Z`)) / 86_400_000 + 1;
  const invalidWindow = window.from > window.to || !window.from || !window.to || !Number.isFinite(windowDays) || windowDays > 90;
  const summary = useQuery({ queryKey: ['usage-summary', organizationId, filters], queryFn: () => management.usageSummary(organizationId, filters), enabled: !invalidWindow, ...analyticsRefresh });
  const daily = useQuery({ queryKey: ['usage-daily', organizationId, filters], queryFn: () => management.usageTimeSeries(organizationId, filters), enabled: !invalidWindow, ...analyticsRefresh });
  const maxRequests = Math.max(1, ...(daily.data?.items.map(item => item.requestCount) ?? []));
  return <><div className="page-heading"><span className="eyebrow">USAGE INTELLIGENCE</span><h1>Analytics</h1>
    <p>Verified token evidence and settled charges across your selected window.</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window} /></div>
    <UsageExportButton organizationId={organizationId} kind="daily" filters={filters} disabled={invalidWindow} />
    {invalidWindow && <p role="alert" className="error">Choose a valid UTC date range of at most 90 days.</p>}
    {(summary.isError || daily.isError) && <p role="alert" className="error">Analytics could not be loaded. Try a shorter range.</p>}
    {summary.isPending ? <p role="status" className="muted">Loading analytics…</p> : summary.data && <>
      <div className="usage-metrics"><div className="panel metric"><span>Requests</span><strong>{summary.data.requestCount.toLocaleString()}</strong><small>{summary.data.completedCount} completed</small></div>
        <div className="panel metric"><span>Verified tokens</span><strong>{(summary.data.inputTokens + summary.data.outputTokens).toLocaleString()}</strong><small>{summary.data.inputTokens} input · {summary.data.outputTokens} output</small></div>
        <div className="panel metric"><span>Settled charges</span><strong>{usageUsdFromMicro(summary.data.chargedMicroUsd)}</strong><small>Pending charges excluded</small>
          {summary.data.refundedMicroUsd != null && summary.data.netChargedMicroUsd != null && <small>Refunded {usageUsdFromMicro(summary.data.refundedMicroUsd)} · net {usageUsdFromMicro(summary.data.netChargedMicroUsd)}</small>}</div>
        <div className="panel metric"><span>Error rate</span><strong>{summary.data.errorRatePercent}%</strong><small>{summary.data.errorCount} errors</small></div></div>
      <div className="usage-analytics-grid"><section className="panel"><h2>Requests by day</h2>
        {daily.data?.items.length ? <div className="usage-bars">{daily.data.items.map(item => <div key={item.day} className="usage-bar-row">
          <span>{item.day}</span><div className="usage-bar-track"><span style={{ width: `${Math.max(2, item.requestCount / maxRequests * 100)}%` }} /></div><strong>{item.requestCount}</strong></div>)}</div>
          : <p className="muted">No request data for this period.</p>}</section>
        <section className="panel"><h2>Top models</h2><Breakdown items={summary.data.topModels} />
          <h2 className="usage-subheading">Top providers</h2><Breakdown items={summary.data.topProviders} /></section></div>
      {(summary.data.aggregation?.pendingBucketCount ?? 0) > 0 && <p role="status" className="muted">Updating usage · {summary.data.aggregation?.pendingBucketCount} hour(s) pending.
        {summary.data.aggregation?.oldestPendingAt && ` Waiting since ${timestamp(summary.data.aggregation.oldestPendingAt)}.`}</p>}
      <p className="muted">{Date.parse(summary.data.dataAsOf) === 0 ? 'Initial aggregation pending.' : `Oldest hourly snapshot: ${timestamp(summary.data.dataAsOf)}.`} UTC dates; settled charges exclude pending usage. Analytics refreshes every 10 seconds while visible.</p></>}
  </>;
}

function Breakdown({ items }: { items: { id: string; name: string; requestCount: number; chargedMicroUsd: string }[] }) {
  return items.length ? <ul className="usage-breakdown">{items.map(item => <li key={item.id}><span>{item.name}<small>{item.requestCount} requests</small></span><strong>{usageUsdFromMicro(item.chargedMicroUsd)}</strong></li>)}</ul>
    : <p className="muted">No data yet.</p>;
}
