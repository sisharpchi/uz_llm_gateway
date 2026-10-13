import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { ApiError } from '@uzllm/api-client';

export function lifecycleError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Your session expired. Sign in again before saving.';
    if (error.status === 403) return 'Permission denied. Your current role, project grant or session does not allow this operation. Ask an organization owner; no change was saved.';
    if (error.status === 404) return 'This resource is unavailable in the current workspace. Refresh the list.';
    if (error.status === 409) return 'The resource changed or has active reservations. Refresh and retry after in-flight work finishes; no automatic retry was made.';
    if (error.status === 400) return 'The API rejected these values. Check the fields and configured ceilings.';
    if (error.status === 429) return 'Too many requests. Wait before trying again.';
    return 'The service could not confirm the operation. Refresh before retrying; it may already have committed.';
  }
  return error instanceof Error && !(error instanceof TypeError) ? error.message
    : 'The connection was lost. Refresh before retrying; the operation may already have committed.';
}

// Imperative commands keep issued secrets out of Query/Mutation caches. Scoped
// parents unmount on tenant/project/key changes; late results cannot reopen UI.
export function useLifecycleAction() {
  const active = useRef(false);
  const pending = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  async function run<T>(command: () => Promise<T>, onSuccess?: (result: T) => void, text = 'Saved.', onCommitted?: () => void) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(''); setSuccess('');
    try {
      const result = await command();
      // Invalidate only scope-keyed metadata even after unmount. Never pass the
      // result here: a secret-bearing response must not enter a cache callback.
      onCommitted?.();
      if (active.current) { onSuccess?.(result); setSuccess(text); }
    } catch (cause) { if (active.current) setError(lifecycleError(cause)); }
    finally { pending.current = false; if (active.current) setBusy(false); }
  }
  return { busy, error, success, run };
}

export function ActionFeedback({ error, success }: { error: string; success: string }) {
  return <>{error && <p role="alert" className="error">{error}</p>}{success && <p role="status" className="notice">{success}</p>}</>;
}

export function LoadError({ error, retry }: { error: unknown; retry: () => void }) {
  return <div className="stack"><p role="alert" className="error">{lifecycleError(error)}</p>
    <button type="button" className="button secondary" onClick={retry}>Retry read</button></div>;
}

export function LifecycleDialog({ title, children, close }: { title: string; children: ReactNode; close: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const id = useId();
  useEffect(() => { const dialog = ref.current; dialog?.showModal(); return () => dialog?.close(); }, []);
  return <dialog ref={ref} className="secret-dialog lifecycle-dialog" aria-labelledby={id}
    onCancel={event => { event.preventDefault(); close(); }}>
    <div className="stack"><h2 id={id}>{title}</h2>{children}</div></dialog>;
}

export function ShowOnceSecret({ secret, close }: { secret: string; close: () => void }) {
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState('');
  async function copy() {
    try { await navigator.clipboard.writeText(secret); setCopied(true); setError(''); }
    catch { setError('Clipboard unavailable. Select and copy the key manually before closing.'); }
  }
  return <LifecycleDialog title="Your new API key" close={close}>
    <span className="eyebrow">SHOW ONCE · KEEP PRIVATE</span>
    <p>Copy this key now. Closing this dialog permanently hides the secret. Never put it in browser storage or source control.</p>
    <code className="secret-value">{secret}</code>
    {error && <p role="alert" className="error">{error}</p>}
    {copied && <p role="status">Key copied. Store it securely.</p>}
    <button type="button" className="button primary" onClick={copy}>Copy key</button>
    <button type="button" className="button secondary" onClick={close}>I saved it</button>
  </LifecycleDialog>;
}

export type CodeListDraft = { mode: 'all' | 'selected' | 'none'; text: string };
export const codeListDraft = (value: string[] | null | undefined): CodeListDraft =>
  ({ mode: value == null ? 'all' : value.length ? 'selected' : 'none', text: value?.join('\n') ?? '' });

export function parseCodes(text: string): string[] {
  const codes = [...new Set(text.split(/[\s,]+/).filter(Boolean).map(value => value.toLowerCase()))];
  if (codes.length > 128 || codes.some(value => !/^[a-z0-9][a-z0-9_.:-]{0,199}$/.test(value)))
    throw new Error('Use up to 128 canonical codes: letters, digits, -, _, ., :; 200 characters each. No wildcards or provider/model aliases.');
  return codes;
}
export function allowlistValue(value: CodeListDraft): string[] | null {
  if (value.mode === 'all') return null;
  if (value.mode === 'none') return [];
  const codes = parseCodes(value.text);
  if (!codes.length) throw new Error('Enter at least one selected code, or explicitly choose Deny all.');
  return codes;
}

export function AllowlistField({ label, value, change }: { label: string; value: CodeListDraft; change: (value: CodeListDraft) => void }) {
  const id = useId();
  return <div className="stack code-list"><label>{label}<select value={value.mode} aria-describedby={id}
    onChange={event => change({ ...value, mode: event.target.value as CodeListDraft['mode'] })}>
    <option value="all">Unrestricted (null)</option><option value="selected">Only selected codes</option><option value="none">Deny all (empty list)</option>
  </select></label>
    {value.mode === 'selected' && <label>{label} codes<textarea value={value.text} required rows={3} maxLength={25856}
      onChange={event => change({ ...value, text: event.target.value })} placeholder="One canonical code per line" /></label>}
    <small id={id} className="muted">Hard restriction. Deny all blocks every route in this dimension.</small>
  </div>;
}

// Budget wire values are Int64 JSON numbers. Refuse unsafe JS integers rather
// than silently rounding money. This is a browser editing bound, not a new cap.
export function exactBudgetNumber(value: string): number {
  if (!/^\d+(\.\d{1,6})?$/.test(value)) throw new Error('Enter a non-negative USD amount with at most 6 decimal places. No exponent or commas.');
  const [whole, fraction = ''] = value.split('.');
  const micro = BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
  if (micro > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error('This amount exceeds exact browser editing precision. Use an exact-integer API client instead.');
  return Number(micro);
}
export function exactBudgetUsd(value: number): string {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error('Budget values exceed exact browser precision. Use an exact-integer API client; no rounded value will be displayed or saved.');
  const amount = BigInt(value);
  return `${amount / 1_000_000n}.${(amount % 1_000_000n).toString().padStart(6, '0')}`;
}
