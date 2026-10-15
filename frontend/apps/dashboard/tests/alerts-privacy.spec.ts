import { test, expect, type Page } from '@playwright/test';

const org = '11111111-1111-4111-8111-111111111111'; const other = '22222222-2222-4222-8222-222222222222';
const project = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; const second = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const requestId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'; const destination = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const rule = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'; const secret = '1a'.repeat(32);
const privateBody = JSON.stringify({ private: 'synthetic-private-body-only', text: '<img src=x onerror="document.body.dataset.payloadExecuted=1">' });

async function fixture(page: Page) {
  const state = { role: 'Owner', denied: false, writeStatus: 0, payloadStatus: 0, expiry: new Date(Date.now() + 86400000).toISOString(), enabled: false, minutes: 1440,
    rules: [] as Record<string, unknown>[], destinations: [] as Record<string, unknown>[], reads: [] as string[], writes: [] as { path: string; method: string; body: Record<string, unknown> | null }[] };
  await page.route('**/management/v1/**', async route => {
    const req = route.request(); const path = new URL(req.url()).pathname; const method = req.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', headers: { 'Cache-Control': 'no-store' }, body: JSON.stringify(body) });
    if (method !== 'GET') { state.writes.push({ path, method, body: req.postData() ? req.postDataJSON() : null }); if (state.writeStatus) return route.fulfill({ status: state.writeStatus }); }
    else state.reads.push(path);
    if (path.endsWith('/auth/session')) return json({ accountId: 'actor', email: 'owner@example.invalid' });
    if (path.endsWith('/organizations')) return json([{ id: org, name: 'Alpha' }, { id: other, name: 'Beta' }]);
    if (path.endsWith('/projects')) return json([{ id: project, organizationId: org, name: 'Production', status: 0, settingsJson: '{}' }, { id: second, organizationId: org, name: 'Staging', status: 0, settingsJson: '{}' }]);
    if (path.endsWith('/billing/wallet')) return json({ availableBalanceMicroUsd: '0' });
    if (path.endsWith('/team/members')) return state.role === 'Owner' || state.role === 'Admin' ? json([{ accountId: 'actor', role: state.role, status: 'Active' }]) : route.fulfill({ status: 403 });
    if (path.includes('/alerts') && (state.denied || state.role === 'Developer' || state.role === 'ReadOnly')) return route.fulfill({ status: 403 });
    if (path.endsWith('/alerts/rules')) {
      if (method === 'POST') { const value = { id: state.rules.length ? crypto.randomUUID() : rule, organizationId: org, enabled: true, lastTriggeredAt: null, ...req.postDataJSON() }; state.rules.push(value); return json(value, 201); }
      return json(path.includes(other) ? [] : state.rules);
    }
    if (path.endsWith('/alerts/rules/' + rule)) { state.rules[0].enabled = req.postDataJSON().enabled; return route.fulfill({ status: 204 }); }
    if (path.endsWith('/alerts/events')) return json(path.includes(other) ? [] : ['Pending', 'Delivered', 'Suppressed'].map((status, index) => ({ id: 'event-' + index, ruleId: rule, type: 'LowBalance', projectId: null, observedValue: 1234567, status, triggeredAt: new Date().toISOString(), deliveredAt: status === 'Delivered' ? new Date().toISOString() : null })));
    if (path.endsWith('/alerts/destinations')) return json(path.includes(other) ? [] : state.destinations);
    if (path.endsWith('/alerts/telegram/link')) return json({ deepLink: 'https://t.me/UzllmTestBot?start=synthetic-private-token', expiresAt: state.expiry });
    if (path.endsWith('/alerts/webhook/destination')) { const value = { id: destination, type: 'Webhook', status: 'Active', endpointUrl: req.postDataJSON().endpointUrl, verifiedAt: new Date().toISOString() }; state.destinations.push(value); return json({ ...value, signingSecret: secret }, 201); }
    if (path.includes('/alerts/destinations/')) { state.destinations[0].status = 'Disabled'; return route.fulfill({ status: 204 }); }
    if (path.endsWith('/budgets')) return json([{ id: 'project-policy', period: 'Monthly', apiKeyId: null }, { id: 'key-policy', period: 'Daily', apiKeyId: 'key' }]);
    if (path.includes('/payload-retention')) {
      if (state.role !== 'Owner' || state.denied) return route.fulfill({ status: 403 });
      if (path.includes('/requests/')) {
        if (state.payloadStatus || !state.enabled) return route.fulfill({ status: state.payloadStatus || 404 });
        return json({ requestId, projectId: project, request: privateBody, response: null, expiresAt: state.expiry });
      }
      if (method === 'PUT') { state.enabled = req.postDataJSON().enabled; state.minutes = req.postDataJSON().retentionMinutes; }
      return json({ enabled: state.enabled, retentionMinutes: state.minutes });
    }
    return route.fulfill({ status: 404 });
  });
  return state;
}
function activeDestination() { return { id: destination, type: 'Webhook', status: 'Active', endpointUrl: 'https://alerts.example.com/uzllm', verifiedAt: new Date().toISOString() }; }

test('owner creates exact low-balance rule, sees delivery evidence and confirms pause/disable', async ({ page }) => {
  const state = await fixture(page); state.destinations.push(activeDestination()); await page.goto(`/organizations/${org}/alerts`);
  await expect(page.getByText('Delivered', { exact: true })).toBeVisible(); await expect(page.getByText('Pending', { exact: true })).toBeVisible(); await expect(page.getByText('Suppressed', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Create alert rule' }).click(); await page.getByLabel('Available balance threshold (USD)').fill('1.234567'); await page.getByLabel('Delivery destination').selectOption(destination);
  await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.writes[0].body).toEqual({ type: 'LowBalance', threshold: 1234567, destinationId: destination, projectId: null, budgetPolicyId: null });
  await page.getByRole('button', { name: 'Pause rule' + ' ' + rule.slice(0, 8) }).click(); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Confirm rule change' }).click(); await expect(page.getByRole('button', { name: 'Enable rule' + ' ' + rule.slice(0, 8) })).toBeVisible();
  await page.getByRole('button', { name: 'Disable Webhook' }).click(); await page.getByRole('button', { name: 'Confirm disable' }).click(); await expect(page.getByRole('button', { name: 'Disable Webhook' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Create alert rule' })).toBeDisabled();
});

test('budget and error rules use basis points and existing project budgets without financial writes', async ({ page }) => {
  const state = await fixture(page); state.destinations.push(activeDestination()); await page.goto(`/organizations/${org}/alerts`);
  await page.getByRole('button', { name: 'Create alert rule' }).click(); await page.getByLabel('Alert type').selectOption('BudgetWarning'); await page.getByLabel('Rule project').selectOption(project);
  await expect(page.getByLabel('Project budget policy').locator('option')).toHaveCount(2); await page.getByLabel('Project budget policy').selectOption('project-policy'); await page.getByLabel('Threshold (%)').fill('80.25'); await page.getByLabel('Delivery destination').selectOption(destination);
  await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0); expect(state.writes[0].body).toMatchObject({ threshold: 8025, projectId: project, budgetPolicyId: 'project-policy' });
  await page.getByRole('button', { name: 'Create alert rule' }).click(); await page.getByLabel('Alert type').selectOption('ErrorSpike'); await page.getByLabel('Threshold (%)').fill('0.01'); await page.getByLabel('Delivery destination').selectOption(destination);
  await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0); expect(state.writes[1].body).toMatchObject({ threshold: 1, projectId: null, budgetPolicyId: null });
  expect(state.writes.every(value => value.path.endsWith('/alerts/rules'))).toBeTruthy();
});

for (const role of ['Admin', 'BillingViewer', 'Developer', 'ReadOnly']) test(`${role} controls match alert and owner-only privacy permissions`, async ({ page }) => {
  const state = await fixture(page); state.role = role; state.destinations.push(activeDestination()); await page.goto(`/organizations/${org}/alerts`);
  if (role === 'Developer' || role === 'ReadOnly') { await expect(page.getByRole('heading', { name: 'Alert access unavailable' })).toBeVisible(); await expect(page.getByText('Delivered', { exact: true })).toHaveCount(0); }
  else { await expect(page.getByText('Delivered', { exact: true })).toBeVisible(); if (role === 'Admin') await expect(page.getByRole('button', { name: 'Create alert rule' })).toBeVisible(); }
  if (role !== 'Admin') await expect(page.getByRole('button', { name: 'Create alert rule' })).toHaveCount(0);
  await page.goto(`/organizations/${org}/privacy?project=${project}`); await expect(page.getByRole('heading', { name: 'Owner access required' })).toBeVisible(); await expect(page.getByRole('button', { name: 'Configure retention' })).toHaveCount(0);
});

test('show-once webhook secret clears on close/scope change and never enters storage', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/alerts`); await page.getByRole('button', { name: 'Add webhook' }).click(); await page.getByLabel('Webhook HTTPS URL').fill('https://alerts.example.com/uzllm');
  await page.getByRole('button', { name: 'Create webhook', exact: true }).click(); await expect(page.getByRole('dialog')).toContainText(secret); await expect(page.getByLabel('Webhook HTTPS URL')).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret); expect(page.url()).not.toContain(secret);
  await page.keyboard.press('Escape'); await expect(page.locator('body')).not.toContainText(secret); await page.getByRole('button', { name: 'Add webhook' }).click(); await expect(page.getByLabel('Webhook HTTPS URL')).toHaveValue('');
  await page.keyboard.press('Escape'); await page.getByLabel('Organization', { exact: true }).selectOption(other); await page.getByRole('link', { name: 'Alerts', exact: true }).click(); await expect(page.locator('body')).not.toContainText('alerts.example.com');
  expect(state.writes).toHaveLength(1);
});

test('Telegram link expiry hides the token and never claims connection was verified', async ({ page }) => {
  const state = await fixture(page); state.expiry = new Date(Date.now() + 3000).toISOString(); await page.goto(`/organizations/${org}/alerts`); await page.getByRole('button', { name: 'Connect Telegram', exact: true }).click();
  await page.getByRole('button', { name: 'Generate private-chat link' }).click(); await expect(page.getByRole('link', { name: 'Open private chat' })).toHaveAttribute('href', /https:\/\/t.me\/UzllmTestBot\?start=/);
  await expect(page.getByRole('status')).toContainText('Link expired', { timeout: 6000 }); await expect(page.getByRole('link', { name: 'Open private chat' })).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain('synthetic-private-token');
});

test('owner opt-in/opt-out requires explicit confirmation and preserves metadata explanation', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/privacy?project=${project}`); await expect(page.getByRole('heading', { name: 'Retention disabled' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open payload inspector' })).toBeDisabled(); await page.getByRole('button', { name: 'Configure retention' }).click(); await page.getByLabel('Payload storage').selectOption('on'); await page.getByLabel('Retention duration (minutes)').fill('60');
  await expect(page.getByRole('button', { name: 'Save retention policy' })).toBeDisabled(); await page.getByRole('checkbox').check(); await page.getByRole('button', { name: 'Save retention policy' }).click(); await expect(page.getByRole('heading', { name: 'Retention enabled' })).toBeVisible();
  expect(state.writes[0].body).toEqual({ enabled: true, retentionMinutes: 60 });
  await page.getByRole('button', { name: 'Configure retention' }).click(); await page.getByLabel('Payload storage').selectOption('off'); await expect(page.getByRole('dialog')).toContainText('cannot be undone'); await page.getByRole('checkbox').check(); await page.getByRole('button', { name: 'Save retention policy' }).click(); await expect(page.getByRole('heading', { name: 'Retention disabled' })).toBeVisible();
  expect(state.writes[1].body).toEqual({ enabled: false, retentionMinutes: 60 }); await expect(page.getByText('metadata and financial history remain', { exact: false })).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Independent retention classes' })).toBeVisible();
});

test('private payload is explicit, text-escaped, expiry-cleared and never cached in browser storage', async ({ page }) => {
  const state = await fixture(page); state.enabled = true; state.expiry = new Date(Date.now() + 4000).toISOString(); await page.goto(`/organizations/${org}/privacy?project=${project}`);
  expect(state.reads.some(value => value.includes('/requests/'))).toBe(false); await page.getByRole('button', { name: 'Open payload inspector' }).click(); await page.getByLabel('Request UUID').fill(requestId); await page.getByRole('button', { name: 'Read retained payload' }).click();
  await expect(page.getByLabel('Private payload contents')).toContainText(privateBody); await expect(page.getByLabel('Request UUID')).toHaveValue(''); await expect(page.getByText('No complete response', { exact: false })).toBeVisible();
  await expect(page.getByLabel('Private payload contents').locator('img')).toHaveCount(0); expect(await page.locator('body').getAttribute('data-payload-executed')).toBeNull();
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(privateBody); expect(page.url()).not.toContain(requestId);
  await expect(page.getByRole('status')).toContainText('Payload expired', { timeout: 6000 }); await expect(page.locator('body')).not.toContainText(privateBody); await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
});

for (const status of [403, 404, 503]) test(`payload read ${status} is visible and cannot reveal a cached body`, async ({ page }) => {
  const state = await fixture(page); state.enabled = true; await page.goto(`/organizations/${org}/privacy?project=${project}`); await page.getByRole('button', { name: 'Open payload inspector' }).click();
  await page.getByLabel('Request UUID').fill(requestId); await page.getByRole('button', { name: 'Read retained payload' }).click(); await expect(page.getByLabel('Private payload contents')).toBeVisible();
  state.payloadStatus = status; await page.getByLabel('Request UUID').fill(requestId); await page.getByRole('button', { name: 'Read retained payload' }).click(); await expect(page.getByRole('alert')).toBeVisible(); await expect(page.locator('body')).not.toContainText(privateBody);
});

test('scope switch aborts pending payload read and hides private dialog', async ({ page }) => {
  const state = await fixture(page); state.enabled = true;
  let release: () => void = () => {}; const hold = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/payload-retention/requests/**', async route => { await hold; await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ requestId, projectId: project, request: privateBody, response: null, expiresAt: state.expiry }) }).catch(() => {}); });
  await page.goto(`/organizations/${org}/privacy?project=${project}`); await page.getByRole('button', { name: 'Open payload inspector' }).click(); await page.getByLabel('Request UUID').fill(requestId); await page.getByRole('button', { name: 'Read retained payload' }).click();
  // Native dialogs block background controls; real navigation still unmounts.
  await page.goto(`/organizations/${org}/privacy?project=${second}`); release(); await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.locator('body')).not.toContainText(privateBody);
});

test('refresh after revocation masks cached configuration and failed writes are not retried', async ({ page }) => {
  const state = await fixture(page); state.destinations.push(activeDestination()); await page.goto(`/organizations/${org}/alerts`); await page.getByRole('button', { name: 'Create alert rule' }).click();
  await page.getByLabel('Available balance threshold (USD)').fill('9007199254.740993'); await page.getByLabel('Delivery destination').selectOption(destination); await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('alert')).toContainText('precision'); expect(state.writes).toHaveLength(0);
  state.writeStatus = 403; await page.getByLabel('Available balance threshold (USD)').fill('1'); await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('alert')).toContainText('Permission denied'); expect(state.writes).toHaveLength(1);
  await page.keyboard.press('Escape'); state.denied = true; await page.getByRole('button', { name: 'Refresh alerts' }).click(); await expect(page.getByRole('heading', { name: 'Alert access unavailable' })).toBeVisible(); await expect(page.getByText('alerts.example.com', { exact: false })).toHaveCount(0);
});

test('mobile keyboard dialogs and reduced motion retain readable layout', async ({ page }, testInfo) => {
  await fixture(page); await page.setViewportSize({ width: 375, height: 812 }); await page.emulateMedia({ reducedMotion: 'reduce' }); await page.goto(`/organizations/${org}/privacy?project=${project}`);
  await expect(page.getByRole('heading', { name: 'Retention disabled' })).toBeVisible(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('privacy-mobile.png'), fullPage: true });
  await page.getByRole('button', { name: 'Configure retention' }).click(); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused(); await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.setViewportSize({ width: 812, height: 375 }); await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); await expect(page.getByRole('button', { name: 'Configure retention' })).toBeVisible();
  await page.evaluate(() => { document.documentElement.style.fontSize = ''; });
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.goto(`/organizations/${org}/alerts`); await expect(page.getByRole('heading', { name: 'Recent deliveries' })).toBeVisible(); await page.screenshot({ path: testInfo.outputPath('alerts-desktop.png'), fullPage: true });
});

test('closing a pending webhook command cannot reopen its late secret', async ({ page }) => {
  await fixture(page); let release: () => void = () => {}; const hold = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/alerts/webhook/destination', async route => { await hold; await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: destination, endpointUrl: 'https://alerts.example.com/hook', signingSecret: secret }) }); });
  await page.goto(`/organizations/${org}/alerts`); await page.getByRole('button', { name: 'Add webhook' }).click(); await page.getByLabel('Webhook HTTPS URL').fill('https://alerts.example.com/hook');
  const requested = page.waitForRequest(value => value.url().endsWith('/alerts/webhook/destination'));
  await page.getByRole('button', { name: 'Create webhook', exact: true }).click(); await requested; await page.keyboard.press('Escape');
  const replied = page.waitForResponse(value => value.url().endsWith('/alerts/webhook/destination')); release(); await replied;
  await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.locator('body')).not.toContainText(secret);
  await page.getByRole('button', { name: 'Add webhook' }).click(); await expect(page.getByLabel('Webhook HTTPS URL')).toHaveValue('');
});

test('unconfigured Telegram and conflicting webhook writes explain uncertainty without retry', async ({ page }) => {
  const state = await fixture(page); state.writeStatus = 503; await page.goto(`/organizations/${org}/alerts`); await page.getByRole('button', { name: 'Connect Telegram', exact: true }).click();
  await page.getByRole('button', { name: 'Generate private-chat link' }).click(); await expect(page.getByRole('alert')).toContainText('not configured'); expect(state.writes).toHaveLength(1);
  await page.keyboard.press('Escape'); state.writeStatus = 409; await page.getByRole('button', { name: 'Add webhook' }).click(); await page.getByLabel('Webhook HTTPS URL').fill('https://alerts.example.com/hook');
  await page.getByRole('button', { name: 'Create webhook', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('already exists'); await expect(page.getByLabel('Webhook HTTPS URL')).toHaveValue(''); expect(state.writes).toHaveLength(2);
});

test('failed project load is an error, not a new-project or authorized privacy editor', async ({ page }) => {
  await fixture(page); await page.route('**/management/v1/organizations/*/projects', route => route.fulfill({ status: 503 }));
  await page.goto(`/organizations/${org}/privacy?project=${project}`); await expect(page.getByRole('alert')).toContainText('503');
  await expect(page.getByRole('heading', { name: 'Create a project first' })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Configure retention' })).toHaveCount(0);
});
