import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { artifactMatches, canonicalize } from './api-contracts.mjs';
import ts from 'typescript';
import { createHash } from 'node:crypto';

const read = name => JSON.parse(readFileSync(new URL('../../../docs/api/' + name, import.meta.url), 'utf8'));
const documents = { gateway: read('gateway-v1.openapi.json'), management: read('management-v1.openapi.json') };
const methods = ['get', 'post', 'put', 'patch', 'delete'];

test('statement client consumes 202 jobs preserves empty auth replies and verifies CSV before returning Blob', async () => {
  const source = readFileSync(new URL('../../packages/api-client/index.ts', import.meta.url), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
  const { management } = await import('data:text/javascript;base64,' + Buffer.from(js).toString('base64'));
  const fetchBefore = globalThis.fetch;
  const documentBefore = Object.getOwnPropertyDescriptor(globalThis, 'document');
  Object.defineProperty(globalThis, 'document', { configurable: true, value: { cookie: '__Host-uzllm-csrf=synthetic-csrf' } });
  try {
    const body = { periodStart: '2026-02-01T00:00:00Z', idempotencyKey: '11111111-1111-4111-8111-111111111111', expectedGeneration: 0, reason: 'Monthly statement' };
    globalThis.fetch = async (path, input) => {
      assert.equal(path, '/management/v1/admin/organizations/tenant/reports/statements');
      assert.equal(input.method, 'POST'); assert.equal(input.credentials, 'same-origin');
      assert.equal(input.headers.get('X-CSRF-Token'), 'synthetic-csrf'); assert.deepEqual(JSON.parse(input.body), body);
      return new Response(JSON.stringify({ id: 'operation', operationStatus: 'Pending', jobState: 'Queued' }), { status: 202, headers: { 'Content-Type': 'application/json' } });
    };
    assert.deepEqual(await management.createStatement('tenant', body), { id: 'operation', operationStatus: 'Pending', jobState: 'Queued' });
    globalThis.fetch = async () => new Response(null, { status: 202 });
    assert.equal(await management.recover('example@example.test'), undefined);
    const csv = '"Not a tax invoice / No payment due from this statement"\r\nordinal,ledger_id,category,posted_at,amount_micro_usd\r\n';
    const sha = createHash('sha256').update(csv).digest('hex');
    for (const mode of ['valid', 'missing-sha', 'mismatch-sha', 'wrong-length']) {
      globalThis.fetch = async path => {
        assert.equal(path, '/management/v1/organizations/tenant/statements/immutable-version/export?format=csv');
        const headers = { 'Content-Type': 'text/csv; charset=utf-8', 'Content-Length': String(Buffer.byteLength(csv) + (mode === 'wrong-length' ? 1 : 0)) };
        if (mode !== 'missing-sha') headers['X-UZLLM-Report-Sha256'] = mode === 'mismatch-sha' ? '0'.repeat(64) : sha;
        return new Response(csv, { headers });
      };
      if (mode === 'valid') assert.equal(await (await management.exportStatement('tenant', 'immutable-version')).text(), csv);
      else await assert.rejects(management.exportStatement('tenant', 'immutable-version'), /Statement download verification failed/);
    }
  } finally {
    globalThis.fetch = fetchBefore;
    if (documentBefore) Object.defineProperty(globalThis, 'document', documentBefore);
    else delete globalThis.document;
  }
});

test('statement contracts pin immutable versions session security exact money and CSV verification', () => {
  const document = documents.management;
  const customer = '/management/v1/organizations/{organizationId}/statements';
  const admin = '/management/v1/admin/organizations/{organizationId}/reports';
  for (const [path, method] of [[customer, 'get'], [customer + '/{id}', 'get'],
    [customer + '/{id}/lines', 'get'], [customer + '/{id}/export', 'get'],
    [admin + '/statements', 'post'], [admin + '/statements/{id}/revisions', 'post'], [admin + '/jobs/{id}', 'get']]) {
    const operation = document.paths[path][method];
    assert.deepEqual(operation.security, [{ Session: [] }]);
    for (const code of ['401', '403', '503', '504']) assert.ok(operation.responses[code]);
    if (method === 'post') {
      assert.ok(operation.parameters.some(item => item.name === 'X-CSRF-Token' && item.required));
      assert.ok(operation.responses['202'].content['application/json']);
      assert.ok(operation.responses['202'].headers.Location);
      for (const code of ['400', '404', '409', '413', '415']) assert.ok(operation.responses[code]);
      assert.match(operation.description, /recent MFA/);
    }
  }
  const fixtures = read('examples.json').filter(item => item.path.startsWith(customer) || item.path.startsWith(admin));
  assert.equal(fixtures.length, 7);
  const metadata = fixtures.find(item => item.path === customer + '/{id}').body;
  assert.equal(metadata.kind, 'AccountStatement');
  assert.equal(metadata.label, 'Not a tax invoice / No payment due from this statement');
  assert.equal(metadata.totals.openingMicroUsd, '9007199254740993');
  assert.equal(metadata.generation, metadata.revision);
  for (const field of ['snapshotJson', 'sources', 'reason', 'requestedByAccountId', 'providerCost', 'paymentAnnotations'])
    assert.equal(metadata[field], undefined);
  const schema = document.paths[customer + '/{id}'].get.responses['200'].content['application/json'].schema;
  const validate = validator(document, schema);
  assert.ok(validate(metadata));
  assert.equal(validate({ ...metadata, totals: { ...metadata.totals, openingMicroUsd: 9007199254740993 } }), false);
  const csv = document.paths[customer + '/{id}/export'].get.responses['200'];
  assert.ok(csv.content['text/csv']);
  for (const header of ['Content-Length', 'X-UZLLM-Report-Sha256', 'Content-Disposition', 'X-Content-Type-Options']) assert.ok(csv.headers[header]);
  const dead = fixtures.find(item => item.path === admin + '/jobs/{id}').body;
  assert.equal(dead.operationStatus, 'Pending'); assert.equal(dead.jobState, 'DeadLettered'); assert.equal(dead.job.attemptCount, 5);
  const create = validator(document, document.paths[admin + '/statements'].post.requestBody.content['application/json'].schema);
  const command = { periodStart: '2026-02-01T00:00:00Z', idempotencyKey: '11111111-1111-4111-8111-111111111111', expectedGeneration: 0, reason: 'Monthly statement', schemaVersion: 1 };
  assert.ok(create(command));
  for (const invalid of [{ ...command, schemaVersion: 2 }, { ...command, expectedGeneration: 1 }, { ...command, amountMicroUsd: '1' }])
    assert.equal(create(invalid), false);
});
function validator(document, schema) {
  const ajv = new Ajv({ strict: false, allErrors: true, allowUnionTypes: true });
  addFormats(ajv);
  for (const format of ['int32', 'int64', 'float', 'double', 'decimal'])
    ajv.addFormat(format, { type: 'number', validate: value => Number.isFinite(value) });
  return ajv.compile({ ...schema, components: document.components });
}

test('versioned documents cover implemented surfaces and resolve all schema references', () => {
  for (const [name, document] of Object.entries(documents)) {
    assert.equal(document.openapi, '3.1.1');
    assert.equal(document.info.version, '1.0.0');
    assert.deepEqual(document.servers, [{ url: '/' }]);
    const ids = new Set();
    for (const [path, item] of Object.entries(document.paths)) {
      assert.ok(path.startsWith(name === 'gateway' ? '/v1/' : '/management/v1/'));
      for (const method of methods.filter(method => item[method])) {
        const operation = item[method];
        assert.ok(operation.operationId && !ids.has(operation.operationId)); ids.add(operation.operationId);
        assert.ok(Object.keys(operation.responses).some(status => /^2\d\d$/.test(status)));
        for (const [status, response] of Object.entries(operation.responses)) {
          if (name === 'management') assert.deepEqual(response.headers['Cache-Control'].schema.enum, ['no-store']);
          const sliJob = path === '/management/v1/admin/sli-reports'
            || path === '/management/v1/admin/sli-reports/{id}/revisions';
          if (status === '204' || status === '202' && !path.includes('/reports/statements') && !sliJob) assert.equal(response.content, undefined);
          if (status === '202' && sliJob) {
            assert.equal(response.content['application/json'].schema.$ref, '#/components/schemas/SliReportJobResponse');
            assert.ok(response.headers.Location);
          }
          for (const media of Object.values(response.content ?? {})) assert.ok(media.schema);
        }
      }
    }
    const visit = value => {
      if (!value || typeof value !== 'object') return;
      if (value.$ref) {
        assert.ok(value.$ref.startsWith('#/components/'), 'External refs must not make offline generation depend on a network');
        assert.ok(value.$ref.slice(2).split('/').reduce((target, key) => target?.[key.replaceAll('~1', '/').replaceAll('~0', '~')], document), value.$ref);
      }
      for (const child of Object.values(value)) visit(child);
    };
    visit(document);
    for (const schema of Object.values(document.components.schemas ?? {})) validator(document, schema);
  }
  assert.deepEqual(Object.keys(documents.gateway.paths).sort(), ['/v1/chat/completions', '/v1/embeddings', '/v1/models', '/v1/responses']);
});

test('wallet credits expose exact amounts independent approval and protected mutation contracts', () => {
  const document = documents.management;
  const path = '/management/v1/admin/wallet-adjustments';
  const create = document.paths[path].post;
  const approve = document.paths[path + '/{id}/approve'].post;
  for (const operation of [create, approve]) {
    assert.ok(operation.security.some(item => item.Session));
    assert.ok(operation.parameters.some(item => item.name === 'X-CSRF-Token' && item.required));
    for (const status of ['200', '400', '401', '403', '404', '409']) assert.ok(operation.responses[status]);
  }
  assert.ok(create.responses['201']);
  const validate = validator(document, approve.requestBody.content['application/json'].schema);
  assert.ok(validate({ reason: 'Independent credit approval' }));
  for (const extra of [{ amountMicroUsd: '9000' }, { organizationId: '11111111-1111-4111-8111-111111111111' }, { direction: 'debit' }])
    assert.equal(validate({ reason: 'Independent credit approval', ...extra }), false);
  const fixtures = read('examples.json').filter(item => item.path.startsWith(path));
  assert.equal(fixtures.length, 6);
  const pending = fixtures.find(item => item.status === 201).body;
  assert.equal(pending.status, 'Pending');
  assert.equal(pending.approval, null);
  assert.equal(pending.amountMicroUsd, '9007199254740993');
  const approved = fixtures.find(item => item.path.endsWith('/approve')).body;
  assert.notEqual(approved.requestedByAccountId, approved.approval.approvedByAccountId);
  assert.ok(fixtures.some(item => item.body.duplicate === true));
});

test('embeddings schema bounds text batches and rejects unimplemented encoding and controls', () => {
  const operation = documents.gateway.paths['/v1/embeddings'].post;
  assert.ok(operation.security.some(item => item.GatewayKey));
  assert.equal(operation.responses['200'].content['text/event-stream'], undefined);
  const validate = validator(documents.gateway, operation.requestBody.content['application/json'].schema);
  for (const input of ['Hello', ['Hello', 'World'], Array(32).fill('Hello')])
    assert.ok(validate({ model: 'example-embedding', input, encoding_format: 'float' }));
  for (const input of [[], [1, 2], [[1, 2]], Array(33).fill('Hello'), ' ', 'a'.repeat(8193)])
    assert.equal(validate({ model: 'example-embedding', input }), false);
  for (const extra of [{ encoding_format: 'base64' }, { dimensions: 3 }, { stream: false },
    { uzllm: { fallback_models: ['other'] } }])
    assert.equal(validate({ model: 'example-embedding', input: 'Hello', ...extra }), false);
  const fixtures = read('examples.json').filter(item => item.path === '/v1/embeddings');
  assert.equal(fixtures.length, 2);
  assert.deepEqual(fixtures[0].body.data.map(item => item.index), [0, 1]);
  assert.deepEqual(fixtures[0].body.usage, { prompt_tokens: 5, total_tokens: 5 });
  assert.equal(fixtures[1].body.usage, null);
});

test('API-key permission contract preserves null empty deny rules and authenticated replacement', () => {
  const document = documents.management;
  const operation = document.paths['/management/v1/api-keys/{apiKeyId}/permissions'].put;
  assert.ok(operation.security.some(item => item.Session));
  assert.ok(operation.parameters.some(item => item.name === 'X-CSRF-Token' && item.required));
  for (const status of ['204', '400', '401', '403', '404']) assert.ok(operation.responses[status]);
  const validate = validator(document, operation.requestBody.content['application/json'].schema);
  for (const policy of [{}, { allowedModels: null }, { allowedModels: [] },
    { allowedModels: ['example-chat'], deniedModels: ['example-chat'], allowedProviders: ['openai'], deniedProviders: ['anthropic'] }])
    assert.ok(validate(policy), JSON.stringify(validate.errors));
  assert.equal(validate({ allowedModels: [123] }), false);
  assert.equal(validate({ allowModels: ['example-chat'] }), false);
  assert.equal(validate({ allowedProviders: Array.from({ length: 129 }, () => 'openai') }), false);
  const keys = read('examples.json').find(item => item.path.endsWith('/projects/{projectId}/api-keys')).body;
  assert.equal(keys[0].permissions.allowedModels[0], 'example-chat');
  assert.equal(keys[1].permissions, null);
});

test('serializer-derived examples match their actual status and media-type schema', () => {
  const examples = read('examples.json');
  assert.ok(examples.length >= 18);
  for (const example of examples) {
    const document = documents[example.document];
    const response = document.paths[example.path][example.method].responses[example.status];
    const validate = validator(document, response.content[example.mediaType].schema);
    assert.ok(validate(example.body), example.path + ' ' + example.status + ': ' + JSON.stringify(validate.errors));
  }
  const wallet = examples.find(item => item.path.endsWith('/wallet'));
  assert.equal(wallet.body.postedBalanceMicroUsd, '9007199254740993');
  const schema = documents.management.paths[wallet.path].get.responses[200].content['application/json'].schema;
  assert.equal(validator(documents.management, schema)({ ...wallet.body, postedBalanceMicroUsd: 9007199254740992 }), false);
  const topups = examples.filter(item => item.path.endsWith('/billing/topups'));
  assert.deepEqual(topups.map(item => [item.status, item.body.duplicate]), [[201, false], [200, true]]);
  assert.equal('hasCredit' in topups[0].body.intent, false);
  const unknown = examples.find(item => item.document === 'gateway' && item.status === 200 && item.body.usage === null);
  assert.ok(unknown, 'Unknown usage must remain nullable, never zero-filled');
});

test('tenant admission limits have bounded nullable numeric controls and protected wire operations', () => {
  for (const resource of ['projects/{projectId}', 'api-keys/{apiKeyId}']) {
    const operation = documents.management.paths['/management/v1/' + resource + '/limits'].put;
    assert.deepEqual(operation.security, [{ Session: [] }]);
    assert.ok(operation.parameters.some(item => item.name === 'X-CSRF-Token' && item.required));
    for (const status of ['200', '400', '401', '403', '404']) assert.ok(operation.responses[status]);
    const validate = validator(documents.management, operation.requestBody.content['application/json'].schema);
    for (const value of [{}, { requestsPerMinute: null, concurrency: null }, { requestsPerMinute: 1, concurrency: 1 },
      { requestsPerMinute: 1000000, concurrency: 10000 }]) assert.ok(validate(value), JSON.stringify(validate.errors));
    for (const value of [{ requestsPerMinute: 0 }, { concurrency: -1 }, { concurrency: 1.5 }, { leaseSeconds: 1 },
      { requestsPerMinute: 1000001 }, { concurrency: 10001 }, { requestsPerMinute: '10' }])
      assert.equal(validate(value), false, JSON.stringify(value));
  }
});

test('project settings expose only defaults metadata and hard allowlists with protected operations', () => {
  const path = '/management/v1/organizations/{organizationId}/projects/{projectId}/settings';
  const operation = documents.management.paths[path].put;
  assert.deepEqual(operation.security, [{ Session: [] }]);
  assert.ok(operation.parameters.some(item => item.name === 'X-CSRF-Token' && item.required));
  for (const status of ['200', '400', '401', '403', '404']) assert.ok(operation.responses[status]);
  const validate = validator(documents.management, operation.requestBody.content['application/json'].schema);
  for (const value of [{}, { routing: null, environment: null, allowedModels: null, allowedProviders: [] },
    { routing: 'priority', environment: 'Production', allowedProviders: ['openai'] }])
    assert.ok(validate(value), JSON.stringify(validate.errors));
  for (const value of [{ routing: 'random' }, { environment: '' }, { environment: 'a'.repeat(33) },
    { provider_key_id: 'secret' }, { max_cost_usd: 1 }, { allow_managed_fallback: true }, { budget: 1 },
    { allowedModels: Array(129).fill('gpt') }]) assert.equal(validate(value), false, JSON.stringify(value));
});

test('gateway request example and rejection boundaries agree with the bounded chat contract', () => {
  const media = documents.gateway.paths['/v1/chat/completions'].post.requestBody.content['application/json'];
  const validate = validator(documents.gateway, media.schema);
  assert.ok(validate(media.example));
  assert.equal(validate({ ...media.example, invented_parameter: true }), false);
  assert.equal(validate({ ...media.example, messages: [] }), false);
  assert.equal(validate({ ...media.example, temperature: 3 }), false);
  assert.equal(validate({ ...media.example, max_completion_tokens: 0 }), false);
  assert.equal(validate({ ...media.example, uzllm: { routing: 'random' } }), false);
  assert.equal(validate({ ...media.example, max_tokens: 20 }), false);
  assert.equal(validate({ ...media.example, stream_options: { unapproved: true } }), false);
  assert.equal(validate({ ...media.example, max_completion_tokens: 2147483648 }), false);
  assert.equal(validate({ ...media.example, tools: [{ type: 'function', function: { name: 'lookup' } }] }), false);
  assert.equal(validate({ ...media.example, response_format: {} }), false);
  assert.equal(validate({ ...media.example, stop: [''] }), false);
});

test('Responses schema advertises stateless text JSON/SSE with existing security and error contracts', () => {
  const operation = documents.gateway.paths['/v1/responses'].post;
  assert.deepEqual(operation.security, [{ GatewayKey: [] }]);
  assert.deepEqual(Object.keys(operation.responses['200'].content), ['application/json', 'text/event-stream']);
  assert.ok(operation.responses['409'].headers['X-Original-Request-Id']);
  assert.ok(operation.responses['429'].headers['Retry-After']);
  for (const status of ['200', '400', '401', '402', '403', '404', '409', '413', '415', '429', '502', '503']) {
    assert.ok(operation.responses[status]);
    assert.deepEqual(operation.responses[status].headers['Cache-Control'].schema.enum, ['no-store']);
  }
  const media = operation.requestBody.content['application/json']; const validate = validator(documents.gateway, media.schema);
  for (const value of [media.example, { ...media.example, stream: true }, { model: 'example-chat', input: [{ role: 'user', content: [{ type: 'input_text', text: 'Hello' }] }], instructions: 'Guide', stream: false }])
    assert.ok(validate(value), JSON.stringify(validate.errors));
  for (const value of [{ stream: 'true' }, { store: true }, { tools: [] }, { previous_response_id: 'resp_old' },
    { input: [] }, { input: '' }, { max_output_tokens: 15 }, { max_output_tokens: '32' }, { max_output_tokens: 2147483648 },
    { input: [{ role: 'tool', content: 'x' }] }, { input: [{ role: 'user', content: [{ type: 'input_image', image_url: 'https://example.test/image' }] }] }])
    assert.equal(validate({ ...media.example, ...value }), false, JSON.stringify(value));
  const examples = read('examples.json').filter(item => item.path === '/v1/responses' && item.status === 200 && item.mediaType === 'application/json');
  assert.equal(examples.length, 4);
  assert.ok(examples.some(item => item.body.usage === null));
  assert.ok(examples.some(item => item.body.status === 'incomplete' && item.body.incomplete_details.reason === 'max_output_tokens'));
});

test('Responses SSE serializer fixtures match named event schemas and terminal usage semantics', () => {
  const fixtures = read('examples.json').filter(item => item.path === '/v1/responses' && item.mediaType === 'text/event-stream');
  assert.equal(fixtures.length, 5);
  const validate = validator(documents.gateway, documents.gateway.components.schemas.ResponsesStreamEvent);
  const terminals = [];
  for (const fixture of fixtures) {
    assert.equal(fixture.body.includes('[DONE]'), false);
    const events = fixture.body.trim().split('\n\n').map((frame, index) => {
      const [name, data] = frame.split('\n'); const event = JSON.parse(data.slice(6));
      assert.equal(name, 'event: ' + event.type); assert.equal(event.sequence_number, index);
      assert.ok(validate(event), JSON.stringify(validate.errors)); return event;
    });
    assert.equal(events[0].type, 'response.created');
    terminals.push(events.at(-1));
  }
  assert.ok(terminals.some(event => event.type === 'response.completed' && event.response.usage === null));
  assert.ok(terminals.some(event => event.type === 'response.incomplete' && event.response.incomplete_details.reason === 'max_output_tokens'));
  assert.ok(terminals.some(event => event.type === 'response.failed' && event.response.error.code === 'finalization_pending'));
  assert.equal(validate({ type: 'response.output_text.delta', sequence_number: 0 }), false);
  assert.equal(validate({ type: 'response.tool_call.delta', sequence_number: 0 }), false);
});

test('usage rollup wire keeps gross refunds and net amounts distinct and reports lag', () => {
  const example = read('examples.json').find(item => item.path.endsWith('/usage/summary'));
  assert.ok(example);
  assert.equal(example.body.chargedMicroUsd, '3200');
  assert.equal(example.body.refundedMicroUsd, '1000');
  assert.equal(example.body.netChargedMicroUsd, '2200');
  assert.equal(example.body.aggregation.pendingBucketCount, 1);
  assert.equal(example.body.aggregation.source, 'HourlyRollupsWithLiveEdges');
  const schema = documents.management.paths[example.path].get.responses[200].content['application/json'].schema;
  const validate = validator(documents.management, schema);
  assert.ok(validate(example.body));
  assert.equal(validate({ ...example.body, refundedMicroUsd: 1000 }), false);
});

test('session CSRF operator and idempotency requirements stay explicit', () => {
  const paths = documents.management.paths;
  assert.equal(paths['/management/v1/auth/login'].post.security, undefined);
  assert.equal(documents.management.components.securitySchemes.Session.name, '__Host-uzllm-session');
  const topup = paths['/management/v1/organizations/{organizationId}/billing/topups'].post;
  assert.deepEqual(topup.security, [{ Session: [] }]);
  assert.ok(topup.parameters.some(parameter => parameter.name === 'X-CSRF-Token' && parameter.required));
  assert.ok(topup.parameters.some(parameter => parameter.name === 'Idempotency-Key' && parameter.required));
  assert.ok(topup.responses['200'] && topup.responses['201'] && topup.responses['409']);
  assert.match(paths['/management/v1/admin/financial/risk'].get.description, /recent MFA/);
  const chat = documents.gateway.paths['/v1/chat/completions'].post;
  assert.deepEqual(chat.security, [{ GatewayKey: [] }]);
  assert.ok(chat.responses['409'].headers['X-Original-Request-Id']);
  assert.ok(chat.responses['200'].content['text/event-stream']);
  assert.match(chat.responses['200'].description, /event: error/);
});

test('usage CSV is a bounded authenticated binary download, not a JSON page', () => {
  const operation = documents.management.paths['/management/v1/organizations/{organizationId}/usage/export'].get;
  assert.deepEqual(operation.security, [{ Session: [] }]);
  assert.deepEqual(Object.keys(operation.responses['200'].content), ['text/csv']);
  assert.deepEqual(operation.responses['200'].content['text/csv'].schema, { format: 'binary', type: 'string' });
  const parameters = Object.fromEntries(operation.parameters.map(item => [item.name, item]));
  assert.equal(parameters.limit, undefined);
  assert.equal(parameters.cursor, undefined);
  assert.deepEqual(parameters.kind.schema.enum, ['activity', 'daily']);
  assert.equal(parameters.rowLimit.schema.minimum, 1);
  assert.equal(parameters.rowLimit.schema.maximum, 5000);
  assert.equal(parameters.rowLimit.schema.default, 1000);
  for (const status of ['400', '401', '403', '413', '504']) assert.ok(operation.responses[status]);
  assert.ok(operation.responses['504'].content['application/problem+json']);
  assert.ok(operation.responses['200'].headers['X-Usage-Data-As-Of']);
});

test('drift comparison detects schema and client changes without treating CRLF as drift', () => {
  assert.equal(artifactMatches('contract\n', 'contract\r\n'), true);
  assert.equal(artifactMatches('contract\n', 'changed\n'), false);
  assert.equal(artifactMatches('contract\n', 'contract'), false);
  assert.deepEqual(canonicalize({ z: 1, a: { z: 2, a: 3 } }), { a: { a: 3, z: 2 }, z: 1 });
});

test('BYOK provider choices are session-protected metadata, not operator credentials', () => {
  const operation = documents.management.paths['/management/v1/organizations/{organizationId}/provider-keys/providers'].get;
  assert.deepEqual(operation.security, [{ Session: [] }]);
  for (const status of ['200', '401', '403']) assert.ok(operation.responses[status]);
  const option = documents.management.components.schemas.ByokProviderOption;
  assert.deepEqual(Object.keys(option.properties).sort(), ['code', 'id', 'name']);
  assert.ok(operation.responses['200'].headers['Cache-Control']);
});

test('customer alert delivery and owner payload contracts are protected and do not mix secret data', () => {
  const root = '/management/v1/organizations/{organizationId}';
  const events = documents.management.paths[root + '/alerts/events'].get;
  assert.deepEqual(events.security, [{ Session: [] }]);
  for (const status of ['200', '401', '403']) assert.ok(events.responses[status]);
  assert.ok(events.responses['200'].headers['Cache-Control']);
  const view = documents.management.components.schemas.AlertEventView;
  assert.deepEqual(Object.keys(view.properties).sort(), ['deliveredAt', 'id', 'observedValue', 'projectId', 'ruleId', 'status', 'triggeredAt', 'type']);
  const policy = documents.management.paths[root + '/projects/{projectId}/payload-retention'];
  assert.deepEqual(policy.get.security, [{ Session: [] }]);
  assert.ok(policy.put.parameters.some(value => value.name === 'X-CSRF-Token' && value.required));
  const payload = documents.management.paths[root + '/projects/{projectId}/payload-retention/requests/{requestId}'].get;
  for (const status of ['200', '401', '403', '404']) assert.ok(payload.responses[status]);
  assert.ok(payload.responses['200'].headers['Cache-Control']);
});
