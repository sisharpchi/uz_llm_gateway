import { randomBytes, randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { chmodSync, cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { get as httpsGet } from 'node:https';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const composeFile = join(root, 'deploy/compose.acceptance.yml');
const scratch = mkdtempSync(join(tmpdir(), 'uzllm-acceptance-'));
const certs = join(scratch, 'certs');
const keyring = join(scratch, 'keyring');
const artifactDir = join(root, 'artifacts/acceptance');
const project = `uzllmacc${randomUUID().slice(0, 8)}`;
const revision = process.env.GITHUB_SHA ?? spawnSync('git', ['rev-parse', 'HEAD'],
  { cwd: root, encoding: 'utf8' }).stdout.trim();
const environment = process.env.GITHUB_ACTIONS === 'true'
  ? 'github-actions-disposable-docker' : 'disposable-local-docker';
const secret = () => randomBytes(32).toString('base64');
const env = {
  ...process.env,
  COMPOSE_PROJECT_NAME: project,
  UZLLM_ACCEPTANCE_ALLOW_TEAM_EXPIRY_FIXTURE: 'disposable-stack',
  UZLLM_ACCEPTANCE_ALLOW_PAYLOAD_EXPIRY_FIXTURE: 'disposable-stack',
  UZLLM_ACCEPTANCE_TMP: scratch.replaceAll('\\', '/'),
  UZLLM_ACCEPTANCE_DB_PASSWORD: randomBytes(20).toString('hex'),
  UZLLM_ACCEPTANCE_PFX_PASSWORD: randomBytes(20).toString('hex'),
  UZLLM_ACCEPTANCE_FINGERPRINT_KEY: secret(),
  UZLLM_ACCEPTANCE_PROVIDER_KEY: secret(),
  UZLLM_ACCEPTANCE_PAYLOAD_KEY: secret(),
  UZLLM_ACCEPTANCE_WEBHOOK_KEY: secret(),
  UZLLM_ACCEPTANCE_PAYME_KEY: randomBytes(20).toString('hex'),
  UZLLM_ACCEPTANCE_CLICK_KEY: randomBytes(20).toString('hex'),
};
const compose = (...args) => run('docker', ['compose', '-f', composeFile, ...args]);
function run(program, args, options = {}) {
  const { silent = false, ...spawnOptions } = options;
  const result = spawnSync(program, args, {
    cwd: root, env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
    ...spawnOptions,
  });
  if (result.status !== 0) {
    const detail = `${result.stderr || result.stdout || result.error || ''}`.slice(-4000);
    throw new Error(`${program} ${args[0]} failed (exit ${result.status}): ${detail}`);
  }
  if (!silent && result.stdout?.trim()) process.stdout.write(result.stdout);
  return result.stdout?.trim() ?? '';
}
function openssl(args) {
  const exe = process.platform === 'win32' ? 'C:/Program Files/Git/usr/bin/openssl.exe' : 'openssl';
  return run(exe, args);
}
function write(path, content) { writeFileSync(path, content, { mode: 0o600 }); }
function port(service, internal) {
  return Number(compose('port', service, String(internal)).split(':').at(-1));
}
async function wait(url, predicate, label) {
  for (let attempt = 0; attempt < 90; attempt++) {
    try {
      const response = url.startsWith('https:')
        ? await new Promise((resolveResponse, reject) => httpsGet(url,
          { rejectUnauthorized: false }, value => {
            value.resume();
            resolveResponse({ status: value.statusCode, ok: value.statusCode >= 200 && value.statusCode < 300 });
          }).on('error', reject))
        : await fetch(url);
      if (predicate(response)) return;
    } catch { /* startup */ }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 1000));
  }
  throw new Error(`${label} did not become ready: ${url}`);
}

mkdirSync(certs);
mkdirSync(keyring);
if (process.platform !== 'win32') {
  chmodSync(scratch, 0o755);
  chmodSync(certs, 0o755);
  chmodSync(keyring, 0o777); // Disposable test ring shared by non-root app containers.
}
write(join(scratch, 'click-allowlist.conf'), 'allow all; # isolated loopback-bound acceptance stack only\n');
try {
  openssl(['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
    '-subj', '/CN=UZLLM Acceptance CA', '-keyout', join(certs, 'ca.key'),
    '-out', join(certs, 'ca.crt')]);
  openssl(['req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=management.internal',
    '-keyout', join(certs, 'internal.key'), '-out', join(certs, 'internal.csr')]);
  write(join(certs, 'internal.ext'),
    'subjectAltName=DNS:management.internal,DNS:gateway.internal,DNS:provider.internal,DNS:mailpit\n');
  openssl(['x509', '-req', '-in', join(certs, 'internal.csr'), '-CA', join(certs, 'ca.crt'),
    '-CAkey', join(certs, 'ca.key'), '-CAcreateserial', '-out', join(certs, 'internal.crt'),
    '-days', '1', '-extfile', join(certs, 'internal.ext')]);
  openssl(['pkcs12', '-export', '-out', join(certs, 'internal.pfx'),
    '-inkey', join(certs, 'internal.key'), '-in', join(certs, 'internal.crt'),
    '-passout', `pass:${env.UZLLM_ACCEPTANCE_PFX_PASSWORD}`]);
  openssl(['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
    '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
    '-keyout', join(certs, 'public.key'), '-out', join(certs, 'public.crt')]);
  writeFileSync(join(certs, 'internal-ca.crt'), readFileSync(join(certs, 'ca.crt')));
  if (process.platform !== 'win32') {
    for (const name of ['ca.crt', 'internal.crt', 'internal.key', 'internal.pfx', 'public.crt', 'public.key', 'internal-ca.crt'])
      chmodSync(join(certs, name), 0o644);
    chmodSync(join(scratch, 'click-allowlist.conf'), 0o644);
  }
  if (!process.argv.includes('--no-build')) {
    if (process.argv.includes('--host-publish')) run('dotnet', ['restore', 'UZLLM.slnx']);
    const projects = { gateway: 'UZLLM.Gateway.Api', management: 'UZLLM.Management.Api',
      worker: 'UZLLM.Worker', migrator: 'UZLLM.Migrator' };
    for (const [target, projectName] of Object.entries(projects)) {
      if (process.argv.includes('--host-publish')) {
        const publish = join(scratch, `publish-${target}`);
        const context = join(scratch, `image-${target}`);
        mkdirSync(context);
        run('dotnet', ['publish', `src/${projectName}/${projectName}.csproj`,
          '--configuration', 'Release', '--no-restore', '-o', publish,
          '/p:UseAppHost=false']);
        cpSync(publish, join(context, 'app'), { recursive: true });
        write(join(context, 'Dockerfile'),
          `FROM mcr.microsoft.com/dotnet/aspnet:10.0\nWORKDIR /app\nCOPY app .\nUSER $APP_UID\nENTRYPOINT ["dotnet", "${projectName}.dll"]\n`);
        run('docker', ['build', '-f', join(context, 'Dockerfile'), '-t',
          `uzllm-${target}:acceptance`, context]);
      } else {
        run('docker', ['build', '-f', 'deploy/Dockerfile.app', '--target', target,
          '-t', `uzllm-${target}:acceptance`, '.']);
      }
    }
    run('docker', ['build', '-f', 'deploy/Dockerfile.edge', '-t', 'uzllm-edge:acceptance', '.']);
  }
  compose('config', '--quiet');
  compose('up', '-d', '--wait', 'postgres', 'redis', 'mailpit', 'provider');
  compose('exec', '-T', 'postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U',
    'uzllm_migrator', '-d', 'uzllm_acceptance', '-c',
    `CREATE ROLE uzllm_runtime LOGIN PASSWORD '${env.UZLLM_ACCEPTANCE_DB_PASSWORD}';`);
  run('docker', ['compose', '-f', composeFile, 'run', '--rm', 'migrator'], { silent: true });
  const grants = `GRANT CONNECT ON DATABASE uzllm_acceptance TO uzllm_runtime;
    GRANT USAGE ON SCHEMA iam, org, gateway, catalog, billing, payment, usage, ops, audit TO uzllm_runtime;
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA iam, org, gateway, catalog, billing, payment, usage, ops, audit TO uzllm_runtime;
    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA iam, org, gateway, catalog, billing, payment, usage, ops, audit TO uzllm_runtime;`;
  compose('exec', '-T', 'postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U',
    'uzllm_migrator', '-d', 'uzllm_acceptance', '-c', grants);
  const postgresPort = port('postgres', 5432);
  run('dotnet', ['run', '--project', 'tests/Integration/UZLLM.AcceptanceSeed',
    '--configuration', 'Release'], { env: {
      ...env, UZLLM_ACCEPTANCE_SEED_ALLOW: 'disposable-stack',
      ConnectionStrings__Postgres: `Host=127.0.0.1;Port=${postgresPort};Database=uzllm_acceptance;Username=uzllm_migrator;Password=${env.UZLLM_ACCEPTANCE_DB_PASSWORD};SSL Mode=Disable`,
      ProviderSecrets__ActiveKeyVersion: 'v1',
      ProviderSecrets__Keys__v1: env.UZLLM_ACCEPTANCE_PROVIDER_KEY,
    } });
  compose('up', '-d', 'management', 'gateway', 'worker', 'edge');
  const edgePort = port('edge', 443);
  const mailpitPort = port('mailpit', 8025);
  const base = `https://127.0.0.1:${edgePort}`;
  await wait(`http://127.0.0.1:${mailpitPort}/api/v1/messages`, response => response.ok, 'Mailpit');
  await wait(base + '/edge/health', response => response.status === 204, 'edge');
  env.UZLLM_ACCEPTANCE_BASE_URL = base;
  env.UZLLM_ACCEPTANCE_MAILPIT_URL = `http://127.0.0.1:${mailpitPort}`;
  // Trust only this disposable edge certificate in SDK child processes. Never disable TLS validation.
  env.UZLLM_ACCEPTANCE_SDK_CA = join(certs, 'public.crt');
  const result = spawnSync(process.execPath,
    [join(root, 'frontend/apps/dashboard/node_modules/playwright/cli.js'),
      'test', '--config', 'playwright.acceptance.config.ts'], {
      cwd: join(root, 'frontend/apps/dashboard'), env, stdio: 'inherit',
    });
  if (result.status !== 0) throw new Error(`Playwright acceptance failed (exit ${result.status}): ${result.error ?? ''}`);
  mkdirSync(artifactDir, { recursive: true });
  writeFileSync(join(artifactDir, 'summary.json'), JSON.stringify({
    task: 'LAUNCH-002', revision, environment,
    composeProject: project, passed: true, completedAt: new Date().toISOString(),
    stack: ['Nginx TLS edge', 'Gateway', 'Management', 'Worker', 'PostgreSQL', 'Redis', 'Mailpit STARTTLS', 'HTTPS provider fixture'],
  }, null, 2));
  process.stdout.write(`LAUNCH-002 PASS; artifact: ${join(artifactDir, 'summary.json')}\n`);
} catch (error) {
  mkdirSync(artifactDir, { recursive: true });
  writeFileSync(join(artifactDir, 'summary.json'), JSON.stringify({
    task: 'LAUNCH-002', revision, environment, composeProject: project,
    passed: false, error: String(error), completedAt: new Date().toISOString(),
  }, null, 2));
  process.exitCode = 1;
  process.stderr.write(`${error}\n`);
} finally {
  try { compose('down', '-v', '--remove-orphans'); }
  catch (error) { process.exitCode = 1; process.stderr.write(`Acceptance cleanup failed: ${error}\n`); }
  rmSync(scratch, { recursive: true, force: true });
}
