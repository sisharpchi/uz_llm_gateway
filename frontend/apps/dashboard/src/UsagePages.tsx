import { useState } from 'react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { management, usageUsdFromMicro, type UsageFilters, type UsageRequestDetail } from '@uzllm/api-client';

const startOfDay = (value: Date) => new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate()));
const today = () => startOfDay(new Date()).toISOString().slice(0, 10);
const daysAgo = (days: number) => startOfDay(new Date(Date.now() - days * 86_400_000)).toISOString().slice(0, 10);
const timestamp = (value: string) => new Date(value).toLocaleString();
const money = (value: string | null) => value === null ? 'Pending' : usageUsdFromMicro(value);

function useWindow(projectId?: string) {
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());
  const filters: UsageFilters = {
    from: from ? new Date(`${from}T00:00:00Z`).toISOString() : undefined,
    to: to ? new Date(new Date(`${to}T00:00:00Z`).getTime() + 86_400_000).toISOString() : undefined,
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

export function ActivityPage({ organizationId, projectId }: { organizationId: string; projectId?: string }) {
  const window = useWindow(projectId);
  const [search, setSearch] = useSearchParams();
  const [status, setStatus] = useState('');
  const [requestId, setRequestId] = useState('');
  const [lookup, setLookup] = useState('');
  const filters = { ...window.filters, status: status || undefined, requestId: lookup || undefined };
  const invalidWindow = window.from > window.to || !window.from || !window.to;
  const activity = useInfiniteQuery({ queryKey: ['activity', organizationId, filters],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) => management.activity(organizationId, filters, 25, pageParam),
    getNextPageParam: page => page.nextCursor ?? undefined, enabled: !invalidWindow });
  const selected = search.get('request');
  const detail = useQuery({ queryKey: ['usage-detail', organizationId, selected],
    queryFn: () => management.usageDetail(organizationId, selected!), enabled: !!selected });
  const rows = activity.data?.pages.flatMap(page => page.items) ?? [];
  function choose(id: string | null) {
    const next = new URLSearchParams(search);
    if (id) next.set('request', id); else next.delete('request');
    setSearch(next);
  }
  return <><div className="page-heading"><span className="eyebrow">INFERENCE AUDIT</span><h1>Activity</h1>
    <p>Every logical gateway request, its attempts, usage evidence, and final charge.</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window} />
      <label>Status<select value={status} onChange={event => setStatus(event.target.value)}><option value="">All states</option>
        {['Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution'].map(value => <option key={value}>{value}</option>)}</select></label>
      <form onSubmit={event => { event.preventDefault(); setLookup(requestId.trim()); }} className="usage-lookup"><label>Exact request ID<input value={requestId} onChange={event => setRequestId(event.target.value)} placeholder="UUID" /></label>
        <button className="button secondary">Find</button></form></div>
    {invalidWindow && <p role="alert" className="error">Choose a valid date range.</p>}
    {activity.isError && <p role="alert" className="error">Activity could not be loaded. Check the filters and try again.</p>}
    <div className="usage-layout"><section className="panel usage-table-panel" aria-label="Request activity">
      <div className="usage-section-head"><h2>Requests</h2><span className="muted">{activity.isFetching ? 'Updating…' : `${rows.length} loaded`}</span></div>
      {activity.isPending && !invalidWindow ? <p role="status" className="muted">Loading activity…</p>
        : rows.length === 0 ? <p className="muted">No requests match this window and filters.</p>
          : <div className="usage-table-scroll"><table className="usage-table"><thead><tr><th>When</th><th>Model / key</th><th>State</th><th>Tokens</th><th>Latency</th><th>Charge</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.requestId} className={selected === row.requestId ? 'selected' : ''}>
              <td><button className="usage-row-link" onClick={() => choose(row.requestId)} aria-label={`Inspect request ${row.requestId}`}>{timestamp(row.startedAt)}</button></td>
              <td><strong>{row.modelCode}</strong><small>{row.apiKeyName} · {row.providerCode ?? 'Unrouted'}</small></td>
              <td><span className={`usage-state ${row.httpStatus && row.httpStatus >= 400 ? 'bad' : ''}`}>{row.httpStatus ?? row.executionState}</span></td>
              <td>{row.inputTokens === null ? 'Unknown' : (row.inputTokens + (row.outputTokens ?? 0)).toLocaleString()}</td>
              <td>{row.durationMs === null ? '—' : `${row.durationMs} ms`}</td><td>{money(row.chargedMicroUsd)}</td></tr>)}</tbody></table></div>}
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
    <dl><dt>Model</dt><dd>{request.modelCode}</dd><dt>Project</dt><dd>{request.projectName}</dd>
      <dt>Key</dt><dd>{request.apiKeyName}</dd><dt>Execution</dt><dd>{request.executionState}</dd>
      <dt>Delivery</dt><dd>{request.deliveryState}</dd><dt>Financial</dt><dd>{request.financialState}</dd>
      <dt>Input / output</dt><dd>{request.inputTokens ?? 'Unknown'} / {request.outputTokens ?? 'Unknown'}</dd>
      <dt>Customer charge</dt><dd>{money(detail.chargedMicroUsd)}</dd>
      <dt>Provider cost</dt><dd>{money(detail.providerCostMicroUsd)}</dd>
      <dt>Trace</dt><dd><code>{detail.traceId ?? '—'}</code></dd></dl>
    <h3>Provider attempts</h3>{detail.attempts.length === 0 ? <p className="muted">No provider call was made.</p>
      : <ol className="usage-attempts">{detail.attempts.map(attempt => <li key={attempt.attemptId}>
        <strong>#{attempt.number} {attempt.providerCode}</strong><span>{attempt.executionState}</span>
        {attempt.errorCategory && <small>{attempt.errorCategory}</small>}</li>)}</ol>}
    <h3>Usage evidence</h3><p className="muted">{detail.evidence.length} record(s) · {detail.unresolvedUsage ? 'Reconciliation pending' : 'No unresolved usage flag'}</p>
    <p className="muted">Prompt and response bodies are not retained in this view.</p></div>;
}

export function AnalyticsPage({ organizationId, projectId }: { organizationId: string; projectId?: string }) {
  const window = useWindow(projectId);
  const filters = window.filters;
  const invalidWindow = window.from > window.to || !window.from || !window.to;
  const summary = useQuery({ queryKey: ['usage-summary', organizationId, filters], queryFn: () => management.usageSummary(organizationId, filters), enabled: !invalidWindow });
  const daily = useQuery({ queryKey: ['usage-daily', organizationId, filters], queryFn: () => management.usageTimeSeries(organizationId, filters), enabled: !invalidWindow });
  const maxRequests = Math.max(1, ...(daily.data?.items.map(item => item.requestCount) ?? []));
  return <><div className="page-heading"><span className="eyebrow">USAGE INTELLIGENCE</span><h1>Analytics</h1>
    <p>Verified token evidence and settled charges across your selected window.</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window} /></div>
    {invalidWindow && <p role="alert" className="error">Choose a valid date range.</p>}
    {(summary.isError || daily.isError) && <p role="alert" className="error">Analytics could not be loaded. Try a shorter range.</p>}
    {summary.isPending ? <p role="status" className="muted">Loading analytics…</p> : summary.data && <>
      <div className="usage-metrics"><div className="panel metric"><span>Requests</span><strong>{summary.data.requestCount.toLocaleString()}</strong><small>{summary.data.completedCount} completed</small></div>
        <div className="panel metric"><span>Verified tokens</span><strong>{(summary.data.inputTokens + summary.data.outputTokens).toLocaleString()}</strong><small>{summary.data.inputTokens} input · {summary.data.outputTokens} output</small></div>
        <div className="panel metric"><span>Settled charges</span><strong>{usageUsdFromMicro(summary.data.chargedMicroUsd)}</strong><small>Pending charges excluded</small></div>
        <div className="panel metric"><span>Error rate</span><strong>{summary.data.errorRatePercent}%</strong><small>{summary.data.errorCount} errors</small></div></div>
      <div className="usage-analytics-grid"><section className="panel"><h2>Requests by day</h2>
        {daily.data?.items.length ? <div className="usage-bars">{daily.data.items.map(item => <div key={item.day} className="usage-bar-row">
          <span>{item.day}</span><div className="usage-bar-track"><span style={{ width: `${Math.max(2, item.requestCount / maxRequests * 100)}%` }} /></div><strong>{item.requestCount}</strong></div>)}</div>
          : <p className="muted">No request data for this period.</p>}</section>
        <section className="panel"><h2>Top models</h2><Breakdown items={summary.data.topModels} />
          <h2 className="usage-subheading">Top providers</h2><Breakdown items={summary.data.topProviders} /></section></div>
      <p className="muted">Data as of {timestamp(summary.data.dataAsOf)}. UTC dates; financial totals come from settled charges.</p></>}
  </>;
}

function Breakdown({ items }: { items: { id: string; name: string; requestCount: number; chargedMicroUsd: string }[] }) {
  return items.length ? <ul className="usage-breakdown">{items.map(item => <li key={item.id}><span>{item.name}<small>{item.requestCount} requests</small></span><strong>{usageUsdFromMicro(item.chargedMicroUsd)}</strong></li>)}</ul>
    : <p className="muted">No data yet.</p>;
}
