import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  testIgnore: 'real-stack.spec.ts',
  use: { baseURL: process.env.UZLLM_ASPIRE_DASHBOARD_URL ?? 'http://127.0.0.1:5174', browserName: 'chromium', channel: 'chrome' },
  webServer: process.env.UZLLM_ASPIRE_DASHBOARD_URL ? [] : [
    { command: 'npm run dev -- --host 127.0.0.1', url: 'http://127.0.0.1:5174', reuseExistingServer: !process.env.CI },
    { command: 'npm run dev --prefix ../admin -- --host 127.0.0.1', url: 'http://127.0.0.1:5175', reuseExistingServer: !process.env.CI }
  ]
});
