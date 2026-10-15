import { randomUUID } from 'node:crypto';
import { test, expect } from '@playwright/test';

const base = process.env.UZLLM_ACCEPTANCE_BASE_URL!; const mailbox = process.env.UZLLM_ACCEPTANCE_MAILPIT_URL!;
const edgeBudget = (slots = 1) => new Promise(done => setTimeout(done, 2200 * slots));
async function verification(email: string) {
  for (let attempt = 0; attempt < 60; attempt++) {
    const list = await fetch(mailbox + '/api/v1/messages').then(value => value.json());
    const item = list.messages?.find((value: { To: { Address: string }[]; Subject: string }) => value.Subject.includes('Verify') && value.To.some(to => to.Address === email));
    if (item) { const text = await fetch(`${mailbox}/view/${item.ID}.txt`).then(value => value.text()); const token = text.match(/verification page:\s*\n\s*([A-Za-z0-9_-]+)/)?.[1]; if (token) return token; }
    await new Promise(done => setTimeout(done, 1000));
  }
  throw new Error('Synthetic verification mail was not delivered.');
}

test('FRONTEND_010_real_localized_registration_budget_topup_payment_inference_activity_and_preferences', async ({ page }, testInfo) => {
  await edgeBudget(10); const email = `locale-${randomUUID()}@example.invalid`; const password = 'Locale-acceptance-password-only-123';
  await page.goto('/register'); await page.getByRole('combobox', { name: 'Language', exact: true }).selectOption('uz');
  await page.getByLabel('Elektron pochta', { exact: true }).fill(email); await page.getByLabel('Parol', { exact: true }).fill(password);
  await edgeBudget(); await page.getByRole('button', { name: 'Akkauntingizni yarating' }).click(); await expect(page.getByRole('status')).toContainText('tasdiqlash tokeni');
  const token = await verification(email); await edgeBudget(); await page.goto('/verify-email'); await page.getByLabel('Tasdiqlash tokeni', { exact: true }).fill(token);
  await edgeBudget(); await page.getByRole('button', { name: 'Pochtangizni tasdiqlang' }).click(); await expect(page.getByRole('status')).toContainText('Pochta tasdiqlandi');
  await edgeBudget(); await page.goto('/login'); await page.getByLabel('Elektron pochta', { exact: true }).fill(email); await page.getByLabel('Parol', { exact: true }).fill(password);
  await edgeBudget(); await page.getByRole('button', { name: 'Kirish', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Tashkilotingizdan boshlang.' })).toBeVisible();
  await page.getByLabel('Tashkilot nomi').fill('Localized acceptance'); await page.getByRole('button', { name: 'Tashkilot yaratish' }).click();
  await page.getByRole('link', { name: 'Loyihalar', exact: true }).click(); await page.getByLabel('Loyiha nomi', { exact: true }).fill('Production metadata only');
  await page.getByRole('button', { name: 'Loyiha yaratish', exact: true }).click(); await expect(page.getByRole('button', { name: 'Limitlar va byudjetlar' })).toBeVisible();
  const orgId = new URL(page.url()).pathname.split('/')[2]; const projectId = await page.getByLabel('Loyiha', { exact: true }).inputValue();
  await page.getByRole('button', { name: 'Limitlar va byudjetlar' }).click(); await page.getByLabel('Byudjet davri').selectOption('Monthly'); await page.getByLabel('Byudjet miqdori (USD)').fill('10.000000');
  const budgetResponse = page.waitForResponse(value => value.request().method() === 'PUT' && value.url().endsWith('/budgets/Monthly'));
  await page.getByRole('button', { name: 'Sarf byudjetini saqlash' }).click(); expect((await budgetResponse).status()).toBe(200);
  await expect(page.locator('main')).toContainText('10,000000 USD');

  await page.getByRole('link', { name: 'API kalitlari', exact: true }).click(); await page.getByLabel('Kalit nomi').fill('Localized integration');
  const keyResponse = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/api-keys'));
  await page.getByRole('button', { name: 'Kalit yaratish', exact: true }).click(); const keyResult = await keyResponse; expect(keyResult.status()).toBe(201); const issued = await keyResult.json();
  await expect(page.getByRole('button', { name: 'Saqlab oldim' })).toBeFocused(); await page.keyboard.press('Escape'); await expect(page.locator('body')).not.toContainText(issued.secret);
  await page.getByRole('link', { name: 'Hisob-kitob', exact: true }).click(); await page.getByLabel('Miqdor (UZS)').fill('100000');
  const quoteResponse = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/billing/quotes'));
  await page.getByRole('button', { name: 'Hisob taklifini olish' }).click(); const quoteResult = await quoteResponse; expect(quoteResult.status()).toBe(200); const quote = await quoteResult.json(); expect(quote.amountTiyin).toBe('10000000');
  await expect(page.getByRole('status')).toContainText('100 000 UZS');
  const intentResponse = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/billing/topups'));
  await page.getByRole('button', { name: 'Davom etish: Payme' }).click(); const intentResult = await intentResponse; expect(intentResult.status()).toBe(201); const intent = await intentResult.json();
  // Only this disposable harness's synthetic Payme credential. Never contact a
  // live checkout or provider. Existing launch acceptance covers callback replay.
  const headers = { Authorization: `Basic ${Buffer.from(`Paycom:${process.env.UZLLM_ACCEPTANCE_PAYME_KEY}`).toString('base64')}` };
  const transaction = `locale-${randomUUID()}`;
  const created = await page.context().request.post(base + '/payments/payme/callback', { headers, data: { id: 1, method: 'CreateTransaction', params: { id: transaction, time: Date.now(), amount: 10000000, account: { intent_id: intent.intent.id } } } }); expect((await created.json()).result.state).toBe(1);
  const paid = await page.context().request.post(base + '/payments/payme/callback', { headers, data: { id: 2, method: 'PerformTransaction', params: { id: transaction } } }); expect((await paid.json()).result.state).toBe(2);
  await page.getByRole('combobox', { name: 'Til', exact: true }).selectOption('ru'); await page.getByRole('combobox', { name: 'Часовой пояс отображения', exact: true }).selectOption('utc'); await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru-RU'); await expect(page.getByLabel('Часовой пояс отображения')).toHaveValue('utc'); await expect(page.getByText('Зачислено', { exact: true })).toBeVisible();
  const inference = await page.context().request.post(base + '/v1/chat/completions', { headers: { Authorization: `Bearer ${issued.secret}` }, data: { model: 'fixture-model', messages: [{ role: 'user', content: 'Synthetic localization qualification' }], max_completion_tokens: 10, stream: false } }); expect(inference.status()).toBe(200);
  await page.getByRole('link', { name: 'Активность', exact: true }).click(); await expect(page.getByRole('table')).toContainText('Успешно');
  await page.getByRole('button', { name: /Просмотреть запрос/ }).first().click(); await expect(page.getByLabel('Сведения о запросе')).toContainText('Урегулировано'); await expect(page.getByLabel('Сведения о запросе')).toContainText('UTC');
  const wallet = await page.context().request.get(base + `/management/v1/organizations/${orgId}/billing/wallet`); const balance = await wallet.json(); expect(balance.reservedBalanceMicroUsd).toBe('0'); expect(BigInt(balance.postedBalanceMicroUsd)).toBeLessThan(BigInt(quote.creditMicroUsd));
  const budgets = await page.context().request.get(base + `/management/v1/projects/${projectId}/budgets`); expect((await budgets.json()).some((value: { period: string; limitMicroUsd: number }) => value.period === 'Monthly' && value.limitMicroUsd === 10000000)).toBe(true);
  const storage = await page.evaluate(() => ({ ...localStorage, ...sessionStorage })); expect(storage).toEqual({ 'uzllm.ui.locale.v1': 'ru', 'uzllm.ui.timezone.v1': 'utc' }); expect(JSON.stringify(storage)).not.toContain(issued.secret);
  await page.screenshot({ path: testInfo.outputPath('real-localized-activity.png'), fullPage: true });
  await page.getByRole('link', { name: 'Команда', exact: true }).click(); await page.getByLabel('Поиск участников').fill('Владелец');
  await expect(page.getByRole('listitem').filter({ hasText: email })).toBeVisible();
});
