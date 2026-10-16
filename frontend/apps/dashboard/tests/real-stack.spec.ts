import { createHash, randomUUID } from 'node:crypto';
import { test, expect, type APIRequestContext, type BrowserContext } from '@playwright/test';

const base = process.env.UZLLM_ACCEPTANCE_BASE_URL!;
const mailbox = process.env.UZLLM_ACCEPTANCE_MAILPIT_URL!;
const paymeAuth = `Basic ${Buffer.from(`Paycom:${process.env.UZLLM_ACCEPTANCE_PAYME_KEY}`).toString('base64')}`;
const password = 'Acceptance-only-password-123';

async function json(request: APIRequestContext, method: 'get' | 'post', path: string,
  options: Record<string, unknown> = {}) {
  const response = await request[method](base + path, options);
  const body = await response.json().catch(() => null);
  return { response, body };
}
async function csrf(context: BrowserContext) {
  const cookie = (await context.cookies()).find(item => item.name === '__Host-uzllm-csrf');
  expect(cookie?.value).toBeTruthy();
  return { 'X-CSRF-Token': cookie!.value };
}
async function verificationToken(email: string, kind: 'Verify' | 'invitation' = 'Verify') {
  for (let attempt = 0; attempt < 60; attempt++) {
    const list = await fetch(`${mailbox}/api/v1/messages`).then(response => response.json()).catch(() => null);
    const message = list?.messages?.find((item: { To: { Address: string }[]; Subject: string }) =>
      item.To?.some(person => person.Address.toLowerCase() === email) && item.Subject.includes(kind));
    if (message) {
      const body = await fetch(`${mailbox}/view/${message.ID}.txt`).then(response => response.text());
      const token = body.match(/(?:verification page|this token):\s*\n\s*([A-Za-z0-9_-]+)/)?.[1];
      if (token) return token;
    }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 1000));
  }
  throw new Error(`Worker did not deliver verification email to ${email}`);
}
function clickFields(intentId: string, transactionId: string, amount: string, action: '0' | '1', prepareId = '') {
  const fields = new URLSearchParams({
    click_trans_id: transactionId, service_id: 'acceptance-service',
    merchant_trans_id: intentId, amount, action, error: '0', error_note: 'Success',
    sign_time: '2026-10-02 10:00:00', click_paydoc_id: '90001',
  });
  if (action === '1') fields.set('merchant_prepare_id', prepareId);
  const signed = transactionId + 'acceptance-service' + process.env.UZLLM_ACCEPTANCE_CLICK_KEY
    + intentId + prepareId + amount + action + fields.get('sign_time');
  fields.set('sign_string', createHash('md5').update(signed).digest('hex'));
  return fields;
}

test('LAUNCH_002_real_stack_registration_payment_inference_and_dashboard', async ({ page, browser }, testInfo) => {
  const email = `acceptance-${randomUUID()}@example.invalid`;
  await page.goto('/register');
  await page.getByRole('textbox', { name: 'Email' }).fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Create your account' }).click();
  await expect(page.getByText(/sent a verification token/)).toBeVisible();
  const token = await verificationToken(email);
  await page.goto('/verify-email');
  await page.getByRole('textbox', { name: 'Verification token' }).fill(token);
  await page.getByRole('button', { name: 'Verify your email' }).click();
  await expect(page.getByRole('status')).toContainText('Email verified');
  await page.goto('/login');
  await page.getByRole('textbox', { name: 'Email' }).fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: /Start with your organization/ })).toBeVisible();

  const auth = await csrf(page.context());
  const request = page.context().request;
  const organization = await json(request, 'post', '/management/v1/organizations',
    { data: { name: 'Acceptance workspace' }, headers: auth });
  expect(organization.response.status()).toBe(201);
  const orgId: string = organization.body.id;
  const project = await json(request, 'post', `/management/v1/organizations/${orgId}/projects`,
    { data: { name: 'Acceptance project' }, headers: auth });
  expect(project.response.status()).toBe(201);
  const projectId: string = project.body.id;
  const key = await json(request, 'post', `/management/v1/projects/${projectId}/api-keys`,
    { data: { name: 'Acceptance key', expiresAt: null }, headers: auth });
  expect(key.response.status()).toBe(201);
  const gatewaySecret: string = key.body.secret;
  expect(gatewaySecret).toMatch(/^uzllm_live_/);
  expect(key.response.headers()['cache-control']).toContain('no-store');
  const keys = await json(request, 'get', `/management/v1/projects/${projectId}/api-keys`);
  expect(JSON.stringify(keys.body)).not.toContain(gatewaySecret);

  const quote = await json(request, 'post', `/management/v1/organizations/${orgId}/billing/quotes`,
    { data: { provider: 'Payme', amountTiyin: '10000000' }, headers: auth });
  expect(quote.response.status()).toBe(200);
  expect(quote.body.creditMicroUsd).toBe('10000000');
  const idempotencyKey = randomUUID();
  const topupPath = `/management/v1/organizations/${orgId}/billing/topups`;
  const topupOptions = { data: { quoteId: quote.body.id },
    headers: { ...auth, 'Idempotency-Key': idempotencyKey } };
  const topup = await json(request, 'post', topupPath, topupOptions);
  expect(topup.response.status()).toBe(201);
  const repeatedTopup = await json(request, 'post', topupPath, topupOptions);
  expect(repeatedTopup.response.status()).toBe(200);
  expect(repeatedTopup.body.duplicate).toBe(true);
  expect(repeatedTopup.body.intent.id).toBe(topup.body.intent.id);
  const intentId: string = topup.body.intent.id;
  const paymentPath = '/payments/payme/callback';
  const paymentHeaders = { Authorization: paymeAuth };
  const wrongAuth = await json(request, 'post', paymentPath,
    { data: { id: 1, method: 'CheckPerformTransaction', params: { amount: 10000000,
      account: { intent_id: intentId } } }, headers: { Authorization: 'Basic invalid' } });
  expect(wrongAuth.body.error.code).toBe(-32504);
  const wrongAmount = await json(request, 'post', paymentPath,
    { data: { id: 2, method: 'CheckPerformTransaction', params: { amount: 9999999,
      account: { intent_id: intentId } } }, headers: paymentHeaders });
  expect(wrongAmount.body.error.code).toBe(-31001);
  const providerTransaction = `payme-${randomUUID()}`;
  const create = await json(request, 'post', paymentPath,
    { data: { id: 3, method: 'CreateTransaction', params: { id: providerTransaction,
      time: Date.now(), amount: 10000000, account: { intent_id: intentId } } }, headers: paymentHeaders });
  expect(create.body.error).toBeUndefined();
  expect(create.body.result.state).toBe(1);
  const performPayload = { id: 4, method: 'PerformTransaction', params: { id: providerTransaction } };
  const paid = await json(request, 'post', paymentPath, { data: performPayload, headers: paymentHeaders });
  expect(paid.body.result.state).toBe(2);
  const replay = await json(request, 'post', paymentPath, { data: performPayload, headers: paymentHeaders });
  expect(replay.body.result.state).toBe(2);
  expect(replay.body.result.perform_time).toBe(paid.body.result.perform_time);
  const walletPath = `/management/v1/organizations/${orgId}/billing/wallet`;
  const walletAfterPayme = await json(request, 'get', walletPath);
  expect(walletAfterPayme.body.postedBalanceMicroUsd).toBe('10000000');
  expect(walletAfterPayme.body.reservedBalanceMicroUsd).toBe('0');

  const clickQuote = await json(request, 'post', `/management/v1/organizations/${orgId}/billing/quotes`,
    { data: { provider: 'Click', amountTiyin: '1000000' }, headers: auth });
  expect(clickQuote.response.status()).toBe(200);
  const clickTopup = await json(request, 'post', topupPath,
    { data: { quoteId: clickQuote.body.id }, headers: { ...auth, 'Idempotency-Key': randomUUID() } });
  expect(clickTopup.response.status()).toBe(201);
  const clickId: string = clickTopup.body.intent.id;
  const clickTransaction = `click-${randomUUID()}`;
  const prepare = clickFields(clickId, clickTransaction, '10000.00', '0');
  const invalid = new URLSearchParams(prepare); invalid.set('sign_string', '0'.repeat(32));
  const clickHeaders = { 'Content-Type': 'application/x-www-form-urlencoded' };
  const denied = await json(request, 'post', '/payments/click/callback',
    { data: invalid.toString(), headers: clickHeaders });
  expect(denied.body.error).toBe(-1);
  const prepared = await json(request, 'post', '/payments/click/callback',
    { data: prepare.toString(), headers: clickHeaders });
  expect(prepared.body.error).toBe(0);
  const complete = clickFields(clickId, clickTransaction, '10000.00', '1',
    String(prepared.body.merchant_prepare_id));
  const clicked = await json(request, 'post', '/payments/click/callback',
    { data: complete.toString(), headers: clickHeaders });
  expect(clicked.body.error).toBe(0);
  const clickedReplay = await json(request, 'post', '/payments/click/callback',
    { data: complete.toString(), headers: clickHeaders });
  expect(clickedReplay.body.error).toBe(-4);
  const walletAfterClick = await json(request, 'get', walletPath);
  expect(walletAfterClick.body.postedBalanceMicroUsd).toBe('11000000');

  const anonymous = await json(request, 'get', '/v1/models');
  expect(anonymous.response.status()).toBe(401);
  const failedKey = await json(request, 'post', '/v1/chat/completions',
    { data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Hello' }], stream: false },
      headers: { Authorization: 'Bearer invalid' } });
  expect(failedKey.response.status()).toBe(401);
  const recoveryProbe = await json(request, 'post', '/v1/chat/completions',
    { data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Hello' }], stream: false },
      headers: { Authorization: `Bearer ${gatewaySecret}` } });
  expect(recoveryProbe.response.status(), JSON.stringify(recoveryProbe.body)).toBe(503);
  expect(recoveryProbe.response.headers()['retry-after']).toBeTruthy();
  await new Promise(resolveDelay => setTimeout(resolveDelay, 3500));
  const inferenceHeaders = { Authorization: `Bearer ${gatewaySecret}`,
    'Idempotency-Key': randomUUID() };
  const inference = await json(request, 'post', '/v1/chat/completions',
    { data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Hello' }], stream: false },
      headers: inferenceHeaders });
  expect(inference.response.status(), JSON.stringify(inference.body)).toBe(200);
  expect(inference.body.choices[0].message.content).toBe('Fixture complete.');
  expect(inference.body.usage.prompt_tokens).toBe(8);
  const streamed = await request.post(base + '/v1/chat/completions', {
    data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Hello' }], stream: true },
    headers: { Authorization: `Bearer ${gatewaySecret}`, 'Idempotency-Key': randomUUID() },
  });
  expect(streamed.status()).toBe(200);
  expect(streamed.headers()['content-type']).toContain('text/event-stream');
  const sse = await streamed.text();
  expect(sse).toContain('Fixture');
  expect(sse).toContain('[DONE]');

  const activityPath = `/management/v1/organizations/${orgId}/usage/activity`;
  await expect.poll(async () => (await json(request, 'get', activityPath)).body?.items?.length)
    .toBe(2);
  const activity = await json(request, 'get', activityPath);
  expect(activity.body.items.map((item: { chargedMicroUsd: string }) => item.chargedMicroUsd))
    .toEqual(['11', '11']);
  const finalWallet = await json(request, 'get', walletPath);
  expect(finalWallet.body.postedBalanceMicroUsd).toBe('10999978');
  expect(finalWallet.body.availableBalanceMicroUsd).toBe('10999978');
  expect(finalWallet.body.reservedBalanceMicroUsd).toBe('0');

  await page.goto(`/organizations/${orgId}/activity`);
  await expect(page.getByRole('heading', { name: 'Activity' })).toBeVisible();
  await expect(page.getByRole('table').getByText('fixture-model')).toHaveCount(2);
  await page.goto(`/organizations/${orgId}/billing`);
  await expect(page.getByText('$10.99').first()).toBeVisible();

  // GATEWAY-005: real TLS-edge/API/Redis/PostgreSQL/provider-adapter execution,
  // not browser route mocks. The existing cold-Redis sentinel above must run first.
  const responseRequest = { model: 'fixture-model', instructions: 'Be concise.', input: 'Hello', max_output_tokens: 32, store: false };
  const responseHeaders = { Authorization: `Bearer ${gatewaySecret}`, 'Idempotency-Key': randomUUID() };
  const response = await json(request, 'post', '/v1/responses', { data: responseRequest, headers: responseHeaders });
  expect(response.response.status(), JSON.stringify(response.body)).toBe(200);
  expect(response.response.headers()['cache-control']).toBe('no-store');
  expect(response.body.object).toBe('response'); expect(response.body.status).toBe('completed'); expect(response.body.store).toBe(false);
  expect(response.body.output[0].content[0]).toMatchObject({ type: 'output_text', text: 'Fixture complete.' });
  expect(response.body.usage).toMatchObject({ input_tokens: 8, output_tokens: 3, total_tokens: 11 });
  const responseReplay = await json(request, 'post', '/v1/responses', { data: responseRequest, headers: responseHeaders });
  expect(responseReplay.response.status()).toBe(409);
  expect(responseReplay.response.headers()['x-original-request-id']).toBe(response.response.headers()['x-request-id']);
  const changedReplay = await json(request, 'post', '/v1/responses', { data: { ...responseRequest, input: 'Different' }, headers: responseHeaders });
  expect(changedReplay.response.status()).toBe(409);
  // GATEWAY-006: read incrementally in the real browser through TLS/edge, not APIRequestContext's buffered body.
  const streamHeaders = { Authorization: `Bearer ${gatewaySecret}`, 'Idempotency-Key': randomUUID() };
  const responseStream = await page.evaluate(async ({ responseRequest, streamHeaders }) => {
    const response = await fetch('/v1/responses', { method: 'POST', headers: { ...streamHeaders, 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...responseRequest, stream: true }) });
    const reader = response.body!.getReader(); const decoder = new TextDecoder(); let body = ''; let incremental = false;
    while (true) {
      const chunk = await reader.read(); if (chunk.done) break;
      body += decoder.decode(chunk.value, { stream: true });
      if (body.includes('event: response.output_text.delta') && !body.includes('event: response.completed')) incremental = true;
    }
    return { status: response.status, contentType: response.headers.get('content-type'), cache: response.headers.get('cache-control'), body, incremental };
  }, { responseRequest, streamHeaders });
  expect(responseStream.status).toBe(200); expect(responseStream.incremental).toBe(true);
  expect(responseStream.contentType).toContain('text/event-stream'); expect(responseStream.cache).toBe('no-store');
  expect(responseStream.body).not.toContain('[DONE]');
  const responseEvents = responseStream.body.trim().split('\n\n').map(frame => JSON.parse(frame.split('\n')[1].slice(6)));
  expect(responseEvents.map(event => event.sequence_number)).toEqual(responseEvents.map((_, index) => index));
  expect(responseEvents.at(-1).type).toBe('response.completed');
  expect(responseEvents.at(-1).response.output[0].content[0].text).toBe('Fixture complete.');
  expect(responseEvents.at(-1).response.usage.total_tokens).toBe(11);
  expect((await json(request, 'post', '/v1/responses', { data: { ...responseRequest, stream: true }, headers: streamHeaders })).response.status()).toBe(409);
  const responseWallet = await json(request, 'get', walletPath);
  expect(responseWallet.body.postedBalanceMicroUsd).toBe('10999956');
  expect(responseWallet.body.reservedBalanceMicroUsd).toBe('0');
  await expect.poll(async () => (await json(request, 'get', activityPath)).body.items.length).toBe(4);
  const responseActivity = (await json(request, 'get', activityPath)).body.items;
  expect(responseActivity.every((item: { chargedMicroUsd: string }) => item.chargedMicroUsd === '11')).toBe(true);
  // GATEWAY-007: actual native embeddings HTTP, input-only pricing and durable settlement.
  const embeddingRequest = { model: 'fixture-embedding', input: ['Hello', 'World'], encoding_format: 'float' };
  const embeddingHeaders = { Authorization: `Bearer ${gatewaySecret}`, 'Idempotency-Key': randomUUID() };
  const embedding = await json(request, 'post', '/v1/embeddings', { data: embeddingRequest, headers: embeddingHeaders });
  expect(embedding.response.status(), JSON.stringify(embedding.body)).toBe(200);
  expect(embedding.response.headers()['cache-control']).toBe('no-store');
  expect(embedding.body.model).toBe('fixture-embedding');
  expect(embedding.body.data).toEqual([{ object: 'embedding', index: 0, embedding: [0.25, -0.5] },
    { object: 'embedding', index: 1, embedding: [0.25, -0.5] }]);
  expect(embedding.body.usage).toEqual({ prompt_tokens: 4, total_tokens: 4 });
  expect((await json(request, 'post', '/v1/embeddings', { data: embeddingRequest, headers: embeddingHeaders })).response.status()).toBe(409);
  const embeddingWallet = (await json(request, 'get', walletPath)).body;
  expect(embeddingWallet.postedBalanceMicroUsd).toBe('10999952');
  expect(embeddingWallet.reservedBalanceMicroUsd).toBe('0');
  await expect.poll(async () => (await json(request, 'get', activityPath)).body.items.length).toBe(5);
  expect((await json(request, 'get', activityPath)).body.items.find((item: { modelCode: string }) => item.modelCode === 'fixture-embedding').chargedMicroUsd).toBe('4');
  // The new path inherits the same real key permission gate, with no extra debit.
  const keyPermissionPath = `/management/v1/api-keys/${key.body.apiKey.id}/permissions`;
  const denyResponse = await request.put(base + keyPermissionPath, { headers: auth, data: { allowedModels: [] } });
  expect(denyResponse.status()).toBe(204);
  const deniedResponse = await json(request, 'post', '/v1/responses', { data: responseRequest, headers: { Authorization: `Bearer ${gatewaySecret}` } });
  expect(deniedResponse.response.status()).toBe(403);
  const deniedEmbedding = await json(request, 'post', '/v1/embeddings', { data: embeddingRequest, headers: { Authorization: `Bearer ${gatewaySecret}` } });
  expect(deniedEmbedding.response.status()).toBe(403);
  expect((await json(request, 'get', walletPath)).body.postedBalanceMicroUsd).toBe('10999952');
  expect((await request.put(base + keyPermissionPath, { headers: auth, data: {} })).status()).toBe(204);

  // FRONTEND-006: real UI commands through TLS edge/session/CSRF/PostgreSQL.
  // No route interception. Secret values stay only in test memory; screenshots
  // below are deliberately taken after the show-once dialog has been destroyed.
  await page.goto(`/organizations/${orgId}/projects?project=${projectId}`);
  await page.getByLabel('Default routing').selectOption('price');
  await page.getByLabel('Environment tag').fill('Staging');
  await page.getByRole('combobox', { name: 'Allowed models', exact: true }).selectOption('selected');
  await page.getByLabel('Allowed models codes').fill('fixture-model');
  await page.getByRole('button', { name: 'Save project settings' }).click();
  await expect(page.getByRole('status')).toContainText('Project settings saved');
  const settingsPath = `/management/v1/organizations/${orgId}/projects/${projectId}/settings`;
  expect((await json(request, 'get', settingsPath)).body).toEqual({ routing: 'price', environment: 'Staging', allowedModels: ['fixture-model'], allowedProviders: null });
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  await page.getByLabel('Requests per minute').fill('120');
  await page.getByLabel('Concurrent requests').fill('4');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByText('Effective 120 · ceiling 600')).toBeVisible();
  await page.getByLabel('Budget amount (USD)').fill('5.000001');
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Budget saved' })).toBeVisible();
  expect((await json(request, 'get', `/management/v1/projects/${projectId}/budgets`)).body)
    .toEqual(expect.arrayContaining([expect.objectContaining({ period: 'Monthly', apiKeyId: null, limitMicroUsd: 5000001 })]));
  await page.reload();
  await expect(page.getByLabel('Environment tag')).toHaveValue('Staging');
  await page.screenshot({ path: testInfo.outputPath('project-controls.png'), fullPage: true });

  await page.goto(`/organizations/${orgId}/keys?project=${projectId}`);
  await page.getByLabel('Key name').fill('Lifecycle key');
  await page.getByLabel('Expiry (UTC, optional)').fill('2099-01-01T12:30');
  const createdResponse = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/api-keys'));
  await page.getByRole('button', { name: 'Create key', exact: true }).click();
  const created = await (await createdResponse).json();
  const lifecycleKeyId: string = created.apiKey.id;
  const originalSecret: string = created.secret;
  await expect(page.getByRole('dialog')).toContainText(originalSecret);
  await page.getByRole('button', { name: 'I saved it' }).click();
  await page.reload();
  await expect(page.getByText(originalSecret)).toHaveCount(0);
  expect(JSON.stringify((await json(request, 'get', `/management/v1/projects/${projectId}/api-keys`)).body)).not.toContain(originalSecret);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(originalSecret);
  await page.getByRole('button', { name: 'Configure Lifecycle key' }).click();
  await page.getByRole('combobox', { name: 'Allowed models', exact: true }).selectOption('selected');
  await page.getByLabel('Allowed models codes').fill('fixture-model');
  await page.getByLabel('Denied provider codes').fill('anthropic');
  await page.getByRole('button', { name: 'Save key permissions' }).click();
  await expect(page.getByRole('status')).toContainText('Key permissions saved');
  expect((await json(request, 'get', `/management/v1/projects/${projectId}/api-keys`)).body.find((item: { id: string }) => item.id === lifecycleKeyId).permissions)
    .toEqual({ allowedModels: ['fixture-model'], allowedProviders: null, deniedModels: [], deniedProviders: ['anthropic'] });
  await page.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
  await page.getByLabel('Requests per minute').fill('30');
  await page.getByLabel('Concurrent requests').fill('2');
  await page.getByRole('button', { name: 'Save request limits' }).click();
  await expect(page.getByText('Effective 30 · ceiling 60')).toBeVisible();
  await page.getByLabel('Budget period').selectOption('Daily');
  await page.getByLabel('Budget amount (USD)').fill('0');
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByText('$0.000000', { exact: true })).toBeVisible();
  const zeroBudget = await json(request, 'post', '/v1/chat/completions', { data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Budget denial' }] },
    headers: { Authorization: `Bearer ${originalSecret}` } });
  expect(zeroBudget.response.status()).toBe(402);
  await page.getByLabel('Budget amount (USD)').fill('1');
  await page.getByRole('button', { name: 'Save spend budget' }).click();
  await expect(page.getByText('$1.000000', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Rotate Lifecycle key' }).click();
  const rotationResponse = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(`/${lifecycleKeyId}/rotate`));
  await page.getByRole('button', { name: 'Confirm rotate' }).click();
  const rotated = await (await rotationResponse).json();
  const replacement: string = rotated.secret;
  expect(rotated.apiKey.id).toBe(lifecycleKeyId);
  await expect(page.getByRole('dialog')).toContainText(replacement);
  await page.getByRole('button', { name: 'I saved it' }).click();
  expect((await json(request, 'get', '/v1/models', { headers: { Authorization: `Bearer ${originalSecret}` } })).response.status()).toBe(401);
  expect((await json(request, 'get', '/v1/models', { headers: { Authorization: `Bearer ${replacement}` } })).response.status()).toBe(200);
  await page.getByRole('button', { name: 'Disable Lifecycle key' }).click();
  await page.getByRole('button', { name: 'Confirm disable' }).click();
  await expect(page.getByRole('button', { name: 'Enable Lifecycle key' })).toBeVisible();
  expect((await json(request, 'get', '/v1/models', { headers: { Authorization: `Bearer ${replacement}` } })).response.status()).toBe(401);
  await page.getByRole('button', { name: 'Enable Lifecycle key' }).click();
  await page.getByRole('button', { name: 'Confirm enable' }).click();
  await expect(page.getByRole('button', { name: 'Disable Lifecycle key' })).toBeVisible();
  expect((await json(request, 'get', '/v1/models', { headers: { Authorization: `Bearer ${replacement}` } })).response.status()).toBe(200);
  await page.getByRole('button', { name: 'Key usage', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Key usage · last 30 days' })).toBeVisible();
  await expect(page.getByText(/As of .* requests unsettled/)).toBeVisible();
  await expect(page.getByText('$0.000000', { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('key-controls.png'), fullPage: true });

  // A disposable second project exercises real create/open/archive without
  // archiving the project needed by subsequent role-denial assertions.
  await page.goto(`/organizations/${orgId}/projects`);
  await page.getByLabel('Project name', { exact: true }).fill('Archive lifecycle');
  await page.getByRole('button', { name: 'Create project', exact: true }).click();
  await page.getByRole('link', { name: 'Open Archive lifecycle', exact: true }).click();
  await page.getByRole('button', { name: 'Archive Archive lifecycle', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm archive' }).click();
  await expect(page.getByText('This project is archived.', { exact: false })).toBeVisible();

  const other = await browser.newContext({ ignoreHTTPSErrors: true });
  try {
    const outsider = `outsider-${randomUUID()}@example.invalid`;
    const registered = await json(other.request, 'post', '/management/v1/auth/register',
      { data: { email: outsider, password } });
    expect(registered.response.status()).toBe(202);
    const outsiderToken = await verificationToken(outsider);
    const verified = await json(other.request, 'post', '/management/v1/auth/verify-email',
      { data: { token: outsiderToken } });
    expect(verified.response.status()).toBe(204);
    const login = await json(other.request, 'post', '/management/v1/auth/login',
      { data: { email: outsider, password } });
    expect(login.response.status()).toBe(204);
    const forbidden = await json(other.request, 'get', walletPath);
    expect(forbidden.response.status()).toBe(403);
    const hidden = await json(other.request, 'get',
      `/management/v1/organizations/${orgId}/usage/requests/${activity.body.items[0].requestId}`);
    expect([403, 404]).toContain(hidden.response.status());
    expect((await json(other.request, 'get', settingsPath)).response.status()).toBe(403);
    expect((await json(other.request, 'get', `/management/v1/api-keys/${lifecycleKeyId}/limits`)).response.status()).toBe(403);

    // Granted Developer can read keys; after a live downgrade the stale screen
    // cannot mutate them. Team setup is fixture-only, not new team UI.
    const invitation = await json(request, 'post', `/management/v1/organizations/${orgId}/team/invitations`,
      { data: { email: outsider, role: 'Developer' }, headers: auth });
    expect(invitation.response.status()).toBe(201);
    const accepted = await json(other.request, 'post', '/management/v1/team/invitations/accept',
      { data: { token: await verificationToken(outsider, 'invitation') }, headers: await csrf(other) });
    expect(accepted.response.status()).toBe(200);
    const grant = await request.put(base + `/management/v1/organizations/${orgId}/team/members/${accepted.body.accountId}/projects/${projectId}`, { headers: auth });
    expect(grant.status()).toBe(204);
    const reader = await other.newPage();
    await reader.goto(base + `/organizations/${orgId}/projects?project=${projectId}`);
    await reader.getByLabel('Environment tag').fill('Forbidden');
    await reader.getByRole('button', { name: 'Save project settings' }).click();
    await expect(reader.getByRole('alert')).toContainText('Permission denied');
    expect((await json(request, 'get', settingsPath)).body.environment).toBe('Staging');
    await reader.goto(base + `/organizations/${orgId}/keys?project=${projectId}`);
    await expect(reader.getByRole('button', { name: 'Disable Lifecycle key' })).toBeVisible();
    const downgrade = await request.patch(base + `/management/v1/organizations/${orgId}/team/members/${accepted.body.accountId}/role`,
      { data: { role: 'ReadOnly' }, headers: auth });
    expect(downgrade.status()).toBe(204);
    await reader.getByLabel('Key name').fill('Forbidden creation');
    await reader.getByRole('button', { name: 'Create key', exact: true }).click();
    await expect(reader.getByRole('alert')).toContainText('Permission denied');
    await reader.getByRole('button', { name: 'Disable Lifecycle key' }).click();
    await reader.getByRole('button', { name: 'Confirm disable' }).click();
    await expect(reader.getByRole('dialog').getByRole('alert')).toContainText('Permission denied');
    await reader.getByRole('button', { name: 'Cancel', exact: true }).click();
    await reader.getByRole('button', { name: 'Configure Lifecycle key' }).click();
    await reader.getByRole('button', { name: 'Save key permissions' }).click();
    await expect(reader.getByRole('form', { name: 'Key permissions' }).getByRole('alert')).toContainText('Permission denied');
    await reader.getByRole('button', { name: 'Limits & budgets', exact: true }).click();
    await reader.getByLabel('Requests per minute').fill('1');
    await reader.getByRole('button', { name: 'Save request limits' }).click();
    await expect(reader.getByRole('form', { name: 'Request limits' }).getByRole('alert')).toContainText('Permission denied');
    expect((await json(request, 'get', `/management/v1/api-keys/${lifecycleKeyId}/limits`)).body.configured.requestsPerMinute).toBe(30);
    await reader.reload();
    await expect(reader.getByRole('alert')).toContainText('Permission denied');
    await expect(reader.getByRole('button', { name: 'Create key', exact: true })).toBeDisabled();
  } finally { await other.close(); }
});
