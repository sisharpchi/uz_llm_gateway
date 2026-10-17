import { randomUUID } from 'node:crypto';
import OpenAI from 'openai';

export const operations = ['models', 'chat', 'chat-stream', 'responses', 'responses-stream', 'embeddings'];

// Never fall back to OPENAI_API_KEY or the SDK's default OpenAI origin.
export function configuration(env, operation) {
  if (!operations.includes(operation)) throw new Error(`Choose one operation: ${operations.join(', ')}.`);
  let url;
  try { url = new URL(env.UZLLM_GATEWAY_URL); }
  catch { throw new Error('Set UZLLM_GATEWAY_URL to the Gateway origin (without /v1).'); }
  const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback))
      || url.username || url.password || url.search || url.hash || url.pathname !== '/') {
    throw new Error('Gateway URL must be an HTTPS origin, or loopback HTTP, without credentials, path or query.');
  }
  if (!env.UZLLM_API_KEY?.trim() || /\s/.test(env.UZLLM_API_KEY)) throw new Error('Set UZLLM_API_KEY in the server process environment.');
  if (operation !== 'models' && env.UZLLM_ACK_BILLING !== 'yes') {
    throw new Error('Inference can charge your wallet/provider account. Set UZLLM_ACK_BILLING=yes to consent.');
  }
  if (operation !== 'models' && !env.UZLLM_MODEL?.trim()) throw new Error('Set UZLLM_MODEL to an authorized published model code.');
  const idempotencyKey = env.UZLLM_IDEMPOTENCY_KEY ?? randomUUID();
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(idempotencyKey)) {
    throw new Error('UZLLM_IDEMPOTENCY_KEY must be a UUID.');
  }
  return { baseURL: `${url.origin}/v1`, apiKey: env.UZLLM_API_KEY, model: env.UZLLM_MODEL, idempotencyKey };
}

export function createClient(config, transport = globalThis.fetch) {
  return new OpenAI({ baseURL: config.baseURL, apiKey: config.apiKey,
    maxRetries: 0, timeout: 60_000, logLevel: 'off',
    // Do not forward a project key through an HTTP redirect.
    fetch: (url, options) => transport(url, { ...options, redirect: 'error' }) });
}

// Return only safe metadata: never print SDK errors, request headers/bodies or URLs.
export function safeError(error, idempotencyKey) {
  return { status: Number.isInteger(error?.status) ? error.status : null,
    requestId: safeId(error?.headers?.get?.('x-request-id')),
    originalRequestId: safeId(error?.headers?.get?.('x-original-request-id')),
    idempotencyKey, outcome: 'Not automatically retried; failure/cancellation does not imply zero cost. Check Activity before retrying.' };
}

function safeId(value) {
  // Gateway inference IDs are Guid.ToString("N"), not arbitrary upstream text.
  return typeof value === 'string' && /^[0-9a-f]{32}$/i.test(value) ? value : null;
}
