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
export interface UsageFilters { from?: string; to?: string; projectId?: Id; apiKeyId?: Id; modelId?: Id; providerId?: Id; status?: string; isStream?: boolean; requestId?: Id }
export interface UsageActivityItem { requestId: Id; projectId: Id; projectName: string; apiKeyId: Id; apiKeyName: string; modelId: Id; modelCode: string; providerId: Id | null; providerCode: string | null; startedAt: string; completedAt: string | null; executionState: string; deliveryState: string; financialState: string; httpStatus: number | null; isStream: boolean; attemptCount: number; durationMs: number | null; inputTokens: number | null; outputTokens: number | null; chargedMicroUsd: DecimalString | null }
export interface UsageActivityPage { items: UsageActivityItem[]; nextCursor: string | null; dataAsOf: string }
export interface UsageBreakdownItem { id: Id; name: string; requestCount: number; errorCount: number; inputTokens: number; outputTokens: number; chargedMicroUsd: DecimalString }
export interface UsageSummary { from: string; to: string; dataAsOf: string; requestCount: number; completedCount: number; errorCount: number; pendingCount: number; inputTokens: number; outputTokens: number; chargedMicroUsd: DecimalString; errorRatePercent: number; topModels: UsageBreakdownItem[]; topProviders: UsageBreakdownItem[] }
export interface UsageDailyBucket { day: string; requestCount: number; errorCount: number; inputTokens: number; outputTokens: number; chargedMicroUsd: DecimalString }
export interface UsageTimeSeries { from: string; to: string; dataAsOf: string; items: UsageDailyBucket[] }
export interface UsageBreakdown { dimension: string; from: string; to: string; dataAsOf: string; items: UsageBreakdownItem[] }
export interface UsageAttemptDetail { attemptId: Id; number: number; providerModelId: Id; providerCode: string; startedAt: string; completedAt: string | null; executionState: string; providerRequestId: string | null; errorCategory: string | null }
export interface UsageEvidenceDetail { evidenceId: Id; attemptId: Id; state: string; source: string; inputTokens: number | null; outputTokens: number | null; cachedInputTokens: number | null; reasoningTokens: number | null; capturedAt: string; reconcileAfter: string | null }
export interface UsageRequestDetail { request: UsageActivityItem; traceId: string | null; routeStrategy: string | null; providerCostMicroUsd: DecimalString | null; chargedMicroUsd: DecimalString | null; platformExposureMicroUsd: DecimalString | null; unresolvedUsage: boolean | null; attempts: UsageAttemptDetail[]; evidence: UsageEvidenceDetail[] }

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
function usageQuery(filters: UsageFilters, extra: Record<string, string | number | undefined> = {}): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, ...extra })) if (value !== undefined && value !== '') query.set(key, String(value));
  return query.size ? `?${query}` : '';
}
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
  topUps: (organizationId: Id) => request<PaymentIntent[]>(`/organizations/${organizationId}/billing/topups`),
  activity: (organizationId: Id, filters: UsageFilters = {}, limit = 25, cursor?: string) => request<UsageActivityPage>(
    `/organizations/${organizationId}/usage/activity${usageQuery(filters, { limit, cursor })}`),
  usageDetail: (organizationId: Id, requestId: Id) => request<UsageRequestDetail>(
    `/organizations/${organizationId}/usage/requests/${encodeURIComponent(requestId)}`),
  usageSummary: (organizationId: Id, filters: UsageFilters = {}) => request<UsageSummary>(
    `/organizations/${organizationId}/usage/summary${usageQuery(filters)}`),
  usageTimeSeries: (organizationId: Id, filters: UsageFilters = {}) => request<UsageTimeSeries>(
    `/organizations/${organizationId}/usage/timeseries${usageQuery(filters)}`),
  usageBreakdown: (organizationId: Id, dimension: 'model' | 'provider' | 'api-key' | 'project', filters: UsageFilters = {}) => request<UsageBreakdown>(
    `/organizations/${organizationId}/usage/by-${dimension}${usageQuery(filters)}`)
};

export function usdFromMicro(value: DecimalString): string {
  const amount = BigInt(value);
  const whole = amount / 1_000_000n;
  const fractional = (amount % 1_000_000n).toString().padStart(6, '0').slice(0, 2);
  return `$${whole.toLocaleString('en-US')}.${fractional}`;
}

export function usageUsdFromMicro(value: DecimalString): string {
  const amount = BigInt(value);
  const whole = amount / 1_000_000n;
  const fractional = (amount % 1_000_000n).toString().padStart(6, '0');
  return `$${whole.toLocaleString('en-US')}.${fractional}`;
}

export function uzsFromTiyin(value: DecimalString): string {
  const amount = BigInt(value);
  return `${(amount / 100n).toLocaleString('en-US')} UZS`;
}
