import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const edgeImage = process.env.UZLLM_EDGE_IMAGE ?? 'uzllm-edge:ops002';

function command(program, args) {
  return execFileSync(program, args, { encoding: 'utf8', cwd: repoRoot,
    stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

function openssl(args) {
  const program = process.platform === 'win32'
    && existsSync('C:/Program Files/Git/usr/bin/openssl.exe')
    ? 'C:/Program Files/Git/usr/bin/openssl.exe' : 'openssl';
  command(program, args);
}

function send(port, path, method = 'POST', body = '', headers = {}, tls = true) {
  return new Promise((resolveResponse, reject) => {
    const transport = tls ? httpsRequest : httpRequest;
    const requestHeaders = { Host: 'api.example.test', ...headers };
    if (!('Transfer-Encoding' in headers)) requestHeaders['Content-Length'] = Buffer.byteLength(body);
    const req = transport({ hostname: '127.0.0.1', port, path, method,
      rejectUnauthorized: false, headers: requestHeaders }, res => {
      const chunks = [];
      res.on('data', chunk => chunks.push(chunk));
      res.on('end', () => {
        const raw = Buffer.concat(chunks).toString('utf8');
        let json;
        try { json = JSON.parse(raw); } catch { json = null; }
        resolveResponse({ status: res.statusCode, raw, json, headers: res.headers });
      });
    });
    req.on('error', reject);
    req.end(body);
  });
}

async function waitForEdge(port) {
  for (let attempt = 0; attempt < 50; attempt++) {
    try {
      if ((await send(port, '/edge/health', 'GET')).status === 204) return;
    } catch { /* Container may still be starting. */ }
    await new Promise(resolveDelay => setTimeout(resolveDelay, 200));
  }
  throw new Error('Nginx edge did not become healthy');
}

function clickBody(signature = true, action = '0', error = '0') {
  const fields = new URLSearchParams({
    click_trans_id: '90001', service_id: 'click-test-service',
    merchant_trans_id: '11111111-1111-1111-1111-111111111111',
    amount: '1000.00', action, error, error_note: 'Success',
    sign_time: '2026-09-25 10:00:00', click_paydoc_id: '80001',
  });
  if (action === '1') fields.set('merchant_prepare_id', '41');
  const material = fields.get('click_trans_id') + fields.get('service_id')
    + 'click-test-secret' + fields.get('merchant_trans_id')
    + (action === '1' ? fields.get('merchant_prepare_id') : '')
    + fields.get('amount') + action + fields.get('sign_time');
  fields.set('sign_string', signature
    ? createHash('md5').update(material).digest('hex') : '0'.repeat(32));
  return fields.toString();
}

test('LAUNCH-001 payment callback edge ingress', async t => {
  const suffix = randomUUID().slice(0, 8);
  const network = `uzllm-payment-edge-${suffix}`;
  const backend = `uzllm-payment-fixture-${suffix}`;
  const edges = [];
  const tempDir = mkdtempSync(join(tmpdir(), 'uzllm-payment-edge-'));
  const publicKey = join(tempDir, 'public.key');
  const publicCrt = join(tempDir, 'public.crt');
  const paymeAuth = `Basic ${Buffer.from('Paycom:payme-test-key').toString('base64')}`;
  const paymeHeaders = { Authorization: paymeAuth, 'Content-Type': 'application/json' };
  const clickHeaders = { 'Content-Type': 'application/x-www-form-urlencoded' };
  let createdNetwork = false;

  function startEdge(allowlist, trustedProxy) {
    const name = `uzllm-edge-fixture-${suffix}-${edges.length}`;
    const args = ['run', '-d', '--name', name, '--network', network,
      '-p', '127.0.0.1::443', '-p', '127.0.0.1::80',
      '--mount', `type=bind,source=${tempDir},target=/run/certs,readonly`,
      '-e', 'NGINX_ENVSUBST_FILTER=^UZLLM_',
      '-e', 'NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx',
      '-e', 'UZLLM_GATEWAY_A=gateway-a:8443',
      '-e', 'UZLLM_GATEWAY_B=gateway-b:8443',
      '-e', 'UZLLM_MANAGEMENT_A=management-a:8443',
      '-e', 'UZLLM_MANAGEMENT_B=management-b:8443'];
    if (allowlist) args.push('--mount',
      `type=bind,source=${allowlist},target=/etc/nginx/click-allowlist.conf,readonly`);
    if (trustedProxy) args.push('--mount',
      `type=bind,source=${trustedProxy},target=/etc/nginx/trusted-proxy-realip.conf,readonly`);
    args.push(edgeImage);
    command('docker', args);
    edges.push(name);
    const port = Number(command('docker', ['port', name, '443/tcp']).split(':').at(-1));
    const httpPort = Number(command('docker', ['port', name, '80/tcp']).split(':').at(-1));
    return { port, httpPort };
  }

  try {
    openssl(['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
      '-subj', '/CN=management.internal',
      '-addext', 'subjectAltName=DNS:management.internal',
      '-keyout', publicKey, '-out', publicCrt]);
    writeFileSync(join(tempDir, 'internal-ca.crt'), readFileSync(publicCrt));
    command('docker', ['network', 'create', network]);
    createdNetwork = true;
    command('docker', ['run', '-d', '--name', backend, '--network', network,
      '--network-alias', 'management-a', '--network-alias', 'management-b',
      '--network-alias', 'gateway-a', '--network-alias', 'gateway-b',
      '--mount', `type=bind,source=${tempDir},target=/run/certs,readonly`,
      '--mount', `type=bind,source=${join(repoRoot, 'deploy/tests/payment-edge-upstream.mjs')},target=/app/fixture.mjs,readonly`,
      'node:22-alpine', 'node', '/app/fixture.mjs']);

    const defaultEdge = startEdge();
    await waitForEdge(defaultEdge.port);
    command('docker', ['exec', edges.at(-1), 'nginx', '-t']);

    await t.test('Payme_authenticated_callback_reaches_management_over_TLS_with_original_body', async () => {
      const body = JSON.stringify({ id: 11, method: 'CheckPerformTransaction', params: { amount: 100000 } });
      const result = await send(defaultEdge.port, '/payments/payme/callback', 'POST', body,
        { ...paymeHeaders, 'X-Forwarded-For': '198.51.100.44',
          'X-Real-IP': '198.51.100.44', Forwarded: 'for=198.51.100.44' });
      assert.equal(result.status, 200);
      assert.equal(result.json.path, '/payments/payme/callback');
      assert.equal(result.json.body, body);
      assert.equal(result.json.error, null);
      assert.equal(result.json.realIp, result.json.forwardedFor);
      assert.notEqual(result.json.forwardedFor, '198.51.100.44');
      assert.equal(result.json.forwarded, undefined);
    });

    await t.test('Payme_invalid_credential_is_rejected_by_protocol_fixture', async () => {
      const result = await send(defaultEdge.port, '/payments/payme/callback', 'POST', '{}',
        { ...paymeHeaders, Authorization: 'Basic invalid' });
      assert.equal(result.status, 200);
      assert.equal(result.json.error.code, -32504);
    });

    await t.test('Callback_HTTP_redirects_and_non_POST_or_unknown_routes_do_not_reach_management', async () => {
      assert.equal((await send(defaultEdge.httpPort, '/payments/payme/callback', 'POST', '{}', {}, false)).status, 308);
      assert.equal((await send(defaultEdge.port, '/payments/payme/callback', 'GET')).status, 405);
      assert.equal((await send(defaultEdge.port, '/payments/other/callback')).status, 404);
    });

    await t.test('Callback_body_limit_accepts_32768_bytes_and_rejects_32769', async () => {
      const prefix = '{"padding":"';
      const suffixBody = '"}';
      const body = prefix + 'x'.repeat(32768 - prefix.length - suffixBody.length) + suffixBody;
      const accepted = await send(defaultEdge.port, '/payments/payme/callback', 'POST', body, paymeHeaders);
      assert.equal(accepted.status, 200);
      assert.equal(accepted.json.bodyBytes, 32768);
      const rejected = await send(defaultEdge.port, '/payments/payme/callback', 'POST', `${body}x`, paymeHeaders);
      assert.equal(rejected.status, 413);
      const chunked = await send(defaultEdge.port, '/payments/payme/callback', 'POST',
        `${body}x`, { ...paymeHeaders, 'Transfer-Encoding': 'chunked' });
      assert.equal(chunked.status, 413);
    });

    await t.test('Payme_duplicate_delivery_is_forwarded_once_per_call_without_proxy_replay', async () => {
      const body = JSON.stringify({ id: 42, method: 'PerformTransaction', params: { id: 'repeat' } });
      const first = await send(defaultEdge.port, '/payments/payme/callback', 'POST', body, paymeHeaders);
      const second = await send(defaultEdge.port, '/payments/payme/callback', 'POST', body, paymeHeaders);
      assert.equal(first.json.seen, 1);
      assert.equal(second.json.seen, 2);
    });

    await t.test('Upstream_503_is_not_retried_by_edge', async () => {
      const body = JSON.stringify({ id: 53 });
      const result = await send(defaultEdge.port, '/payments/payme/callback?simulate-upstream-503',
        'POST', body, paymeHeaders);
      assert.equal(result.status, 503);
      assert.equal(result.json.seen, 1);
    });

    await t.test('Upstream_disconnect_does_not_replay_payment_POST', async () => {
      const body = JSON.stringify({ id: 55 });
      const path = '/payments/payme/callback?simulate-upstream-reset-once';
      const first = await send(defaultEdge.port, path, 'POST', body, paymeHeaders);
      assert.equal(first.status, 502);
      const second = await send(defaultEdge.port, path, 'POST', body, paymeHeaders);
      assert.equal(second.status, 200);
      assert.equal(second.json.seen, 2);
    });

    await t.test('CLICK_is_denied_by_default_even_with_valid_signature', async () => {
      assert.equal((await send(defaultEdge.port, '/payments/click/callback', 'POST',
        clickBody(), clickHeaders)).status, 403);
    });

    await t.test('CLICK_unsigned_reversal_error_cannot_bypass_default_deny', async () => {
      const completed = new URLSearchParams(clickBody(true, '1', '0'));
      const reversed = new URLSearchParams(clickBody(true, '1', '-1'));
      assert.equal(completed.get('sign_string'), reversed.get('sign_string'));
      assert.equal((await send(defaultEdge.port, '/payments/click/callback', 'POST',
        reversed.toString(), clickHeaders)).status, 403);
    });

    const source = (await send(defaultEdge.port, '/payments/payme/callback', 'POST',
      '{"id":54}', paymeHeaders)).json.forwardedFor;
    assert.match(source, /^[0-9a-f:.]+$/i);
    const allowlist = join(tempDir, 'click-allowlist.conf');
    writeFileSync(allowlist, `allow ${source};\ndeny all;\n`);
    const allowedEdge = startEdge(allowlist);
    await waitForEdge(allowedEdge.port);
    command('docker', ['exec', edges.at(-1), 'nginx', '-t']);

    await t.test('CLICK_signed_Prepare_and_Complete_reach_management_only_with_source_policy', async () => {
      assert.equal(new URLSearchParams(clickBody()).get('sign_string'),
        '0924b2ab999ab10e46bbaf628ac275cf');
      for (const action of ['0', '1']) {
        const body = clickBody(true, action);
        const result = await send(allowedEdge.port, '/payments/click/callback', 'POST', body, clickHeaders);
        assert.equal(result.status, 200);
        assert.equal(result.json.error, 0);
        assert.equal(result.json.body, body);
        assert.equal(result.json.forwardedFor, source);
      }
    });

    await t.test('CLICK_invalid_signature_is_rejected_and_oversize_is_blocked_at_edge', async () => {
      const invalid = await send(allowedEdge.port, '/payments/click/callback', 'POST',
        clickBody(false), clickHeaders);
      assert.equal(invalid.status, 200);
      assert.equal(invalid.json.error, -1);
      const oversized = await send(allowedEdge.port, '/payments/click/callback', 'POST',
        `padding=${'x'.repeat(32770)}`, clickHeaders);
      assert.equal(oversized.status, 413);
    });

    const fictionalSource = '198.51.100.44';
    writeFileSync(allowlist, `allow ${fictionalSource};\ndeny all;\n`);
    const deniedEdge = startEdge(allowlist);
    await waitForEdge(deniedEdge.port);
    command('docker', ['exec', edges.at(-1), 'nginx', '-t']);
    await t.test('Untrusted_X_Forwarded_For_cannot_bypass_CLICK_source_policy', async () => {
      const result = await send(deniedEdge.port, '/payments/click/callback', 'POST',
        clickBody(), { ...clickHeaders, 'X-Forwarded-For': fictionalSource });
      assert.equal(result.status, 403);
    });

    const trustedProxy = join(tempDir, 'trusted-proxy.conf');
    writeFileSync(trustedProxy,
      `set_real_ip_from ${source};\nreal_ip_header X-Forwarded-For;\nreal_ip_recursive on;\n`);
    const trustedEdge = startEdge(allowlist, trustedProxy);
    await waitForEdge(trustedEdge.port);
    command('docker', ['exec', edges.at(-1), 'nginx', '-t']);
    await t.test('Explicitly_trusted_proxy_can_forward_allowlisted_CLICK_source', async () => {
      const result = await send(trustedEdge.port, '/payments/click/callback', 'POST',
        clickBody(), { ...clickHeaders, 'X-Forwarded-For': fictionalSource });
      assert.equal(result.status, 200);
      assert.equal(result.json.error, 0);
      assert.equal(result.json.forwardedFor, fictionalSource);
    });
  } finally {
    for (const name of edges.reverse()) {
      try { command('docker', ['rm', '-f', name]); } catch { /* Best-effort cleanup. */ }
    }
    try { command('docker', ['rm', '-f', backend]); } catch { /* Container may not exist. */ }
    if (createdNetwork) {
      try { command('docker', ['network', 'rm', network]); } catch { /* Best-effort cleanup. */ }
    }
    if (dirname(tempDir) !== resolve(tmpdir())
        || !tempDir.startsWith(join(resolve(tmpdir()), 'uzllm-payment-edge-')))
      throw new Error(`Refusing to remove unexpected fixture path: ${tempDir}`);
    rmSync(tempDir, { recursive: true, force: true });
  }
});
