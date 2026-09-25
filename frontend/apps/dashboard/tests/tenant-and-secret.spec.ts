import { test, expect, type Page } from '@playwright/test';

const orgOne = '11111111-1111-1111-1111-111111111111';
const orgTwo = '22222222-2222-2222-2222-222222222222';
const projectOne = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const projectTwo = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const secret = 'uzllm_one_time_secret_browser_fixture';

async function mockManagement(page: Page) {
  let created = false;
  const calls: string[] = [];
  await page.route('**/management/v1/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    calls.push(`${request.method()} ${path}`);
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner-1', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return json([{ id: orgOne, name: 'Alpha', status: 0 }, { id: orgTwo, name: 'Beta', status: 0 }]);
    if (path.includes(`/${orgOne}/projects`)) return json([{ id: projectOne, organizationId: orgOne, name: 'Alpha Production', status: 0 }]);
    if (path.includes(`/${orgTwo}/projects`)) return json([{ id: projectTwo, organizationId: orgTwo, name: 'Beta Sandbox', status: 0 }]);
    if (path.endsWith('/billing/wallet')) return json({ organizationId: path.includes(orgOne) ? orgOne : orgTwo,
      postedBalanceMicroUsd: '1000000', reservedBalanceMicroUsd: '0', availableBalanceMicroUsd: '1000000', version: 1 });
    if (path.endsWith('/billing/topups')) return json([]);
    if (path.endsWith(`/${projectOne}/api-keys`) && request.method() === 'POST') {
      created = true;
      return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({
        apiKey: { id: 'created-key', projectId: projectOne, name: 'New key', prefix: 'uzllm_created', status: 0, expiresAt: null, createdAt: new Date().toISOString() },
        secret
      }) });
    }
    if (path.endsWith(`/${projectOne}/api-keys`)) return json([{ id: 'key-alpha', projectId: projectOne, name: 'Alpha only key', prefix: 'uzllm_alpha', status: 0,
      expiresAt: null, createdAt: new Date().toISOString() }, ...(created ? [{ id: 'created-key', projectId: projectOne, name: 'New key', prefix: 'uzllm_created', status: 0, expiresAt: null, createdAt: new Date().toISOString() }] : [])]);
    if (path.endsWith(`/${projectTwo}/api-keys`)) return json([{ id: 'key-beta', projectId: projectTwo, name: 'Beta only key', prefix: 'uzllm_beta', status: 0,
      expiresAt: null, createdAt: new Date().toISOString() }]);
    return route.fulfill({ status: 404 });
  });
  return calls;
}

test('tenant switch changes project and key data without displaying the prior tenant', async ({ page }) => {
  const calls = await mockManagement(page);
  await page.goto(`/organizations/${orgOne}/keys`);
  await expect(page.getByText('Alpha only key')).toBeVisible();
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(orgTwo);
  await page.getByRole('link', { name: 'API keys' }).click();
  await expect(page.getByText('Beta only key')).toBeVisible();
  await expect(page.getByText('Alpha only key')).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Project' })).toHaveValue(projectTwo);
  expect(calls).toContain(`GET /management/v1/projects/${projectTwo}/api-keys`);
});

test('issued secret is shown once and never retained in browser storage or key metadata', async ({ page }) => {
  const calls = await mockManagement(page);
  await page.goto(`/organizations/${orgOne}/keys`);
  await page.getByRole('textbox', { name: 'Key name' }).fill('New key');
  await page.getByRole('button', { name: 'Create key' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText(secret)).toBeVisible();
  await dialog.getByRole('button', { name: 'I saved it' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByText(secret)).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain(secret);
  await page.reload();
  await expect(page.getByText('New key')).toBeVisible();
  await expect(page.getByText(secret)).toHaveCount(0);
  expect(calls.filter(call => call === `POST /management/v1/projects/${projectOne}/api-keys`)).toHaveLength(1);
});

test('non-operator cannot see privileged operator shell', async ({ page }) => {
  await page.route('**/management/v1/auth/session', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ accountId: 'customer', email: 'customer@example.uz', emailVerified: true, isOperator: false }) }));
  await page.goto('http://127.0.0.1:5175');
  await expect(page.getByText('Operator access required')).toBeVisible();
  await expect(page.getByText('Provider, pricing, payment')).toHaveCount(0);
});

test('onboarding creates organization and project through scoped endpoints', async ({ page }) => {
  const calls: string[] = [];
  let organizationCreated = false;
  let projectCreated = false;
  await page.route('**/management/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    calls.push(`${route.request().method()} ${path}`);
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations') && route.request().method() === 'POST') { organizationCreated = true; return json({ id: orgOne, name: 'New Org', status: 0 }, 201); }
    if (path.endsWith('/organizations')) return json(organizationCreated ? [{ id: orgOne, name: 'New Org', status: 0 }] : []);
    if (path.endsWith(`/${orgOne}/projects`) && route.request().method() === 'POST') { projectCreated = true; return json({ id: projectOne, organizationId: orgOne, name: 'First Project', status: 0 }, 201); }
    if (path.endsWith(`/${orgOne}/projects`)) return json(projectCreated ? [{ id: projectOne, organizationId: orgOne, name: 'First Project', status: 0 }] : []);
    if (path.endsWith('/billing/wallet')) return json({ organizationId: orgOne, postedBalanceMicroUsd: '0', reservedBalanceMicroUsd: '0', availableBalanceMicroUsd: '0', version: 0 });
    return route.fulfill({ status: 404 });
  });
  await page.goto('/');
  await page.getByRole('textbox', { name: 'Organization name' }).fill('New Org');
  await page.getByRole('button', { name: 'Create organization' }).click();
  await page.getByRole('link', { name: 'Projects' }).click();
  await page.getByRole('textbox', { name: 'Project name' }).fill('First Project');
  await page.getByRole('button', { name: 'Create project' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'First Project' })).toBeVisible();
  expect(calls).toContain('POST /management/v1/organizations');
  expect(calls).toContain(`POST /management/v1/organizations/${orgOne}/projects`);
});

test('billing quotes and starts payment without fabricating wallet credit', async ({ page }) => {
  const calls = await mockManagement(page);
  await page.route(`**/management/v1/organizations/${orgOne}/billing/quotes`, async route => {
    const body = route.request().postDataJSON();
    expect(body).toEqual({ provider: 'Payme', amountTiyin: '100000' });
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
      id: 'quote-1', organizationId: orgOne, provider: 'Payme', amountTiyin: '100000', feeTiyin: '1000',
      creditMicroUsd: '99000', uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60000).toISOString()
    }) });
  });
  await page.route(`**/management/v1/organizations/${orgOne}/billing/topups`, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    expect(route.request().headers()['idempotency-key']).toBeTruthy();
    await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({
      intent: { id: 'intent-1', organizationId: orgOne, provider: 'Payme', status: 'Pending',
        amountTiyin: '100000', feeTiyin: '1000', creditMicroUsd: '99000', createdAt: new Date().toISOString() },
      duplicate: false, checkoutUrl: 'https://checkout.paycom.uz/test'
    }) });
  });
  await page.goto(`/organizations/${orgOne}/billing`);
  await expect(page.getByText('$1.00').first()).toBeVisible();
  await page.getByRole('button', { name: 'Get quote' }).click();
  await expect(page.getByText('$0.09')).toBeVisible();
  await page.getByRole('button', { name: 'Continue to Payme' }).click();
  await expect(page.getByRole('link', { name: /Open secure Payme checkout/ })).toHaveAttribute('href', 'https://checkout.paycom.uz/test');
  await expect(page.getByText('$1.00').first()).toBeVisible();
  expect(calls).toContain(`GET /management/v1/organizations/${orgOne}/billing/wallet`);
});

test('top-up retry reuses the same idempotency key for one quote', async ({ page }) => {
  await mockManagement(page);
  await page.route(`**/management/v1/organizations/${orgOne}/billing/quotes`, route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify({
      id: 'retry-quote', organizationId: orgOne, provider: 'Payme', amountTiyin: '100000', feeTiyin: '0',
      creditMicroUsd: '100000', uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60000).toISOString()
    })
  }));
  const keys: string[] = [];
  await page.route(`**/management/v1/organizations/${orgOne}/billing/topups`, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    keys.push(route.request().headers()['idempotency-key']);
    if (keys.length === 1) return route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"temporary"}' });
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({
      intent: { id: 'retry-intent', organizationId: orgOne, provider: 'Payme', status: 'Pending',
        amountTiyin: '100000', feeTiyin: '0', creditMicroUsd: '100000', createdAt: new Date().toISOString() },
      duplicate: true, checkoutUrl: 'https://checkout.paycom.uz/retry'
    }) });
  });
  await page.goto(`/organizations/${orgOne}/billing`);
  await page.getByRole('button', { name: 'Get quote' }).click();
  await page.getByRole('button', { name: 'Continue to Payme' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('button', { name: 'Continue to Payme' }).click();
  await expect(page.getByRole('link', { name: /Open secure Payme checkout/ })).toBeVisible();
  expect(keys).toHaveLength(2);
  expect(keys[0]).toBeTruthy();
  expect(keys[1]).toBe(keys[0]);
});

test('registration and email verification use the public identity flow', async ({ page }) => {
  const calls: string[] = [];
  await page.route('**/management/v1/auth/**', async route => {
    calls.push(`${route.request().method()} ${new URL(route.request().url()).pathname}`);
    await route.fulfill({ status: route.request().url().endsWith('/auth/register') ? 202 : 204 });
  });
  await page.goto('/register');
  await page.getByRole('textbox', { name: 'Email' }).fill('new@example.uz');
  await page.getByLabel('Password').fill('a-secure-password-123');
  await page.getByRole('button', { name: 'Create your account' }).click();
  await expect(page.getByText(/Check your email for the verification token/)).toBeVisible();
  await page.getByRole('link', { name: 'Verify email' }).click();
  await page.getByRole('textbox', { name: 'Verification token' }).fill('mail-delivered-token');
  await page.getByRole('button', { name: 'Verify your email' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  expect(calls).toContain('POST /management/v1/auth/register');
  expect(calls).toContain('POST /management/v1/auth/verify-email');
});
