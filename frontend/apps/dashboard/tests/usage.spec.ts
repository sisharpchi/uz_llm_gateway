import { test, expect, type Page } from '@playwright/test';

const one = '11111111-1111-1111-1111-111111111111';
const two = '22222222-2222-2222-2222-222222222222';
const requestId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

async function mockUsage(page: Page) {
  const calls: string[] = [];
  await page.route('**/management/v1/**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    calls.push(path);
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return json([{ id: one, name: 'Alpha', status: 0 }, { id: two, name: 'Beta', status: 0 }]);
    if (path.endsWith('/projects')) return json([{ id: path.includes(one) ? 'project-one' : 'project-two', name: path.includes(one) ? 'Alpha project' : 'Beta project', status: 0 }]);
    if (path.endsWith('/billing/wallet')) return json({ organizationId: one, postedBalanceMicroUsd: '0', reservedBalanceMicroUsd: '0', availableBalanceMicroUsd: '0', version: 0 });
    if (path.endsWith('/usage/activity')) return json({ dataAsOf: new Date().toISOString(), nextCursor: null,
      items: path.includes(one) ? [{ requestId, projectId: 'project-one', projectName: 'Alpha project', apiKeyId: 'key-one', apiKeyName: 'Alpha key',
        modelId: 'model-one', modelCode: 'gpt-test', providerId: 'provider-one', providerCode: 'OpenAI',
        startedAt: new Date().toISOString(), completedAt: new Date().toISOString(), executionState: 'Succeeded', deliveryState: 'Completed',
        financialState: 'Settled', httpStatus: 200, isStream: false, attemptCount: 1, durationMs: 145,
        inputTokens: 10, outputTokens: 5, chargedMicroUsd: '3200' }] : [] });
    if (path.endsWith(`/usage/requests/${requestId}`)) return json({ request: {
      requestId, projectId: 'project-one', projectName: 'Alpha project', apiKeyId: 'key-one', apiKeyName: 'Alpha key',
      modelId: 'model-one', modelCode: 'gpt-test', providerId: 'provider-one', providerCode: 'OpenAI',
      startedAt: new Date().toISOString(), completedAt: new Date().toISOString(), executionState: 'Succeeded', deliveryState: 'Completed',
      financialState: 'Settled', httpStatus: 200, isStream: false, attemptCount: 1, durationMs: 145,
      inputTokens: 10, outputTokens: 5, chargedMicroUsd: '3200' }, traceId: 'safe-trace', routeStrategy: 'priority',
      providerCostMicroUsd: '2000', chargedMicroUsd: '3200', platformExposureMicroUsd: '0', unresolvedUsage: false,
      attempts: [{ attemptId: 'attempt-one', number: 1, providerModelId: 'provider-model', providerCode: 'OpenAI',
        startedAt: new Date().toISOString(), completedAt: new Date().toISOString(), executionState: 'Succeeded', providerRequestId: 'safe-id', errorCategory: null }], evidence: [] });
    if (path.endsWith('/usage/summary')) return json({ dataAsOf: new Date().toISOString(), requestCount: path.includes(one) ? 3 : 0,
      completedCount: 3, errorCount: 1, pendingCount: 0, inputTokens: 10, outputTokens: 5, chargedMicroUsd: '3200', errorRatePercent: 33.33,
      topModels: [{ id: 'model-one', name: 'gpt-test', requestCount: 3, errorCount: 1, inputTokens: 10, outputTokens: 5, chargedMicroUsd: '3200' }], topProviders: [] });
    if (path.endsWith('/usage/timeseries')) return json({ dataAsOf: new Date().toISOString(), items: [{ day: '2026-09-25', requestCount: 3, errorCount: 1, inputTokens: 10, outputTokens: 5, chargedMicroUsd: '3200' }] });
    return route.fulfill({ status: 404 });
  });
  return calls;
}

test('activity opens safe detail and tenant switch does not retain prior rows', async ({ page }) => {
  const calls = await mockUsage(page);
  await page.goto(`/organizations/${one}/activity`);
  await expect(page.getByText('gpt-test')).toBeVisible();
  await page.getByRole('button', { name: `Inspect request ${requestId}` }).click();
  await expect(page.getByText('safe-trace')).toBeVisible();
  await expect(page.getByText('Prompt and response bodies are not retained in this view.')).toBeVisible();
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(two);
  await page.getByRole('link', { name: 'Activity' }).click();
  await expect(page.getByText('No requests match this window and filters.')).toBeVisible();
  await expect(page.getByText('safe-trace')).toHaveCount(0);
  expect(calls).toContain(`/management/v1/organizations/${two}/usage/activity`);
});

test('analytics shows authoritative summary and UTC daily data', async ({ page }) => {
  const calls = await mockUsage(page);
  await page.goto(`/organizations/${one}/analytics`);
  await expect(page.getByRole('heading', { name: 'Analytics' })).toBeVisible();
  await expect(page.getByText('gpt-test')).toBeVisible();
  await expect(page.getByText('$0.003200').first()).toBeVisible();
  await expect(page.getByText('2026-09-25')).toBeVisible();
  expect(calls).toContain(`/management/v1/organizations/${one}/usage/summary`);
  expect(calls).toContain(`/management/v1/organizations/${one}/usage/timeseries`);
});
