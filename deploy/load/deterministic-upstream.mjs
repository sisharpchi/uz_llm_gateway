import { createServer } from 'node:https';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';

const cert = process.env.UZLLM_FIXTURE_CERT;
const key = process.env.UZLLM_FIXTURE_KEY;
if (!cert || !key) throw new Error('Set UZLLM_FIXTURE_CERT and UZLLM_FIXTURE_KEY.');
const port = Number(process.env.UZLLM_FIXTURE_PORT ?? '9443');
const durationMs = Number(process.env.UZLLM_FIXTURE_DURATION_MS ?? '30000');
if (!Number.isInteger(port) || port < 1 || port > 65535 ||
    !Number.isInteger(durationMs) || durationMs < 1000 || durationMs > 120000) {
  throw new Error('Invalid fixture port or duration.');
}

createServer({ cert: readFileSync(cert), key: readFileSync(key) }, async (request, response) => {
  if (request.method !== 'POST' || request.url !== '/v1/chat/completions') {
    response.writeHead(404).end();
    return;
  }
  const chunks = [];
  let bytes = 0;
  for await (const chunk of request) {
    bytes += chunk.length;
    if (bytes > 65536) { response.writeHead(413).end(); return; }
    chunks.push(chunk);
  }
  let body;
  try { body = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { response.writeHead(400).end(); return; }
  if (!body.stream || !body.model || !Array.isArray(body.messages)) {
    response.writeHead(400).end();
    return;
  }
  const id = randomUUID();
  response.writeHead(200, {
    'content-type': 'text/event-stream',
    'cache-control': 'no-cache',
    'x-request-id': id,
  });
  response.write(`data: ${JSON.stringify({ id, model: body.model, choices: [
    { index: 0, delta: { content: 'Fixture' }, finish_reason: null },
  ] })}\n\n`);
  const timer = setTimeout(() => {
    if (response.destroyed) return;
    response.write(`data: ${JSON.stringify({ id, model: body.model, choices: [
      { index: 0, delta: { content: ' complete.' }, finish_reason: 'stop' },
    ] })}\n\n`);
    response.write(`data: ${JSON.stringify({ id, choices: [], usage: {
      prompt_tokens: 8, completion_tokens: 3,
    } })}\n\n`);
    response.end('data: [DONE]\n\n');
  }, durationMs);
  response.on('close', () => clearTimeout(timer));
}).listen(port, '0.0.0.0', () => {
  process.stdout.write(`Deterministic SSE fixture listening on ${port}\n`);
});
