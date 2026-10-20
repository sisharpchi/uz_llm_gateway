import { createHash, createHmac, randomUUID } from 'node:crypto';
import { open, readdir, rename, stat, unlink, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const maxBytes = 2 * 1024 * 1024;
const label = value => typeof value === 'string' && /^[A-Za-z0-9_.-]{1,64}$/.test(value);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

export function sign(producer, batch, key) {
  if (!label(producer) || key.length < 32) throw new Error('Invalid private collector identity/key');
  const raw = Buffer.from(JSON.stringify(batch));
  const payload = raw.toString('base64');
  const sha256 = hash(raw);
  const signature = createHmac('sha256', key).update(`${producer}\n${sha256}\n${payload}`).digest('hex');
  const envelope = Buffer.from(JSON.stringify({ producer, payload, sha256, signature }));
  if (envelope.length > maxBytes) throw new Error('Manifest exceeds bound');
  return envelope;
}

export function edgeObservations(segment, firstSequence) {
  if (Buffer.byteLength(segment) > 512 * 1024 || !Number.isSafeInteger(firstSequence) || firstSequence < 1)
    throw new Error('Invalid closed log segment');
  const lines = segment.split('\n').filter(line => line.trim());
  if (lines.length > 1024) throw new Error('Split closed segments at 1024 records; never truncate');
  const seen = new Set();
  return lines.map((line, index) => {
    const item = JSON.parse(line);
    if (Object.keys(item).sort().join(',') !== 'at,completion,duration,executionId,gatewayResponse,operation,status'
      || !/^[a-f0-9]{32}$/.test(item.executionId) || /^0+$/.test(item.executionId)
      || !['Chat', 'Responses', 'Embeddings'].includes(item.operation)
      || !Number.isInteger(item.status) || item.status < 100 || item.status > 599
      || !/^\d{1,12}\.\d{3}$/.test(item.at) || !/^\d{1,6}\.\d{3}$/.test(item.duration)
      || !['OK', ''].includes(item.completion) || !['Seen', 'Missing'].includes(item.gatewayResponse) || seen.has(item.executionId))
      throw new Error('Invalid safe edge observation');
    seen.add(item.executionId);
    const at = Math.round(Number(item.at) * 1000);
    const start = at - Math.round(Number(item.duration) * 1000);
    const id = item.executionId.replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5');
    return { sequence: firstSequence + index, executionId: id, phase: 'Terminal', operation: item.operation,
      mode: 'Unknown', at: new Date(at).toISOString(), startedAt: new Date(start).toISOString(),
      trustedEdge: true, httpStatus: item.status, terminalSuccess: false,
      clientDisconnected: item.status === 499 || item.completion !== 'OK', gatewayFault: item.status >= 500 && item.gatewayResponse === 'Missing',
      policyRejection: false, providerFault: 'None', credential: 'Unknown', requestId: null, attemptId: null };
  });
}

// Independent deployment runner only. No inference, API keys, redirects or body retention.
export async function readinessObservation(url, sequence, fetcher = fetch, now = () => new Date()) {
  const target = new URL(url);
  if (target.protocol !== 'https:' || target.username || target.password || target.search || target.hash
    || !['/edge/health', '/health/ready'].includes(target.pathname)) throw new Error('Only HTTPS readiness targets are allowed');
  const startedAt = now().toISOString();
  let httpStatus = 0;
  try {
    const response = await fetcher(target, { method: 'GET', redirect: 'manual', signal: AbortSignal.timeout(5000) });
    httpStatus = response.status;
    await response.body?.cancel();
  } catch { /* DNS/TLS/timeout/transport failure is evidence, never an empty success. */ }
  return { sequence, executionId: randomUUID(), phase: 'Probe', operation: 'Readiness', mode: 'Unknown',
    at: now().toISOString(), startedAt, trustedEdge: false, httpStatus, terminalSuccess: false,
    clientDisconnected: false, gatewayFault: httpStatus < 200 || httpStatus >= 300,
    policyRejection: false, providerFault: 'None', credential: 'Unknown', requestId: null, attemptId: null };
}

async function boundedRead(path, limit) {
  if ((await stat(path)).size > limit) throw new Error('Collector input exceeds bound');
  const file = await open(path, 'r');
  try {
    const bytes = Buffer.alloc(limit + 1);
    let length = 0;
    while (length < bytes.length) {
      const read = await file.read(bytes, length, bytes.length - length, null);
      if (read.bytesRead === 0) break;
      length += read.bytesRead;
    }
    if (length > limit) throw new Error('Collector input exceeds bound');
    return bytes.subarray(0, length);
  } finally { await file.close(); }
}

async function atomic(path, bytes) {
  await writeFile(path + '.tmp', bytes, { mode: 0o600 });
  await rename(path + '.tmp', path);
}

export async function collect(config) {
  if (!label(config.producer) || !label(config.region) || !['Edge', 'Probe'].includes(config.source)
    || !config.stateFile || !config.outputDirectory || !config.keyFile) throw new Error('Invalid collector configuration');
  // Single writer per producer/epoch. Never erase a stale lock automatically after a crash.
  const lock = await open(config.stateFile + '.lock', 'wx', 0o600);
  try {
    if ((await readdir(config.outputDirectory)).length >= 128) throw new Error('Private spool full; coverage incomplete');
    const key = Buffer.from((await boundedRead(config.keyFile, 256)).toString('utf8').trim(), 'base64');
    let state;
    try { state = JSON.parse((await boundedRead(config.stateFile, 2 * maxBytes)).toString('utf8')); }
    catch (error) {
      if (error.code !== 'ENOENT') throw error;
      state = { producer: config.producer, source: config.source, region: config.region,
        epoch: randomUUID(), nextBatch: 1, nextSequence: 1, lastEnd: new Date().toISOString(), lastInput: null };
    }
    if (!Number.isSafeInteger(state.nextBatch) || state.nextBatch < 1 || !Number.isSafeInteger(state.nextSequence)
      || state.nextSequence < 1 || !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(state.epoch)
      || state.producer !== config.producer || state.source !== config.source || state.region !== config.region)
      throw new Error('Invalid collector state; quarantine, do not reset');
    if (!state.pending) {
      let observations, inputHash = null;
      if (config.source === 'Edge') {
        const segment = await boundedRead(config.segmentFile, 512 * 1024);
        inputHash = hash(segment);
        observations = edgeObservations(segment.toString('utf8'), state.nextSequence);
        if (observations.length > 0 && inputHash === state.lastInput) throw new Error('Closed segment already collected');
      } else observations = [await readinessObservation(config.url, state.nextSequence)];
      const end = new Date().toISOString();
      const start = observations.reduce((value, item) => item.at < value ? item.at : value, state.lastEnd);
      if (new Date(end) - new Date(start) > 3600000) throw new Error('Collector interval exceeds one hour; reconcile missing coverage before a new epoch');
      const manifest = { version: 1, epoch: state.epoch, batchNumber: state.nextBatch, source: config.source,
        region: config.region, firstSequence: state.nextSequence,
        lastSequence: state.nextSequence + observations.length - 1, intervalStart: start, intervalEnd: end,
        dropped: 0, collectorContinuous: false, clockSynchronized: config.clockSynchronized === true,
        terminalCoverage: config.source === 'Probe' ? 'NotApplicable' : 'Unknown', observations };
      state.pending = { envelope: sign(config.producer, manifest, key).toString('base64'), end, inputHash, count: observations.length };
      // Persist exact bytes before delivery. Lost delivery acknowledgement replays the same manifest.
      await atomic(config.stateFile, JSON.stringify(state));
    }
    const output = join(config.outputDirectory, `${config.producer}-${state.epoch}-${state.nextBatch}.manifest`);
    const bytes = Buffer.from(state.pending.envelope, 'base64');
    try {
      if (!(await boundedRead(output, maxBytes)).equals(bytes)) throw new Error('Conflicting pending delivery');
    }
    catch (error) {
      if (error.code !== 'ENOENT') throw error;
      await atomic(output, bytes);
    }
    state.nextBatch++;
    state.nextSequence += state.pending.count;
    state.lastEnd = state.pending.end;
    state.lastInput = state.pending.inputHash;
    delete state.pending;
    await atomic(config.stateFile, JSON.stringify(state));
    return output;
  } finally { await lock.close(); await unlink(config.stateFile + '.lock'); }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { await collect(JSON.parse((await boundedRead(process.argv[2], 8192)).toString('utf8'))); }
  catch { console.error('SLI collector failed; reconcile private state/spool and missing coverage. No inference was invoked.'); process.exitCode = 1; }
}
