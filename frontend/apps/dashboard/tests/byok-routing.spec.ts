import { test, expect, type Page } from '@playwright/test';

const org = '11111111-1111-4111-8111-111111111111';
const other = '22222222-2222-4222-8222-222222222222';
const project = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const second = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const id = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const now = '2026-10-15T00:00:00Z';
const secret = 'synthetic-private-secret-never-cache-1234';
const provider = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const codes = ['openai', 'anthropic', 'google', 'deepseek'];

async function fixture(page: Page) {
  const state = { keys: [{ id, organizationId: org, providerId: provider, providerCode: 'openai', name: 'Primary credential', maskedKey: '••••1234', status: 'Active', createdAt: now, updatedAt: now,
    lastTestStatus: null as string | null, lastTestedAt: null as string | null, projectIds: [project], allowedModels: null as string[] | null, spendLimitMicroUsd: null as string | null, externalSpentMicroUsd: '0', externalReservedMicroUsd: '0' }],
    settings: { routing: 'price' as string | null, environment: 'Production', allowedModels: null as string[] | null, allowedProviders: null as string[] | null },
    testStatus: 'Valid', denied: false, writeStatus: 0, writes: [] as { method: string; path: string; body: Record<string, unknown> | null }[] };
  await page.route('**/management/v1/**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (method !== 'GET') {
      state.writes.push({ method, path, body: request.postData() ? request.postDataJSON() : null });
      if (state.writeStatus) return route.fulfill({ status: state.writeStatus });
    }
    if (path.endsWith('/auth/session')) return json({ accountId: 'actor', email: 'owner@example.invalid' });
    if (path.endsWith('/organizations')) return json([{ id: org, name: 'Alpha' }, { id: other, name: 'Beta' }]);
    if (path.endsWith('/projects')) return json([{ id: project, organizationId: org, name: 'Production', status: 0, settingsJson: '{}' }, { id: second, organizationId: org, name: 'Staging', status: 0, settingsJson: '{}' }]);
    if (path.endsWith('/billing/wallet')) return json({ availableBalanceMicroUsd: '0' });
    if (path.includes('/provider-keys') && state.denied) return route.fulfill({ status: 403 });
    if (path.endsWith('/provider-keys/providers')) return json(codes.map((code, index) => ({ id: index ? `provider-${code}` : provider, code, name: code })));
    if (path.endsWith('/provider-keys')) {
      if (method === 'POST') { const body = request.postDataJSON(); state.keys.push({ ...state.keys[0], id: 'created', name: body.name, projectIds: [], maskedKey: '••••1234' }); return json(state.keys.at(-1), 201); }
      return json(path.includes(other) ? [] : state.keys);
    }
    if (path.endsWith('/test')) { state.keys[0].lastTestStatus = state.testStatus; state.keys[0].lastTestedAt = now; return json({ status: state.testStatus }); }
    if (path.endsWith('/restrictions')) { Object.assign(state.keys[0], request.postDataJSON()); return json(state.keys[0]); }
    if (path.includes('/provider-keys/') && path.includes('/projects/')) {
      const target = path.split('/').at(-1)!;
      state.keys[0].projectIds = method === 'PUT' ? [...state.keys[0].projectIds, target] : state.keys[0].projectIds.filter(value => value !== target);
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith('/disable')) { state.keys[0].status = 'Disabled'; return route.fulfill({ status: 204 }); }
    if (path.endsWith('/provider-keys/' + id)) {
      if (method === 'DELETE') { state.keys = []; return route.fulfill({ status: 204 }); }
      const body = request.postDataJSON(); state.keys[0].name = body.name; state.keys[0].lastTestStatus = null; return json(state.keys[0]);
    }
    if (path.endsWith('/settings')) { if (method === 'PUT') Object.assign(state.settings, request.postDataJSON()); return json(state.settings); }
    if (path.endsWith('/catalog/models')) return json({ dataAsOf: now, models: ['gpt-test', 'alternative'].map(model => ({ id: model, displayName: model, providers: (model === 'alternative' ? ['anthropic'] : codes).map((code, index) => ({ mappingId: String(index), provider: code, providerName: code })) })) });
    return route.fulfill({ status: 404 });
  });
  return state;
}

async function configure(page: Page) { await page.getByRole('button', { name: 'Configure Primary credential' }).click(); }
async function preview(page: Page) { await page.goto(`/organizations/${org}/routing?project=${project}`); await page.getByLabel('Primary model', { exact: true }).selectOption('gpt-test'); }

test('create and rotate never reveal or store plaintext; all four provider options are catalog identities', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/provider-keys`);
  await page.getByRole('button', { name: 'Add provider key' }).click();
  await expect(page.getByLabel('Provider', { exact: true }).locator('option')).toHaveCount(5);
  await page.getByLabel('Provider', { exact: true }).selectOption(provider);
  await page.getByLabel('Credential name').fill('New provider'); await page.getByLabel('Provider secret', { exact: true }).fill(secret);
  await expect(page.getByLabel('Provider secret', { exact: true })).toHaveAttribute('type', 'password');
  await page.getByRole('button', { name: 'Save credential' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.writes[0].body).toEqual({ providerId: provider, name: 'New provider', secret });
  await expect(page.locator('body')).not.toContainText(secret);
  await page.getByRole('button', { name: 'Edit Primary credential' }).click();
  await page.getByLabel('Replacement provider secret (optional)').fill(secret + '-rotated');
  await page.getByRole('button', { name: 'Save credential' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.writes.at(-1)?.method).toBe('PATCH');
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret);
  expect(page.url()).not.toContain(secret);
  await page.getByRole('button', { name: 'Add provider key' }).click(); await expect(page.getByLabel('Provider secret', { exact: true })).toHaveValue('');
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('exact external cap, model restrictions and independent project grants use protected commands', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/provider-keys`); await configure(page);
  await page.getByLabel('Model access').selectOption('selected'); await page.getByLabel('Allowed canonical models').fill('gpt-test');
  await page.getByLabel('Lifetime external cap (USD)').fill('9007199254.740993'); await page.getByRole('button', { name: 'Save restrictions' }).click();
  await expect(page.getByText(/Restrictions saved/)).toBeVisible();
  expect(state.writes.at(-1)?.body).toEqual({ allowedModels: ['gpt-test'], spendLimitMicroUsd: '9007199254740993' });
  await page.getByLabel('Project to grant').selectOption(second); await page.getByRole('button', { name: 'Grant project access', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused(); await page.getByRole('button', { name: 'Confirm grant change' }).click();
  await expect(page.getByRole('button', { name: 'Remove grant Staging' })).toBeVisible();
  expect(state.writes.at(-1)?.path).toBe(`/management/v1/organizations/${org}/provider-keys/${id}/projects/${second}`);
  await page.getByRole('button', { name: 'Remove grant Staging' }).click(); await page.getByRole('button', { name: 'Confirm grant change' }).click();
  await expect(page.getByRole('button', { name: 'Remove grant Staging' })).toHaveCount(0); expect(state.writes.at(-1)?.method).toBe('DELETE');
  await page.getByLabel('Lifetime external cap (USD)').fill('9223372036854.775808'); await page.getByRole('button', { name: 'Save restrictions' }).click();
  await expect(page.getByRole('alert')).toContainText('Int64'); expect(state.writes).toHaveLength(3);
});

for (const status of ['Valid', 'Invalid', 'Unavailable']) test(`verification explains ${status} without claiming live health`, async ({ page }) => {
  const state = await fixture(page); state.testStatus = status;
  await page.goto(`/organizations/${org}/provider-keys`); await configure(page); await page.getByRole('button', { name: 'Test credential' }).click();
  await expect(page.getByRole('status').filter({ hasText: status })).toBeVisible();
  await expect(page.getByText('Verification does not reset inference health.', { exact: false })).toBeVisible();
  expect(state.writes).toHaveLength(1);
});

test('disable and crypto-shred require confirmation and never expose a re-enable or reveal action', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/provider-keys`);
  await page.getByRole('button', { name: 'Disable Primary credential' }).click(); await page.getByRole('button', { name: 'Confirm disable' }).click();
  await expect(page.getByRole('button', { name: 'Edit Primary credential' })).toBeDisabled();
  await configure(page); await expect(page.getByRole('button', { name: 'Test credential' })).toBeDisabled();
  await expect(page.getByRole('button', { name: /reveal|re-enable/i })).toHaveCount(0);
  await page.getByRole('button', { name: 'Delete Primary credential' }).click(); await page.getByRole('button', { name: 'Confirm delete' }).click();
  await expect(page.getByText(/No provider keys/)).toBeVisible(); expect(state.keys).toHaveLength(0);
});

test('managed inheritance, override, BYOK-first and explicit Hybrid consent produce accurate request examples', async ({ page }) => {
  await fixture(page); await preview(page); const json = page.getByLabel('Chat request JSON');
  await expect(json).not.toContainText('provider_key_id'); await expect(json).not.toContainText('routing');
  await expect(page.getByText('Effective strategy:', { exact: false })).toContainText('price');
  await page.getByLabel('Request routing override').selectOption('priority'); await expect(json).toContainText('"routing": "priority"');
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('byok'); await expect(page.getByRole('alert')).toContainText('Select a currently readable');
  await page.getByLabel('Request provider key').selectOption(id); await expect(json).toContainText('"allow_managed_fallback": false');
  await expect(page.getByText('Managed fallback', { exact: true })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('hybrid'); await expect(page.getByRole('alert')).toContainText('explicit managed-fallback consent');
  await page.getByRole('checkbox').check(); await expect(json).toContainText('"allow_managed_fallback": true');
  await expect(page.locator('.route-steps li').first()).toContainText('BYOK primary'); await expect(page.locator('.route-steps li').nth(1)).toContainText('Managed fallback');
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('managed'); await expect(json).not.toContainText('provider_key_id');
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('hybrid'); await expect(page.getByRole('checkbox')).not.toBeChecked();
});

test('request-only fallback is explicit and cannot bypass model, provider, grant or spend restrictions', async ({ page }) => {
  const state = await fixture(page); state.keys[0].projectIds = []; await preview(page);
  await page.getByText('Optional request-only model fallback', { exact: true }).click(); await page.getByLabel('First fallback model').selectOption('alternative');
  await page.getByLabel('Provider pin').selectOption('openai');
  await expect(page.getByLabel('Chat request JSON')).toContainText('openai/gpt-test');
  await expect(page.getByText(/pin constrains the primary model only/)).toBeVisible();
  await expect(page.getByLabel('Chat request JSON')).toContainText('fallback_models');
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('byok'); await page.getByLabel('Request provider key').selectOption(id);
  await expect(page.getByRole('alert')).toContainText('no explicit grant'); await expect(page.getByRole('alert')).toContainText('Cross-model fallback');
  state.keys[0].projectIds = [project]; state.keys[0].spendLimitMicroUsd = '0'; await page.getByRole('button', { name: 'Refresh preview data' }).click();
  await expect(page.getByRole('alert')).toContainText('reached the lifetime cap');
  state.keys[0].spendLimitMicroUsd = null; state.keys[0].allowedModels = ['alternative']; state.settings.allowedProviders = ['anthropic'];
  // New authoritative settings reset request-only drafts/consent. Re-enter only
  // after a fresh scoped read, not racing independent refetch completions.
  await page.reload(); await page.getByLabel('Primary model', { exact: true }).selectOption('gpt-test'); await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('byok'); await page.getByLabel('Request provider key').selectOption(id);
  await expect(page.getByRole('alert')).toContainText('does not allow'); await expect(page.getByRole('alert')).toContainText('project provider restrictions');
});

test('read denial masks cached controls and credential metadata; failed writes are not retried', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/provider-keys`);
  state.writeStatus = 409; await page.getByRole('button', { name: 'Edit Primary credential' }).click();
  await page.getByLabel('Replacement provider secret (optional)').fill(secret); await page.getByRole('button', { name: 'Save credential' }).click();
  await expect(page.getByRole('alert')).toContainText('credential changed'); await expect(page.getByLabel('Replacement provider secret (optional)')).toHaveValue(''); expect(state.writes).toHaveLength(1);
  await page.keyboard.press('Escape'); state.denied = true; await page.getByRole('button', { name: 'Refresh keys' }).click();
  await expect(page.getByRole('alert')).toContainText('Permission denied'); await expect(page.getByRole('button', { name: 'Add provider key' })).toHaveCount(0); await expect(page.getByText('Primary credential', { exact: true })).toHaveCount(0);
});

test('tenant switch discards drafts and late command UI; returning never reopens a secret form', async ({ page }) => {
  await fixture(page); let release!: () => void; let started!: () => void; const pending = new Promise<void>(done => { release = done; }); const received = new Promise<void>(done => { started = done; });
  await page.route(`**/organizations/${org}/provider-keys`, async route => {
    if (route.request().method() !== 'POST') return route.fallback(); started(); await pending;
    await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'late' }) });
  });
  await page.goto(`/organizations/${org}/provider-keys`); await page.getByRole('button', { name: 'Add provider key' }).click(); await page.getByLabel('Provider', { exact: true }).selectOption(provider);
  await page.getByLabel('Credential name').fill('Late key'); await page.getByLabel('Provider secret', { exact: true }).fill(secret); await page.getByRole('button', { name: 'Save credential' }).click(); await received;
  // Native modal blocks background controls: Escape first, then switch tenant.
  await page.keyboard.press('Escape'); await page.getByLabel('Organization', { exact: true }).selectOption(other); release();
  await page.getByRole('link', { name: 'Provider keys', exact: true }).click(); await expect(page.getByText(/No provider keys/)).toBeVisible(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByLabel('Organization', { exact: true }).selectOption(org); await page.getByRole('link', { name: 'Provider keys', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0); expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret);
});

test('responsive keyboard UI keeps configuration readable and captures secret-free review evidence', async ({ page }, testInfo) => {
  await fixture(page); await preview(page); await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.screenshot({ path: testInfo.outputPath('routing-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('routing-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.goto(`/organizations/${org}/provider-keys`); await configure(page);
  await page.screenshot({ path: testInfo.outputPath('provider-keys.png'), fullPage: true });
});

for (const [status, message] of [[400, 'API rejected'], [403, 'Permission denied'], [429, 'Too many requests'], [503, 'could not confirm']] as const)
  test(`credential write ${status} is actionable, clears input and never retries`, async ({ page }) => {
    const state = await fixture(page); state.writeStatus = status;
    await page.goto(`/organizations/${org}/provider-keys`); await page.getByRole('button', { name: 'Edit Primary credential' }).click();
    await page.getByLabel('Replacement provider secret (optional)').fill(secret); await page.getByRole('button', { name: 'Save credential' }).click();
    await expect(page.getByRole('alert')).toContainText(message); await expect(page.getByLabel('Replacement provider secret (optional)')).toHaveValue(''); expect(state.writes).toHaveLength(1);
  });

test('lost credential write explains uncertain outcome without leaking plaintext or retrying', async ({ page }) => {
  await fixture(page); let writes = 0;
  await page.route(`**/provider-keys/${id}`, async route => { writes++; await route.abort('connectionfailed'); });
  await page.goto(`/organizations/${org}/provider-keys`); await page.getByRole('button', { name: 'Edit Primary credential' }).click();
  await page.getByLabel('Replacement provider secret (optional)').fill(secret); await page.getByRole('button', { name: 'Save credential' }).click();
  await expect(page.getByRole('alert')).toContainText('may already have committed'); expect(writes).toBe(1);
  await expect(page.getByLabel('Replacement provider secret (optional)')).toHaveValue(''); await expect(page.locator('body')).not.toContainText(secret);
});

test('project change resets request-only mode, override and Hybrid consent', async ({ page }) => {
  await fixture(page); await preview(page);
  await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('hybrid'); await page.getByLabel('Request provider key').selectOption(id);
  await page.getByRole('checkbox').check(); await page.getByLabel('Request routing override').selectOption('latency');
  await page.getByLabel('Project', { exact: true }).selectOption(second);
  await expect(page.getByRole('combobox', { name: 'Execution mode', exact: true })).toHaveValue('managed'); await expect(page.getByLabel('Request routing override')).toHaveValue('');
  await expect(page.getByRole('checkbox')).toHaveCount(0);
});
