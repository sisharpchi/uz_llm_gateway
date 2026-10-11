import { useLayoutEffect, useRef, useState } from 'react';
import { ApiError, management, type UsageFilters } from '@uzllm/api-client';

export function UsageExportButton({ organizationId, kind, filters, disabled }: {
  organizationId: string; kind: 'activity' | 'daily'; filters: UsageFilters; disabled: boolean;
}) {
  const scope = JSON.stringify([organizationId, kind, filters]);
  const active = useRef<AbortController | null>(null);
  const [result, setResult] = useState<{ scope: string; status: 'pending' | 'error' | 'done'; message: string }>();
  const current = result?.scope === scope ? result : undefined;
  // Abort at the scope-change commit, before an old response can download after paint.
  useLayoutEffect(() => () => { active.current?.abort(); active.current = null; }, [scope]);

  async function download() {
    if (disabled || active.current) return;
    const controller = new AbortController();
    active.current = controller;
    setResult({ scope, status: 'pending', message: 'Preparing complete CSV…' });
    try {
      const blob = await management.exportUsage(organizationId, kind, filters, controller.signal);
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url; link.download = `uzllm-${kind}.csv`;
      document.body.append(link); link.click(); link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      setResult({ scope, status: 'done', message: 'CSV downloaded. UTC dates; amounts are integer micro-USD. Import untrusted names as text in spreadsheets.' });
    } catch (error: unknown) {
      if (controller.signal.aborted) return;
      const message = error instanceof ApiError && error.status === 413
        ? 'Export exceeds 1,000 rows or 4 MiB. Narrow the date range or filters; no partial CSV was downloaded.'
        : error instanceof ApiError && (error.status === 401 || error.status === 403)
          ? 'Export is not authorized for this scope. Check your session and project access.'
          : 'Export could not be downloaded. Try a smaller range and retry.';
      setResult({ scope, status: 'error', message });
    } finally {
      if (active.current === controller) active.current = null;
    }
  }

  return <div className="usage-export"><button type="button" className="button secondary"
    disabled={disabled || current?.status === 'pending'} onClick={download}>
    {current?.status === 'pending' ? 'Preparing CSV…' : kind === 'activity' ? 'Export activity CSV' : 'Export daily CSV'}
  </button>{current && <p role={current.status === 'error' ? 'alert' : 'status'}
    className={current.status === 'error' ? 'error' : 'muted'}>{current.message}</p>}</div>;
}
