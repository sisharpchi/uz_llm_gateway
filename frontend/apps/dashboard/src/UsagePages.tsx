import { formatCount, formatDateTime, usageUsdFromMicro } from './LocaleFormatting';
import { t } from './I18n';
import { useState } from 'react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { management, type Project, type UsageFilters, type UsageRequestDetail } from '@uzllm/api-client';
import { UsageExportButton } from './UsageExportButton';

const startOfDay = (value: Date) => new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate()));
const today = () => startOfDay(new Date()).toISOString().slice(0, 10);
const daysAgo = (days: number) => startOfDay(new Date(Date.now() - days * 86_400_000)).toISOString().slice(0, 10);
const timestamp = (value: string) => formatDateTime(new Date(value));
const money = (value: string | null | undefined) => value == null ? t('Unknown / unsettled') : usageUsdFromMicro(value);
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
  return <div className="usage-window"><label>{t("From (UTC)")}<input type="date" value={from} max={to} onChange={event => setFrom(event.target.value)} /></label>
    <label>{t("To (UTC)")}<input type="date" value={to} min={from} onChange={event => setTo(event.target.value)} /></label></div>;
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
  return <><div className="page-heading"><span className="eyebrow">{t("INFERENCE AUDIT")}</span><h1>{t("Activity")}</h1>
    <p>{t("Every logical gateway request, its attempts, usage evidence, and final charge.")}</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window}
      setFrom={value => { window.setFrom(value); setModelId(''); setProviderId(''); choose(null); }}
      setTo={value => { window.setTo(value); setModelId(''); setProviderId(''); choose(null); }} />
      <label>{t("Filter project")}<select value={projectId ?? ''} onChange={event => changeProject(event.target.value)}><option value="">{t("All projects")}</option>
        {projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <label>{t("API key")}<select value={effectiveKey ?? ''} disabled={!projectId || keys.isPending || keys.isError} onChange={event => setApiKeyId(event.target.value)}>
        <option value="">{t("All keys")}{!projectId ? t(" · select a project") : ''}</option>{keys.data?.map(key => <option key={key.id} value={key.id}>{key.name} · {key.prefix}</option>)}</select></label>
      <label>{t("Model")}<select value={modelId} onChange={event => setModelId(event.target.value)}><option value="">{t("All models")}</option>
        {models.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>{t("Provider")}<select value={providerId} onChange={event => setProviderId(event.target.value)}><option value="">{t("All providers")}</option>
        {providers.data?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>{t("Status")}<select value={status} onChange={event => setStatus(event.target.value)}><option value="">{t("All states")}</option>
        {['Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution'].map(value => <option key={value} value={value}>{t(value)}</option>)}</select></label>
      <label>{t("Delivery")}<select value={stream} onChange={event => setStream(event.target.value)}><option value="">{t("All types")}</option><option value="true">{t("Streaming")}</option><option value="false">{t("Non-streaming")}</option></select></label>
      <form onSubmit={event => { event.preventDefault(); findRequest(); }} className="usage-lookup"><label>{t("Exact request ID")}<input value={requestId} onChange={event => setRequestId(event.target.value)} placeholder="UUID" aria-invalid={!!requestId.trim() && !uuid.test(requestId.trim())} /></label>
        <button className="button secondary">{t("Find")}</button></form></div>
    {invalidWindow && <p role="alert" className="error">{t("Choose a valid UTC date range of at most 90 days.")}</p>}
    {!!requestId.trim() && !uuid.test(requestId.trim()) && <p role="alert" className="error">{t("Request ID must be a UUID.")}</p>}
    {keys.isError && projectId && <p role="alert" className="error">{t("API keys could not be loaded for this project.")}</p>}
    {activity.isError && <p role="alert" className="error">{t("Activity could not be loaded. Check the filters and try again. ")}<button className="text-button" onClick={() => activity.refetch()}>{t("Retry")}</button></p>}
    {effectiveKey && keySummary.data && <div className="panel usage-key-summary" role="status"><strong>{keys.data?.find(key => key.id === effectiveKey)?.name}{t(" usage")}</strong>
      <span>{formatCount(keySummary.data.requestCount)}{t(" requests")}</span><span>{formatCount((keySummary.data.inputTokens + keySummary.data.outputTokens))}{t(" verified tokens")}</span>
      <span>{usageUsdFromMicro(keySummary.data.chargedMicroUsd)}{t(" settled charge")}</span></div>}
    <UsageExportButton organizationId={organizationId} kind="activity" filters={filters} disabled={invalidWindow} />
    <div className="usage-layout"><section className="panel usage-table-panel" aria-label={t("Request activity")}>
      <div className="usage-section-head"><h2>{t("Requests")}</h2><span className="muted">{activity.isFetching ? t("Updating…") : t("{p0} loaded", { p0: rows.length })}</span></div>
      {activity.isPending && !invalidWindow ? <p role="status" className="muted">{t("Loading activity…")}</p>
        : activity.isError || invalidWindow ? null : rows.length === 0 ? <p className="muted">{t("No requests match this window and filters.")}</p>
          : <div className="usage-table-scroll"><table className="usage-table"><thead><tr><th>{t("When")}</th><th>{t("Project / model")}</th><th>{t("Key / provider")}</th><th>{t("Execution / delivery")}</th><th>{t("Input / output")}</th><th>{t("Latency")}</th><th>{t("Charge")}</th></tr></thead>
            <tbody>{rows.map(row => <tr key={row.requestId} className={selected === row.requestId ? 'selected' : ''}>
              <td><button className="usage-row-link" onClick={() => choose(row.requestId)} aria-label={t("Inspect request {p0}", { p0: row.requestId })}>{timestamp(row.startedAt)}</button></td>
              <td><strong>{row.projectName}</strong><small>{row.modelCode}</small></td>
              <td><strong>{row.apiKeyName}</strong><small>{row.providerCode ?? t("Unrouted")}</small></td>
              <td><span className={`usage-state ${row.httpStatus && row.httpStatus >= 400 ? 'bad' : ''}`}>{t(row.executionState)}</span><small>{t(row.deliveryState)}{row.isStream ? t(" · Stream") : ''}</small></td>
              <td>{row.inputTokens ?? t("Unknown")} / {row.outputTokens ?? t("Unknown")}</td>
              <td>{row.durationMs === null ? '—' : t("{p0} ms", { p0: row.durationMs })}</td><td>{money(row.chargedMicroUsd)}</td></tr>)}</tbody></table></div>}
      {activity.isFetchNextPageError && <p role="alert" className="error">{t("More activity could not be loaded. Retry below.")}</p>}
      {activity.hasNextPage && <button className="button secondary usage-more" disabled={activity.isFetchingNextPage} onClick={() => activity.fetchNextPage()}>
        {activity.isFetchingNextPage ? t("Loading…") : t("Load more")}</button>}
    </section><aside className="panel usage-inspector" aria-label={t("Request details")}>
      <div className="usage-section-head"><h2>{t("Request details")}</h2>{selected && <button className="text-button" onClick={() => choose(null)} data-dialog-dismiss="">{t("Close")}</button>}</div>
      {!selected ? <p className="muted">{t("Select a request to inspect its safe metadata and provider attempts.")}</p>
        : detail.isPending ? <p role="status" className="muted">{t("Loading request…")}</p>
          : detail.isError ? <p role="alert" className="error">{t("This request is unavailable or outside your organization.")}</p>
            : <RequestInspector detail={detail.data} />}
    </aside></div></>;
}

function RequestInspector({ detail }: { detail: UsageRequestDetail }) {
  const request = detail.request;
  return <div className="usage-detail"><code className="usage-id">{request.requestId}</code>
    {(request.executionState === 'OutcomeUnknown' || detail.unresolvedUsage) && <p role="status" className="usage-warning">{t("Outcome or provider usage is unknown. Reconciliation may change the final charge; this does not mean the provider billed zero.")}</p>}
    {(request.deliveryState !== 'Completed' && request.isStream) && <p role="status" className="usage-warning">{t("Streaming delivery was partial or interrupted. Provider usage may differ from tokens delivered to the client.")}</p>}
    <dl><dt>{t("Requested model")}</dt><dd>{request.modelCode}</dd><dt>{t("Selected model")}</dt><dd>{request.selectedModelCode ?? (request.attemptCount ? t("Unknown") : t("Not selected"))}</dd><dt>{t("Project")}</dt><dd>{request.projectName}</dd>
      <dt>{t("Key")}</dt><dd>{request.apiKeyName}</dd><dt>{t("Execution")}</dt><dd>{t(request.executionState)}</dd>
      <dt>{t("Delivery")}</dt><dd>{t(request.deliveryState)}{request.isStream ? t(" · Streaming") : ''}</dd><dt>{t("HTTP status")}</dt><dd>{request.httpStatus ?? t("Unknown")}</dd><dt>{t("Financial")}</dt><dd>{t(request.financialState)}</dd>
      <dt>{t("Started / finished")}</dt><dd>{timestamp(request.startedAt)} / {request.completedAt ? timestamp(request.completedAt) : t("Unknown")}</dd>
      <dt>{t("Duration")}</dt><dd>{request.durationMs == null ? t("Unknown") : t("{p0} ms", { p0: request.durationMs })}</dd>
      <dt>{t("Input / output")}</dt><dd>{request.inputTokens ?? t("Unknown")} / {request.outputTokens ?? t("Unknown")}</dd>
      <dt>{t("Customer charge")}</dt><dd>{money(detail.chargedMicroUsd)}</dd>
      <dt>{t("Provider cost")}</dt><dd>{money(detail.providerCostMicroUsd)}</dd>
      <dt>{t("External provider spend")}</dt><dd>{money(detail.externalProviderSpendMicroUsd)}</dd>
      <dt>{t("Late provider spend")}</dt><dd>{money(detail.lateExternalSpendMicroUsd)}</dd>
      <dt>{t("Platform exposure")}</dt><dd>{money(detail.platformExposureMicroUsd)}</dd>
      <dt>{t("Routing strategy")}</dt><dd>{detail.routeStrategy ?? t("Unknown")}</dd>
      <dt>{t("Trace")}</dt><dd><code>{detail.traceId ?? t("Unknown")}</code></dd></dl>
    <h3>{t("Provider attempts")}</h3>{detail.attempts.length === 0 ? <p className="muted">{t("No provider call was made.")}</p>
      : <ol className="usage-attempts">{detail.attempts.map(attempt => <li key={attempt.attemptId}>
        <strong>#{attempt.number} {attempt.providerCode}{attempt.modelCode ? ` · ${attempt.modelCode}` : ''}</strong><span>{t(attempt.executionState)} · {timestamp(attempt.startedAt)}{attempt.completedAt ? ` → ${timestamp(attempt.completedAt)}` : ''}</span>
        <small>{t("Upstream ID: ")}{attempt.providerRequestId ?? t("Unavailable")}{attempt.errorCategory ? ` · ${attempt.errorCategory}` : ''}</small></li>)}</ol>}
    <h3>{t("Usage evidence")}</h3>{detail.evidence.length === 0 ? <p className="muted">{t("No provider usage evidence recorded yet.")}</p>
      : <ol className="usage-attempts">{detail.evidence.map(item => <li key={item.evidenceId}><strong>{t(item.state)} · {t(item.source)}</strong>
        <span>{t("Input ")}{item.inputTokens ?? t("Unknown")}{t(" · Output ")}{item.outputTokens ?? t("Unknown")}{t(" · Cached ")}{item.cachedInputTokens ?? t("Unknown")}{t(" · Reasoning ")}{item.reasoningTokens ?? t("Unknown")}</span>
        <small>{t("Captured ")}{timestamp(item.capturedAt)}{item.reconcileAfter ? t(" · Recheck after {p0}", { p0: timestamp(item.reconcileAfter) }) : ''}</small></li>)}</ol>}
    <p className="muted">{detail.unresolvedUsage ? t("Reconciliation pending") : t("No unresolved usage flag")}</p>
    <p className="muted">{t("Prompt and response bodies are not retained in this view.")}</p></div>;
}

export function AnalyticsPage({ organizationId, projectId }: { organizationId: string; projectId?: string }) {
  const window = useWindow(projectId);
  const filters = window.filters;
  const windowDays = (Date.parse(`${window.to}T00:00:00Z`) - Date.parse(`${window.from}T00:00:00Z`)) / 86_400_000 + 1;
  const invalidWindow = window.from > window.to || !window.from || !window.to || !Number.isFinite(windowDays) || windowDays > 90;
  const summary = useQuery({ queryKey: ['usage-summary', organizationId, filters], queryFn: () => management.usageSummary(organizationId, filters), enabled: !invalidWindow, ...analyticsRefresh });
  const daily = useQuery({ queryKey: ['usage-daily', organizationId, filters], queryFn: () => management.usageTimeSeries(organizationId, filters), enabled: !invalidWindow, ...analyticsRefresh });
  const maxRequests = Math.max(1, ...(daily.data?.items.map(item => item.requestCount) ?? []));
  return <><div className="page-heading"><span className="eyebrow">{t("USAGE INTELLIGENCE")}</span><h1>{t("Analytics")}</h1>
    <p>{t("Verified token evidence and settled charges across your selected window.")}</p></div>
    <div className="usage-toolbar panel"><WindowControls {...window} /></div>
    <UsageExportButton organizationId={organizationId} kind="daily" filters={filters} disabled={invalidWindow} />
    {invalidWindow && <p role="alert" className="error">{t("Choose a valid UTC date range of at most 90 days.")}</p>}
    {(summary.isError || daily.isError) && <p role="alert" className="error">{t("Analytics could not be loaded. Try a shorter range.")}</p>}
    {summary.isPending ? <p role="status" className="muted">{t("Loading analytics…")}</p> : summary.data && <>
      <div className="usage-metrics"><div className="panel metric"><span>{t("Requests")}</span><strong>{formatCount(summary.data.requestCount)}</strong><small>{formatCount(summary.data.completedCount)}{t(" completed")}</small></div>
        <div className="panel metric"><span>{t("Verified tokens")}</span><strong>{formatCount((summary.data.inputTokens + summary.data.outputTokens))}</strong><small>{formatCount(summary.data.inputTokens)}{t(" input · ")}{formatCount(summary.data.outputTokens)}{t(" output")}</small></div>
        <div className="panel metric"><span>{t("Settled charges")}</span><strong>{usageUsdFromMicro(summary.data.chargedMicroUsd)}</strong><small>{t("Pending charges excluded")}</small>
          {summary.data.refundedMicroUsd != null && summary.data.netChargedMicroUsd != null && <small>{t("Refunded ")}{usageUsdFromMicro(summary.data.refundedMicroUsd)}{t(" · net ")}{usageUsdFromMicro(summary.data.netChargedMicroUsd)}</small>}</div>
        <div className="panel metric"><span>{t("Error rate")}</span><strong>{summary.data.errorRatePercent}%</strong><small>{formatCount(summary.data.errorCount)}{t(" errors")}</small></div></div>
      <div className="usage-analytics-grid"><section className="panel"><h2>{t("Requests by day")}</h2>
        {daily.data?.items.length ? <div className="usage-bars">{daily.data.items.map(item => <div key={item.day} className="usage-bar-row">
          <span>{item.day}</span><div className="usage-bar-track"><span style={{ width: `${Math.max(2, item.requestCount / maxRequests * 100)}%` }} /></div><strong>{formatCount(item.requestCount)}</strong></div>)}</div>
          : <p className="muted">{t("No request data for this period.")}</p>}</section>
        <section className="panel"><h2>{t("Top models")}</h2><Breakdown items={summary.data.topModels} />
          <h2 className="usage-subheading">{t("Top providers")}</h2><Breakdown items={summary.data.topProviders} /></section></div>
      {(summary.data.aggregation?.pendingBucketCount ?? 0) > 0 && <p role="status" className="muted">{t("Updating usage · ")}{summary.data.aggregation?.pendingBucketCount}{t(" hour(s) pending.")}{summary.data.aggregation?.oldestPendingAt && t(" Waiting since {p0}.", { p0: timestamp(summary.data.aggregation.oldestPendingAt) })}</p>}
      <p className="muted">{Date.parse(summary.data.dataAsOf) === 0 ? t("Initial aggregation pending.") : t("Oldest hourly snapshot: {p0}.", { p0: timestamp(summary.data.dataAsOf) })}{t(" UTC dates; settled charges exclude pending usage. Analytics refreshes every 10 seconds while visible.")}</p></>}
  </>;
}

function Breakdown({ items }: { items: { id: string; name: string; requestCount: number; chargedMicroUsd: string }[] }) {
  return items.length ? <ul className="usage-breakdown">{items.map(item => <li key={item.id}><span>{item.name}<small>{formatCount(item.requestCount)}{t(" requests")}</small></span><strong>{usageUsdFromMicro(item.chargedMicroUsd)}</strong></li>)}</ul>
    : <p className="muted">{t("No data yet.")}</p>;
}
