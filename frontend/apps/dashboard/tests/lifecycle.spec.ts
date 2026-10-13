import { test, expect, type Page } from '@playwright/test';

const org = '11111111-1111-1111-1111-111111111111';
const otherOrg = '22222222-2222-2222-2222-222222222222';
const project = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const otherProject = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const keyId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const secret = 'uzllm_synthetic_lifecycle_show_once';
const key = { id: keyId, projectId: project, name: 'Production key', prefix: 'uzllm_public', status: 0,
  createdAt: '2026-09-01T00:00:00Z', expiresAt: null, permissions: {} };

async function fixture(page: Page) {
  const state = {
    settings: { routing: null as string | null, environment: 'Staging', allowedModels: null as string[] | null, allowedProviders: null as string[] | null },
    keys: [{ ...key }], projects: [{ id: project, organizationId: org, name: 'Gateway production', status: 0, settingsJson: '{}' }],
    limits: { requestsPerMinute: null as number | null, concurrency: null as number | null },
    budgets: [] as Record<string, unknown>[], writes: [] as { path: string; body: Record<string, unknown> | null; method: string }[], denied: false,
  };
  await page.route('**/management/v1/**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (request.method() !== 'GET') {
      state.writes.push({ path, body: request.postData() ? request.postDataJSON() : null, method: request.method() });
      if (state.denied) return route.fulfill({ status: 403 });
    }
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.invalid' });
    if (path.endsWith('/organizations')) return json([{ id: org, name: 'Alpha' }, { id: otherOrg, name: 'Beta' }]);
    if (path.endsWith(`/${otherOrg}/projects`)) return json([{ id: otherProject, organizationId: otherOrg, name: 'Beta project', status: 0 }]);
    if (path.endsWith(`/${org}/projects`)) {
      if (request.method() === 'POST') state.projects.push({ id: 'new-project', organizationId: org, name: request.postDataJSON().name, status: 0, settingsJson: '{}' });
      return json(request.method() === 'POST' ? state.projects.at(-1) : state.projects, request.method() === 'POST' ? 201 : 200);
    }
    if (path.endsWith('/archive')) { state.projects[0].status = 1; return route.fulfill({ status: 204 }); }
    if (path.endsWith('/settings')) {
      if (request.method() === 'PUT') { state.settings = request.postDataJSON(); state.projects[0].settingsJson = JSON.stringify({ defaults: state.settings }); }
      return json(state.settings);
    }
    if (path.endsWith('/limits')) {
      if (request.method() === 'PUT') state.limits = request.postDataJSON();
      return json({ configured: state.limits, effective: { requestsPerMinute: state.limits.requestsPerMinute ?? 60, concurrency: state.limits.concurrency ?? 8 },
        ceiling: { requestsPerMinute: 60, concurrency: 8 } });
    }
    if (path.endsWith('/budgets')) return json(state.budgets);
    if (path.includes('/budgets/')) {
      const body = request.postDataJSON(); const period = path.split('/').at(-1)!;
      const budget = { id: 'budget-' + period, projectId: project, apiKeyId: body.apiKeyId, period, limitMicroUsd: body.limitMicroUsd,
        capturedMicroUsd: 100, reservedMicroUsd: 200, windowStart: '2026-10-01T00:00:00Z', windowEnd: period === 'Lifetime' ? null : '2026-11-01T00:00:00Z' };
      state.budgets = [...state.budgets.filter(value => value.period !== period || value.apiKeyId !== body.apiKeyId), budget]; return json(budget);
    }
    if (path.endsWith(`/${otherProject}/api-keys`)) return json([]);
    if (path.endsWith('/api-keys') && request.method() === 'POST') {
      const created = { ...key, id: 'new-key', name: request.postDataJSON().name, expiresAt: request.postDataJSON().expiresAt };
      state.keys.push(created); return json({ apiKey: created, secret }, 201);
    }
    if (path.endsWith('/api-keys')) return json(state.keys);
    if (path.endsWith('/permissions')) { state.keys[0].permissions = request.postDataJSON(); return route.fulfill({ status: 204 }); }
    if (path.endsWith('/rotate')) return json({ apiKey: { ...state.keys[0], prefix: 'uzllm_rotated' }, secret: secret + '_rotated' });
    if (path.endsWith('/' + keyId) && request.method() === 'PATCH') { state.keys[0].status = request.postDataJSON().status; return route.fulfill({ status: 204 }); }
    if (path.endsWith('/billing/wallet')) return json({ availableBalanceMicroUsd: '1000000' });
    if (path.endsWith('/usage/summary')) return json({ requestCount: 4, inputTokens: 8, outputTokens: 4, netChargedMicroUsd: '11', pendingCount: 1,
      dataAsOf: '2026-10-13T00:00:00Z', aggregation: { pendingBucketCount: 2 } });
    return route.fulfill({ status: 404 });
  });
  return state;
}

test('project defaults preserve null vs deny-all, limits inherit ceilings, budgets use exact micro USD', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/projects?project=${project}`);
  await page.getByLabel('Default routing').selectOption('price');
  await page.getByLabel('Environment tag').fill('Production');
  await page.getByRole('combobox', { name: 'Allowed models', exact: true }).selectOption('selected');
  await page.getByLabel('Allowed models codes').fill('gpt-test\nGPT-TEST');
  await page.getByRole('combobox', { name: 'Allowed providers', exact: true }).selectOption('none');
  await page.getByRole('button', { name: 'Save project settings' }).click();
  await expect(page.getByRole('status')).toContainText('Project settings saved');
  expect(state.settings).toEqual({ routing: 'price', environment: 'Production', allowedModels: ['gpt-test'], allowedProviders: [] });
  await expect(page.locator('.environment')).toHaveText('Production');
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  await page.getByLabel('Requests per minute').fill('15');
  await page.getByLabel('Concurrent requests').fill('2');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByText('Effective 15 · ceiling 60')).toBeVisible();
  await page.getByLabel('Requests per minute').fill('');
  await page.getByLabel('Concurrent requests').fill('');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByText('Effective 60 · ceiling 60')).toBeVisible();
  expect(state.limits).toEqual({ requestsPerMinute: null, concurrency: null });
  await page.getByLabel('Budget amount (USD)').fill('25.000001');
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Budget saved' })).toBeVisible();
  expect(state.writes.at(-1)?.body).toEqual({ limitMicroUsd: 25000001, apiKeyId: null });
  await expect(page.getByText('Captured $0.000100')).toBeVisible();
  await page.getByLabel('Budget amount (USD)').fill('9007199254.740992');
  const count = state.writes.length;
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByRole('alert')).toContainText('exact browser editing precision');
  expect(state.writes).toHaveLength(count);
});

test('key permissions, rotation and disable/enable use scoped APIs; modal keyboard and clipboard failure are safe', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/keys?project=${project}`);
  await page.getByRole('button', { name: 'Configure Production key' }).click();
  await page.getByRole('combobox', { name: 'Allowed models', exact: true }).selectOption('none');
  await page.getByLabel('Denied provider codes').fill('Anthropic');
  await page.getByRole('button', { name: 'Save key permissions' }).click();
  await expect(page.getByRole('status')).toContainText('Key permissions saved');
  expect(state.keys[0].permissions).toEqual({ allowedModels: [], allowedProviders: null, deniedModels: [], deniedProviders: ['anthropic'] });
  await page.getByRole('button', { name: 'Rotate Production key' }).click();
  await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
  await page.keyboard.press('Escape');
  expect(state.writes.filter(value => value.path.endsWith('/rotate'))).toHaveLength(0);
  await page.getByRole('button', { name: 'Rotate Production key' }).click();
  await page.getByRole('button', { name: 'Confirm rotate' }).click();
  await expect(page.getByRole('dialog')).toContainText(secret + '_rotated');
  await page.evaluate(() => Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true }));
  await page.getByRole('button', { name: 'Copy key' }).click();
  await expect(page.getByRole('alert')).toContainText('Clipboard unavailable');
  await page.keyboard.press('Escape');
  await expect(page.getByText(secret + '_rotated')).toHaveCount(0);
  await page.getByRole('button', { name: 'Disable Production key' }).click();
  await page.getByRole('button', { name: 'Confirm disable' }).click();
  await expect(page.getByRole('button', { name: 'Enable Production key' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Rotate Production key' })).toBeDisabled();
  await page.getByRole('button', { name: 'Enable Production key' }).click();
  await page.getByRole('button', { name: 'Confirm enable' }).click();
  await expect(page.getByRole('button', { name: 'Disable Production key' })).toBeVisible();
  expect(state.writes.filter(value => value.method === 'PATCH').map(value => value.body)).toEqual([{ status: 1 }, { status: 0 }]);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret);
});

test('create key validates future UTC expiry and never repeats a pending mutation', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/keys`);
  await page.getByLabel('Key name').fill('Expiring key');
  await page.getByLabel('Expiry (UTC, optional)').fill('2020-01-01T12:30');
  await page.getByRole('button', { name: 'Create key', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('future UTC');
  expect(state.writes).toHaveLength(0);
  await page.getByLabel('Expiry (UTC, optional)').fill('2099-01-01T12:30');
  await page.getByRole('button', { name: 'Create key', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText(secret);
  expect(state.writes[0].body).toEqual({ name: 'Expiring key', expiresAt: '2099-01-01T12:30:00.000Z' });
  await page.getByRole('button', { name: 'I saved it' }).click();
  await page.reload();
  await expect(page.getByText(secret)).toHaveCount(0);
  expect(state.writes).toHaveLength(1);
});

test('a late issued secret cannot appear after tenant switching', async ({ page }) => {
  await fixture(page);
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/projects/${project}/api-keys`, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await pending; await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ apiKey: key, secret }) });
  });
  await page.goto(`/organizations/${org}/keys`);
  await page.getByLabel('Key name').fill('Delayed key');
  const sent = page.waitForRequest(request => request.method() === 'POST' && request.url().endsWith('/api-keys'));
  await page.getByRole('button', { name: 'Create key', exact: true }).click(); await sent;
  await page.getByRole('combobox', { name: 'Organization', exact: true }).selectOption(otherOrg);
  await page.getByRole('link', { name: 'API keys', exact: true }).click();
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/api-keys'));
  release(); await response;
  await expect(page.getByText('No API keys for this project.')).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText(secret)).toHaveCount(0);
  await expect(page.getByLabel('Key name')).toHaveValue('');
});

test('project switching clears drafts and ignores the previous project mutation result', async ({ page }) => {
  const state = await fixture(page);
  state.projects.push({ id: otherProject, organizationId: org, name: 'Second project', status: 0, settingsJson: '{}' });
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/projects/${project}/api-keys`, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await pending; await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ apiKey: key, secret }) });
  });
  await page.goto(`/organizations/${org}/keys?project=${project}`);
  await page.getByLabel('Key name').fill('Old project draft');
  const sent = page.waitForRequest(value => value.method() === 'POST' && value.url().endsWith('/api-keys'));
  await page.getByRole('button', { name: 'Create key', exact: true }).click(); await sent;
  await page.getByRole('combobox', { name: 'Project', exact: true }).selectOption(otherProject);
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/api-keys'));
  release(); await response;
  await expect(page.getByText('No API keys for this project.')).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByLabel('Key name')).toHaveValue('');
});

test('unsafe budget responses are not rounded and conflicts never show a false success', async ({ page }) => {
  const state = await fixture(page);
  state.budgets.push({ id: 'large', apiKeyId: null, projectId: project, period: 'Monthly', limitMicroUsd: Number.MAX_SAFE_INTEGER + 1, capturedMicroUsd: 0, reservedMicroUsd: 0 });
  await page.goto(`/organizations/${org}/projects`);
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('no rounded value');
  await expect(page.getByRole('button', { name: 'Save spend budget' })).toHaveCount(0);
  await page.goto(`/organizations/${org}/keys`);
  await page.route('**/rotate', route => route.fulfill({ status: 409 }));
  await page.getByRole('button', { name: 'Rotate Production key' }).click();
  await page.getByRole('button', { name: 'Confirm rotate' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('no automatic retry');
  await expect(page.getByText(secret)).toHaveCount(0);
});

test('a save completing after its editor closes invalidates only scoped metadata for the next read', async ({ page }) => {
  const state = await fixture(page);
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/settings', async route => {
    if (route.request().method() !== 'PUT') return route.fallback();
    await pending; state.settings = route.request().postDataJSON();
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state.settings) });
  });
  await page.goto(`/organizations/${org}/projects`);
  await page.getByLabel('Environment tag').fill('Production');
  const sent = page.waitForRequest(value => value.method() === 'PUT' && value.url().endsWith('/settings'));
  await page.getByRole('button', { name: 'Save project settings' }).click(); await sent;
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  const response = page.waitForResponse(value => value.request().method() === 'PUT' && value.url().endsWith('/settings'));
  release(); await response;
  await page.getByRole('button', { name: 'Settings & restrictions', exact: true }).click();
  await expect(page.getByLabel('Environment tag')).toHaveValue('Production');
  await expect(page.getByText('Project settings saved.')).toHaveCount(0);
});

test('permission and read failures do not report success or an empty list; archive requires confirmation', async ({ page }) => {
  const state = await fixture(page); state.denied = true;
  await page.goto(`/organizations/${org}/projects`);
  await page.getByLabel('Environment tag').fill('Development');
  await page.getByRole('button', { name: 'Save project settings' }).click();
  await expect(page.getByRole('alert')).toContainText('Permission denied');
  await expect(page.getByText('Project settings saved')).toHaveCount(0);
  await page.getByRole('button', { name: 'Archive Gateway production' }).click();
  await page.getByRole('button', { name: 'Confirm archive' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Permission denied');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  state.denied = false;
  await page.getByRole('button', { name: 'Archive Gateway production' }).click();
  await page.getByRole('button', { name: 'Confirm archive' }).click();
  await expect(page.getByText('Archived', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save project settings' })).toHaveCount(0);
  await page.route('**/api-keys', route => route.fulfill({ status: 403 }));
  await page.goto(`/organizations/${otherOrg}/keys`);
  await expect(page.getByRole('alert')).toContainText('Permission denied');
  await expect(page.getByText('No API keys for this project.')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Create key', exact: true })).toBeDisabled();
});

test('key read denial hides previously loaded controls instead of keeping an authorized-looking stale editor', async ({ page }) => {
  await fixture(page);
  await page.goto(`/organizations/${org}/keys`);
  await page.getByRole('button', { name: 'Configure Production key' }).click();
  await expect(page.getByRole('form', { name: 'Key permissions' })).toBeVisible();
  await page.route('**/api-keys', route => route.request().method() === 'GET' ? route.fulfill({ status: 403 }) : route.fallback());
  await page.getByRole('button', { name: 'Disable Production key' }).click();
  await page.getByRole('button', { name: 'Confirm disable' }).click();
  await expect(page.getByRole('alert')).toContainText('Permission denied');
  await expect(page.getByRole('form', { name: 'Key permissions' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Create key', exact: true })).toBeDisabled();
});

test('key-scoped limits, budgets and usage carry the selected key and refuse invalid money/limits', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/keys`);
  await page.getByRole('button', { name: 'Configure Production key' }).click();
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  await page.getByLabel('Requests per minute').fill('61');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByRole('alert')).toContainText('operator ceiling');
  expect(state.writes).toHaveLength(0);
  await page.getByLabel('Requests per minute').fill('10');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByText('Effective 10 · ceiling 60')).toBeVisible();
  expect(state.writes[0].path).toBe(`/management/v1/api-keys/${keyId}/limits`);
  await page.getByLabel('Budget period').selectOption('Daily');
  for (const invalid of ['-1', '1e3', '0.0000001', '1,000']) {
    await page.getByLabel('Budget amount (USD)').fill(invalid);
    await page.getByRole('button', { name: 'Save spend budget' }).click();
    await expect(page.getByRole('alert')).toContainText('non-negative USD');
  }
  expect(state.writes).toHaveLength(1);
  await page.getByLabel('Budget amount (USD)').fill('0');
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByText('$0.000000', { exact: true })).toBeVisible();
  expect(state.writes.at(-1)?.body).toEqual({ limitMicroUsd: 0, apiKeyId: keyId });
  await page.getByRole('button', { name: 'Key usage', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Key usage · last 30 days' })).toBeVisible();
  await expect(page.getByText('$0.000011', { exact: true })).toBeVisible();
  await expect(page.getByText(/1 requests unsettled/)).toBeVisible();
});

test('lifecycle pages fit mobile and desktop and retain visible keyboard focus', async ({ page }, testInfo) => {
  await fixture(page);
  await page.goto(`/organizations/${org}/projects`);
  await expect(page.getByLabel('Environment tag')).toHaveValue('Staging');
  await page.getByRole('button', { name: 'Save project settings' }).focus();
  await expect(page.getByRole('button', { name: 'Save project settings' })).toBeFocused();
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.screenshot({ path: testInfo.outputPath('projects-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('projects-mobile.png'), fullPage: true });
  await page.goto(`/organizations/${org}/keys`);
  await expect(page.getByText('Production key', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('keys-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.screenshot({ path: testInfo.outputPath('keys-desktop.png'), fullPage: true });
});
