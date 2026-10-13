import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
import { test, expect, type BrowserContext, type Page } from '@playwright/test';

const base = process.env.UZLLM_ACCEPTANCE_BASE_URL!;
const mailbox = process.env.UZLLM_ACCEPTANCE_MAILPIT_URL!;
const password = 'Team-acceptance-only-password-123';

// Real edge auth/invitation requests share 30/minute, burst 10, including
// invitation GETs. Pace this synthetic workflow; never bypass the limiter or
// automatically retry a write. Three slots cover a write and refreshed reads.
const edgeBudget = (slots = 1) => new Promise(done => setTimeout(done, 2200 * slots));

async function api(context: BrowserContext, method: 'get' | 'post' | 'put' | 'patch' | 'delete', path: string, data?: unknown) {
  if (path.includes('/auth/') || path.includes('/invitations')) await edgeBudget();
  const csrf = (await context.cookies()).find(cookie => cookie.name === '__Host-uzllm-csrf')?.value;
  const response = await context.request[method](base + path, { data, headers: csrf ? { 'X-CSRF-Token': csrf } : {} });
  return { status: response.status(), body: await response.json().catch(() => null) };
}

async function mailToken(email: string, subject: 'Verify' | 'invitation', exclude?: string): Promise<string> {
  for (let attempt = 0; attempt < 60; attempt++) {
    const list = await fetch(`${mailbox}/api/v1/messages`).then(value => value.json()).catch(() => null);
    for (const message of list?.messages ?? []) {
      if (!message.To?.some((person: { Address: string }) => person.Address.toLowerCase() === email) || !message.Subject.includes(subject)) continue;
      const body = await fetch(`${mailbox}/view/${message.ID}.txt`).then(value => value.text());
      const token = body.match(/(?:verification page|this token):\s*\n\s*([A-Za-z0-9_-]+)/)?.[1];
      if (token && token !== exclude) return token;
    }
    await new Promise(done => setTimeout(done, 1000));
  }
  throw new Error('Worker did not deliver the expected synthetic team email.');
}

async function register(context: BrowserContext, email: string) {
  expect((await api(context, 'post', '/management/v1/auth/register', { email, password })).status).toBe(202);
  expect((await api(context, 'post', '/management/v1/auth/verify-email', { token: await mailToken(email, 'Verify') })).status).toBe(204);
  expect((await api(context, 'post', '/management/v1/auth/login', { email, password })).status).toBe(204);
  const session = await api(context, 'get', '/management/v1/auth/session');
  expect(session.status).toBe(200);
  return session.body.accountId as string;
}

async function invite(page: Page, email: string, role: string) {
  await edgeBudget(3);
  await page.getByLabel('Member email').fill(email);
  await page.getByLabel('Invitation role').selectOption(role);
  const response = page.waitForResponse(value => value.request().method() === 'POST' && value.url().endsWith('/team/invitations'));
  await page.getByRole('button', { name: 'Send invitation' }).click();
  const received = await response; expect(received.status()).toBe(201);
  const body = await received.json();
  expect(body).not.toHaveProperty('token');
  await expect(page.getByRole('status').filter({ hasText: 'Invitation issued' })).toBeVisible();
  return body.id as string;
}

async function accept(page: Page, token: string, success: boolean) {
  await edgeBudget();
  await page.goto(base + '/accept-invitation');
  await page.getByLabel('Invitation token').fill(token);
  await page.getByRole('button', { name: 'Accept invitation', exact: true }).click();
  if (success) {
    await expect(page.getByRole('status')).toContainText('Invitation accepted');
    await expect(page.getByLabel('Invitation token')).toHaveCount(0);
    expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain(token);
  } else await expect(page.getByRole('alert')).toContainText('invalid, expired, already used, revoked, or does not match');
}

// Test-only fixture: move exactly this newly issued synthetic invitation back
// seven days. No production clock override, runtime endpoint or business SQL is
// added. Guard the dedicated harness/project/database and validate every value.
function expireInvitation(organizationId: string, invitationId: string, email: string) {
  const projectName = process.env.COMPOSE_PROJECT_NAME;
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (process.env.UZLLM_ACCEPTANCE_ALLOW_TEAM_EXPIRY_FIXTURE !== 'disposable-stack'
    || !/^uzllmacc[0-9a-f]{8}$/.test(projectName ?? '') || !uuid.test(organizationId) || !uuid.test(invitationId)
    || !/^team-expired-[0-9a-f-]+@example\.invalid$/.test(email)) throw new Error('Refusing expiry fixture outside the isolated acceptance stack.');
  const sql = `UPDATE org.invitation SET created_at = now() - interval '8 days', expires_at = now() - interval '1 day'
    WHERE id = '${invitationId}' AND organization_id = '${organizationId}' AND email = '${email}'
      AND accepted_at IS NULL AND revoked_at IS NULL RETURNING id;`;
  const result = spawnSync('docker', ['compose', '-p', projectName!, '-f', resolve('../../../deploy/compose.acceptance.yml'),
    'exec', '-T', 'postgres', 'psql', '-qAt', '-v', 'ON_ERROR_STOP=1', '-U', 'uzllm_migrator', '-d', 'uzllm_acceptance', '-c', sql], { encoding: 'utf8' });
  expect(result.status, 'Isolated expiry fixture failed.').toBe(0);
  expect(result.stdout.trim()).toBe(invitationId);
}

test('FRONTEND_007_real_team_owner_admin_grants_expiry_replay_and_tenant_isolation', async ({ page, browser }, testInfo) => {
  const ownerEmail = `team-owner-${randomUUID()}@example.invalid`;
  const adminEmail = `team-admin-${randomUUID()}@example.invalid`;
  const candidateEmail = `team-expired-${randomUUID()}@example.invalid`;
  const adminContext = await browser.newContext({ baseURL: base, ignoreHTTPSErrors: true });
  const candidateContext = await browser.newContext({ baseURL: base, ignoreHTTPSErrors: true });
  try {
    // Drain the preceding launch scenario's edge burst, not the Redis policy.
    await edgeBudget(10);
    const ownerId = await register(page.context(), ownerEmail);
    const adminId = await register(adminContext, adminEmail);
    const candidateId = await register(candidateContext, candidateEmail);
    const org = await api(page.context(), 'post', '/management/v1/organizations', { name: 'Team acceptance workspace' });
    expect(org.status).toBe(201);
    const orgId: string = org.body.id;
    const project = await api(page.context(), 'post', `/management/v1/organizations/${orgId}/projects`, { name: 'Team production' });
    expect(project.status).toBe(201);
    const projectId: string = project.body.id;
    const foreign = await api(candidateContext, 'post', '/management/v1/organizations', { name: 'Foreign team workspace' });
    expect(foreign.status).toBe(201);
    const foreignProject = await api(candidateContext, 'post', `/management/v1/organizations/${foreign.body.id}/projects`, { name: 'Foreign production' });
    expect(foreignProject.status).toBe(201);
    const teamPath = `/management/v1/organizations/${orgId}/team`;
    const candidate = await candidateContext.newPage();
    await candidate.goto(base + `/organizations/${orgId}/team`);
    await expect(candidate.getByRole('heading', { name: 'Workspace not found' })).toBeVisible();
    expect((await api(candidateContext, 'get', teamPath + '/members')).status).toBe(403);
    expect((await api(candidateContext, 'get', teamPath + '/invitations')).status).toBe(403);
    await expect(candidate.getByText(ownerEmail)).toHaveCount(0);

    await page.goto(`/organizations/${orgId}/team`);
    await expect(page.getByRole('button', { name: `Revoke ${ownerEmail}`, exact: true })).toBeDisabled();
    expect((await api(page.context(), 'patch', teamPath + `/members/${ownerId}/role`, { role: 'ReadOnly' })).status).toBe(409);
    expect((await api(page.context(), 'delete', teamPath + `/members/${ownerId}`)).status).toBe(409);
    const invitationId = await invite(page, candidateEmail, 'Developer');
    const expiredToken = await mailToken(candidateEmail, 'invitation');
    await edgeBudget(3);
    await page.getByLabel('Member email').fill(candidateEmail);
    await page.getByRole('button', { name: 'Send invitation' }).click();
    await expect(page.getByRole('form', { name: 'Invite member' }).getByRole('alert')).toContainText('active member/invitation');
    expireInvitation(orgId, invitationId, candidateEmail);
    await accept(candidate, expiredToken, false);
    await page.getByRole('button', { name: 'Refresh team' }).click();
    await expect(page.getByText('Expired', { exact: true })).toBeVisible();
    await invite(page, candidateEmail, 'Developer');
    const validToken = await mailToken(candidateEmail, 'invitation', expiredToken);
    await accept(candidate, expiredToken, false);
    await accept(candidate, validToken, true);
    await accept(candidate, validToken, false);
    const metadata = await api(page.context(), 'get', teamPath + '/invitations');
    expect(JSON.stringify(metadata.body)).not.toContain(validToken);
    expect(JSON.stringify(metadata.body)).not.toContain(expiredToken);

    await candidate.goto(base + `/organizations/${orgId}/team`);
    await expect(candidate.getByRole('heading', { name: 'Owner or Admin access required' })).toBeVisible();
    await expect(candidate.getByLabel('Member email')).toHaveCount(0);
    await page.getByRole('button', { name: 'Refresh team' }).click();
    await page.getByRole('button', { name: `Project access for ${candidateEmail}` }).click();
    await page.getByLabel('Project to grant').selectOption(projectId);
    await page.getByRole('button', { name: 'Grant project access', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm change' }).click();
    await expect(page.getByRole('button', { name: 'Remove access to Team production' })).toBeVisible();
    expect((await api(candidateContext, 'get', `/management/v1/projects/${projectId}/api-keys`)).status).toBe(200);
    expect((await api(page.context(), 'put', teamPath + `/members/${candidateId}/projects/${foreignProject.body.id}`)).status).toBe(404);
    const members = await api(page.context(), 'get', teamPath + '/members');
    expect(members.body.find((value: { accountId: string }) => value.accountId === candidateId).projectIds).toEqual([projectId]);
    await page.getByRole('button', { name: 'Remove access to Team production' }).click();
    await page.getByRole('button', { name: 'Confirm change' }).click();
    await expect(page.getByText('No project access. This member cannot access project-scoped resources yet.')).toBeVisible();
    expect((await api(candidateContext, 'get', `/management/v1/projects/${projectId}/api-keys`)).status).toBe(403);

    await invite(page, adminEmail, 'Admin');
    const admin = await adminContext.newPage();
    await accept(admin, await mailToken(adminEmail, 'invitation'), true);
    await admin.goto(base + `/organizations/${orgId}/team`);
    await expect(admin.getByLabel('Invitation role').locator('option[value="Admin"]')).toHaveCount(0);
    await expect(admin.getByRole('button', { name: `Revoke ${ownerEmail}`, exact: true })).toBeDisabled();
    await expect(admin.getByRole('button', { name: `Change role for ${adminEmail}`, exact: true })).toBeDisabled();
    expect((await api(adminContext, 'post', teamPath + '/invitations', { email: `forbidden-${randomUUID()}@example.invalid`, role: 'Admin' })).status).toBe(403);
    expect((await api(adminContext, 'patch', teamPath + `/members/${ownerId}/role`, { role: 'Developer' })).status).toBe(403);
    expect((await api(adminContext, 'delete', teamPath + `/members/${adminId}`)).status).toBe(403);
    await admin.getByRole('button', { name: `Change role for ${candidateEmail}`, exact: true }).click();
    await admin.getByLabel('New role').selectOption('ReadOnly');
    await admin.getByRole('button', { name: 'Confirm change' }).click();
    await expect(admin.getByRole('listitem').filter({ hasText: candidateEmail }).getByText(/Read-only · Joined/)).toBeVisible();
    await admin.screenshot({ path: testInfo.outputPath('team-admin.png'), fullPage: true });
    await admin.getByRole('button', { name: `Revoke ${candidateEmail}`, exact: true }).click();
    await admin.getByRole('button', { name: 'Confirm change' }).click();
    await expect(admin.getByRole('listitem').filter({ has: admin.getByRole('button', { name: `Revoke ${candidateEmail}`, exact: true }) }).getByText('Revoked', { exact: true })).toBeVisible();
    expect((await api(candidateContext, 'get', `/management/v1/organizations/${orgId}/projects/${projectId}`)).status).toBe(403);
    await page.getByRole('button', { name: 'Refresh team' }).click();
    await expect(page.getByRole('listitem').filter({ hasText: candidateEmail }).filter({ has: page.getByRole('button', { name: `Revoke ${candidateEmail}`, exact: true }) }).getByText('Revoked', { exact: true })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('team-owner.png'), fullPage: true });
  } finally { await adminContext.close(); await candidateContext.close(); }
});
