import { formatCount, formatDateTime, usageUsdFromMicro } from './LocaleFormatting';
import { t } from './I18n';
import { useEffect, useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, management, type AlertDestination, type AlertRule, type Organization, type Project } from '@uzllm/api-client';
import { ActionFeedback, exactBudgetNumber, LifecycleDialog, LoadError, useLifecycleAction } from './LifecycleControls';
import './Lifecycle.css';
import './AlertsPrivacy.css';

type RuleType = 'LowBalance' | 'BudgetWarning' | 'ErrorSpike';
const labels: Record<string, string> = { LowBalance: 'Low balance', BudgetWarning: 'Budget warning', ErrorSpike: 'Error spike' };
const date = (value: string | null) => value ? formatDateTime(new Date(value)) : t('Not recorded');
const thresholdText = (type: string, value: number) => Number.isSafeInteger(value) && value >= 0
  ? type === 'LowBalance' ? usageUsdFromMicro(String(value)) : `${formatCount(value / 100)}%`
  : t('Outside exact browser precision');
const alertKey = (accountId: string, orgId: string) => ['customer-alerts', accountId, orgId];

export function AlertsPage({ accountId, organization, projects }: { accountId: string; organization: Organization; projects: Project[] }) {
  const orgId = organization.id; const client = useQueryClient(); const key = alertKey(accountId, orgId);
  const rules = useQuery({ queryKey: [...key, 'rules'], queryFn: ({ signal }) => management.alertRules(orgId, signal), staleTime: 0 });
  const destinations = useQuery({ queryKey: [...key, 'destinations'], queryFn: ({ signal }) => management.alertDestinations(orgId, signal), staleTime: 0 });
  const events = useQuery({ queryKey: [...key, 'events'], queryFn: ({ signal }) => management.alertEvents(orgId, signal), staleTime: 0 });
  const members = useQuery({ queryKey: [...key, 'role'], queryFn: ({ signal }) => management.teamMembers(orgId, signal), staleTime: 0 });
  const role = !members.isError && members.isFetchedAfterMount ? members.data?.find(value => value.accountId === accountId && value.status === 'Active')?.role : undefined;
  const canManage = role === 'Owner' || role === 'Admin';
  const ready = rules.isSuccess && destinations.isSuccess && rules.isFetchedAfterMount && destinations.isFetchedAfterMount;
  const [dialog, setDialog] = useState<'rule' | 'telegram' | 'webhook' | null>(null);
  const [disable, setDisable] = useState<AlertDestination | null>(null);
  const [toggle, setToggle] = useState<AlertRule | null>(null);
  const action = useLifecycleAction();
  const refresh = () => { void client.invalidateQueries({ queryKey: key }); };
  const error = rules.error ?? destinations.error;
  return <div className="lifecycle-page alerts-page"><div className="page-heading"><span className="eyebrow">{t("SIGNAL, NOT NOISE · ")}{organization.name}</span><h1>{t("Alerts")}</h1><p>{t("Set a threshold. Choose a destination. Follow the delivery evidence.")}</p></div>
    <p className="lifecycle-callout">{t("Organization-wide alerts · financial reads require Owner, Admin or BillingViewer. Only Owner/Admin can change rules or destinations. Project selection does not filter this page.")}</p>
    {error ? <section className="panel stack"><h2>{t("Alert access unavailable")}</h2><LoadError error={error} retry={refresh} /></section> : !ready ? <p role="status">{t("Loading alert configuration…")}</p> : <>
      <div className="section-title"><p className="muted">{canManage ? t("Management access confirmed.") : t("Read-only until current Owner/Admin access is confirmed.")}</p><button className="button secondary" onClick={refresh}>{t("Refresh alerts")}</button></div>
      <section className="panel stack"><div className="section-title"><div><span className="eyebrow">{t("01 · DESTINATIONS")}</span><h2>{t("Where alerts go")}</h2></div>{canManage && <div className="row-actions"><button className="button secondary" onClick={() => setDialog('telegram')}>{t("Connect Telegram")}</button><button className="button secondary" onClick={() => setDialog('webhook')}>{t("Add webhook")}</button></div>}</div>
        <p className="muted">{t("Telegram requires private-chat proof. Webhook Active means HTTPS/DNS validation, not endpoint ownership or successful delivery. One destination per channel.")}</p>
        {destinations.data.length ? <ul className="row-list">{destinations.data.map(value => <li key={value.id}><div><strong>{value.type}</strong><small>{t(value.status)} · {value.type === 'Telegram' ? t("Private chat ID hidden") : value.endpointUrl}</small><small>{t("Validated ")}{date(value.verifiedAt)}</small></div>{canManage && <button className="button secondary" disabled={value.status === 'Disabled'} onClick={() => setDisable(value)}>{t("Disable ")}{value.type}</button>}</li>)}</ul> : <p>{t("No destinations yet. Connect one before creating a rule.")}</p>}
      </section>
      <section className="panel stack"><div className="section-title"><div><span className="eyebrow">{t("02 · RULES")}</span><h2>{t("Threshold rules")}</h2></div>{canManage && <button className="button primary" disabled={!destinations.data.some(value => value.status === 'Active' || value.status === 'Verified')} onClick={() => setDialog('rule')}>{t("Create alert rule")}</button>}</div>
        <p className="muted">{t("Low balance uses available wallet credits. Budget use includes captured spend + reservations. Error spike uses completed requests over five minutes, with at least 20 samples. Persistent breaches are deduplicated until recovery/new budget window. Provider outages remain operator alerts.")}</p>
        {rules.data.length ? <ul className="row-list">{rules.data.map(rule => <li key={rule.id}><div><strong>{t(labels[rule.type] ?? rule.type)} · {thresholdText(rule.type, rule.threshold)}</strong><small>{rule.projectId ? projects.find(project => project.id === rule.projectId)?.name ?? rule.projectId : t("Organization")} · {destinations.data.find(value => value.id === rule.destinationId)?.type ?? t("Destination unavailable")} · {rule.enabled ? t("Enabled") : t("Disabled")}</small><small>{t("Last triggered: ")}{date(rule.lastTriggeredAt)}</small><small>{t("Rule ")}{rule.id}</small></div>{canManage && <button className="button secondary" onClick={() => setToggle(rule)}>{rule.enabled ? t("Pause") : t("Enable")}{t(" rule ")}{rule.id.slice(0, 8)}</button>}</li>)}</ul> : <p>{t("No rules. Start with a low-balance alert.")}</p>}
      </section>
      <section className="panel stack"><div><span className="eyebrow">{t("03 · DELIVERY EVIDENCE")}</span><h2>{t("Recent deliveries")}</h2></div><p className="muted">{t("Latest 100 events, newest first. Pending means no recorded acknowledgement; retries or exhausted work may still need support. Delivered means transport accepted, not that a person read it. Suppressed means delivery was stopped. At-least-once delivery can duplicate a message.")}</p>
        {events.isError ? <LoadError error={events.error} retry={() => void events.refetch()} /> : !events.isFetchedAfterMount ? <p role="status">{t("Loading delivery evidence…")}</p> : events.data?.length ? <ul className="row-list">{events.data.map(value => <li key={value.id}><div><strong>{t(labels[value.type] ?? value.type)}{t(" · observed ")}{thresholdText(value.type, value.observedValue)}</strong><small>{t("Triggered ")}{date(value.triggeredAt)}{t(" · accepted ")}{date(value.deliveredAt)}</small><small>{t("Event ")}{value.id}{t(" · rule ")}{value.ruleId}</small></div><span className="tag">{t(value.status)}</span></li>)}</ul> : <p>{t("No delivery events recorded. An enabled rule is not evidence that a message was sent.")}</p>}
      </section>
      {canManage && dialog === 'rule' && <RuleDialog accountId={accountId} orgId={orgId} projects={projects} destinations={destinations.data} close={() => setDialog(null)} refresh={refresh} />}
      {canManage && (dialog === 'telegram' || dialog === 'webhook') && <DestinationDialog orgId={orgId} kind={dialog} close={() => setDialog(null)} refresh={refresh} />}
      {canManage && disable && <LifecycleDialog title={t("Disable {p0}?", { p0: disable.type })} close={() => setDisable(null)}><p>{t("Stop future deliveries to this destination. Rules and financial history stay unchanged; pending messages may be suppressed. This does not revoke a Telegram link already issued.")}</p><ActionFeedback {...action} /><div className="row-actions"><button autoFocus className="button secondary" onClick={() => setDisable(null)} data-dialog-dismiss="">{t("Cancel")}</button><button className="button danger" disabled={action.busy} onClick={() => void action.run(() => management.disableAlertDestination(orgId, disable.id), () => setDisable(null), 'Destination disabled.', refresh)}>{t("Confirm disable")}</button></div></LifecycleDialog>}
      {canManage && toggle && <LifecycleDialog title={toggle.enabled ? t("Pause rule?") : t("Enable rule?")} close={() => setToggle(null)}><p>{toggle.enabled ? t("Stops new evaluation and suppresses pending deliveries where possible.") : t("Rearms this rule; an existing breach can produce another event. A disabled destination still cannot deliver.")}</p><ActionFeedback {...action} /><div className="row-actions"><button autoFocus className="button secondary" onClick={() => setToggle(null)} data-dialog-dismiss="">{t("Cancel")}</button><button className="button primary" disabled={action.busy} onClick={() => void action.run(() => management.setAlertRule(orgId, toggle.id, !toggle.enabled), () => setToggle(null), 'Rule updated.', refresh)}>{t("Confirm rule change")}</button></div></LifecycleDialog>}
    </>}</div>;
}

function RuleDialog({ accountId, orgId, projects, destinations, close, refresh }: { accountId: string; orgId: string; projects: Project[]; destinations: AlertDestination[]; close: () => void; refresh: () => void }) {
  const [type, setType] = useState<RuleType>('LowBalance'); const [threshold, setThreshold] = useState('5'); const [projectId, setProject] = useState(''); const [policyId, setPolicy] = useState(''); const [destinationId, setDestination] = useState('');
  const action = useLifecycleAction();
  const budgets = useQuery({ queryKey: ['alert-budgets', accountId, orgId, projectId], queryFn: () => management.budgets(projectId), enabled: type === 'BudgetWarning' && !!projectId, staleTime: 0 });
  async function submit(event: FormEvent) {
    event.preventDefault();
    await action.run(() => {
      let value: number;
      if (type === 'LowBalance') value = exactBudgetNumber(threshold);
      else {
        if (!/^\d+(\.\d{1,2})?$/.test(threshold)) throw new Error('Use a percentage with at most two decimals.');
        const [whole, fraction = ''] = threshold.split('.'); value = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
        if (!Number.isSafeInteger(value) || value < 1 || value > 10000) throw new Error('Percentage must be 0.01–100%.');
      }
      return management.createAlertRule(orgId, { type, threshold: value, destinationId, projectId: type === 'LowBalance' ? null : projectId || null, budgetPolicyId: type === 'BudgetWarning' ? policyId : null });
    }, close, '', refresh);
  }
  return <LifecycleDialog title={t("Create alert rule")} close={close}><form className="stack" onSubmit={submit}>
    <label>{t("Alert type")}<select value={type} onChange={event => { setType(event.target.value as RuleType); setThreshold(event.target.value === 'LowBalance' ? '5' : '80'); setProject(''); setPolicy(''); }}><option value="LowBalance">{t("Low balance")}</option><option value="BudgetWarning">{t("Budget warning")}</option><option value="ErrorSpike">{t("Error spike")}</option></select></label>
    {type !== 'LowBalance' && <label>{t("Rule project")}<select required={type === 'BudgetWarning'} value={projectId} onChange={event => { setProject(event.target.value); setPolicy(''); }}><option value="">{type === 'ErrorSpike' ? t("Entire organization") : t("Choose a project")}</option>{projects.map(value => <option key={value.id} value={value.id}>{value.name}</option>)}</select></label>}
    {type === 'BudgetWarning' && <><label>{t("Project budget policy")}<select required value={policyId} onChange={event => setPolicy(event.target.value)}><option value="">{t("Choose an existing project budget")}</option>{!budgets.isError && budgets.data?.filter(value => !value.apiKeyId).map(value => <option key={value.id} value={value.id}>{t(value.period)} · {value.id.slice(0, 8)}</option>)}</select></label>{budgets.isError && <LoadError error={budgets.error} retry={() => void budgets.refetch()} />}<p className="muted">{t("Configure a project budget on Projects first. This does not create or change spend limits.")}</p></>}
    <label>{type === 'LowBalance' ? t("Available balance threshold (USD)") : t("Threshold (%)")}<input required inputMode="decimal" value={threshold} onChange={event => setThreshold(event.target.value)} /><small>{type === 'LowBalance' ? t("At or below this amount. Up to six decimals; zero is valid.") : t("At or above this percentage. 0.01–100%, two decimals.")}</small></label>
    <label>{t("Delivery destination")}<select required value={destinationId} onChange={event => setDestination(event.target.value)}><option value="">{t("Choose an active destination")}</option>{destinations.filter(value => value.status === 'Verified' || value.status === 'Active').map(value => <option key={value.id} value={value.id}>{value.type}</option>)}</select></label>
    <ActionFeedback {...action} /><div className="row-actions"><button type="button" autoFocus className="button secondary" onClick={close} data-dialog-dismiss="">{t("Cancel")}</button><button className="button primary" disabled={action.busy || (type === 'BudgetWarning' && (budgets.isError || budgets.isFetching || !policyId))}>{t("Save alert rule")}</button></div>
  </form></LifecycleDialog>;
}

function DestinationDialog({ orgId, kind, close, refresh }: { orgId: string; kind: 'telegram' | 'webhook'; close: () => void; refresh: () => void }) {
  const [url, setUrl] = useState(''); const [issued, setIssued] = useState<{ secret?: string; link?: string; expires?: string } | null>(null);
  const [now, setNow] = useState(Date.now()); const action = useLifecycleAction();
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  const expired = !!issued?.expires && Date.parse(issued.expires) <= now;
  useEffect(() => { if (expired) setIssued(value => value ? { expires: value.expires } : null); }, [expired]);
  async function submit(event: FormEvent) {
    event.preventDefault(); const endpoint = url; setUrl('');
    await action.run(async () => {
      try {
        if (kind === 'telegram') { const result = await management.telegramLink(orgId); return { link: result.deepLink, expires: result.expiresAt }; }
        const result = await management.createAlertWebhook(orgId, endpoint); return { secret: result.signingSecret };
      } catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) throw new Error('An active webhook already exists. Refresh, then disable it before replacing. No automatic retry.');
        if (cause instanceof ApiError && cause.status === 503) throw new Error('This channel is not configured or is unavailable. Ask the operator; no live connection is confirmed.');
        throw cause;
      }
    }, setIssued, '', refresh);
  }
  // Only the backend-issued official HTTPS Telegram bot link may be navigated.
  let safeLink: string | undefined;
  if (issued?.link && !expired) {
    try { const parsed = new URL(issued.link); if (parsed.origin === 'https://t.me' && !parsed.username && !parsed.password && /^\/[A-Za-z0-9_]+$/.test(parsed.pathname)) safeLink = parsed.href; } catch { /* Fail closed for unexpected link. */ }
  }
  return <LifecycleDialog title={kind === 'telegram' ? t("Connect Telegram") : t("Add signed webhook")} close={close}>
    {!issued ? <form className="stack" onSubmit={submit}><p>{kind === 'telegram' ? t("Generate a one-use, 10-minute private-chat link. Only the configured bot can verify your destination; never enter a chat ID here.") : t("Public HTTPS hostname on port 443 only. No IP literals, user info, query or fragment. The signing secret is shown once. Creating does not prove endpoint ownership or delivery.")}</p>
      {kind === 'webhook' && <label>{t("Webhook HTTPS URL")}<input type="url" autoComplete="off" required value={url} onChange={event => setUrl(event.target.value)} placeholder="https://alerts.example.com/uzllm" /></label>}
      <ActionFeedback {...action} /><div className="row-actions"><button type="button" autoFocus className="button secondary" onClick={close} data-dialog-dismiss="">{t("Cancel")}</button><button className="button primary" disabled={action.busy}>{kind === 'telegram' ? t("Generate private-chat link") : t("Create webhook")}</button></div></form> : <div className="stack">
      <span className="eyebrow">{t("PRIVATE · THIS DIALOG ONLY")}</span>
      {kind === 'telegram' ? <><p>{t("Expires ")}{date(issued.expires ?? null)}{t(". Linking is not confirmed until the destination list says Verified. Closing hides this link; it does not revoke the pending challenge.")}</p>{expired ? <p role="status">{t("Link expired. Close and generate a new link.")}</p> : safeLink ? <a className="button primary" href={safeLink} target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer">{t("Open private chat in Telegram")}</a> : <p role="alert">{t("The returned link is not a valid official Telegram URL.")}</p>}</> : <><p>{t("Save this signing secret in your receiver’s secret store now. Closing hides it permanently. Verify HMAC signatures and deduplicate event IDs; see the API contract.")}</p><code className="secret-value">{issued.secret}</code></>}
      <button autoFocus className="button secondary" onClick={close} data-dialog-dismiss="">{t("Close private dialog")}</button></div>}
  </LifecycleDialog>;
}
