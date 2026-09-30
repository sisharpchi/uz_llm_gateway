import { expect, test, type Page } from '@playwright/test';

const alpha = '11111111-1111-1111-1111-111111111111';
const beta = '22222222-2222-2222-2222-222222222222';
const alphaProject = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const betaProject = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

async function mockCatalog(page: Page) {
  const calls: string[] = [];
  await page.route('**/management/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    calls.push(path);
    const ok = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path.endsWith('/auth/session')) return ok({ accountId: 'owner', email: 'owner@example.uz', emailVerified: true, isOperator: false });
    if (path.endsWith('/organizations')) return ok([{ id: alpha, name: 'Alpha', status: 0 }, { id: beta, name: 'Beta', status: 0 }]);
    if (path.endsWith('/projects')) return ok([{ id: path.includes(alpha) ? alphaProject : betaProject, name: 'Main', status: 0 }]);
    if (path.endsWith('/billing/wallet')) return ok({ organizationId: alpha, availableBalanceMicroUsd: '1000000' });
    if (path.endsWith('/catalog/models')) return ok({ dataAsOf: '2026-09-30T00:00:00Z',
      feePolicyVersionId: 'fee-version', markupBasisPoints: 2500, fixedFeeMicroUsdPerRequest: '100',
      models: path.includes(alpha) ? [{ id: 'gpt-catalog', displayName: 'Catalog Chat', contextLength: 8192,
        maxOutputTokens: 1024, capabilities: ['Text', 'Tools'], status: 'Published',
        providers: [{ mappingId: 'mapping-a', provider: 'openai', providerName: 'OpenAI', capabilities: ['Text', 'Tools'],
          priceVersionId: 'price-a', priceEffectiveFrom: '2026-09-29T00:00:00Z', priceEffectiveTo: null,
          inputPriceMicroUsdPerMillion: '1250000', outputPriceMicroUsdPerMillion: '2500000',
          cachedInputPriceMicroUsdPerMillion: '625000' },
        { mappingId: 'mapping-b', provider: 'anthropic', providerName: 'Anthropic', capabilities: ['Text'],
          priceVersionId: 'price-b', priceEffectiveFrom: '2026-09-29T00:00:00Z', priceEffectiveTo: null,
          inputPriceMicroUsdPerMillion: '2000000', outputPriceMicroUsdPerMillion: '3000000',
          cachedInputPriceMicroUsdPerMillion: null }] }] : [] });
    return route.fulfill({ status: 404 });
  });
  return calls;
}

test('published model detail shows effective managed rates and a valid chat quickstart without a secret', async ({ page }) => {
  const calls = await mockCatalog(page);
  await page.goto(`/organizations/${alpha}/models?project=${alphaProject}`);
  await expect(page.getByRole('heading', { name: 'Models' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toBeVisible();
  await expect(page.getByText('Input $1.25 / 1M')).toBeVisible();
  await expect(page.getByText('Output $2.50 / 1M')).toBeVisible();
  await expect(page.getByText('A fixed $0.000100 is added per request.', { exact: false })).toBeVisible();
  const example = await page.locator('.catalog-detail pre').innerText();
  expect(example).toContain('http://localhost:5214/v1/chat/completions');
  expect(example).toContain('Authorization: Bearer YOUR_API_KEY');
  expect(example).not.toContain('uzllm_live_');
  const json = example.match(/ -d '([^']+)'$/)?.[1];
  expect(json).toBeTruthy();
  expect(JSON.parse(json!)).toEqual({ model: 'gpt-catalog', messages: [{ role: 'user', content: 'Hello from UZLLM' }],
    max_completion_tokens: 100, stream: false });
  expect(calls).toContain(`/management/v1/organizations/${alpha}/projects/${alphaProject}/catalog/models`);
  expect(calls.some(path => path.includes('/admin/'))).toBe(false);
});

test('catalog filters provider capabilities and clears tenant data on organization switch', async ({ page }) => {
  const calls = await mockCatalog(page);
  await page.goto(`/organizations/${alpha}/models?project=${alphaProject}`);
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Provider' }).selectOption('anthropic');
  await page.getByRole('combobox', { name: 'Capability' }).selectOption('Tools');
  await expect(page.getByText('No published models match these filters.')).toBeVisible();
  await page.getByRole('combobox', { name: 'Capability' }).selectOption('Text');
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toBeVisible();
  await page.getByRole('spinbutton', { name: 'Max input price · USD / 1M' }).fill('1.50');
  await expect(page.getByText('No published models match these filters.')).toBeVisible();
  await page.getByRole('combobox', { name: 'Provider' }).selectOption('openai');
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toBeVisible();
  await page.getByRole('spinbutton', { name: 'Min context tokens' }).fill('9000');
  await expect(page.getByText('No published models match these filters.')).toBeVisible();
  await page.getByRole('spinbutton', { name: 'Min context tokens' }).fill('');
  await page.getByRole('spinbutton', { name: 'Max input price · USD / 1M' }).fill('');
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Organization' }).selectOption(beta);
  await page.getByRole('link', { name: 'Models' }).click();
  await expect(page.getByRole('heading', { name: 'Catalog Chat' })).toHaveCount(0);
  expect(calls).toContain(`/management/v1/organizations/${beta}/projects/${betaProject}/catalog/models`);
});

test('catalog denial shows a safe project access message without price data', async ({ page }) => {
  await mockCatalog(page);
  await page.route(`**/organizations/${alpha}/projects/${alphaProject}/catalog/models`, route =>
    route.fulfill({ status: 403, contentType: 'application/json', body: '{}' }));
  await page.goto(`/organizations/${alpha}/models?project=${alphaProject}`);
  await expect(page.getByRole('alert')).toHaveText('You do not have access to this project.');
  await expect(page.getByText('Catalog Chat')).toHaveCount(0);
});
