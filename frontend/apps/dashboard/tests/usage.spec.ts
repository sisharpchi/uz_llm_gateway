import { test, expect, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';

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
    if (path.endsWith('/api-keys')) return json([{ id: 'key-one', projectId: 'project-one', name: 'Alpha key', prefix: 'uzllm_test', status: 0 }]);
    if (path.endsWith('/usage/by-model')) return json({ items: [{ id: 'model-one', name: 'gpt-test' }] });
    if (path.endsWith('/usage/by-provider')) return json({ items: [{ id: 'provider-one', name: 'OpenAI' }] });
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
  await expect(page.getByRole('button', { name: `Inspect request ${requestId}` })).toBeVisible();
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

test('analytics refreshes rollup counters and makes pending corrections and refunds explicit', async ({ page }) => {
  await page.clock.install();
  await mockUsage(page);
  let reads = 0;
  let corrected = false;
  await page.route('**/usage/summary?*', route => {
    reads += 1;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
      dataAsOf: !corrected ? '1970-01-01T00:00:00Z' : new Date().toISOString(), requestCount: !corrected ? 3 : 4,
      completedCount: 3, errorCount: 1, pendingCount: 0, inputTokens: 10, outputTokens: 5,
      chargedMicroUsd: '3200', refundedMicroUsd: '1000', netChargedMicroUsd: '2200', errorRatePercent: 33.33,
      topModels: [], topProviders: [], aggregation: { pendingBucketCount: !corrected ? 1 : 0,
        oldestPendingAt: !corrected ? '2026-10-11T00:00:00Z' : null, source: 'HourlyRollupsWithLiveEdges' }
    }) });
  });
  await page.goto(`/organizations/${one}/analytics`);
  await expect(page.getByText('Updating usage · 1 hour(s) pending.', { exact: false })).toBeVisible();
  await expect(page.getByText('Initial aggregation pending.', { exact: false })).toBeVisible();
  await expect(page.getByText('Refunded $0.001000 · net $0.002200')).toBeVisible();
  const requests = page.locator('.metric').filter({ has: page.getByText('Requests', { exact: true }) });
  await expect(requests.locator('strong')).toHaveText('3');
  const initialReads = reads;
  corrected = true;
  await page.clock.runFor(10_000);
  await expect(requests.locator('strong')).toHaveText('4');
  await expect(page.getByText('Updating usage ·', { exact: false })).toHaveCount(0);
  await expect(page.getByText('Oldest hourly snapshot:', { exact: false })).toBeVisible();
  expect(reads).toBe(initialReads + 1);
});

test('activity downloads server CSV with active UTC filters, not just loaded rows', async ({ page }) => {
  await mockUsage(page);
  const csv = '\uFEFF"requestId","chargedMicroUsd"\r\n"first","9007199254740993"\r\n"not-loaded",""\r\n';
  let query: URLSearchParams | undefined;
  await page.route('**/usage/export?*', route => {
    query = new URL(route.request().url()).searchParams;
    return route.fulfill({ contentType: 'text/csv; charset=utf-8', body: csv });
  });
  await page.goto(`/organizations/${one}/activity`);
  await expect(page.getByRole('button', { name: `Inspect request ${requestId}` })).toBeVisible();
  await page.getByRole('combobox', { name: 'Filter project' }).selectOption('project-one');
  await page.getByRole('combobox', { name: 'API key' }).selectOption('key-one');
  await page.getByRole('combobox', { name: 'Model', exact: true }).selectOption('model-one');
  await page.getByRole('combobox', { name: 'Provider', exact: true }).selectOption('provider-one');
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('OutcomeUnknown');
  await page.getByRole('combobox', { name: 'Delivery', exact: true }).selectOption('true');
  const event = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export activity CSV' }).click();
  const download = await event;
  expect(download.suggestedFilename()).toBe('uzllm-activity.csv');
  expect(await readFile((await download.path())!, 'utf8')).toBe(csv);
  expect(query?.get('kind')).toBe('activity');
  expect(query?.get('rowLimit')).toBe('1000');
  expect(query?.get('projectId')).toBe('project-one');
  expect(query?.get('apiKeyId')).toBe('key-one');
  expect(query?.get('modelId')).toBe('model-one');
  expect(query?.get('providerId')).toBe('provider-one');
  expect(query?.get('status')).toBe('OutcomeUnknown');
  expect(query?.get('isStream')).toBe('true');
  expect(query?.get('from')).toMatch(/T00:00:00.000Z$/);
  expect(query?.get('to')).toMatch(/T00:00:00.000Z$/);
  expect(query?.has('limit')).toBe(false);
  expect(query?.has('cursor')).toBe(false);
  await expect(page.getByText('CSV downloaded.', { exact: false })).toBeVisible();
});

test('daily CSV preserves backend refund/net fields and does not export chart pixels', async ({ page }) => {
  await mockUsage(page);
  const csv = '"day","chargedMicroUsd","refundedMicroUsd","netChargedMicroUsd"\r\n"2026-10-01","3200","1000","2200"\r\n';
  await page.route('**/usage/export?*', route => {
    expect(new URL(route.request().url()).searchParams.get('kind')).toBe('daily');
    return route.fulfill({ contentType: 'text/csv', body: csv });
  });
  await page.goto(`/organizations/${one}/analytics`);
  const event = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export daily CSV' }).click();
  const download = await event;
  expect(download.suggestedFilename()).toBe('uzllm-daily.csv');
  expect(await readFile((await download.path())!, 'utf8')).toBe(csv);
  await page.getByLabel('From (UTC)').fill('2020-01-01');
  await expect(page.getByRole('button', { name: 'Export daily CSV' })).toBeDisabled();
});

test('oversized export shows no-partial error, allows retry and disables invalid date ranges', async ({ page }) => {
  await mockUsage(page);
  let attempts = 0;
  const downloads: string[] = [];
  page.on('download', download => downloads.push(download.suggestedFilename()));
  await page.route('**/usage/export?*', route => {
    attempts += 1;
    return attempts === 1
      ? route.fulfill({ status: 413, contentType: 'application/problem+json', body: '{"title":"Export too large"}' })
      : route.fulfill({ contentType: 'text/csv', body: '"requestId"\r\n' });
  });
  await page.goto(`/organizations/${one}/activity`);
  const button = page.getByRole('button', { name: 'Export activity CSV' });
  await button.click();
  await expect(page.getByRole('alert').filter({ hasText: 'no partial CSV was downloaded' })).toBeVisible();
  expect(downloads).toEqual([]);
  const event = page.waitForEvent('download');
  await button.click();
  await event;
  expect(attempts).toBe(2);
  await page.getByLabel('From (UTC)').fill('2020-01-01');
  await expect(button).toBeDisabled();
  await expect(page.getByRole('alert').filter({ hasText: 'at most 90 days' })).toBeVisible();
});

test('tenant switch aborts pending export and never downloads the old tenant response', async ({ page }) => {
  await mockUsage(page);
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  const downloads: string[] = [];
  page.on('download', download => downloads.push(download.suggestedFilename()));
  await page.route('**/usage/export?*', async route => {
    await held;
    await route.fulfill({ contentType: 'text/csv', body: 'old tenant' }).catch(() => {});
  });
  await page.goto(`/organizations/${one}/activity`);
  const started = page.waitForRequest('**/usage/export?*');
  await page.getByRole('button', { name: 'Export activity CSV' }).click();
  const request = await started;
  await expect(page.getByRole('button', { name: 'Preparing CSV…' })).toBeDisabled();
  const aborted = page.waitForEvent('requestfailed', { predicate: failed => failed === request });
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(two);
  await aborted;
  release();
  await page.getByRole('link', { name: 'Activity' }).click();
  await expect(page.getByText('No requests match this window and filters.')).toBeVisible();
  await expect(page.getByText('Preparing complete CSV…')).toHaveCount(0);
  expect(downloads).toEqual([]);
});

for (const status of [403, 504]) {
  test(`CSV ${status} shows a safe error without downloading a file`, async ({ page }) => {
    await mockUsage(page);
    const downloads: string[] = [];
    page.on('download', download => downloads.push(download.suggestedFilename()));
    await page.route('**/usage/export?*', route => route.fulfill({ status, contentType: 'application/problem+json', body: '{}' }));
    await page.goto(`/organizations/${one}/activity`);
    const button = page.getByRole('button', { name: 'Export activity CSV' });
    await button.click();
    await expect(page.getByRole('alert').filter({ hasText: status === 403 ? 'not authorized for this scope' : 'could not be downloaded' })).toBeVisible();
    await expect(button).toBeEnabled();
    expect(downloads).toEqual([]);
  });
}
