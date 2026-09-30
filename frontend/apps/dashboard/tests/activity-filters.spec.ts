import { expect, test, type Page } from '@playwright/test';

const org = '11111111-1111-1111-1111-111111111111';
const otherOrg = '22222222-2222-2222-2222-222222222222';
const project = '33333333-3333-3333-3333-333333333333';
const key = '44444444-4444-4444-4444-444444444444';
const model = '55555555-5555-5555-5555-555555555555';
const provider = '66666666-6666-6666-6666-666666666666';
const request = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const secondRequest = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const row = (id: string) => ({ requestId: id, projectId: project, projectName: 'Production', apiKeyId: key,
  apiKeyName: 'Deploy key', modelId: model, modelCode: 'gpt-demo', selectedModelId: model, selectedModelCode: 'gpt-demo',
  providerId: provider, providerCode: 'OpenAI', startedAt: new Date().toISOString(), completedAt: null,
  executionState: 'OutcomeUnknown', deliveryState: 'ClientDisconnected', financialState: 'PendingEvidence',
  httpStatus: null, isStream: true, attemptCount: 1, durationMs: null, inputTokens: null, outputTokens: null,
  chargedMicroUsd: null });

async function mockActivity(page: Page) {
  const activityQueries: URLSearchParams[] = [];
  await page.route('**/management/v1/**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return json([{ id: org, name: 'Alpha', status: 0 }, { id: otherOrg, name: 'Beta', status: 0 }]);
    if (path.endsWith('/projects')) return json(path.includes(org) ? [{ id: project, organizationId: org, name: 'Production', status: 0 }] : []);
    if (path.endsWith('/billing/wallet')) return json({ organizationId: org, availableBalanceMicroUsd: '0' });
    if (path.endsWith('/api-keys')) return json([{ id: key, projectId: project, name: 'Deploy key', prefix: 'uzllm_123', status: 0 }]);
    if (path.endsWith('/usage/by-model')) return json({ items: [{ id: model, name: 'gpt-demo', requestCount: 2 }] });
    if (path.endsWith('/usage/by-provider')) return json({ items: [{ id: provider, name: 'OpenAI', requestCount: 2 }] });
    if (path.endsWith('/usage/summary')) return json({ requestCount: 2, inputTokens: 20, outputTokens: 10, chargedMicroUsd: '1200' });
    if (path.endsWith('/usage/activity')) {
      activityQueries.push(new URLSearchParams(url.search));
      if (path.includes(otherOrg)) return json({ items: [], nextCursor: null, dataAsOf: new Date().toISOString() });
      return json({ items: [row(url.searchParams.has('cursor') ? secondRequest : request)],
        nextCursor: url.searchParams.has('cursor') ? null : 'opaque-next', dataAsOf: new Date().toISOString() });
    }
    if (path.endsWith(`/usage/requests/${request}`)) return json({ request: row(request), traceId: 'safe-trace',
      routeStrategy: 'priority', providerCostMicroUsd: null, chargedMicroUsd: null,
      platformExposureMicroUsd: null, unresolvedUsage: true, externalProviderSpendMicroUsd: null,
      attempts: [{ attemptId: 'attempt-1', number: 1, providerCode: 'OpenAI', modelCode: 'gpt-demo',
        startedAt: new Date().toISOString(), completedAt: null, executionState: 'OutcomeUnknown',
        providerRequestId: 'upstream-safe', errorCategory: 'timeout' }],
      evidence: [{ evidenceId: 'evidence-1', attemptId: 'attempt-1', state: 'Unknown', source: 'Provider',
        inputTokens: null, outputTokens: null, cachedInputTokens: null, reasoningTokens: null,
        capturedAt: new Date().toISOString(), reconcileAfter: new Date().toISOString() }],
      prompt: 'SECRET PROMPT MUST NOT APPEAR', response: 'SECRET RESPONSE MUST NOT APPEAR' });
    return route.fulfill({ status: 404 });
  });
  return activityQueries;
}

test('activity filters combine with cursor, key totals, and reset for another tenant', async ({ page }) => {
  const queries = await mockActivity(page);
  await page.goto(`/organizations/${org}/activity`);
  await expect(page.getByRole('button', { name: `Inspect request ${request}` })).toBeVisible();
  await page.getByRole('combobox', { name: 'Filter project' }).selectOption(project);
  await page.getByRole('combobox', { name: 'API key' }).selectOption(key);
  await page.getByRole('combobox', { name: 'Model' }).selectOption(model);
  await page.getByRole('combobox', { name: 'Provider' }).selectOption(provider);
  await page.getByRole('combobox', { name: 'Status' }).selectOption('OutcomeUnknown');
  await page.getByRole('combobox', { name: 'Delivery' }).selectOption('true');
  await expect(page.getByText('Deploy key usage')).toBeVisible();
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('button', { name: `Inspect request ${secondRequest}` })).toBeVisible();
  expect(queries.some(query => query.get('projectId') === project && query.get('apiKeyId') === key &&
    query.get('modelId') === model && query.get('providerId') === provider && query.get('status') === 'OutcomeUnknown' &&
    query.get('isStream') === 'true' && !!query.get('from') && !!query.get('to') &&
    query.get('cursor') === 'opaque-next')).toBe(true);
  await page.getByRole('combobox', { name: 'Status' }).selectOption('Failed');
  await expect.poll(() => queries.at(-1)?.get('cursor')).toBeNull();
  expect(queries.at(-1)?.get('status')).toBe('Failed');
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(otherOrg);
  await page.getByRole('link', { name: 'Activity' }).click();
  await expect(page.getByText('No requests match this window and filters.')).toBeVisible();
  await expect(page.getByText('Deploy key usage')).toHaveCount(0);
});

test('unknown interrupted request detail is explicit and never renders raw payload', async ({ page }) => {
  await mockActivity(page);
  await page.goto(`/organizations/${org}/activity`);
  await page.getByRole('button', { name: `Inspect request ${request}` }).click();
  await expect(page.getByText('Outcome or provider usage is unknown.', { exact: false })).toBeVisible();
  await expect(page.getByText('Streaming delivery was partial or interrupted.', { exact: false })).toBeVisible();
  await expect(page.getByText('Unknown / unsettled').first()).toBeVisible();
  await expect(page.getByText('safe-trace')).toBeVisible();
  await expect(page.getByText('SECRET PROMPT MUST NOT APPEAR')).toHaveCount(0);
  await expect(page.getByText('SECRET RESPONSE MUST NOT APPEAR')).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Exact request ID' }).fill('not-a-uuid');
  await page.getByRole('button', { name: 'Find' }).click();
  await expect(page.getByText('Request ID must be a UUID.')).toBeVisible();
});

test('UTC date filter changes the query and rejects a window over 90 days', async ({ page }) => {
  const queries = await mockActivity(page);
  await page.goto(`/organizations/${org}/activity`);
  await expect(page.getByRole('button', { name: `Inspect request ${request}` })).toBeVisible();
  const threeDaysAgo = new Date(Date.now() - 3 * 86_400_000).toISOString().slice(0, 10);
  await page.getByLabel('From (UTC)').fill(threeDaysAgo);
  await expect.poll(() => queries.at(-1)?.get('from')).toBe(`${threeDaysAgo}T00:00:00.000Z`);
  await page.getByLabel('From (UTC)').fill('2020-01-01');
  await expect(page.getByText('Choose a valid UTC date range of at most 90 days.')).toBeVisible();
});
