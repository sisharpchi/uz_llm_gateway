import test from 'node:test';
import assert from 'node:assert/strict';
import { assessQualification } from './qualify-report.mjs';
import { parsePrometheus } from './collect-metrics.mjs';

function evidence() {
  const metric = values => ({ values });
  return {
    summary: { metrics: {
      http_reqs: metric({ count: 601, rate: 6.7 }),
      uzllm_incomplete_streams: metric({ count: 0 }),
      checks: metric({ passes: 1803, fails: 0, value: 1 }),
      http_req_failed: metric({ passes: 0, fails: 601, value: 0 }),
      http_req_waiting: metric({ med: 80, 'p(95)': 120, 'p(99)': 170 }),
      http_req_duration: metric({ med: 30080, 'p(95)': 30120, 'p(99)': 30170 }),
    } },
    telemetry: { samples: Array.from({ length: 30 }, () => ({ a: 101, b: 100 })),
      ttft: { a: { sum: 2400, count: 300 }, b: { sum: 2700, count: 301 } } },
    financialSamples: { samples: Array.from({ length: 30 }, () => ({
      organizationId: 'tenant', posted: '100000', reserved: '4000',
      reservedSum: '4000', ledgerSum: '100000' })), failures: [] },
    before: { organizationId: 'tenant', posted: '100000', reserved: '0', ledgerSum: '100000',
      openReservations: 0, reservedSum: '0', settledCount: 0, capturedSum: '0',
      usageChargeCount: 0, usageChargeSum: '0' },
    after: { organizationId: 'tenant', posted: '93389', reserved: '0', ledgerSum: '93389',
      openReservations: 0, reservedSum: '0', settledCount: 601, capturedSum: '6611',
      usageChargeCount: 601, usageChargeSum: '-6611' },
    topology: { gatewayA: { host: 'node-a.example', instanceId: 'a' },
      gatewayB: { host: 'node-b.example', instanceId: 'b' } },
  };
}

test('OPS_006_10_RPS_60s_and_200_streams_on_distinct_nodes_pass_with_reconciled_finances', () => {
  const result = assessQualification(evidence());
  assert.equal(result.passed, true, JSON.stringify(result));
  assert.equal(result.load.requests, 601);
  assert.equal(result.load.peakActiveStreams, 201);
  assert.equal(result.load.bothNodesActive, true);
  assert.equal(result.load.clientTtfbMs.p95, 120);
  assert.equal(result.load.gatewayTtft.a.count, 300);
  assert.equal(result.financial.capturedDelta, '6611');
  assert.equal(result.financial.reserved, '0');
});

test('OPS_006_same_host_or_instance_cannot_masquerade_as_two_nodes', () => {
  const sameHost = evidence();
  sameHost.topology.gatewayB.host = sameHost.topology.gatewayA.host;
  assert.throws(() => assessQualification(sameHost), /Two distinct Gateway hosts/);
  const sameInstance = evidence();
  sameInstance.topology.gatewayB.instanceId = 'a';
  assert.throws(() => assessQualification(sameInstance), /Two distinct Gateway hosts/);
});

test('OPS_006_missing_server_telemetry_or_TTFT_rejects_qualification', () => {
  const noActive = evidence();
  noActive.telemetry.samples = [];
  assert.throws(() => assessQualification(noActive), /active-stream samples/);
  const noTtft = evidence();
  noTtft.telemetry.ttft.b.count = 0;
  assert.throws(() => assessQualification(noTtft), /Gateway TTFT/);
});

test('OPS_006_199_streams_or_only_one_active_node_fails', () => {
  const tooFew = evidence();
  tooFew.telemetry.samples = Array.from({ length: 30 }, () => ({ a: 100, b: 99 }));
  assert.equal(assessQualification(tooFew).passed, false);
  const oneNode = evidence();
  oneNode.telemetry.samples = Array.from({ length: 30 }, () => ({ a: 201, b: 0 }));
  assert.equal(assessQualification(oneNode).passed, false);
});

test('OPS_006_dropped_incomplete_or_failed_requests_fail', () => {
  for (const mutate of [
    run => { run.summary.metrics.dropped_iterations = { values: { count: 1 } }; },
    run => { run.summary.metrics.uzllm_incomplete_streams.values.count = 1; },
    run => { run.summary.metrics.checks.values.fails = 1; },
    run => { run.summary.metrics.http_req_failed.values.value = 0.02; },
    run => { run.summary.metrics.http_reqs.values.count = 599; },
  ]) {
    const run = evidence(); mutate(run);
    assert.equal(assessQualification(run).passed, false);
  }
});

test('OPS_006_over_admission_or_duplicate_financial_capture_fails', () => {
  for (const mutate of [
    run => { run.after.reserved = '1'; },
    run => { run.after.ledgerSum = '93390'; },
    run => { run.after.capturedSum = '6612'; },
    run => { run.after.usageChargeCount = 602; },
    run => { run.after.posted = '-1'; },
    run => { run.financialSamples.samples[10].reserved = '100001'; },
    run => { run.financialSamples.samples[10].organizationId = 'other-tenant'; },
  ]) {
    const run = evidence(); mutate(run);
    assert.equal(assessQualification(run).passed, false);
  }
});

test('OPS_006_unreconciled_baseline_is_rejected_before_load_delta', () => {
  const run = evidence();
  run.before.ledgerSum = '99999';
  assert.throws(() => assessQualification(run), /Baseline wallet and ledger/);
});

test('OPS_006_missing_or_failed_during_load_financial_samples_reject_qualification', () => {
  const tooFew = evidence();
  tooFew.financialSamples.samples.length = 19;
  assert.throws(() => assessQualification(tooFew), /20 consistent/);
  const failed = evidence();
  failed.financialSamples.failures.push({ message: 'PostgreSQL unavailable' });
  assert.throws(() => assessQualification(failed), /20 consistent/);
});

test('OPS_006_prometheus_samples_separate_instance_streams_and_TTFT', () => {
  const text = [
    'uzllm_gateway_active_streams{service_instance_id="a",outcome="success"} 101',
    'uzllm_gateway_active_streams{service_instance_id="b",outcome="success"} 100',
    'uzllm_gateway_ttft_ms_sum{service_instance_id="a"} 2400',
    'uzllm_gateway_ttft_ms_count{service_instance_id="a"} 300',
    'uzllm_gateway_ttft_ms_sum{service_instance_id="b"} 2700',
    'uzllm_gateway_ttft_ms_count{service_instance_id="b"} 301',
    'uzllm_gateway_active_streams{service_instance_id="unrelated"} 999',
  ].join('\n');
  const parsed = parsePrometheus(text, ['a', 'b']);
  assert.deepEqual(parsed.active, { a: 101, b: 100 });
  assert.deepEqual(parsed.ttft, { a: { sum: 2400, count: 300 },
    b: { sum: 2700, count: 301 } });
  assert.deepEqual(parsed.seen.sort(), ['a', 'b']);
});

test('OPS_006_k6_summary_export_flat_metrics_are_accepted', () => {
  const run = evidence();
  run.summary.metrics = Object.fromEntries(Object.entries(run.summary.metrics)
    .map(([name, metric]) => [name, metric.values]));
  assert.equal(assessQualification(run).passed, true);
});
