import { test, expect, type Page } from '@playwright/test';

const org = '11111111-1111-1111-1111-111111111111';
const otherOrg = '22222222-2222-2222-2222-222222222222';
const project = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const actorId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const memberId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const adminId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
const token = 't'.repeat(43);
const joined = '2026-09-01T00:00:00Z';

async function fixture(page: Page, role = 'Owner') {
  const state = {
    denied: false,
    members: [
      { accountId: actorId, email: 'owner@example.invalid', role, status: 'Active', projectIds: [] as string[], createdAt: joined },
      { accountId: adminId, email: 'admin@example.invalid', role: 'Admin', status: 'Active', projectIds: [] as string[], createdAt: joined },
      { accountId: memberId, email: 'dev@example.invalid', role: 'Developer', status: 'Active', projectIds: [] as string[], createdAt: joined },
    ],
    invitations: [{ id: 'expired', email: 'expired@example.invalid', role: 'ReadOnly', acceptedAt: null, revokedAt: null, expiresAt: joined }],
    writes: [] as { method: string; path: string; body: Record<string, unknown> | null }[],
  };
  if (role === 'Admin') state.members.push({ accountId: 'owner-id', email: 'other-owner@example.invalid', role: 'Owner', status: 'Active', projectIds: [], createdAt: joined });
  await page.route('**/management/v1/**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    const json = (value: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
    if (request.method() !== 'GET') {
      state.writes.push({ method: request.method(), path, body: request.postData() ? request.postDataJSON() : null });
      if (state.denied) return route.fulfill({ status: 403 });
    }
    if (path.endsWith('/auth/session')) return json({ accountId: actorId, email: 'owner@example.invalid', emailVerified: true });
    if (path.endsWith('/organizations')) return json([{ id: org, name: 'Alpha' }, { id: otherOrg, name: 'Beta' }]);
    if (path.endsWith('/projects')) return json([{ id: project, organizationId: org, name: 'Production', status: 0 }]);
    if (path.endsWith('/billing/wallet')) return json({ availableBalanceMicroUsd: '0' });
    if (path.includes(otherOrg + '/team/members')) return json([{ accountId: actorId, email: 'beta-owner@example.invalid', role: 'Owner', status: 'Active', projectIds: [], createdAt: joined }]);
    if (path.endsWith('/team/members')) return state.denied ? route.fulfill({ status: 403 }) : json(state.members);
    if (path.endsWith('/team/invitations')) {
      if (request.method() === 'POST') {
        const body = request.postDataJSON();
        state.invitations.push({ id: 'invited', email: body.email, role: body.role, acceptedAt: null, revokedAt: null, expiresAt: '2099-01-01T00:00:00Z' });
        return json(state.invitations.at(-1), 201);
      }
      return json(path.includes(otherOrg) ? [] : state.invitations);
    }
    if (path.endsWith('/role')) { state.members.find(value => path.includes(value.accountId))!.role = request.postDataJSON().role; return route.fulfill({ status: 204 }); }
    if (path.includes('/members/') && path.includes('/projects/')) {
      const member = state.members.find(value => path.includes(value.accountId))!;
      member.projectIds = request.method() === 'PUT' ? [project] : [];
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith('/members/' + memberId)) { state.members[2].status = 'Revoked'; state.members[2].projectIds = []; return route.fulfill({ status: 204 }); }
    return route.fulfill({ status: 404 });
  });
  return state;
}

test('owner invites, changes roles and grants/revokes project access through explicit scoped commands', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/team`);
  await page.getByLabel('Member email').fill('new@example.invalid');
  await page.getByLabel('Invitation role').selectOption('Admin');
  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByRole('status')).toContainText('Invitation issued');
  expect(state.writes[0]).toEqual({ method: 'POST', path: `/management/v1/organizations/${org}/team/invitations`, body: { email: 'new@example.invalid', role: 'Admin' } });
  await expect(page.getByText('Pending', { exact: true })).toBeVisible();
  await expect(page.getByText('Expired', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Project access for dev@example.invalid' }).click();
  await page.getByLabel('Project to grant').selectOption(project);
  await page.getByRole('button', { name: 'Grant project access', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Confirm change' }).click();
  await expect(page.getByRole('button', { name: 'Remove access to Production' })).toBeVisible();
  expect(state.writes.at(-1)?.method).toBe('PUT');
  expect(state.writes.at(-1)?.path).toBe(`/management/v1/organizations/${org}/team/members/${memberId}/projects/${project}`);
  await page.getByRole('button', { name: 'Remove access to Production' }).click();
  await page.getByRole('button', { name: 'Confirm change' }).click();
  await expect(page.getByText('No project access. This member cannot access project-scoped resources yet.')).toBeVisible();
  await page.getByRole('button', { name: 'Change role for dev@example.invalid' }).click();
  await page.getByLabel('New role').selectOption('ReadOnly');
  await page.getByRole('button', { name: 'Confirm change' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'dev@example.invalid' }).getByText(/Read-only · Joined/)).toBeVisible();
  expect(state.writes.at(-1)?.body).toEqual({ role: 'ReadOnly' });
});

test('admin cannot invite/change/revoke admins or owners, while the last owner is protected', async ({ page }) => {
  const state = await fixture(page, 'Admin');
  await page.goto(`/organizations/${org}/team`);
  await expect(page.getByLabel('Invitation role').locator('option[value="Admin"]')).toHaveCount(0);
  await expect(page.getByLabel('Invitation role').locator('option[value="Owner"]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Change role for admin@example.invalid' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Revoke other-owner@example.invalid' })).toBeDisabled();
  await page.getByRole('button', { name: 'Change role for dev@example.invalid' }).click();
  await expect(page.getByLabel('New role').locator('option[value="Admin"]')).toHaveCount(0);
  await page.keyboard.press('Escape');
  expect(state.writes).toHaveLength(0);
});

test('revocation needs confirmation, preserves metadata and never allows removing the last owner', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/team`);
  await expect(page.getByRole('button', { name: 'Revoke owner@example.invalid' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Change role for owner@example.invalid' })).toBeDisabled();
  await page.getByRole('button', { name: 'Revoke dev@example.invalid' }).click();
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(state.writes).toHaveLength(0);
  await page.getByRole('button', { name: 'Revoke dev@example.invalid' }).click();
  await page.getByRole('button', { name: 'Confirm change' }).click();
  await expect(page.getByText('Revoked', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Revoke dev@example.invalid' })).toBeDisabled();
  expect(state.writes.at(-1)?.method).toBe('DELETE');
});

test('live permission/conflict failures never display a successful mutation or bypass target boundaries', async ({ page }) => {
  const state = await fixture(page);
  await page.goto(`/organizations/${org}/team`);
  await expect(page.getByLabel('Member email')).toBeVisible();
  state.denied = true;
  await page.getByLabel('Member email').fill('denied@example.invalid');
  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByRole('alert')).toContainText('Permission denied');
  await expect(page.getByText('Invitation issued.')).toHaveCount(0);
  await page.route('**/team/invitations', route => route.request().method() === 'POST' ? route.fulfill({ status: 409 }) : route.fallback());
  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByRole('alert')).toContainText('active member/invitation');
  await page.getByRole('button', { name: 'Refresh team' }).click();
  await expect(page.getByRole('heading', { name: 'Owner or Admin access required' })).toBeVisible();
  await expect(page.getByText('dev@example.invalid', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Member email')).toHaveCount(0);
});

test('tenant switching drops drafts and late invite feedback without showing the previous team', async ({ page }) => {
  await fixture(page);
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/organizations/${org}/team/invitations`, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await pending; await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'issued', email: 'late@example.invalid' }) });
  });
  await page.goto(`/organizations/${org}/team`);
  await page.getByLabel('Member email').fill('late@example.invalid');
  const sent = page.waitForRequest(value => value.method() === 'POST' && value.url().endsWith('/invitations'));
  await page.getByRole('button', { name: 'Send invitation' }).click(); await sent;
  await page.getByRole('combobox', { name: 'Organization', exact: true }).selectOption(otherOrg);
  await page.getByRole('link', { name: 'Team', exact: true }).click();
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/invitations'));
  release(); await response;
  await expect(page.getByText('beta-owner@example.invalid (you)', { exact: true })).toBeVisible();
  await expect(page.getByText('dev@example.invalid', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Member email')).toHaveValue('');
  await expect(page.getByText(/Invitation issued/)).toHaveCount(0);
});

test('invitation acceptance explains expired/replayed/mismatched tokens, scrubs accidental URL tokens, and clears secrets on success', async ({ page }) => {
  await fixture(page);
  let expired = true; const submitted: unknown[] = [];
  await page.route('**/team/invitations/accept', route => {
    submitted.push(route.request().postDataJSON());
    return expired ? route.fulfill({ status: 404 }) : route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ accountId: actorId, email: 'owner@example.invalid', role: 'Developer', status: 'Active', projectIds: [] }) });
  });
  await page.goto(`/accept-invitation#token=${token}`);
  await expect(page).toHaveURL(/\/accept-invitation$/);
  await expect(page.getByLabel('Invitation token')).toHaveValue('');
  expect(submitted).toHaveLength(0);
  await page.getByLabel('Invitation token').fill(token);
  await page.getByRole('button', { name: 'Accept invitation', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('invalid, expired, already used, revoked, or does not match');
  expired = false;
  await page.getByRole('button', { name: 'Accept invitation', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Invitation accepted');
  await expect(page.getByLabel('Invitation token')).toHaveCount(0);
  expect(submitted).toEqual([{ token }, { token }]);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(token);
  await page.reload();
  await expect(page.getByLabel('Invitation token')).toHaveValue('');
});

test('anonymous or unverified accounts cannot accept; read service failures are not login prompts', async ({ page }) => {
  await fixture(page);
  let status = 401;
  await page.route('**/auth/session', route => status === 200 ? route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ accountId: actorId, email: 'unverified@example.invalid', emailVerified: false }) }) : route.fulfill({ status }));
  await page.goto('/accept-invitation');
  await expect(page.getByRole('heading', { name: 'Sign in to accept' })).toBeVisible();
  await expect(page.getByLabel('Invitation token')).toHaveCount(0);
  status = 503; await page.reload();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sign in to accept' })).toHaveCount(0);
  status = 200; await page.reload();
  await expect(page.getByRole('heading', { name: 'Verify your email first' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Accept invitation', exact: true })).toHaveCount(0);
});

test('invitation expiry updates on the next poll even when the API metadata has not changed', async ({ page }) => {
  await page.clock.install();
  const state = await fixture(page);
  state.invitations[0].expiresAt = new Date(Date.now() + 90_000).toISOString();
  await page.goto(`/organizations/${org}/team`);
  await expect(page.getByText('Pending', { exact: true })).toBeVisible();
  await page.clock.fastForward(120_000);
  await expect(page.getByText('Expired', { exact: true })).toBeVisible();
});

test('team and invitation screens stay usable on mobile, landscape and large text', async ({ page }, testInfo) => {
  await fixture(page);
  await page.goto(`/organizations/${org}/team`);
  await expect(page.getByLabel('Member email')).toBeVisible();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.screenshot({ path: testInfo.outputPath('team-desktop.png'), fullPage: true });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('team-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 667, height: 375 });
  await page.evaluate(() => { document.documentElement.style.fontSize = '150%'; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.goto('/accept-invitation');
  await expect(page.getByLabel('Invitation token')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
