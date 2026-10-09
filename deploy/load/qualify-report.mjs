import { readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const integer = (value, label) => {
  if (!Number.isSafeInteger(Number(value)) || Number(value) < 0)
    throw new Error(`${label} must be a non-negative safe integer`);
  return Number(value);
};
const money = (value, label) => {
  if (typeof value !== 'string' || !/^-?\d+$/.test(value))
    throw new Error(`${label} must be an integer string`);
  return BigInt(value);
};
const metric = (summary, name, field) => {
  const values = summary?.metrics?.[name]?.values ?? summary?.metrics?.[name];
  const value = values?.[field];
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error(`Missing k6 metric ${name}.${field}`);
  return value;
};

export function assessQualification({ summary, telemetry, financialSamples, before, after, topology }) {
  const hostA = topology?.gatewayA?.host;
  const hostB = topology?.gatewayB?.host;
  if (!hostA || !hostB || hostA === hostB || topology?.gatewayA?.instanceId === topology?.gatewayB?.instanceId)
    throw new Error('Two distinct Gateway hosts and instance IDs are required');
  if (!topology.gatewayA.instanceId || !topology.gatewayB.instanceId)
    throw new Error('Both Gateway instance IDs are required');
  const nodeIds = [topology.gatewayA.instanceId, topology.gatewayB.instanceId];
  const samples = telemetry?.samples;
  if (!Array.isArray(samples) || samples.length < 20)
    throw new Error('At least 20 collector-backed active-stream samples are required');
  let peak = 0;
  let bothActive = false;
  for (const sample of samples) {
    const a = integer(sample[nodeIds[0]], `active streams ${nodeIds[0]}`);
    const b = integer(sample[nodeIds[1]], `active streams ${nodeIds[1]}`);
    peak = Math.max(peak, a + b);
    if (a > 0 && b > 0) bothActive = true;
  }
  const gatewayTtft = {};
  for (const id of nodeIds) {
    const sum = telemetry.ttft?.[id]?.sum;
    const count = telemetry.ttft?.[id]?.count;
    if (!Number.isFinite(sum) || !Number.isSafeInteger(count) || count <= 0)
      throw new Error(`Missing collector-backed Gateway TTFT for ${id}`);
    gatewayTtft[id] = { count, meanMs: sum / count };
  }
  const requests = integer(metric(summary, 'http_reqs', 'count'), 'request count');
  // k6 omits dropped_iterations entirely when no iterations were dropped.
  const dropped = summary?.metrics?.dropped_iterations
    ? integer(metric(summary, 'dropped_iterations', 'count'), 'dropped iterations') : 0;
  const incomplete = integer(metric(summary, 'uzllm_incomplete_streams', 'count'), 'incomplete streams');
  const checkFailures = integer(metric(summary, 'checks', 'fails'), 'check failures');
  const errorRate = metric(summary, 'http_req_failed', 'value');
  const observedRps = metric(summary, 'http_reqs', 'rate');
  const waiting = summary?.metrics?.http_req_waiting?.values ?? summary?.metrics?.http_req_waiting;
  const duration = summary?.metrics?.http_req_duration?.values ?? summary?.metrics?.http_req_duration;
  for (const [name, values] of [['TTFB', waiting], ['duration', duration]])
    for (const percentile of ['med', 'p(95)', 'p(99)'])
      if (typeof values?.[percentile] !== 'number' || !Number.isFinite(values[percentile]))
        throw new Error(`Missing ${name} ${percentile}`);
  if (before.organizationId !== after.organizationId || !before.organizationId)
    throw new Error('Financial snapshots must identify the same organization');
  if (money(before.posted, 'baseline posted') !== money(before.ledgerSum, 'baseline ledger')
    || money(before.reserved, 'baseline reserved') !== 0n
    || money(before.reservedSum, 'baseline holds') !== 0n
    || integer(before.openReservations, 'baseline open reservations') !== 0)
    throw new Error('Baseline wallet and ledger must reconcile with no open holds');
  const posted = money(after.posted, 'posted');
  const reserved = money(after.reserved, 'reserved');
  const ledgerSum = money(after.ledgerSum, 'ledger sum');
  const reservedSum = money(after.reservedSum, 'reservation sum');
  const capturedDelta = money(after.capturedSum, 'captured sum') - money(before.capturedSum, 'baseline captured sum');
  const chargeDelta = money(after.usageChargeSum, 'charge sum') - money(before.usageChargeSum, 'baseline charge sum');
  const chargeCountDelta = integer(after.usageChargeCount, 'charge count') - integer(before.usageChargeCount, 'baseline charge count');
  const settledDelta = integer(after.settledCount, 'settled count') - integer(before.settledCount, 'baseline settled count');
  const reservedCount = integer(after.openReservations, 'open reservation count');
  if (!Array.isArray(financialSamples?.samples) || financialSamples.samples.length < 20
    || !Array.isArray(financialSamples.failures) || financialSamples.failures.length)
    throw new Error('At least 20 consistent during-load financial snapshots are required');
  const unsafeSample = financialSamples.samples.find(sample => {
    if (sample.organizationId !== before.organizationId) return true;
    const samplePosted = money(sample.posted, 'sample posted');
    const sampleReserved = money(sample.reserved, 'sample reserved');
    return samplePosted < sampleReserved || sampleReserved < 0n
      || sampleReserved !== money(sample.reservedSum, 'sample holds')
      || samplePosted !== money(sample.ledgerSum, 'sample ledger');
  });
  const financialPass = posted >= reserved && reserved === reservedSum && posted === ledgerSum
    && capturedDelta === -chargeDelta && chargeCountDelta === settledDelta
    && reservedCount === 0 && reserved === 0n && capturedDelta >= 0n && !unsafeSample;
  // k6's observed rate includes the grace period while 30-second streams drain;
  // fixed 10/s arrival for 60s is proven by the scripted schedule and count.
  const loadPass = requests >= 600 && dropped === 0
    && incomplete === 0 && checkFailures === 0 && errorRate < 0.01
    && peak >= 200 && bothActive;
  return {
    passed: loadPass && financialPass,
    load: { requests, scheduledRps: 10, observedRps, dropped, incomplete, checkFailures, errorRate,
      clientTtfbMs: { p50: waiting.med, p95: waiting['p(95)'], p99: waiting['p(99)'] },
      durationMs: { p50: duration.med, p95: duration['p(95)'], p99: duration['p(99)'] },
      gatewayTtft, peakActiveStreams: peak, bothNodesActive: bothActive, passed: loadPass },
    financial: { posted: posted.toString(), reserved: reserved.toString(),
      capturedDelta: capturedDelta.toString(), chargeDelta: chargeDelta.toString(),
      settledDelta, chargeCountDelta, observedSamples: financialSamples.samples.length,
      unsafeSampleObserved: Boolean(unsafeSample), passed: financialPass },
    topology: { gatewayA: topology.gatewayA, gatewayB: topology.gatewayB },
  };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [summaryPath, telemetryPath, financialPath, beforePath, afterPath, topologyPath, reportPath] = process.argv.slice(2);
  if (![summaryPath, telemetryPath, financialPath, beforePath, afterPath, topologyPath, reportPath].every(Boolean)) {
    process.stderr.write('Usage: node qualify-report.mjs k6.json telemetry.json financial-samples.json before.json after.json topology.json report.json\n');
    process.exitCode = 2;
  } else {
    try {
      const revision = process.env.GITHUB_SHA ?? spawnSync('git', ['rev-parse', 'HEAD'],
        { encoding: 'utf8' }).stdout?.trim();
      const environment = process.env.UZLLM_LOAD_ENVIRONMENT;
      if (!/^[a-f0-9]{40}$/i.test(revision ?? '') || !environment)
        throw new Error('A Git revision and UZLLM_LOAD_ENVIRONMENT are required for an evidence report');
      const read = path => JSON.parse(readFileSync(path, 'utf8'));
      const result = assessQualification({ summary: read(summaryPath), telemetry: read(telemetryPath),
        financialSamples: read(financialPath),
        before: read(beforePath), after: read(afterPath), topology: read(topologyPath) });
      writeFileSync(reportPath, JSON.stringify({ task: 'OPS-006',
        revision, environment,
        completedAt: new Date().toISOString(), ...result }, null, 2));
      process.stdout.write(`OPS-006 report: ${reportPath}; passed=${result.passed}\n`);
      if (!result.passed) process.exitCode = 1;
    } catch (error) {
      process.stderr.write(`${error}\n`);
      process.exitCode = 1;
    }
  }
}
