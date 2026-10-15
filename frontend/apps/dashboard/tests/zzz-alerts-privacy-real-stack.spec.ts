import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { test, expect, type BrowserContext } from '@playwright/test';

const base = process.env.UZLLM_ACCEPTANCE_BASE_URL!; const mailbox = process.env.UZLLM_ACCEPTANCE_MAILPIT_URL!;
const password = 'Privacy-acceptance-password-only-123';
const edgeBudget = (slots = 1) => new Promise(done => setTimeout(done, 2200 * slots));
async function api(context: BrowserContext, method: 'get' | 'post' | 'put' | 'patch' | 'delete', path: string, data?: unknown) {
  if (path.includes('/auth/') || path.includes('/invitations')) await edgeBudget();
  const csrf = (await context.cookies()).find(cookie => cookie.name === '__Host-uzllm-csrf')?.value;
  const response = await context.request[method](base + path, { data, headers: csrf ? { 'X-CSRF-Token': csrf } : {} });
  return { status: response.status(), body: await response.json().catch(() => null), headers: response.headers() };
}
async function mail(email: string, subject: string) {
  let token: string | undefined;
  for (let attempt = 0; attempt < 60 && !token; attempt++) {
    const list = await fetch(mailbox + '/api/v1/messages').then(value => value.json());
    for (const item of list.messages ?? []) {
      if (!item.To?.some((value: { Address: string }) => value.Address === email) || !item.Subject.includes(subject)) continue;
      const body = await fetch(`${mailbox}/view/${item.ID}.txt`).then(value => value.text());
      token = body.match(/(?:verification page|this token):\s*\n\s*([A-Za-z0-9_-]+)/)?.[1];
    }
    if (!token) await new Promise(done => setTimeout(done, 1000));
  }
  expect(token).toBeTruthy(); return token;
}
async function register(context: BrowserContext) {
  const email = `privacy-${randomUUID()}@example.invalid`;
  expect((await api(context, 'post', '/management/v1/auth/register', { email, password })).status).toBe(202);
  expect((await api(context, 'post', '/management/v1/auth/verify-email', { token: await mail(email, 'Verify') })).status).toBe(204);
  expect((await api(context, 'post', '/management/v1/auth/login', { email, password })).status).toBe(204); return email;
}
function expirePayload(orgId: string, projectId: string, requestId: string) {
  const compose = process.env.COMPOSE_PROJECT_NAME; const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (process.env.UZLLM_ACCEPTANCE_ALLOW_PAYLOAD_EXPIRY_FIXTURE !== 'disposable-stack' || !/^uzllmacc[0-9a-f]{8}$/.test(compose ?? '') || ![orgId, projectId, requestId].every(value => uuid.test(value))) throw new Error('Refusing payload fixture outside isolated acceptance stack.');
  // Advance only this synthetic payload's dates, not the production clock or API.
  const sql = `UPDATE usage.payload SET created_at = now() - interval '2 hours', expires_at = now() - interval '1 minute' WHERE organization_id = '${orgId}' AND project_id = '${projectId}' AND request_id = '${requestId}' RETURNING request_id;`;
  const result = spawnSync('docker', ['compose', '-p', compose!, '-f', resolve('../../../deploy/compose.acceptance.yml'), 'exec', '-T', 'postgres', 'psql', '-qAt', '-v', 'ON_ERROR_STOP=1', '-U', 'uzllm_migrator', '-d', 'uzllm_acceptance', '-c', sql], { encoding: 'utf8' });
  expect(result.status, 'Isolated payload fixture failed.').toBe(0); expect(result.stdout.trim()).toBe(requestId);
}

test('FRONTEND_009_real_alert_configuration_owner_retention_expiry_optout_and_role_tenant_denial', async ({ page, browser }, testInfo) => {
  await edgeBudget(10); await register(page.context());
  const created = await api(page.context(), 'post', '/management/v1/organizations', { name: 'Alerts privacy acceptance' }); expect(created.status).toBe(201);
  const orgId: string = created.body.id; const orgPath = `/management/v1/organizations/${orgId}`;
  const project = await api(page.context(), 'post', orgPath + '/projects', { name: 'Private workload' }); expect(project.status).toBe(201); const projectId: string = project.body.id;
  const retention = orgPath + `/projects/${projectId}/payload-retention`;
  await page.goto(base + `/organizations/${orgId}/privacy?project=${projectId}`); await expect(page.getByRole('heading', { name: 'Retention disabled' })).toBeVisible();
  await page.getByRole('button', { name: 'Configure retention' }).click(); await page.getByLabel('Payload storage').selectOption('on'); await page.getByLabel('Retention duration (minutes)').fill('60'); await page.getByRole('checkbox').check();
  await page.getByRole('button', { name: 'Save retention policy' }).click(); await expect(page.getByRole('heading', { name: 'Retention enabled' })).toBeVisible();
  const providers = await api(page.context(), 'get', orgPath + '/provider-keys/providers');
  const credential = await api(page.context(), 'post', orgPath + '/provider-keys', { providerId: providers.body.find((value: { code: string }) => value.code === 'openai').id, name: 'Privacy fixture', secret: 'synthetic-privacy-provider-only-key' }); expect(credential.status).toBe(201);
  expect((await api(page.context(), 'put', orgPath + `/provider-keys/${credential.body.id}/projects/${projectId}`)).status).toBe(204);
  const key = await api(page.context(), 'post', `/management/v1/projects/${projectId}/api-keys`, { name: 'Privacy fixture gateway', expiresAt: null }); expect(key.status).toBe(201);
  const inference = () => page.context().request.post(base + '/v1/chat/completions', { headers: { Authorization: `Bearer ${key.body.secret}` }, data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Synthetic privacy acceptance' }], stream: false, uzllm: { provider_key_id: credential.body.id } } });
  const first = await inference(); expect(first.status(), await first.text()).toBe(200);
  let activity = (await api(page.context(), 'get', orgPath + '/usage/activity')).body.items;
  const firstId: string = activity[0].requestId;
  const retained = await api(page.context(), 'get', retention + '/requests/' + firstId); expect(retained.status).toBe(200); expect(retained.headers['cache-control']).toContain('no-store'); expect(retained.body.request).toContain('Synthetic privacy acceptance');
  await page.getByRole('button', { name: 'Open payload inspector' }).click(); await page.getByLabel('Request UUID').fill(firstId); await page.getByRole('button', { name: 'Read retained payload' }).click(); await expect(page.getByLabel('Private payload contents')).toContainText('Synthetic privacy acceptance');
  await page.getByRole('button', { name: 'Close and clear payload' }).click(); await expect(page.locator('body')).not.toContainText('Synthetic privacy acceptance');
  expirePayload(orgId, projectId, firstId); expect((await api(page.context(), 'get', retention + '/requests/' + firstId)).status).toBe(404);
  await page.getByRole('button', { name: 'Open payload inspector' }).click(); await page.getByLabel('Request UUID').fill(firstId); await page.getByRole('button', { name: 'Read retained payload' }).click(); await expect(page.getByRole('alert')).toContainText('expired'); await page.keyboard.press('Escape');
  const second = await inference(); expect(second.status()).toBe(200); activity = (await api(page.context(), 'get', orgPath + '/usage/activity')).body.items; const secondId = activity[0].requestId; expect(secondId).not.toBe(firstId);
  expect((await api(page.context(), 'get', retention + '/requests/' + secondId)).status).toBe(200);
  await page.getByRole('button', { name: 'Configure retention' }).click(); await page.getByLabel('Payload storage').selectOption('off'); await page.getByRole('checkbox').check(); await page.getByRole('button', { name: 'Save retention policy' }).click(); await expect(page.getByRole('heading', { name: 'Retention disabled' })).toBeVisible();
  expect((await api(page.context(), 'get', retention + '/requests/' + secondId)).status).toBe(404);
  const detail = await api(page.context(), 'get', orgPath + '/usage/requests/' + secondId); expect(detail.status).toBe(200); expect(JSON.stringify(detail.body)).not.toContain('Synthetic privacy acceptance');
  await page.screenshot({ path: testInfo.outputPath('real-privacy.png'), fullPage: true });

  await page.getByRole('link', { name: 'Alerts', exact: true }).click(); await expect(page.getByText('No delivery events recorded.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Add webhook' }).click(); await page.getByLabel('Webhook HTTPS URL').fill('https://example.com/uzllm-acceptance');
  const webhookResponse = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/alerts/webhook/destination'));
  await page.getByRole('button', { name: 'Create webhook', exact: true }).click(); const webhook = await webhookResponse; expect(webhook.status()).toBe(201); const hook = await webhook.json(); expect(hook.signingSecret).toHaveLength(64);
  await expect(page.getByRole('dialog')).toContainText(hook.signingSecret); await page.getByRole('button', { name: 'Close private dialog' }).click(); await expect(page.locator('body')).not.toContainText(hook.signingSecret);
  // With fewer than 20 requests this ErrorSpike rule cannot trigger. No remote POST is authorized or performed.
  await page.getByRole('button', { name: 'Create alert rule' }).click(); await page.getByLabel('Alert type').selectOption('ErrorSpike'); await page.getByLabel('Threshold (%)').fill('100'); await page.getByLabel('Delivery destination').selectOption(hook.id); await page.getByRole('button', { name: 'Save alert rule' }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  const rules = await api(page.context(), 'get', orgPath + '/alerts/rules'); expect(rules.body).toHaveLength(1); const ruleId: string = rules.body[0].id;
  await page.getByRole('button', { name: `Pause rule ${ruleId.slice(0, 8)}` }).click(); await page.getByRole('button', { name: 'Confirm rule change' }).click(); await expect(page.getByRole('button', { name: `Enable rule ${ruleId.slice(0, 8)}` })).toBeVisible();
  await page.getByRole('button', { name: 'Disable Webhook' }).click(); await page.getByRole('button', { name: 'Confirm disable' }).click(); await expect(page.getByRole('button', { name: 'Disable Webhook' })).toBeDisabled();
  expect((await api(page.context(), 'get', orgPath + '/alerts/events')).body).toEqual([]);
  expect((await api(page.context(), 'post', orgPath + '/alerts/webhook/destination', { endpointUrl: 'http://127.0.0.1/hook' })).status).toBe(400);
  await page.screenshot({ path: testInfo.outputPath('real-alerts.png'), fullPage: true });
  const stranger = await browser.newContext({ baseURL: base, ignoreHTTPSErrors: true });
  try {
    expect((await api(stranger, 'get', retention)).status).toBe(401); const email = await register(stranger);
    expect((await api(stranger, 'get', retention)).status).toBe(403); expect((await api(stranger, 'get', orgPath + '/alerts/events')).status).toBe(403);
    expect((await api(stranger, 'put', retention, { enabled: true, retentionMinutes: 60 })).status).toBe(403);
    expect((await api(page.context(), 'post', orgPath + '/team/invitations', { email, role: 'Admin' })).status).toBe(201);
    expect((await api(stranger, 'post', '/management/v1/team/invitations/accept', { token: await mail(email, 'invitation') })).status).toBe(200);
    expect((await api(stranger, 'get', orgPath + '/alerts/events')).status).toBe(200);
    const admin = await stranger.newPage(); await admin.goto(base + `/organizations/${orgId}/privacy?project=${projectId}`); await expect(admin.getByRole('heading', { name: 'Owner access required' })).toBeVisible(); await expect(admin.getByRole('button', { name: 'Configure retention' })).toHaveCount(0);
    expect((await api(stranger, 'get', retention + '/requests/' + secondId)).status).toBe(403);
  } finally { await stranger.close(); }
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(hook.signingSecret);
});
