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
export interface AdminAccount { id: Id; email: string; status: string; emailVerified: boolean; isOperator: boolean; createdAt: string }
export interface AdminOrganization { id: Id; name: string; status: string; postedBalanceMicroUsd: DecimalString | null; reservedBalanceMicroUsd: DecimalString | null; createdAt: string }
export interface AdminMapping { id: Id; providerId: Id; modelId: Id; modelCode: string; modelStatus: string; upstreamModelCode: string; status: string }
export interface AdminCredential { id: Id; providerId: Id; status: string; keyVersion: string; createdAt: string }
export interface AdminProvider { id: Id; code: string; name: string; status: string; mappings: AdminMapping[]; credentials: AdminCredential[] }
export interface AdminPrice { id: Id; providerModelId: Id; effectiveFrom: string; effectiveTo: string | null; inputPriceMicroUsdPerMillion: DecimalString; outputPriceMicroUsdPerMillion: DecimalString; cachedInputPriceMicroUsdPerMillion: DecimalString | null }
export interface AdminLedgerEntry { id: Id; organizationId: Id; type: string; amountMicroUsd: DecimalString; referenceType: string; referenceId: Id; occurredAt: string }
export interface AdminPayment { id: Id; organizationId: Id; provider: string; localStatus: string; providerObservation: string; amountTiyin: DecimalString; creditMicroUsd: DecimalString; externalTransactionId: string | null; hasCredit: boolean; hasReversal: boolean; reconciliationReason: string | null; reconciliationStatus: string | null; callbackCount: number; createdAt: string }
export interface AdminControl { feature: 'ManagedTraffic' | 'TopUps'; enabled: boolean; updatedAt: string }
export interface AdminAudit { id: Id; organizationId: Id | null; actorAccountId: Id; action: string; resourceType: string; resourceId: Id | null; occurredAt: string }

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
  operatorAccess: () => request<{ recentMfa: boolean }>('/admin/access'),
  enrollOperatorMfa: (password: string) => request<{ sharedSecret: string }>('/auth/operator/mfa/enroll', { method: 'POST', body: json({ password }) }),
  verifyOperatorMfa: (code: string) => request<void>('/auth/operator/mfa/verify', { method: 'POST', body: json({ code }) }),
  adminAccounts: (query: string) => request<AdminAccount[]>(`/admin/accounts?query=${encodeURIComponent(query)}`),
  adminOrganizations: (query: string) => request<AdminOrganization[]>(`/admin/organizations?query=${encodeURIComponent(query)}`),
  adminProviders: () => request<AdminProvider[]>('/admin/providers'),
  adminPrices: (mappingId: Id) => request<AdminPrice[]>(`/admin/mappings/${encodeURIComponent(mappingId)}/prices`),
  adminLedger: (organizationId: Id) => request<AdminLedgerEntry[]>(`/admin/organizations/${encodeURIComponent(organizationId)}/ledger`),
  adminPayments: (organizationId?: Id) => request<AdminPayment[]>(`/admin/payments${organizationId ? `?organizationId=${encodeURIComponent(organizationId)}` : ''}`),
  adminAudit: () => request<AdminAudit[]>('/admin/audit'),
  adminControls: () => request<AdminControl[]>('/admin/controls'),
  adminCreateProvider: (code: string, name: string, reason: string) => request<{ id: Id }>('/admin/providers', { method: 'POST', body: json({ code, name, reason }) }),
  adminCreateModel: (code: string, name: string, contextLength: number, maxOutputTokens: number, capabilities: string[], reason: string) => request<{ id: Id }>('/admin/models', { method: 'POST', body: json({ code, name, contextLength, maxOutputTokens, capabilities, reason }) }),
  adminCreateMapping: (providerId: Id, modelId: Id, upstreamModelCode: string, reason: string) => request<{ id: Id }>('/admin/mappings', { method: 'POST', body: json({ providerId, modelId, upstreamModelCode, endpointReference: null, reason }) }),
  adminCreateCredential: (providerId: Id, secret: string, reason: string) => request<{ id: Id }>('/admin/credentials', { method: 'POST', body: json({ providerId, secret, reason }) }),
  adminSchedulePrice: (providerModelId: Id, effectiveFrom: string, inputPriceMicroUsdPerMillion: number, outputPriceMicroUsdPerMillion: number, reason: string) => request<{ id: Id }>('/admin/prices', { method: 'POST', body: json({ providerModelId, effectiveFrom, inputPriceMicroUsdPerMillion, outputPriceMicroUsdPerMillion, cachedInputPriceMicroUsdPerMillion: null, reason }) }),
  adminSetStatus: (kind: 'providers' | 'models' | 'mappings' | 'credentials' | 'controls', id: Id, enabled: boolean, reason: string) => request<void>(`/admin/${kind}/${encodeURIComponent(id)}`, { method: 'PATCH', body: json({ enabled, reason }) }),
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
  const magnitude = amount < 0n ? -amount : amount;
  const fraction = magnitude % 100n;
  return `${amount < 0n ? '-' : ''}${(magnitude / 100n).toLocaleString('en-US')}${fraction ? `.${fraction.toString().padStart(2, '0')}` : ''} UZS`;
}

export function tiyinFromWholeUzs(value: string): DecimalString {
  if (!/^[0-9]+$/.test(value) || BigInt(value) < 1n)
    throw new Error('Enter a whole UZS amount of at least 1.');
  const amountTiyin = BigInt(value) * 100n;
  if (amountTiyin > 9_223_372_036_854_775_807n)
    throw new Error('Amount exceeds the supported payment limit.');
  return amountTiyin.toString();
}
