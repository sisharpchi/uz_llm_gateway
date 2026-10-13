import { useState, type FormEvent } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { management, type BudgetPeriod, type BudgetPolicy, type TenantLimits } from '@uzllm/api-client';
import { ActionFeedback, exactBudgetNumber, exactBudgetUsd, LoadError, useLifecycleAction } from './LifecycleControls';

type Scope = { organizationId: string; projectId: string; apiKeyId?: string };
type LimitView = Awaited<ReturnType<typeof management.projectLimits>>;

export function LimitsEditor(scope: Scope) {
  const key = ['tenant-limits', scope.organizationId, scope.projectId, scope.apiKeyId ?? 'project'];
  const query = useQuery({ queryKey: key, queryFn: () => scope.apiKeyId ? management.keyLimits(scope.apiKeyId) : management.projectLimits(scope.projectId), refetchOnWindowFocus: false });
  return <section className="panel lifecycle-section"><h3>Request limits</h3>
    <p className="muted">Project and key limits both apply. Blank inherits the operator ceiling, never unlimited. Existing streams are not cancelled.</p>
    {query.isPending || (!query.isFetchedAfterMount && query.isFetching) ? <p role="status">Loading request limits…</p> : query.isError ? <LoadError error={query.error} retry={() => void query.refetch()} />
      : <LimitsForm scope={scope} value={query.data} />}</section>;
}

function LimitsForm({ scope, value }: { scope: Scope; value: LimitView }) {
  const [rpm, setRpm] = useState(value.configured.requestsPerMinute?.toString() ?? '');
  const [concurrency, setConcurrency] = useState(value.configured.concurrency?.toString() ?? '');
  const action = useLifecycleAction(); const client = useQueryClient();
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      const parse = (input: string, ceiling: number | null | undefined): number | null => {
        if (!input) return null;
        const number = Number(input);
        if (!/^\d+$/.test(input) || !Number.isSafeInteger(number) || number < 1 || ceiling == null || number > ceiling)
          throw new Error('Limits must be whole positive numbers at or below the operator ceiling. Blank inherits.');
        return number;
      };
      const limits: TenantLimits = { requestsPerMinute: parse(rpm, value.ceiling.requestsPerMinute), concurrency: parse(concurrency, value.ceiling.concurrency) };
      return scope.apiKeyId ? management.setKeyLimits(scope.apiKeyId, limits) : management.setProjectLimits(scope.projectId, limits);
    }, result => { client.setQueryData(['tenant-limits', scope.organizationId, scope.projectId, scope.apiKeyId ?? 'project'], result); }, 'Request limits saved.',
    () => { void client.invalidateQueries({ queryKey: ['tenant-limits', scope.organizationId, scope.projectId, scope.apiKeyId ?? 'project'] }); });
  }
  return <form className="stack" onSubmit={save} aria-label="Request limits">
    <div className="control-pair"><label>Requests per minute<input inputMode="numeric" value={rpm} onChange={event => setRpm(event.target.value)} placeholder="Inherit" />
      <small>Effective {value.effective.requestsPerMinute} · ceiling {value.ceiling.requestsPerMinute}</small></label>
      <label>Concurrent requests<input inputMode="numeric" value={concurrency} onChange={event => setConcurrency(event.target.value)} placeholder="Inherit" />
        <small>Effective {value.effective.concurrency} · ceiling {value.ceiling.concurrency}</small></label></div>
    <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Save request limits</button>
  </form>;
}

const periods: BudgetPeriod[] = ['Lifetime', 'Daily', 'Weekly', 'Monthly'];
export function BudgetsEditor(scope: Scope) {
  const query = useQuery({ queryKey: ['budgets', scope.organizationId, scope.projectId], queryFn: () => management.budgets(scope.projectId), refetchOnWindowFocus: false });
  const budgets = query.data?.filter(value => value.apiKeyId === (scope.apiKeyId ?? null));
  let precisionError = '';
  try { budgets?.forEach(value => { exactBudgetUsd(value.limitMicroUsd); exactBudgetUsd(value.capturedMicroUsd); exactBudgetUsd(value.reservedMicroUsd); }); }
  catch (cause) { precisionError = cause instanceof Error ? cause.message : 'Unsafe budget precision.'; }
  return <section className="panel lifecycle-section"><h3>Spend budgets · USD</h3>
    <p className="muted">All configured project and key budgets are hard gates. Zero blocks new spend. Windows reset in UTC; holds and captured spend are preserved. No cap-removal operation is available.</p>
    {query.isPending ? <p role="status">Loading spend budgets…</p> : query.isError ? <LoadError error={query.error} retry={() => void query.refetch()} />
      : precisionError ? <p role="alert" className="error">{precisionError}</p> : <>
        <div className="budget-grid">{periods.map(period => <BudgetCard key={period} period={period} budget={budgets?.find(value => value.period === period)} />)}</div>
        <BudgetForm scope={scope} budgets={budgets ?? []} />
      </>}
  </section>;
}

function BudgetCard({ period, budget }: { period: BudgetPeriod; budget?: BudgetPolicy }) {
  const used = budget ? BigInt(budget.capturedMicroUsd) + BigInt(budget.reservedMicroUsd) : 0n;
  const percentage = budget && budget.limitMicroUsd > 0 ? Number(used * 100n / BigInt(budget.limitMicroUsd)) : used > 0n ? 100 : 0;
  return <div className="budget-card"><h4>{period}</h4>{budget ? <>
    <strong>${exactBudgetUsd(budget.limitMicroUsd)}</strong>
    <progress max={100} value={Math.min(100, percentage)} aria-label={`${period} budget utilization`} />
    <small>Captured ${exactBudgetUsd(budget.capturedMicroUsd)}</small><small>Reserved ${exactBudgetUsd(budget.reservedMicroUsd)}</small>
    <small>{budget.windowEnd ? `Resets ${new Date(budget.windowEnd).toISOString().replace('T', ' ')} (UTC)` : 'No reset · all time'}</small>
  </> : <><strong>Not configured</strong><small>Other budgets and wallet checks still apply.</small></>}</div>;
}

function BudgetForm({ scope, budgets }: { scope: Scope; budgets: BudgetPolicy[] }) {
  const [period, setPeriod] = useState<BudgetPeriod>('Monthly'); const [amount, setAmount] = useState('');
  const action = useLifecycleAction(); const client = useQueryClient();
  async function save(event: FormEvent) {
    event.preventDefault();
    await action.run(() => management.setBudget(scope.projectId, period, exactBudgetNumber(amount), scope.apiKeyId ?? null),
      () => { setAmount(''); }, 'Budget saved. Existing holds are preserved.', () => { void client.invalidateQueries({ queryKey: ['budgets', scope.organizationId, scope.projectId] }); });
  }
  return <form className="stack budget-form" onSubmit={save} aria-label="Spend budget">
    <div className="control-pair"><label>Budget period<select value={period} onChange={event => { setPeriod(event.target.value as BudgetPeriod); setAmount(''); }}>
      {periods.map(value => <option key={value}>{value}</option>)}</select></label>
      <label>Budget amount (USD)<input value={amount} inputMode="decimal" required maxLength={24} onChange={event => setAmount(event.target.value)}
        placeholder={budgets.find(value => value.period === period) ? exactBudgetUsd(budgets.find(value => value.period === period)!.limitMicroUsd) : 'e.g. 25.000000'} /></label></div>
    <small className="muted">Up to 6 decimal places. Saving replaces this period only. Budget and request-limit updates are independent; Owner/Admin billing permission is required.</small>
    <ActionFeedback {...action} /><button className="button primary" disabled={action.busy}>Save spend budget</button>
  </form>;
}
