import { test, expect, type Page } from '@playwright/test';
import { tiyinFromWholeUzs, uzsFromTiyin } from '@uzllm/api-client';

const organizationId = '11111111-1111-1111-1111-111111111111';
const quotePath = `**/management/v1/organizations/${organizationId}/billing/quotes`;
const topUpPath = `**/management/v1/organizations/${organizationId}/billing/topups`;

async function mockBilling(page: Page) {
  await page.route('**/management/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return json({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return json([{ id: organizationId, name: 'Alpha', status: 0 }]);
    if (path.endsWith('/projects')) return json([]);
    if (path.endsWith('/billing/wallet')) return json({ organizationId, postedBalanceMicroUsd: '0', reservedBalanceMicroUsd: '0', availableBalanceMicroUsd: '0', version: 0 });
    if (path.endsWith('/billing/topups')) return json([]);
    if (path.endsWith('/billing/refunds')) return json([]);
    return route.fulfill({ status: 404 });
  });
}

test('whole UZS is converted to an exact tiyin decimal string within Int64 bounds', () => {
  expect(tiyinFromWholeUzs('100000')).toBe('10000000');
  expect(tiyinFromWholeUzs('0001')).toBe('100');
  expect(tiyinFromWholeUzs('92233720368547758')).toBe('9223372036854775800');
});

test('fractional, nonpositive, and overflowing UZS amounts are rejected', () => {
  for (const value of ['0', '-1', '1.01', '1,000', ' 1', 'abc', '92233720368547759'])
    expect(() => tiyinFromWholeUzs(value), value).toThrow();
});

test('tiyin amounts display without losing sub-UZS precision', () => {
  expect(uzsFromTiyin('10000000')).toBe('100,000 UZS');
  expect(uzsFromTiyin('1001')).toBe('10.01 UZS');
  expect(uzsFromTiyin('1')).toBe('0.01 UZS');
});

test('invalid UZS input never requests a quote', async ({ page }) => {
  await mockBilling(page);
  let quoteRequests = 0;
  await page.route(quotePath, route => { quoteRequests++; return route.fulfill({ status: 500 }); });
  await page.goto(`/organizations/${organizationId}/billing`);
  for (const value of ['1.5', '92233720368547759', '0']) {
    await page.getByRole('textbox', { name: 'Amount (UZS)' }).fill(value);
    await page.getByRole('button', { name: 'Get quote' }).click();
    await expect(page.getByRole('alert')).toBeVisible();
  }
  expect(quoteRequests).toBe(0);
});

test('a quote with a different amount cannot be paid', async ({ page }) => {
  await mockBilling(page);
  await page.route(quotePath, route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
    id: 'wrong-quote', organizationId, provider: 'Payme', amountTiyin: '100000', feeTiyin: '0', creditMicroUsd: '100000',
    uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60_000).toISOString()
  }) }));
  await page.goto(`/organizations/${organizationId}/billing`);
  await page.getByRole('button', { name: 'Get quote' }).click();
  await expect(page.getByRole('alert')).toContainText('Quote amount differs');
  await expect(page.getByRole('button', { name: /Continue to Payme/ })).toHaveCount(0);
});

test('a payment intent with a different amount cannot expose checkout', async ({ page }) => {
  await mockBilling(page);
  await page.route(quotePath, route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
    id: 'correct-quote', organizationId, provider: 'Payme', amountTiyin: '10000000', feeTiyin: '0', creditMicroUsd: '100000',
    uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60_000).toISOString()
  }) }));
  await page.route(topUpPath, route => route.request().method() === 'POST'
    ? route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({
      intent: { id: 'wrong-intent', organizationId, provider: 'Payme', status: 'Pending', amountTiyin: '100000',
        feeTiyin: '0', creditMicroUsd: '100000', createdAt: new Date().toISOString() },
      duplicate: false, checkoutUrl: 'https://checkout.paycom.uz/wrong'
    }) })
    : route.fallback());
  await page.goto(`/organizations/${organizationId}/billing`);
  await page.getByRole('button', { name: 'Get quote' }).click();
  await page.getByRole('button', { name: 'Continue to Payme' }).click();
  await expect(page.getByRole('alert')).toContainText('Payment amount differs');
  await expect(page.getByRole('link', { name: /Open secure Payme checkout/ })).toHaveCount(0);
});

test('editing UZS input while a quote is pending discards the stale quote', async ({ page }) => {
  await mockBilling(page);
  let releaseQuote!: () => void;
  const gate = new Promise<void>(resolve => { releaseQuote = resolve; });
  await page.route(quotePath, async route => {
    await gate;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
      id: 'stale-quote', organizationId, provider: 'Payme', amountTiyin: '10000000', feeTiyin: '0', creditMicroUsd: '100000',
      uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60_000).toISOString()
    }) });
  });
  await page.goto(`/organizations/${organizationId}/billing`);
  const requested = page.waitForRequest(request => request.url().endsWith('/billing/quotes'));
  await page.getByRole('button', { name: 'Get quote' }).click();
  await requested;
  await page.getByRole('textbox', { name: 'Amount (UZS)' }).fill('200000');
  releaseQuote();
  await expect(page.getByRole('button', { name: 'Get quote' })).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Continue to Payme' })).toHaveCount(0);
});

test('editing UZS input while top-up is pending hides the stale checkout', async ({ page }) => {
  await mockBilling(page);
  await page.route(quotePath, route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
    id: 'current-quote', organizationId, provider: 'Payme', amountTiyin: '10000000', feeTiyin: '0', creditMicroUsd: '100000',
    uzsTiyinPerUsd: '1000000', expiresAt: new Date(Date.now() + 60_000).toISOString()
  }) }));
  let releaseTopUp!: () => void;
  const gate = new Promise<void>(resolve => { releaseTopUp = resolve; });
  await page.route(topUpPath, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await gate;
    await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({
      intent: { id: 'stale-intent', organizationId, provider: 'Payme', status: 'Pending', amountTiyin: '10000000',
        feeTiyin: '0', creditMicroUsd: '100000', createdAt: new Date().toISOString() },
      duplicate: false, checkoutUrl: 'https://checkout.paycom.uz/stale'
    }) });
  });
  await page.goto(`/organizations/${organizationId}/billing`);
  await page.getByRole('button', { name: 'Get quote' }).click();
  const requested = page.waitForRequest(request => request.url().endsWith('/billing/topups') && request.method() === 'POST');
  await page.getByRole('button', { name: 'Continue to Payme' }).click();
  await requested;
  await page.getByRole('textbox', { name: 'Amount (UZS)' }).fill('200000');
  releaseTopUp();
  await expect(page.getByRole('button', { name: 'Get quote' })).toBeEnabled();
  await expect(page.getByRole('link', { name: /Open secure Payme checkout/ })).toHaveCount(0);
});
