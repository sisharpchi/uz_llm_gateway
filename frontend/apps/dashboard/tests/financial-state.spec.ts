import { test, expect, type Page } from '@playwright/test';

const first = '11111111-1111-1111-1111-111111111111';
const second = '22222222-2222-2222-2222-222222222222';
const settlement = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

async function mockCustomer(page: Page) {
  const calls: string[] = [];
  await page.route('**/management/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    calls.push(path);
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return json([{ id: first, name: 'Alpha', status: 0 }, { id: second, name: 'Beta', status: 0 }]);
    if (path.endsWith('/projects')) return json([]);
    if (path.endsWith('/billing/wallet')) return json({ organizationId: path.includes(first) ? first : second,
      postedBalanceMicroUsd: '2500000', reservedBalanceMicroUsd: '500000', availableBalanceMicroUsd: '2000000',
      recoveryDebtMicroUsd: path.includes(first) ? '300000' : '0', spendingHeld: path.includes(first), version: 3 });
    if (path.endsWith('/billing/topups')) return json(path.includes(first) ? [
      { id: 'pending', organizationId: first, provider: 'Payme', status: 'Pending', amountTiyin: '10000000', creditMicroUsd: '7000000', createdAt: '2026-09-28T00:00:00Z', hasCredit: false, hasReversal: false, hasOpenReconciliationCase: false },
      { id: 'credited', organizationId: first, provider: 'Click', status: 'Paid', amountTiyin: '20000000', creditMicroUsd: '15000000', createdAt: '2026-09-28T00:00:00Z', hasCredit: true, hasReversal: false, hasOpenReconciliationCase: false },
      { id: 'reversed', organizationId: first, provider: 'Payme', status: 'Canceled', amountTiyin: '30000000', creditMicroUsd: '20000000', createdAt: '2026-09-28T00:00:00Z', hasCredit: true, hasReversal: true, hasOpenReconciliationCase: false },
      { id: 'disputed', organizationId: first, provider: 'Click', status: 'Paid', amountTiyin: '40000000', creditMicroUsd: '30000000', createdAt: '2026-09-28T00:00:00Z', hasCredit: true, hasReversal: false, hasOpenReconciliationCase: true }
    ] : []);
    if (path.endsWith('/billing/refunds')) return json(path.includes(first) ? [{ id: 'refund', settlementId: settlement, organizationId: first, amountMicroUsd: '250000', createdAt: '2026-09-28T00:00:00Z' }] : []);
    return route.fulfill({ status: 404 });
  });
  return calls;
}

test('customer sees distinct financial states, debt and wallet-only refunds without operator evidence', async ({ page }) => {
  const calls = await mockCustomer(page);
  await page.goto(`/organizations/${first}/billing`);
  await expect(page.getByText('Managed spending is on hold.')).toBeVisible();
  await expect(page.getByText('$0.30')).toBeVisible();
  for (const state of ['Pending', 'Credited', 'Reversed', 'Under review'])
    await expect(page.getByText(state, { exact: true })).toBeVisible();
  await expect(page.getByText('Wallet-credit refunds')).toBeVisible();
  await expect(page.getByText('$0.25 credited')).toBeVisible();
  expect(calls).toContain(`/management/v1/organizations/${first}/billing/refunds`);
  expect(calls.some(path => path.includes('/admin/'))).toBe(false);
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(second);
  await page.getByRole('link', { name: 'Billing' }).click();
  await expect(page.getByText('Managed spending is on hold.')).toHaveCount(0);
  await expect(page.getByText('$0.25 credited')).toHaveCount(0);
});

test('operator investigates cases, debt and refund using MFA-gated APIs', async ({ page }) => {
  const calls: string[] = [];
  await page.route('**/management/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    calls.push(`${route.request().method()} ${path}`);
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'operator', email: 'ops@example.uz', isOperator: true, emailVerified: true });
    if (path.endsWith('/admin/access')) return json({ recentMfa: true });
    if (path.endsWith('/admin/controls')) return json([]);
    if (path.endsWith('/admin/payments')) return json([{ id: first, organizationId: first, provider: 'Click', localStatus: 'Paid', providerObservation: 'VerifiedCallbackSeen', amountTiyin: '10000000', creditMicroUsd: '7000000', externalTransactionId: 'external-1', hasCredit: true, hasReversal: false, reconciliationReason: 'amount_mismatch', reconciliationStatus: 'Open', callbackCount: 2, createdAt: '2026-09-28T00:00:00Z' }]);
    if (path.endsWith('/admin/payment-reconciliation/cases')) return json([{ id: second, intentId: first, provider: 'Click', externalTransactionId: 'external-1', reason: 'amount_mismatch', status: 'Open', createdAt: '2026-09-28T00:00:00Z', resolvedAt: null, resolutionReference: null }]);
    if (path.endsWith('/admin/refunds') && route.request().method() === 'POST') return json({ id: 'refund', settlementId: settlement, organizationId: first, actorAccountId: 'operator', refundKey: 'key', amountMicroUsd: '250000', reason: 'verified overcharge', createdAt: '2026-09-28T00:00:00Z', duplicate: false }, 201);
    if (path.endsWith('/admin/refunds')) return json([]);
    if (path.endsWith('/admin/financial/risk')) return json({ dataAsOf: '2026-09-28T00:00:00Z', pending: [], exposure: [], debt: [{ organizationId: first, outstandingMicroUsd: '300000', spendingHeld: true }] });
    if (path.endsWith('/resolve')) return route.fulfill({ status: 204 });
    return route.fulfill({ status: 404 });
  });
  page.once('dialog', dialog => dialog.accept());
  await page.goto('http://127.0.0.1:5175');
  await page.getByRole('button', { name: 'Payments', exact: true }).click();
  await expect(page.getByText('Disputed')).toBeVisible();
  await expect(page.getByText('amount_mismatch').first()).toBeVisible();
  await expect(page.getByText('Spending holds')).toBeVisible();
  await page.getByRole('textbox', { name: 'Investigation reason' }).fill('Merchant statement checked');
  await page.getByRole('textbox', { name: 'Resolution reference' }).fill('statement-row-1');
  await page.getByRole('button', { name: 'Resolve' }).click();
  await expect(page.getByText('Case resolved and audited; wallet unchanged.')).toBeVisible();
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('textbox', { name: 'Settlement ID' }).fill(settlement);
  await page.getByRole('textbox', { name: 'Amount (micro-USD)' }).fill('250000');
  await page.getByRole('textbox', { name: 'Auditable reason' }).fill('verified overcharge');
  await page.getByRole('button', { name: 'Issue wallet credit' }).click();
  await expect(page.getByText('Wallet-credit refund posted and audited.')).toBeVisible();
  expect(calls).toContain(`PATCH /management/v1/admin/payment-reconciliation/cases/${second}/resolve`);
  expect(calls).toContain('POST /management/v1/admin/refunds');
  expect(calls.some(call => call.includes(`/organizations/${first}/billing/`))).toBe(false);
});
