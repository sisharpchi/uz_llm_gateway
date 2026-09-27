import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:https';

// Test-only TLS Management stand-in. Production protocol handling remains in
// PaymentEndpointExtensions/PaymeMerchantApi/ClickShopApi and their DB tests.
const seen = new Map();
const expectedPayme = `Basic ${Buffer.from('Paycom:payme-test-key').toString('base64')}`;

createServer({
  key: readFileSync('/run/certs/public.key'),
  cert: readFileSync('/run/certs/public.crt'),
}, async (request, response) => {
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  const body = Buffer.concat(chunks).toString('utf8');
  const path = request.url ?? '';
  const hash = createHash('sha256').update(`${path}\n${body}`).digest('hex');
  const count = (seen.get(hash) ?? 0) + 1;
  seen.set(hash, count);
  const common = {
    path, method: request.method, body, bodyBytes: Buffer.byteLength(body),
    forwardedFor: request.headers['x-forwarded-for'],
    realIp: request.headers['x-real-ip'], forwarded: request.headers.forwarded,
    seen: count,
  };

  if (path.includes('simulate-upstream-reset-once') && count === 1) {
    request.socket.destroy();
    return;
  }

  let result;
  if (path.startsWith('/payments/payme/callback')) {
    result = { ...common, error: request.headers.authorization === expectedPayme
      ? null : { code: -32504 } };
  } else if (path.startsWith('/payments/click/callback')) {
    const fields = new URLSearchParams(body);
    const action = fields.get('action');
    const material = (fields.get('click_trans_id') ?? '')
      + (fields.get('service_id') ?? '') + 'click-test-secret'
      + (fields.get('merchant_trans_id') ?? '')
      + (action === '1' ? fields.get('merchant_prepare_id') ?? '' : '')
      + (fields.get('amount') ?? '') + (action ?? '')
      + (fields.get('sign_time') ?? '');
    const expected = createHash('md5').update(material).digest('hex');
    result = { ...common, error: fields.get('sign_string') === expected ? 0 : -1 };
  } else {
    response.writeHead(404).end();
    return;
  }

  response.writeHead(path.includes('simulate-upstream-503') ? 503 : 200,
    { 'content-type': 'application/json' });
  response.end(JSON.stringify(result));
}).listen(8443, '0.0.0.0');
