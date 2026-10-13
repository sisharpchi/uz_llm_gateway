import { defineConfig } from '@playwright/test';

const baseURL = process.env.UZLLM_ACCEPTANCE_BASE_URL;
if (!baseURL) throw new Error('UZLLM_ACCEPTANCE_BASE_URL is required. Run deploy/tests/run-launch-acceptance.mjs.');

export default defineConfig({
  testDir: './tests',
  testMatch: '*real-stack.spec.ts',
  timeout: 180_000,
  workers: 1,
  retries: 0,
  use: { baseURL, browserName: 'chromium', ignoreHTTPSErrors: true,
    trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  outputDir: '../../../artifacts/acceptance/playwright',
});
