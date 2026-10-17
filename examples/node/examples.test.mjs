import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { configuration, createClient, operations, safeError } from './client.mjs';
import { runExample } from './operations.mjs';

const fixtures = JSON.parse(readFileSync(new URL('../../docs/api/examples.json', import.meta.url), 'utf8'));
const document = JSON.parse(readFileSync(new URL('../../docs/api/gateway-v1.openapi.json', import.meta.url), 'utf8'));
const env = { UZLLM_GATEWAY_URL: 'https://gateway.example.invalid', UZLLM_API_KEY: 'synthetic-example-key',
  UZLLM_MODEL: 'example-chat', UZLLM_ACK_BILLING: 'yes', UZLLM_IDEMPOTENCY_KEY: '11111111-1111-4111-8111-111111111111' };
const responses = fixtures.filter(item => item.path === '/v1/responses' && item.status === 200);
const embeddings = fixtures.filter(item => item.path === '/v1/embeddings');
const requestId = '11111111111141118111111111111111';
const originalId = '22222222222242228222222222222222';
const chat = fixtures.find(item => item.path === '/v1/chat/completions' && item.body?.choices?.[0]?.finish_reason === 'stop');
const ajv = new Ajv({ strict: false, allowUnionTypes: true });
addFormats(ajv);
for (const format of ['int32', 'int64', 'float', 'double', 'decimal'])
  ajv.addFormat(format, { type: 'number', validate: Number.isFinite });
function mock(body, mediaType = 'application/json', status = 200) {
  const calls = [];
  const config = configuration(env, 'chat');
  const client = createClient(config, async (url, options) => {
    calls.push({ url: String(url), ...options });
    return new Response(mediaType === 'application/json' ? JSON.stringify(body) : body,
      { status, headers: { 'content-type': mediaType, 'x-request-id': requestId, 'x-original-request-id': originalId } });
  });
  return { calls, config, client };
}
function chatSse(finish = true, usage = null) {
  const chunk = { id: 'chatcmpl-fixture', object: 'chat.completion.chunk', model: 'example-chat', created: 1 };
  return `data: ${JSON.stringify({ ...chunk, choices: [{ index: 0, delta: { content: 'Hello' }, finish_reason: finish ? 'stop' : null }], usage })}\n\n`
    + 'data: [DONE]\n\n';
}

for (const operation of operations) test(`${operation}: real SDK sends exactly one bounded, schema-valid operation to configured Gateway`, async () => {
  const path = operation === 'models' ? '/v1/models' : operation.startsWith('chat') ? '/v1/chat/completions'
    : operation.startsWith('responses') ? '/v1/responses' : '/v1/embeddings';
  const body = operation === 'models' ? { object: 'list', data: [] }
    : operation === 'chat-stream' ? chatSse(true, { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 })
      : operation === 'chat' ? chat.body : operation === 'embeddings' ? embeddings[0].body
        : responses.find(item => item.mediaType === (operation.endsWith('stream') ? 'text/event-stream' : 'application/json')).body;
  const { client, config, calls } = mock(body, operation.endsWith('stream') ? 'text/event-stream' : 'application/json');
  const result = await runExample(client, config, operation);
  assert.equal(calls.length, 1); assert.equal(calls[0].url, env.UZLLM_GATEWAY_URL + path);
  const headers = new Headers(calls[0].headers);
  assert.equal(headers.get('authorization'), 'Bearer synthetic-example-key');
  assert.equal(headers.get('idempotency-key'), env.UZLLM_IDEMPOTENCY_KEY);
  assert.equal(calls[0].redirect, 'error');
  assert.equal(result.requestId, requestId); assert.equal(client.logLevel, 'off');
  if (operation === 'models') assert.deepEqual(result.models, []);
  else {
    const requestBody = JSON.parse(calls[0].body);
    const validate = ajv.compile({ ...document.paths[path].post.requestBody.content['application/json'].schema, components: document.components });
    assert.ok(validate(requestBody), JSON.stringify(validate.errors));
    assert.equal(requestBody.uzllm, undefined); // Managed only; no implicit credentials/fallback.
    assert.ok(result.usage !== undefined);
    if (operation === 'embeddings') {
      assert.equal(requestBody.encoding_format, 'float'); assert.deepEqual(requestBody.input, ['Hello', 'World']);
      assert.deepEqual(result.vectors, [{ index: 0, dimensions: 2 }, { index: 1, dimensions: 2 }]);
    } else if (operation.startsWith('responses')) {
      assert.equal(requestBody.store, false); assert.equal(requestBody.max_output_tokens, 32);
      assert.equal(result.status, 'completed'); assert.ok(result.text.length > 0);
    } else { assert.equal(requestBody.max_completion_tokens, 32); assert.equal(result.text, 'Hello'); }
    if (operation.endsWith('stream')) assert.ok(result.deltas > 0);
  }
});

for (const [index, fixture] of responses.entries()) test(`Responses serializer fixture ${index}: preserve incomplete/refusal/null usage or reject failed terminal`, async () => {
  const { client, config } = mock(fixture.body, fixture.mediaType);
  const stream = fixture.mediaType === 'text/event-stream';
  const failed = stream && fixture.body.includes('event: response.failed');
  if (failed) await assert.rejects(runExample(client, config, 'responses-stream'), /failed/);
  else {
    const result = await runExample(client, config, stream ? 'responses-stream' : 'responses');
    const terminal = stream ? JSON.parse(fixture.body.trim().split('\n\n').at(-1).split('\n')[1].slice(6)).response : fixture.body;
    assert.equal(result.status, terminal.status);
    assert.deepEqual(result.usage, terminal.usage);
    assert.deepEqual(result.incompleteDetails, terminal.incomplete_details);
    assert.equal(result.text, terminal.output.flatMap(item => item.content).filter(part => part.type === 'output_text').map(part => part.text).join(''));
    assert.equal(result.refusal, terminal.output.flatMap(item => item.content).filter(part => part.type === 'refusal').map(part => part.refusal).join(''));
  }
});

for (const operation of ['chat', 'chat-stream', 'embeddings']) test(`${operation}: missing usage stays null, never zero`, async () => {
  const body = operation === 'embeddings' ? embeddings[1].body : operation === 'chat-stream' ? chatSse() : chat.body;
  const { client, config } = mock(body, operation === 'chat-stream' ? 'text/event-stream' : 'application/json');
  assert.equal((await runExample(client, config, operation)).usage, null);
});

for (const status of [400, 401, 402, 403, 409, 429, 502, 503]) test(`HTTP ${status}: SDK never retries or leaks server-supplied secrets in error output`, async () => {
  const { client, config, calls } = mock({ error: { message: env.UZLLM_API_KEY, code: env.UZLLM_API_KEY, type: 'server_error' } }, 'application/json', status);
  await assert.rejects(runExample(client, config, 'responses'), error => {
    const summary = safeError(error, config.idempotencyKey);
    assert.equal(summary.status, status); assert.equal(summary.requestId, requestId);
    assert.equal(summary.originalRequestId, originalId); assert.equal(summary.idempotencyKey, config.idempotencyKey);
    assert.equal(JSON.stringify(summary).includes(env.UZLLM_API_KEY), false);
    return true;
  });
  assert.equal(calls.length, 1);
});

test('transport failure is not retried', async () => {
  let count = 0; const config = configuration(env, 'chat');
  const client = createClient(config, async () => { count++; throw new Error(env.UZLLM_API_KEY); });
  await assert.rejects(runExample(client, config, 'chat'));
  assert.equal(count, 1);
});

test('error metadata rejects arbitrary request-ID text rather than echoing a secret', () => {
  const summary = safeError({ status: 503, headers: new Headers({ 'x-request-id': env.UZLLM_API_KEY,
    'x-original-request-id': env.UZLLM_API_KEY }) }, env.UZLLM_IDEMPOTENCY_KEY);
  assert.equal(summary.requestId, null); assert.equal(summary.originalRequestId, null);
  assert.equal(JSON.stringify(summary).includes(env.UZLLM_API_KEY), false);
});

test('cancelled stream aborts consumption without a second request or successful terminal', async () => {
  const controller = new AbortController(); let cancelled = false; let count = 0;
  const config = configuration(env, 'responses-stream');
  const client = createClient(config, async () => {
    count++;
    return new Response(new ReadableStream({ start(stream) { controller.signal.addEventListener('abort', () => {
      cancelled = true; stream.error(new DOMException('Cancelled', 'AbortError'));
    }, { once: true }); }, cancel() { cancelled = true; } }), { headers: { 'content-type': 'text/event-stream' } });
  });
  const running = runExample(client, config, 'responses-stream', controller.signal);
  setTimeout(() => controller.abort(), 20);
  await assert.rejects(running); assert.equal(count, 1); assert.equal(cancelled, true);
});

for (const operation of ['chat-stream', 'responses-stream']) test(`${operation}: truncated stream is not completion`, async () => {
  const body = operation === 'chat-stream' ? chatSse(false) : responses.find(item => item.mediaType === 'text/event-stream').body.split('event: response.completed')[0];
  const { client, config } = mock(body, 'text/event-stream');
  await assert.rejects(runExample(client, config, operation));
});

test('configuration forbids accidental provider destination, URL secrets, nonlocal HTTP and implicit billing consent', () => {
  for (const url of [undefined, 'http://example.invalid', 'https://user:secret@example.invalid', 'https://example.invalid/v1', 'https://example.invalid/?key=secret', 'file:///tmp/key', 'https://example.invalid/#key'])
    assert.throws(() => configuration({ ...env, UZLLM_GATEWAY_URL: url }, 'chat'));
  for (const override of [{ UZLLM_API_KEY: undefined, OPENAI_API_KEY: 'synthetic-fallback' }, { UZLLM_API_KEY: 'has whitespace' },
    { UZLLM_ACK_BILLING: undefined }, { UZLLM_MODEL: '' }, { UZLLM_IDEMPOTENCY_KEY: 'not-a-uuid' }])
    assert.throws(() => configuration({ ...env, ...override }, 'chat'));
  assert.throws(() => configuration(env, 'unknown'));
  assert.equal(configuration({ ...env, UZLLM_GATEWAY_URL: 'http://localhost:5214' }, 'chat').baseURL, 'http://localhost:5214/v1');
  assert.ok(configuration({ ...env, UZLLM_ACK_BILLING: undefined, UZLLM_MODEL: undefined }, 'models'));
});

test('CLI configuration errors cannot print environment secrets or URLs', () => {
  const result = spawnSync(process.execPath, ['run.mjs', 'chat'], { cwd: new URL('.', import.meta.url),
    env: { ...process.env, ...env, UZLLM_GATEWAY_URL: `https://user:${env.UZLLM_API_KEY}@example.invalid` }, encoding: 'utf8' });
  assert.equal(result.status, 1); assert.equal(result.stdout, '');
  assert.equal(result.stderr.includes(env.UZLLM_API_KEY), false);
  assert.match(result.stderr, /no request was sent/);
});
