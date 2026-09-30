import { test, expect } from '@playwright/test';

test('verification link confirms once, scrubs the secret, and explains an expired or replayed token', async ({ page }) => {
  const tokens: string[] = [];
  await page.route('**/management/v1/auth/verify-email', route => {
    const token = route.request().postDataJSON().token as string;
    tokens.push(token);
    return route.fulfill({ status: tokens.length === 1 ? 204 : 400 });
  });
  await page.goto('/verify-email#token=one-time-verification');
  await expect(page).toHaveURL(/\/verify-email$/);
  await expect(page.getByRole('textbox', { name: 'Verification token' })).toHaveValue('one-time-verification');
  await page.getByRole('button', { name: 'Verify your email' }).click();
  await expect(page.getByRole('status')).toContainText('Email verified.');
  await expect(page.getByText('one-time-verification')).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain('one-time-verification');

  await page.goto('/verify-email#token=one-time-verification');
  await page.getByRole('button', { name: 'Verify your email' }).click();
  await expect(page.getByRole('alert')).toContainText('invalid, expired, or already used');
  expect(tokens).toEqual(['one-time-verification', 'one-time-verification']);
});

test('recovery acceptance is identical for registered and unknown email', async ({ page }) => {
  const emails: string[] = [];
  await page.route('**/management/v1/auth/recover', route => {
    emails.push(route.request().postDataJSON().email as string);
    return route.fulfill({ status: 202 });
  });
  for (const email of ['known@example.uz', 'unknown@example.uz']) {
    await page.goto('/recover');
    await page.getByRole('textbox', { name: 'Email' }).fill(email);
    await page.getByRole('button', { name: 'Recover your account' }).click();
    await expect(page.getByRole('status')).toHaveText(/If an account exists for this email, we sent password reset instructions/);
    await expect(page.getByText(email)).toHaveCount(0);
  }
  expect(emails).toEqual(['known@example.uz', 'unknown@example.uz']);
});

test('reset link handles success and replay without retaining the token', async ({ page }) => {
  const bodies: { token: string; newPassword: string }[] = [];
  await page.route('**/management/v1/auth/reset-password', route => {
    bodies.push(route.request().postDataJSON());
    return route.fulfill({ status: bodies.length === 1 ? 204 : 400 });
  });
  await page.goto('/reset-password#token=recovery-secret');
  await expect(page).toHaveURL(/\/reset-password$/);
  await page.getByRole('textbox', { name: 'New password' }).fill('new-password-at-least-12');
  await page.getByRole('button', { name: 'Set a new password' }).click();
  await expect(page.getByRole('status')).toContainText('Password updated.');
  await expect(page.getByText('recovery-secret')).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain('recovery-secret');
  await page.goto('/reset-password#token=recovery-secret');
  await page.getByRole('textbox', { name: 'New password' }).fill('new-password-at-least-12');
  await page.getByRole('button', { name: 'Set a new password' }).click();
  await expect(page.getByRole('alert')).toContainText('invalid, expired, or already used');
  expect(bodies).toEqual([
    { token: 'recovery-secret', newPassword: 'new-password-at-least-12' },
    { token: 'recovery-secret', newPassword: 'new-password-at-least-12' }
  ]);
});

test('throttled recovery and verification show a neutral retry message', async ({ page }) => {
  await page.route('**/management/v1/auth/recover', route => route.fulfill({ status: 429, headers: { 'Retry-After': '120' } }));
  await page.goto('/recover');
  await page.getByRole('textbox', { name: 'Email' }).fill('any@example.uz');
  await page.getByRole('button', { name: 'Recover your account' }).click();
  await expect(page.getByRole('alert')).toHaveText('Too many attempts. Wait a few minutes before trying again.');
  await page.route('**/management/v1/auth/verify-email', route => route.fulfill({ status: 429 }));
  await page.goto('/verify-email#token=limited-token');
  await page.getByRole('button', { name: 'Verify your email' }).click();
  await expect(page.getByRole('alert')).toHaveText('Too many attempts. Wait a few minutes before trying again.');
});
