import { expect, test } from '@playwright/test';

test('Aspire browser surfaces proxy Management from dashboard and admin', async ({ page, request }) => {
  const dashboard = process.env.UZLLM_ASPIRE_DASHBOARD_URL;
  const admin = process.env.UZLLM_ASPIRE_ADMIN_URL;
  test.skip(!dashboard || !admin, 'Run against the Aspire stack with both URL variables set.');

  await page.goto(dashboard!);
  await expect(page.locator('#root')).toBeVisible();
  const dashboardApi = await request.get(`${dashboard}/management/v1/auth/login`);
  expect(dashboardApi.status()).toBe(405);
  expect(dashboardApi.headers()['x-request-id']).toBeTruthy();

  await page.goto(admin!);
  await expect(page.locator('#root')).toBeVisible();
  const adminApi = await request.get(`${admin}/management/v1/auth/login`);
  expect(adminApi.status()).toBe(405);
  expect(adminApi.headers()['x-request-id']).toBeTruthy();
});
