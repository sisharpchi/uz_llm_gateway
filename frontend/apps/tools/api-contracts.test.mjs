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

test('drift comparison detects schema and client changes without treating CRLF as drift', () => {
  assert.equal(artifactMatches('contract\n', 'contract\r\n'), true);
  assert.equal(artifactMatches('contract\n', 'changed\n'), false);
  assert.equal(artifactMatches('contract\n', 'contract'), false);
  assert.deepEqual(canonicalize({ z: 1, a: { z: 2, a: 3 } }), { a: { a: 3, z: 2 }, z: 1 });
});
