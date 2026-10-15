import { test, expect, type Page } from '@playwright/test';
import { messages } from '../src/messages';
import { localeTag, type Locale } from '../src/LocaleStore';
import { usageUsdFromMicro, usdFromMicro, uzsFromTiyin } from '../src/LocaleFormatting';

const org = '11111111-1111-4111-8111-111111111111'; const other = '22222222-2222-4222-8222-222222222222';
const project = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; const key = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const now = '2026-10-16T09:04:00Z'; const opaqueName = 'Monthly <img src=x> Production';
const secret = 'uzllm_live_synthetic_localization_secret_only';
const copy = (source: string, locale: Locale) => locale === 'en' ? source : messages[source]?.[locale === 'uz' ? 0 : 1] ?? source;

async function fixture(page: Page) {
  const state = { writes: [] as { path: string; body: Record<string, unknown> | null }[], reads: [] as string[], denied: false, anonymous: false, empty: false,
    budgets: [] as Record<string, unknown>[], settings: { routing: null, environment: 'Production', allowedModels: null, allowedProviders: null } };
  await page.route('**/management/v1/**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (method !== 'GET') state.writes.push({ path, body: request.postData() ? request.postDataJSON() : null }); else state.reads.push(path + new URL(request.url()).search);
    if (path.endsWith('/auth/session')) return state.anonymous ? route.fulfill({ status: 401 }) : json({ accountId: 'owner', email: 'owner@example.invalid', emailVerified: true });
    if (path.endsWith('/organizations')) return method === 'POST' ? json({ id: org }, 201) : json(state.empty ? [] : [{ id: org, name: 'Alpha' }, { id: other, name: 'Beta' }]);
    if (path.endsWith('/projects')) return json([{ id: project, organizationId: org, name: opaqueName, status: 0, settingsJson: '{}' }]);
    if (path.endsWith('/billing/wallet')) return json({ availableBalanceMicroUsd: '9223372036854775807', reservedBalanceMicroUsd: '1234567', recoveryDebtMicroUsd: '1', spendingHeld: true });
    if (state.denied && !path.includes('/billing/')) return route.fulfill({ status: 403 });
    if (path.endsWith('/billing/quotes')) return json({ id: 'quote', amountTiyin: request.postDataJSON().amountTiyin, feeTiyin: '1', creditMicroUsd: '1234567', expiresAt: '2099-12-31T23:59:59Z' });
    if (path.endsWith('/billing/refunds')) return json([{ id: 'refund', settlementId: key, amountMicroUsd: '123456', createdAt: now }]);
    if (path.endsWith('/billing/topups')) return json(['Pending', 'Paid', 'Canceled'].map((status, index) => ({ id: `payment-${index}`, provider: 'Payme', status, amountTiyin: '10000000', creditMicroUsd: '1234567', createdAt: now, hasCredit: index > 0, hasReversal: index === 2, hasOpenReconciliationCase: false })));
    if (path.endsWith('/settings')) { if (method === 'PUT') Object.assign(state.settings, request.postDataJSON()); return json(state.settings); }
    if (path.endsWith('/limits')) return json({ configured: {}, effective: { requestsPerMinute: 600, concurrency: 10 }, ceiling: { requestsPerMinute: 600, concurrency: 10 } });
    if (path.includes('/budgets/')) { const value = { id: key, period: path.split('/').at(-1), ...request.postDataJSON(), capturedMicroUsd: 1, reservedMicroUsd: 2, windowEnd: now }; state.budgets.push(value); return json(value); }
    if (path.endsWith('/budgets')) return json(state.budgets);
    if (path.endsWith('/api-keys')) return method === 'POST' ? json({ id: key, secret }, 201) : json([{ id: key, name: 'Integration key', prefix: 'uzllm_live_abcd', status: 0, createdAt: now, expiresAt: null, permissions: null }]);
    if (path.endsWith('/team/members')) return json([{ accountId: 'owner', email: 'owner@example.invalid', role: 'Owner', status: 'Active', createdAt: now, projectIds: [] }]);
    if (path.endsWith('/team/invitations')) return json([]);
    if (path.endsWith('/provider-keys/providers')) return json([{ id: 'provider', code: 'openai', name: 'OpenAI' }]);
    if (path.endsWith('/provider-keys')) return json([]);
    if (path.includes('/alerts/')) return json([]);
    if (path.endsWith('/payload-retention')) return json({ enabled: false, retentionMinutes: 1440 });
    if (path.endsWith('/catalog/models')) return json({ dataAsOf: now, markupBasisPoints: 2501, fixedFeeMicroUsdPerRequest: '1', models: [{ id: 'gpt-test', displayName: 'GPT fixture', contextLength: 8192, maxOutputTokens: 1024, capabilities: ['Text', 'Tools'], status: 'Published', providers: [{ mappingId: 'mapping', provider: 'openai', providerName: 'OpenAI', capabilities: ['Text', 'Tools'], priceVersionId: 'price', priceEffectiveFrom: now, inputPriceMicroUsdPerMillion: '0.125', outputPriceMicroUsdPerMillion: '2500000', cachedInputPriceMicroUsdPerMillion: null }] }] });
    if (path.endsWith('/usage/activity')) return json({ items: [], nextCursor: null });
    if (path.includes('/usage/')) {
      if (path.endsWith('/summary')) return json({ requestCount: 12345, completedCount: 1, inputTokens: 1000, outputTokens: 1234, errorRatePercent: 1.25, errorCount: 1, chargedMicroUsd: '1234567', refundedMicroUsd: '1', netChargedMicroUsd: '1234566', dataAsOf: now, aggregation: { pendingBucketCount: 1, oldestPendingAt: now }, topModels: [], topProviders: [] });
      return json({ items: [], dataAsOf: now });
    }
    return route.fulfill({ status: 404 });
  });
  return state;
}
async function language(page: Page, locale: Locale) {
  await page.getByRole('combobox', { name: /^(Language|Til|Язык)$/, exact: true }).selectOption(locale);
  await expect(page.locator('html')).toHaveAttribute('lang', localeTag(locale));
}
async function namedControls(page: Page) {
  for (const control of await page.locator('main :is(input,select,textarea,button)').all()) {
    if (await control.isVisible()) await expect(control).toHaveAccessibleName(/\S/);
  }
}

for (const locale of ['en', 'uz', 'ru'] as Locale[]) test(`${locale}: every customer page has localized navigation, headings, labels and exact units`, async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/overview`); await language(page, locale);
  const pages = { overview: 'Overview', projects: 'Projects', keys: 'API keys', models: 'Models', billing: 'Billing', activity: 'Activity', analytics: 'Analytics', team: 'Team', 'provider-keys': 'Provider keys', routing: 'Routing', alerts: 'Alerts', privacy: 'Privacy' };
  for (const [path, title] of Object.entries(pages)) {
    await page.getByRole('navigation', { name: copy('Main navigation', locale) }).getByRole('link', { name: copy(title, locale), exact: true }).click();
    await expect(page.getByRole('heading', { name: copy(title, locale), exact: true }).first()).toBeVisible();
    if (path !== 'overview') await expect(page.locator('main')).toBeFocused();
    await expect(page.getByRole('combobox', { name: copy('Project', locale), exact: true })).toContainText(opaqueName);
    await namedControls(page);
    if (path === 'billing') {
      await expect(page.locator('main')).toContainText(usdFromMicro('9223372036854775807', locale));
      await expect(page.locator('main')).toContainText(copy('Reversed', locale));
      await page.getByLabel(copy('Amount (UZS)', locale)).fill('100000'); await page.getByRole('button', { name: copy('Get quote', locale) }).click();
      await expect(page.getByRole('status')).toContainText(uzsFromTiyin('10000000', locale));
      await expect(page.getByRole('status')).toContainText(uzsFromTiyin('1', locale));
      expect(state.writes.at(-1)?.body).toEqual({ provider: 'Payme', amountTiyin: '10000000' });
    }
    if (path === 'models') await expect(page.locator('main')).toContainText(locale === 'en' ? '$0.000000125' : (locale === 'ru' ? '0,000000125 USD' : '0,000000125 USD'));
    if (path === 'models') await expect(page.locator('main')).toContainText(locale === 'en' ? '25.01%' : '25,01%');
    if (path === 'team') {
      await page.getByLabel(copy('Search members', locale)).fill(copy('Owner', locale)); await expect(page.getByRole('listitem').filter({ hasText: 'owner@example.invalid' })).toBeVisible();
      await page.getByLabel(copy('Search members', locale)).fill(copy('Active', locale)); await expect(page.getByRole('listitem').filter({ hasText: 'owner@example.invalid' })).toBeVisible();
    }
  }
  expect(state.writes).toHaveLength(1);
  expect(await page.locator('main img').count()).toBe(0);
});

test('language changes preserve drafts, canonical budget/routing values and show-once secret lifetime', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/projects`);
  await page.getByLabel('Project name', { exact: true }).fill('Unsaved draft'); await language(page, 'uz');
  await expect(page.getByLabel('Loyiha nomi', { exact: true })).toHaveValue('Unsaved draft');
  await page.getByRole('button', { name: 'Limitlar va byudjetlar' }).click(); await page.getByLabel('Byudjet davri').selectOption('Monthly');
  await page.getByLabel('Byudjet miqdori (USD)').fill('1.234567'); await page.getByRole('button', { name: 'Sarf byudjetini saqlash' }).click();
  await expect.poll(() => state.writes.length).toBe(1); expect(state.writes[0].body).toEqual({ limitMicroUsd: 1234567, apiKeyId: null }); expect(state.writes[0].path).toContain('/budgets/Monthly');
  await expect(page.locator('main')).toContainText(usageUsdFromMicro('1234567', 'uz'));
  await page.getByRole('link', { name: 'Yo‘naltirish', exact: true }).click(); await page.getByLabel('Standart yo‘naltirish').selectOption('price');
  await page.getByRole('button', { name: 'Loyiha sozlamalarini saqlash' }).click(); await expect.poll(() => state.writes.length).toBe(2);
  expect(state.writes[1].body).toEqual({ routing: 'price', environment: 'Production', allowedModels: null, allowedProviders: null });
  await page.getByRole('link', { name: 'API kalitlari', exact: true }).click(); await page.getByLabel('Kalit nomi').fill('New key'); await page.getByRole('button', { name: 'Kalit yaratish', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText(secret); await expect(page.getByRole('button', { name: 'Saqlab oldim' })).toBeFocused();
  // Native modal keeps the global language selector inert. A cross-tab preference
  // event updates copy without remounting or extending the secret's lifetime.
  await page.evaluate(() => { localStorage.setItem('uzllm.ui.locale.v1', 'ru'); window.dispatchEvent(new StorageEvent('storage', { key: 'uzllm.ui.locale.v1', newValue: 'ru' })); });
  await expect(page.getByRole('dialog')).toHaveAccessibleName('Ваш новый API-ключ'); await expect(page.getByRole('dialog')).toContainText(secret);
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.locator('body')).not.toContainText(secret);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(secret);
  expect(state.writes).toHaveLength(3);
});

test('preferences persist and invalid or unavailable browser storage fails safely without API writes', async ({ page }) => {
  const state = await fixture(page); await page.goto(`/organizations/${org}/overview`); await language(page, 'ru');
  await page.getByLabel('Часовой пояс отображения').selectOption('utc'); await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru-RU'); await expect(page.getByLabel('Часовой пояс отображения')).toHaveValue('utc');
  await page.evaluate(() => { localStorage.setItem('uzllm.ui.locale.v1', '<script>invalid</script>'); localStorage.setItem('uzllm.ui.timezone.v1', 'arbitrary'); }); await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US'); await expect(page.getByLabel('Display time zone')).toHaveValue('local');
  await page.addInitScript(() => { Object.defineProperty(Storage.prototype, 'getItem', { value: () => { throw new Error('storage denied'); } }); Object.defineProperty(Storage.prototype, 'setItem', { value: () => { throw new Error('storage denied'); } }); });
  await page.reload(); await language(page, 'uz'); await expect(page.getByRole('heading', { name: 'Umumiy ko‘rinish' })).toBeVisible();
  expect(state.writes).toEqual([]);
});

test('display timezone changes dates, not UTC analytics query windows or financial data', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ baseURL, timezoneId: 'Asia/Tashkent' }); const page = await context.newPage(); const state = await fixture(page);
  try {
    await page.goto(`/organizations/${org}/billing`); await language(page, 'uz');
    await expect(page.locator('main')).toContainText('14:04:00');
    await page.getByLabel('Ko‘rsatish vaqt mintaqasi').selectOption('utc'); await expect(page.locator('main')).toContainText('09:04:00 UTC');
    await page.getByRole('link', { name: 'Tahlil', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Tahlil' })).toBeVisible();
    const before = await page.getByLabel('Boshlanish (UTC)').inputValue(); await page.getByLabel('Ko‘rsatish vaqt mintaqasi').selectOption('local');
    await expect(page.getByLabel('Boshlanish (UTC)')).toHaveValue(before);
    const usage = state.reads.filter(value => value.includes('/usage/summary')); expect(usage.length).toBeGreaterThan(0);
    for (const url of usage) expect(new URL(url, 'https://fixture.invalid').searchParams.get('from')).toBe(`${before}T00:00:00.000Z`);
    expect(state.writes).toEqual([]);
  } finally { await context.close(); }
});

for (const locale of ['uz', 'ru'] as Locale[]) test(`${locale}: auth, onboarding, invitation and errors are localized without retaining tokens`, async ({ page }) => {
  const state = await fixture(page); state.anonymous = true; await page.goto('/login'); await language(page, locale);
  for (const [path, title] of [['login', 'Sign in'], ['register', 'Create your account'], ['verify-email', 'Verify your email'], ['recover', 'Recover your account'], ['reset-password', 'Set a new password']]) {
    await page.goto('/' + path); await expect(page.getByRole('heading', { name: copy(title, locale), exact: true })).toBeVisible(); await namedControls(page);
  }
  await page.goto('/accept-invitation#token=accidental-token'); await expect(page.getByRole('heading', { name: copy('Sign in to accept', locale) })).toBeVisible(); expect(page.url()).not.toContain('token=');
  state.anonymous = false; state.empty = true; await page.goto('/'); await expect(page.getByRole('heading', { name: copy('Start with your organization.', locale) })).toBeVisible(); await namedControls(page);
  state.empty = false; state.denied = true; await page.goto(`/organizations/${org}/team`);
  await expect(page.getByRole('alert')).toContainText(copy('Permission denied. Your current role, project grant or session does not allow this operation. Ask an organization owner; no change was saved.', locale));
  await language(page, locale === 'uz' ? 'ru' : 'uz'); await expect(page.getByRole('alert')).not.toContainText('Permission denied'); expect(state.writes).toEqual([]);
});

test('Russian mobile/large-text UI has named controls, visible keyboard focus, safe dialog focus and no page overflow', async ({ page }, testInfo) => {
  await fixture(page); await page.emulateMedia({ reducedMotion: 'reduce' }); await page.setViewportSize({ width: 375, height: 812 });
  await page.goto(`/organizations/${org}/privacy`); await language(page, 'ru');
  await page.addStyleTag({ content: ':root { font-size: 200% !important; }' });
  await page.getByRole('button', { name: 'Настроить хранение' }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog')).toHaveAccessibleName('Настроить хранение проекта'); await expect(page.getByRole('button', { name: 'Отмена', exact: true })).toBeFocused(); await namedControls(page);
  await page.keyboard.press('Shift+Tab'); expect(await page.getByRole('dialog').evaluate(dialog => dialog.contains(document.activeElement))).toBe(true);
  await expect(page.getByRole('button', { name: 'Сохранить политику хранения' })).toBeDisabled();
  await page.keyboard.press('Escape'); await expect(page.getByRole('button', { name: 'Настроить хранение' })).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('localization-ru-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addStyleTag({ content: ':root { font-size: 100% !important; }' });
  await page.getByRole('link', { name: 'Биллинг', exact: true }).click(); await page.screenshot({ path: testInfo.outputPath('localization-ru-desktop.png'), fullPage: true });
  await language(page, 'uz'); await page.screenshot({ path: testInfo.outputPath('localization-uz-desktop.png'), fullPage: true });
});
