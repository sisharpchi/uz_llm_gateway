import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { artifactMatches, canonicalize } from './api-contracts.mjs';

const read = name => JSON.parse(readFileSync(new URL('../../../docs/api/' + name, import.meta.url), 'utf8'));
const documents = { gateway: read('gateway-v1.openapi.json'), management: read('management-v1.openapi.json') };
const methods = ['get', 'post', 'put', 'patch', 'delete'];
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
          if (status === '202' || status === '204') assert.equal(response.content, undefined);
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
  assert.deepEqual(Object.keys(documents.gateway.paths).sort(), ['/v1/chat/completions', '/v1/models']);
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
