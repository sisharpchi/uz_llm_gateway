import { randomUUID } from 'node:crypto';
import { test, expect, type BrowserContext } from '@playwright/test';

const base = process.env.UZLLM_ACCEPTANCE_BASE_URL!;
const mailbox = process.env.UZLLM_ACCEPTANCE_MAILPIT_URL!;
const password = 'Byok-acceptance-password-only-123';
const edgeBudget = (slots = 1) => new Promise(done => setTimeout(done, slots * 2200));

async function api(context: BrowserContext, method: 'get' | 'post' | 'put' | 'patch' | 'delete', path: string, data?: unknown) {
  if (path.includes('/auth/')) await edgeBudget();
  const csrf = (await context.cookies()).find(cookie => cookie.name === '__Host-uzllm-csrf')?.value;
  const response = await context.request[method](base + path, { data, headers: csrf ? { 'X-CSRF-Token': csrf } : {} });
  return { status: response.status(), body: await response.json().catch(() => null), headers: response.headers() };
}
async function register(context: BrowserContext) {
  const email = `byok-${randomUUID()}@example.invalid`;
  expect((await api(context, 'post', '/management/v1/auth/register', { email, password })).status).toBe(202);
  let token: string | undefined;
  for (let attempt = 0; attempt < 60 && !token; attempt++) {
    const list = await fetch(mailbox + '/api/v1/messages').then(value => value.json());
    for (const message of list.messages ?? []) {
      if (!message.To?.some((value: { Address: string }) => value.Address === email) || !message.Subject.includes('Verify')) continue;
      const body = await fetch(`${mailbox}/view/${message.ID}.txt`).then(value => value.text());
      token = body.match(/verification page:\s*\n\s*([A-Za-z0-9_-]+)/)?.[1];
    }
    if (!token) await new Promise(done => setTimeout(done, 1000));
  }
  expect(token).toBeTruthy();
  expect((await api(context, 'post', '/management/v1/auth/verify-email', { token })).status).toBe(204);
  expect((await api(context, 'post', '/management/v1/auth/login', { email, password })).status).toBe(204);
}

test('FRONTEND_008_real_BYOK_lifecycle_routing_zero_fee_settlement_and_tenant_denial', async ({ page, browser }, testInfo) => {
  // Drain the shared real edge burst from preceding scenarios; no policy bypass.
  await edgeBudget(10); await register(page.context());
  const org = await api(page.context(), 'post', '/management/v1/organizations', { name: 'BYOK acceptance' }); expect(org.status).toBe(201);
  const orgId: string = org.body.id; const orgPath = `/management/v1/organizations/${orgId}`;
  const project = await api(page.context(), 'post', orgPath + '/projects', { name: 'BYOK workload' }); expect(project.status).toBe(201);
  const projectId: string = project.body.id;
  const choices = await api(page.context(), 'get', orgPath + '/provider-keys/providers'); expect(choices.status).toBe(200); expect(choices.headers['cache-control']).toContain('no-store');
  const provider = choices.body.find((value: { code: string }) => value.code === 'openai'); expect(provider).toBeTruthy();
  const secret = 'synthetic-browser-BYOK-never-persist-1234';
  await page.goto(base + `/organizations/${orgId}/provider-keys?project=${projectId}`);
  await page.getByRole('button', { name: 'Add provider key' }).click(); await page.getByLabel('Provider', { exact: true }).selectOption(provider.id);
  await page.getByLabel('Credential name').fill('Fixture BYOK'); await page.getByLabel('Provider secret', { exact: true }).fill(secret);
  const created = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/provider-keys'));
  await page.getByRole('button', { name: 'Save credential' }).click(); const response = await created; expect(response.status()).toBe(201);
  const credential = await response.json(); expect(JSON.stringify(credential)).not.toContain(secret); expect(response.headers()['cache-control']).toContain('no-store');
  const id: string = credential.id; const path = orgPath + '/provider-keys/' + id;
  await page.getByRole('button', { name: 'Configure Fixture BYOK' }).click();
  await page.getByLabel('Model access').selectOption('selected'); await page.getByLabel('Allowed canonical models').fill('fixture-model'); await page.getByLabel('Lifetime external cap (USD)').fill('1');
  await page.getByRole('button', { name: 'Save restrictions' }).click();
  await expect.poll(async () => (await api(page.context(), 'get', path)).body.spendLimitMicroUsd).toBe('1000000');
  await page.getByLabel('Project to grant').selectOption(projectId); await page.getByRole('button', { name: 'Grant project access', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm grant change' }).click(); await expect(page.getByRole('button', { name: 'Remove grant BYOK workload' })).toBeVisible();
  const gatewayKey = await api(page.context(), 'post', `/management/v1/projects/${projectId}/api-keys`, { name: 'Fixture gateway', expiresAt: null }); expect(gatewayKey.status).toBe(201);
  const inference = async (stream: boolean) => page.context().request.post(base + '/v1/chat/completions', {
    headers: { Authorization: `Bearer ${gatewayKey.body.secret}` }, data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Hello' }], stream, uzllm: { provider_key_id: id, allow_managed_fallback: false } }
  });
  // The launch scenario owns the first cold-Redis sentinel. Keep this spec
  // after it; standalone execution also verifies fail-closed bootstrap, without
  // treating arbitrary provider errors as safe to replay.
  let chat = await inference(false);
  if (chat.status() === 503) {
    expect((await chat.json()).error.code).toBe('admission_unavailable');
    expect(chat.headers()['retry-after']).toBeTruthy();
    const undispatched = (await api(page.context(), 'get', path)).body;
    expect(undispatched.externalSpentMicroUsd).toBe('0'); expect(undispatched.externalReservedMicroUsd).toBe('0');
    await new Promise(done => setTimeout(done, 3500));
    chat = await inference(false);
  }
  expect(chat.status(), await chat.text()).toBe(200); expect((await chat.json()).choices[0].message.content).toContain('Fixture');
  const sse = await inference(true); expect(sse.status()).toBe(200); expect(await sse.text()).toContain('[DONE]');
  await expect.poll(async () => (await api(page.context(), 'get', path)).body.externalSpentMicroUsd).toBe('22');
  const final = (await api(page.context(), 'get', path)).body; expect(final.externalReservedMicroUsd).toBe('0');
  expect((await api(page.context(), 'get', orgPath + '/billing/wallet')).body.postedBalanceMicroUsd).toBe('0');
  await page.getByRole('link', { name: 'Routing', exact: true }).click(); await page.getByLabel('Default routing').selectOption('price'); await page.getByRole('button', { name: 'Save project settings' }).click();
  await expect(page.getByText('Effective strategy:', { exact: false })).toContainText('price'); await page.getByLabel('Primary model', { exact: true }).selectOption('fixture-model');
  await expect(page.getByLabel('Chat request JSON')).not.toContainText('provider_key_id'); await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('byok'); await page.getByLabel('Request provider key').selectOption(id);
  await expect(page.getByLabel('Chat request JSON')).toContainText(id); await page.getByRole('combobox', { name: 'Execution mode', exact: true }).selectOption('hybrid'); await expect(page.getByRole('alert')).toContainText('explicit managed-fallback consent');
  await page.getByRole('checkbox').check(); await expect(page.getByLabel('Chat request JSON')).toContainText('"allow_managed_fallback": true');
  await page.screenshot({ path: testInfo.outputPath('real-routing.png'), fullPage: true });
  const stranger = await browser.newContext({ baseURL: base, ignoreHTTPSErrors: true });
  try {
    expect((await api(stranger, 'get', orgPath + '/provider-keys/providers')).status).toBe(401); await register(stranger);
    expect((await api(stranger, 'get', orgPath + '/provider-keys/providers')).status).toBe(403); expect((await api(stranger, 'get', path)).status).toBe(403);
    expect((await api(stranger, 'patch', path, { name: 'Forbidden', secret: null })).status).toBe(403);
  } finally { await stranger.close(); }
  await page.getByRole('link', { name: 'Provider keys', exact: true }).click(); await page.getByRole('button', { name: 'Edit Fixture BYOK' }).click();
  await page.getByLabel('Replacement provider secret (optional)').fill(secret + '-rotated'); await page.getByRole('button', { name: 'Save credential' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  const rotated = (await api(page.context(), 'get', path)).body; expect(rotated.lastTestStatus).toBeNull(); expect(JSON.stringify(rotated)).not.toContain(secret);
  await page.getByRole('button', { name: 'Disable Fixture BYOK' }).click(); await page.getByRole('button', { name: 'Confirm disable' }).click();
  await expect(page.getByRole('button', { name: 'Edit Fixture BYOK' })).toBeDisabled(); expect((await inference(false)).status()).not.toBe(200);
  await page.getByRole('button', { name: 'Delete Fixture BYOK' }).click(); await page.getByRole('button', { name: 'Confirm delete' }).click(); await expect(page.getByText(/No provider keys/)).toBeVisible();
  expect((await api(page.context(), 'get', path)).status).toBe(404);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret);
});
