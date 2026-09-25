export type Id = string;
export type DecimalString = string;

export interface Session { accountId: Id; email: string; emailVerified: boolean; isOperator: boolean }
export interface Organization { id: Id; name: string; status: 0 | 1 }
export interface Project { id: Id; organizationId: Id; name: string; status: 0 | 1 }
export interface ApiKey { id: Id; projectId: Id; name: string; prefix: string; status: 0 | 1; expiresAt: string | null; createdAt: string }
export interface IssuedApiKey { apiKey: ApiKey; secret: string }
export interface Wallet { organizationId: Id; postedBalanceMicroUsd: DecimalString; reservedBalanceMicroUsd: DecimalString; availableBalanceMicroUsd: DecimalString; version: number }
export interface PaymentQuote { id: Id; organizationId: Id; provider: 'Payme' | 'Click'; amountTiyin: DecimalString; feeTiyin: DecimalString; creditMicroUsd: DecimalString; uzsTiyinPerUsd: DecimalString; expiresAt: string }
export interface PaymentIntent { id: Id; organizationId: Id; provider: 'Payme' | 'Click'; status: 'Pending' | 'Created' | 'Paid' | 'Canceled' | 'Expired'; amountTiyin: DecimalString; feeTiyin: DecimalString; creditMicroUsd: DecimalString; createdAt: string }
export interface CreatedTopUp { intent: PaymentIntent; duplicate: boolean; checkoutUrl: string | null }

export class ApiError extends Error {
  constructor(readonly status: number) { super(`Request failed (${status})`); }
}

function csrfToken(): string | null {
  const encoded = document.cookie.split('; ').find(part => part.startsWith('__Host-uzllm-csrf='));
  return encoded ? decodeURIComponent(encoded.split('=').slice(1).join('=')) : null;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set('Content-Type', 'application/json');
  if (init.method && init.method !== 'GET') {
    const csrf = csrfToken();
    if (csrf) headers.set('X-CSRF-Token', csrf);
  }
  const response = await fetch(`/management/v1${path}`, { ...init, headers, credentials: 'same-origin' });
  if (!response.ok) throw new ApiError(response.status);
  return response.status === 204 || response.status === 202 ? undefined as T : await response.json() as T;
}

const json = (value: unknown) => JSON.stringify(value);
export const management = {
  session: () => request<Session>('/auth/session'),
  register: (email: string, password: string) => request<void>('/auth/register', { method: 'POST', body: json({ email, password }) }),
  verifyEmail: (token: string) => request<void>('/auth/verify-email', { method: 'POST', body: json({ token }) }),
  login: (email: string, password: string) => request<void>('/auth/login', { method: 'POST', body: json({ email, password }) }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  organizations: () => request<Organization[]>('/organizations'),
  createOrganization: (name: string) => request<Organization>('/organizations', { method: 'POST', body: json({ name }) }),
  projects: (organizationId: Id) => request<Project[]>(`/organizations/${organizationId}/projects`),
  createProject: (organizationId: Id, name: string) => request<Project>(`/organizations/${organizationId}/projects`, { method: 'POST', body: json({ name }) }),
  keys: (projectId: Id) => request<ApiKey[]>(`/projects/${projectId}/api-keys`),
  createKey: (projectId: Id, name: string) => request<IssuedApiKey>(`/projects/${projectId}/api-keys`, { method: 'POST', body: json({ name, expiresAt: null }) }),
  disableKey: (id: Id) => request<void>(`/api-keys/${id}`, { method: 'PATCH', body: json({ status: 1 }) }),
  wallet: (organizationId: Id) => request<Wallet>(`/organizations/${organizationId}/billing/wallet`),
  quotes: (organizationId: Id, provider: 'Payme' | 'Click', amountTiyin: DecimalString) => request<PaymentQuote>(
    `/organizations/${organizationId}/billing/quotes`, { method: 'POST', body: json({ provider, amountTiyin }) }),
  createTopUp: (organizationId: Id, quoteId: Id, idempotencyKey: string) => request<CreatedTopUp>(
    `/organizations/${organizationId}/billing/topups`, { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: json({ quoteId }) }),
  topUps: (organizationId: Id) => request<PaymentIntent[]>(`/organizations/${organizationId}/billing/topups`)
};

export function usdFromMicro(value: DecimalString): string {
  const amount = BigInt(value);
  const whole = amount / 1_000_000n;
  const fractional = (amount % 1_000_000n).toString().padStart(6, '0').slice(0, 2);
  return `$${whole.toLocaleString('en-US')}.${fractional}`;
}

export function uzsFromTiyin(value: DecimalString): string {
  const amount = BigInt(value);
  return `${(amount / 100n).toLocaleString('en-US')} UZS`;
}
