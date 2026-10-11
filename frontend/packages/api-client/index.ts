import type { components, paths } from './generated/management-v1';

export type Id = string;
export type DecimalString = string;
type Schema = components['schemas'];
export type Session = Schema['SessionResponse'];
export type Organization = Schema['Organization'];
export type Project = Schema['Project'];
export type ApiKey = Schema['IssuedGatewayApiKey']['apiKey'];
export type IssuedApiKey = Schema['IssuedGatewayApiKey'];
export type Wallet = Schema['WalletResponse'];
export type PaymentQuote = Schema['PaymentQuoteResponse'];
export type PaymentIntent = Schema['CustomerTopUpList'][number];
export type CreatedTopUp = Schema['CreatedTopUpResponse'];
export type CustomerRefund = Schema['CustomerSettlementRefundList'][number];
export type CatalogProviderPrice = Schema['CustomerCatalogMappingResponse'];
export type CatalogModel = Schema['CustomerCatalogModelResponse'];
export type CustomerCatalog = Schema['CustomerCatalogResponse'];
export type UsageActivityItem = Schema['UsageActivityItem'];
export type UsageActivityPage = Schema['UsageActivityPage'];
export type UsageBreakdownItem = Schema['UsageBreakdownItem'];
export type UsageSummary = Schema['UsageSummary'];
export type UsageDailyBucket = Schema['UsageDailyBucket'];
export type UsageTimeSeries = Schema['UsageTimeSeries'];
export type UsageBreakdown = Schema['UsageBreakdown'];
export type UsageAttemptDetail = Schema['UsageRequestDetail']['attempts'][number];
export type UsageEvidenceDetail = Schema['UsageRequestDetail']['evidence'][number];
export type UsageRequestDetail = Schema['UsageRequestDetail'];
export type AdminAccount = Schema['AdminAccountResponse'];
export type AdminOrganization = Schema['AdminOrganizationResponse'];
export type AdminMapping = Schema['AdminMappingResponse'];
export type AdminCredential = Schema['AdminCredentialResponse'];
export type AdminProvider = Schema['AdminProviderResponse'];
export type AdminPrice = Schema['AdminPriceResponse'];
export type AdminLedgerEntry = Schema['AdminLedgerEntryResponse'];
export type AdminPayment = Schema['AdminPaymentResponse'];
export type AdminPaymentCase = Schema['PaymentReconciliationCase'];
export type AdminRefund = Schema['AdminSettlementRefundResponse'];
export type AdminFinancialRisk = Schema['AdminFinancialRiskResponse'];
export type AdminControl = Schema['AdminPlatformControlResponse'];
export type AdminAudit = Schema['AdminAuditResponse'];
export type UsageFilters = Omit<NonNullable<paths['/management/v1/organizations/{organizationId}/usage/activity']['get']['parameters']['query']>, 'limit' | 'cursor'>;

export class ApiError extends Error {
  constructor(readonly status: number) { super('Request failed (' + status + ')'); }
}

function csrfToken(): string | null {
  const encoded = document.cookie.split('; ').find(part => part.startsWith('__Host-uzllm-csrf='));
  return encoded ? decodeURIComponent(encoded.split('=').slice(1).join('=')) : null;
}

type Verb = 'get' | 'post' | 'put' | 'patch' | 'delete';
type Operation<P extends keyof paths, M extends Verb & keyof paths[P]> = NonNullable<paths[P][M]>;
type ParametersOf<O> = O extends { parameters: infer P } ? P : never;
type OptionalParameter<O, K extends PropertyKey> = K extends keyof ParametersOf<O> ? NonNullable<ParametersOf<O>[K]> : never;
type HeadersOf<O> = Omit<OptionalParameter<O, 'header'>, 'X-CSRF-Token'>;
type Input<O> = { signal?: AbortSignal } &
  (ParametersOf<O> extends { path: infer P } ? { path: P } : { path?: never }) &
  (ParametersOf<O> extends { query: infer Q } ? { query: Q } : { query?: OptionalParameter<O, 'query'> }) &
  (O extends { requestBody: { content: { 'application/json': infer B } } } ? { body: B } : { body?: never }) &
  ({} extends HeadersOf<O> ? { headers?: HeadersOf<O> } : { headers: HeadersOf<O> });
type Success<O> = O extends { responses: infer R } ? {
  [S in keyof R]: S extends 200 | 201 | 202 | 204
    ? R[S] extends { content: { 'application/json': infer T } } ? T
      : R[S] extends { content: { 'text/csv': unknown } } ? Blob : void : never
}[keyof R] : never;

// Only the transport casts decoded JSON; every caller's method/path/input/output comes from OpenAPI.
async function call<P extends keyof paths, M extends Verb & keyof paths[P]>(
  template: P, method: M, ...args: {} extends Input<Operation<P, M>>
    ? [input?: Input<Operation<P, M>>] : [input: Input<Operation<P, M>>]
): Promise<Success<Operation<P, M>>> {
  const input = args[0] as { path?: Record<string, unknown>; query?: Record<string, unknown>; body?: unknown; headers?: HeadersInit; signal?: AbortSignal } | undefined;
  const path = template.replace(/\{([^}]+)\}/g, (_, name: string) => encodeURIComponent(String(input?.path?.[name])));
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(input?.query ?? {}))
    if (value !== undefined && value !== '') query.set(key, String(value));
  const headers = new Headers(input?.headers);
  if (input?.body !== undefined) headers.set('Content-Type', 'application/json');
  if (method !== 'get') { const csrf = csrfToken(); if (csrf) headers.set('X-CSRF-Token', csrf); }
  const response = await fetch(path + (query.size ? '?' + query : ''), {
    method: method.toUpperCase(), headers, credentials: 'same-origin', signal: input?.signal,
    body: input?.body === undefined ? undefined : JSON.stringify(input.body)
  });
  if (!response.ok) throw new ApiError(response.status);
  if (response.headers.get('Content-Type')?.startsWith('text/csv'))
    return await response.blob() as Success<Operation<P, M>>;
  return response.status === 204 || response.status === 202
    ? undefined as Success<Operation<P, M>> : await response.json() as Success<Operation<P, M>>;
}

const org = '/management/v1/organizations/{organizationId}' as const;
export const management = {
  session: () => call('/management/v1/auth/session', 'get'),
  register: (email: string, password: string) => call('/management/v1/auth/register', 'post', { body: { email, password } }),
  verifyEmail: (token: string) => call('/management/v1/auth/verify-email', 'post', { body: { token } }),
  recover: (email: string) => call('/management/v1/auth/recover', 'post', { body: { email } }),
  resetPassword: (token: string, newPassword: string) => call('/management/v1/auth/reset-password', 'post', { body: { token, newPassword } }),
  login: (email: string, password: string) => call('/management/v1/auth/login', 'post', { body: { email, password } }),
  logout: () => call('/management/v1/auth/logout', 'post'),
  operatorAccess: () => call('/management/v1/admin/access', 'get'),
  enrollOperatorMfa: (password: string) => call('/management/v1/auth/operator/mfa/enroll', 'post', { body: { password } }),
  confirmOperatorMfa: (code: string) => call('/management/v1/auth/operator/mfa/confirm', 'post', { body: { code } }),
  verifyOperatorMfa: (code: string) => call('/management/v1/auth/operator/mfa/verify', 'post', { body: { code } }),
  resetOperatorMfa: (accountId: Id, reason: string) => call('/management/v1/admin/operators/{accountId}/mfa/reset', 'post', { path: { accountId }, body: { reason } }),
  adminAccounts: (query: string) => call('/management/v1/admin/accounts', 'get', { query: { query } }),
  adminOrganizations: (query: string) => call('/management/v1/admin/organizations', 'get', { query: { query } }),
  adminProviders: () => call('/management/v1/admin/providers', 'get'),
  adminPrices: (id: Id) => call('/management/v1/admin/mappings/{id}/prices', 'get', { path: { id } }),
  adminLedger: (id: Id) => call('/management/v1/admin/organizations/{id}/ledger', 'get', { path: { id } }),
  adminPayments: (organizationId?: Id) => call('/management/v1/admin/payments', 'get', { query: { organizationId } }),
  adminPaymentCases: () => call('/management/v1/admin/payment-reconciliation/cases', 'get'),
  adminResolvePaymentCase: (id: Id, reason: string, resolutionReference: string) => call('/management/v1/admin/payment-reconciliation/cases/{id}/resolve', 'patch', { path: { id }, body: { reason, resolutionReference } }),
  adminRefunds: (organizationId?: Id) => call('/management/v1/admin/refunds', 'get', { query: { organizationId } }),
  adminCreateRefund: (settlementId: Id, refundKey: string, amountMicroUsd: DecimalString, reason: string) => call('/management/v1/admin/refunds', 'post', { body: { settlementId, refundKey, amountMicroUsd, reason } }),
  adminFinancialRisk: () => call('/management/v1/admin/financial/risk', 'get'),
  adminAudit: () => call('/management/v1/admin/audit', 'get'),
  adminControls: () => call('/management/v1/admin/controls', 'get'),
  adminCreateProvider: (code: string, name: string, reason: string) => call('/management/v1/admin/providers', 'post', { body: { code, name, reason } }),
  adminCreateModel: (code: string, name: string, contextLength: number, maxOutputTokens: number, capabilities: string[], reason: string) => call('/management/v1/admin/models', 'post', { body: { code, name, contextLength, maxOutputTokens, capabilities, reason } }),
  adminCreateMapping: (providerId: Id, modelId: Id, upstreamModelCode: string, reason: string) => call('/management/v1/admin/mappings', 'post', { body: { providerId, modelId, upstreamModelCode, endpointReference: null, reason } }),
  adminCreateCredential: (providerId: Id, secret: string, reason: string) => call('/management/v1/admin/credentials', 'post', { body: { providerId, secret, reason } }),
  adminSchedulePrice: (providerModelId: Id, effectiveFrom: string, inputPriceMicroUsdPerMillion: number, outputPriceMicroUsdPerMillion: number, reason: string) => call('/management/v1/admin/prices', 'post', { body: { providerModelId, effectiveFrom, inputPriceMicroUsdPerMillion, outputPriceMicroUsdPerMillion, cachedInputPriceMicroUsdPerMillion: null, reason } }),
  adminSetStatus: (kind: 'providers' | 'models' | 'mappings' | 'credentials' | 'controls', id: Id, enabled: boolean, reason: string) => {
    const body = { enabled, reason };
    if (kind === 'controls') return call('/management/v1/admin/controls/{feature}', 'patch', { path: { feature: id }, body });
    if (kind === 'providers') return call('/management/v1/admin/providers/{id}', 'patch', { path: { id }, body });
    if (kind === 'models') return call('/management/v1/admin/models/{id}', 'patch', { path: { id }, body });
    if (kind === 'mappings') return call('/management/v1/admin/mappings/{id}', 'patch', { path: { id }, body });
    return call('/management/v1/admin/credentials/{id}', 'patch', { path: { id }, body });
  },
  organizations: () => call('/management/v1/organizations', 'get'),
  createOrganization: (name: string) => call('/management/v1/organizations', 'post', { body: { name } }),
  projects: (organizationId: Id) => call(`${org}/projects`, 'get', { path: { organizationId } }),
  catalog: (organizationId: Id, projectId: Id) => call(`${org}/projects/{projectId}/catalog/models`, 'get', { path: { organizationId, projectId } }),
  createProject: (organizationId: Id, name: string) => call(`${org}/projects`, 'post', { path: { organizationId }, body: { name } }),
  keys: (projectId: Id) => call('/management/v1/projects/{projectId}/api-keys', 'get', { path: { projectId } }),
  createKey: (projectId: Id, name: string) => call('/management/v1/projects/{projectId}/api-keys', 'post', { path: { projectId }, body: { name, expiresAt: null } }),
  disableKey: (apiKeyId: Id) => call('/management/v1/api-keys/{apiKeyId}', 'patch', { path: { apiKeyId }, body: { status: 1 } }),
  wallet: (organizationId: Id) => call(`${org}/billing/wallet`, 'get', { path: { organizationId } }),
  quotes: (organizationId: Id, provider: 'Payme' | 'Click', amountTiyin: DecimalString) => call(`${org}/billing/quotes`, 'post', { path: { organizationId }, body: { provider, amountTiyin } }),
  createTopUp: (organizationId: Id, quoteId: Id, idempotencyKey: string) => call(`${org}/billing/topups`, 'post', { path: { organizationId }, headers: { 'Idempotency-Key': idempotencyKey }, body: { quoteId } }),
  topUps: (organizationId: Id) => call(`${org}/billing/topups`, 'get', { path: { organizationId } }),
  customerRefunds: (organizationId: Id) => call(`${org}/billing/refunds`, 'get', { path: { organizationId } }),
  activity: (organizationId: Id, filters: UsageFilters = {}, limit = 25, cursor?: string) => call(`${org}/usage/activity`, 'get', { path: { organizationId }, query: { ...filters, limit, cursor } }),
  usageDetail: (organizationId: Id, requestId: Id) => call(`${org}/usage/requests/{requestId}`, 'get', { path: { organizationId, requestId } }),
  usageSummary: (organizationId: Id, filters: UsageFilters = {}) => call(`${org}/usage/summary`, 'get', { path: { organizationId }, query: filters }),
  usageTimeSeries: (organizationId: Id, filters: UsageFilters = {}) => call(`${org}/usage/timeseries`, 'get', { path: { organizationId }, query: filters }),
  exportUsage: (organizationId: Id, kind: 'activity' | 'daily', filters: UsageFilters = {}, signal?: AbortSignal) =>
    call(`${org}/usage/export`, 'get', { path: { organizationId }, query: { ...filters, kind, rowLimit: 1000 }, signal }),
  usageBreakdown: (organizationId: Id, dimension: 'model' | 'provider' | 'api-key' | 'project', filters: UsageFilters = {}) => {
    const input = { path: { organizationId }, query: filters };
    if (dimension === 'model') return call(`${org}/usage/by-model`, 'get', input);
    if (dimension === 'provider') return call(`${org}/usage/by-provider`, 'get', input);
    if (dimension === 'api-key') return call(`${org}/usage/by-api-key`, 'get', input);
    return call(`${org}/usage/by-project`, 'get', input);
  }
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
