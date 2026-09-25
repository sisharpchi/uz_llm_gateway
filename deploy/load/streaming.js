import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const incompleteStreams = new Counter('uzllm_incomplete_streams');

export const options = {
  scenarios: {
    streaming: {
      executor: 'constant-arrival-rate',
      rate: 10,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 400,
      maxVUs: 400,
      gracefulStop: '45s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    dropped_iterations: ['count==0'],
    uzllm_incomplete_streams: ['count==0'],
  },
};

export default function () {
  const baseUrl = __ENV.UZLLM_LOAD_URL;
  const apiKey = __ENV.UZLLM_LOAD_API_KEY;
  const model = __ENV.UZLLM_LOAD_MODEL;
  const runId = __ENV.UZLLM_LOAD_RUN_ID;
  if (!baseUrl || !apiKey || !model || !/^[0-9a-f]{8}$/i.test(runId ?? '')) {
    throw new Error('Load URL, key, model and an eight-hex-digit run ID are required.');
  }
  const suffix = (__VU * 1_000_000 + __ITER + 1).toString(16).padStart(12, '0');
  const idempotencyKey = `${runId}-0000-4000-8000-${suffix}`;
  const response = http.post(`${baseUrl.replace(/\/$/, '')}/v1/chat/completions`, JSON.stringify({
    model,
    messages: [{ role: 'user', content: 'Say one short sentence.' }],
    max_completion_tokens: 32,
    stream: true,
  }), {
    headers: {
      Authorization: `Bearer ${apiKey}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    timeout: '45s',
    tags: { endpoint: 'chat-stream' },
  });
  const complete = response.status === 200 && response.body.includes('data: [DONE]');
  if (!complete) incompleteStreams.add(1);
  check(response, {
    'stream returns 200': value => value.status === 200,
    'stream ends with DONE': value => value.body.includes('data: [DONE]'),
    'response has request id': value => Boolean(value.headers['X-Request-Id']),
  });
}
