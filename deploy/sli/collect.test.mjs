import assert from 'node:assert/strict';
import { createHash, createHmac, randomUUID } from 'node:crypto';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { collect, edgeObservations, readinessObservation, sign } from './collect.mjs';

const key = Buffer.from(Array.from({ length: 32 }, (_, index) => index + 1));
const line = { executionId: '123456781234123412341234567890ab', operation: 'Chat',
  at: '1792411200.000', duration: '1.250', status: 200, completion: 'OK', gatewayResponse: 'Seen' };

test('fixed Node-to-Dotnet protocol fixture stays byte-for-byte reproducible from safe edge normalization', async () => {
  const fixed = JSON.parse(await readFile(new URL('../../tests/Fixtures/sli/edge-manifest.json', import.meta.url), 'utf8'));
  const batch = JSON.parse(Buffer.from(fixed.payload, 'base64'));
  assert.deepEqual(batch.observations, edgeObservations(JSON.stringify({ ...line, status: 502, gatewayResponse: 'Missing' }), 1));
  assert.deepEqual(JSON.parse(sign('edge-a', batch, key)), fixed);
});

test('edge metadata keeps start month, origin ambiguity and no HTTP200 success inference', () => {
  const rows = edgeObservations(JSON.stringify(line), 4);
  assert.equal(rows[0].sequence, 4);
  assert.equal(rows[0].executionId, '12345678-1234-1234-1234-1234567890ab');
  assert.equal(new Date(rows[0].at) - new Date(rows[0].startedAt), 1250);
  assert.equal(rows[0].terminalSuccess, false);
  assert.equal(rows[0].mode, 'Unknown');
  assert.equal(edgeObservations(JSON.stringify({ ...line, status: 502 }), 1)[0].gatewayFault, false);
  assert.equal(edgeObservations(JSON.stringify({ ...line, status: 502, gatewayResponse: 'Missing' }), 1)[0].gatewayFault, true);
  assert.equal(edgeObservations(JSON.stringify({ ...line, status: 499, completion: '' }), 1)[0].clientDisconnected, true);
});

test('safe edge parser rejects content, duplicate identities, enums, bounds and invalid timestamps', () => {
  for (const item of [{ ...line, prompt: 'private' }, { ...line, operation: 'arbitrary/model' },
    { ...line, at: 'NaN' }, { ...line, duration: '-1.000' }, { ...line, status: 600 },
    { ...line, executionId: '0'.repeat(32) }, { ...line, gatewayResponse: 'private header' }])
    assert.throws(() => edgeObservations(JSON.stringify(item), 1), /Invalid safe/);
  assert.throws(() => edgeObservations(`${JSON.stringify(line)}\n${JSON.stringify(line)}`, 1), /Invalid safe/);
  assert.throws(() => edgeObservations('x'.repeat(512 * 1024 + 1), 1), /Invalid closed/);
  assert.throws(() => edgeObservations(Array.from({ length: 1025 }, () => '{}').join('\n'), 1), /never truncate/);
  assert.deepEqual(edgeObservations('', 1), []);
});

test('manifest signature is keyed authentication over exact independent raw digest and payload', () => {
  const batch = { version: 1, observations: edgeObservations(JSON.stringify(line), 1) };
  const envelope = JSON.parse(sign('edge-a', batch, key));
  const raw = Buffer.from(envelope.payload, 'base64');
  assert.deepEqual(JSON.parse(raw), batch);
  assert.equal(envelope.sha256, createHash('sha256').update(raw).digest('hex'));
  assert.equal(envelope.signature, createHmac('sha256', key)
    .update(`edge-a\n${envelope.sha256}\n${envelope.payload}`).digest('hex'));
  assert.throws(() => sign('../forged', batch, key), /identity/);
  assert.throws(() => sign('edge-a', batch, Buffer.alloc(31)), /identity/);
});

test('independent readiness probe never calls inference or treats DNS TLS timeout as success', async () => {
  for (const code of ['ENOTFOUND', 'CERT_HAS_EXPIRED', 'TimeoutError']) {
    let calls = 0;
    const row = await readinessObservation('https://edge.example.test/edge/health', 7, async (url, options) => {
      calls++; assert.equal(url.pathname, '/edge/health'); assert.equal(options.method, 'GET');
      assert.equal(options.redirect, 'manual'); assert.ok(options.signal); throw new Error(code);
    });
    assert.equal(calls, 1); assert.equal(row.httpStatus, 0); assert.equal(row.gatewayFault, true);
    assert.equal(row.terminalSuccess, false); assert.equal(row.operation, 'Readiness');
    assert.ok(!JSON.stringify(row).includes(code));
  }
  let canceled = 0;
  const success = await readinessObservation('https://edge.example.test/health/ready', 1,
    async () => ({ status: 200, body: { cancel: async () => canceled++ } }));
  assert.equal(canceled, 1); assert.equal(success.gatewayFault, false); assert.equal(success.terminalSuccess, false);
  for (const url of ['http://edge.example.test/edge/health', 'https://a:b@edge.example.test/edge/health',
    'https://edge.example.test/v1/chat/completions', 'https://edge.example.test/edge/health?key=private'])
    await assert.rejects(readinessObservation(url, 1, () => assert.fail('no network for invalid target')), /Only HTTPS/);
});

test('closed edge segments and empty heartbeats retain epoch and reject duplicate source or changed scope', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'uzllm-sli-node-'));
  try {
    const config = { producer: 'edge-a', source: 'Edge', region: 'test',
      stateFile: join(directory, 'state'), outputDirectory: directory,
      keyFile: join(directory, 'key'), segmentFile: join(directory, 'segment') };
    await writeFile(config.keyFile, key.toString('base64'));
    const now = (Date.now() / 1000).toFixed(3);
    await writeFile(config.segmentFile, JSON.stringify({ ...line, at: now }));
    const first = await collect(config);
    const raw = JSON.parse(await readFile(first, 'utf8'));
    const batch = JSON.parse(Buffer.from(raw.payload, 'base64'));
    assert.equal(batch.observations.length, 1); assert.equal(batch.batchNumber, 1);
    assert.equal(batch.collectorContinuous, false); assert.equal(batch.clockSynchronized, false);
    await assert.rejects(collect(config), /already collected/);
    await writeFile(config.segmentFile, '');
    const empty = JSON.parse(Buffer.from(JSON.parse(await readFile(await collect(config), 'utf8')).payload, 'base64'));
    assert.equal(empty.epoch, batch.epoch); assert.equal(empty.batchNumber, 2);
    assert.equal(empty.firstSequence, 2); assert.equal(empty.lastSequence, 1);
    assert.deepEqual(empty.observations, []); assert.equal(empty.terminalCoverage, 'Unknown');
    assert.equal(JSON.parse(Buffer.from(JSON.parse(await readFile(await collect(config), 'utf8')).payload, 'base64')).batchNumber, 3);
    await assert.rejects(collect({ ...config, region: 'other' }), /Invalid collector state/);
    assert.equal((await readdir(directory)).filter(name => name.endsWith('.manifest')).length, 3);
  } finally {
    assert.equal(dirname(directory), resolve(tmpdir()));
    assert.ok(directory.startsWith(join(resolve(tmpdir()), 'uzllm-sli-node-')));
    await rm(directory, { recursive: true, force: true });
  }
});

test('pending delivery after crash reuses exact bytes and refuses conflicting acknowledgement', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'uzllm-sli-node-'));
  try {
    const config = { producer: 'edge-a', source: 'Edge', region: 'test', stateFile: join(directory, 'state'),
      outputDirectory: directory, keyFile: join(directory, 'key'), segmentFile: join(directory, 'never-read') };
    await writeFile(config.keyFile, key.toString('base64'));
    const batch = { version: 1, epoch: randomUUID(), batchNumber: 1, observations: [] };
    const bytes = sign('edge-a', batch, key);
    const state = { producer: config.producer, source: config.source, region: config.region, epoch: batch.epoch,
      nextBatch: 1, nextSequence: 1, lastEnd: new Date().toISOString(),
      pending: { envelope: bytes.toString('base64'), end: new Date().toISOString(), inputHash: null, count: 0 } };
    await writeFile(config.stateFile, JSON.stringify(state));
    const output = join(directory, `edge-a-${batch.epoch}-1.manifest`);
    await writeFile(output, bytes);
    assert.equal(await collect(config), output);
    assert.deepEqual(await readFile(output), bytes);
    assert.equal(JSON.parse(await readFile(config.stateFile, 'utf8')).nextBatch, 2);
    await writeFile(config.stateFile, JSON.stringify(state));
    await writeFile(output, 'conflicting delivery');
    await assert.rejects(collect(config), /Conflicting pending/);
    assert.equal(JSON.parse(await readFile(config.stateFile, 'utf8')).nextBatch, 1);
  } finally {
    assert.equal(dirname(directory), resolve(tmpdir()));
    assert.ok(directory.startsWith(join(resolve(tmpdir()), 'uzllm-sli-node-')));
    await rm(directory, { recursive: true, force: true });
  }
});
