# Node SDK examples

Server/terminal only; Node 22+, official `openai` SDK pinned in the lockfile.
From the repository root:

```sh
npm ci --ignore-scripts --prefix examples/node
```

Supply these **process environment** values through your secret manager or an
interactive prompt, not a committed file, shell-history command or URL:

| Variable | Value |
|---|---|
| `UZLLM_GATEWAY_URL` | Your HTTPS Gateway **origin**, without `/v1`; local HTTP is allowed only on loopback (e.g. `http://localhost:5214`). No default provider destination. |
| `UZLLM_API_KEY` | The project key saved from its show-once creation dialog. Never a provider/Management key. |
| `UZLLM_MODEL` | Authorized canonical model code from `/v1/models`; select an embeddings-only model for embeddings. |
| `UZLLM_ACK_BILLING` | `yes` explicitly consents to one potentially billable inference operation. Not required for models. |
| `UZLLM_IDEMPOTENCY_KEY` | Optional UUID for this logical operation. Otherwise generated and printed **before** network I/O. |

Run **one** example at a time (do not run all against a paid account by accident):

```sh
node examples/node/run.mjs models
node examples/node/run.mjs chat
node examples/node/run.mjs chat-stream
node examples/node/run.mjs responses
node examples/node/run.mjs responses-stream
node examples/node/run.mjs embeddings
```

`client.mjs` configures `baseURL`, a project key, `maxRetries: 0`, a 60-second
timeout, disabled SDK logging (even if `OPENAI_LOG` is set) and redirect rejection.
`operations.mjs` shows the actual SDK calls:
bounded 32-token chat/Responses requests, explicit `store:false`, named Responses
terminal events, chat usage chunks and explicit `encoding_format:'float'` for
two text embeddings. No tool loop, pagination or hidden extra model call.
Output goes to your terminal as JSON: text/refusal/status, nullable usage and
request ID; embeddings print indexes/dimensions, not vectors. Treat generated
text as potentially sensitive and do not pipe it into ordinary telemetry.

## Billing, retries and secrets

- Managed is the existing default. These examples do **not** select BYOK,
  Hybrid, managed fallback or project-policy overrides. Published models still
  require authorization, effective pricing, funds and provider availability.
- Inference reserves a worst-case hold and settles measured usage using frozen
  prices/fees. Missing usage is **null**, never free/zero. A timeout, disconnect,
  incomplete/refused output or HTTP error can still be billable; Activity and
  the wallet are authoritative. A successful response with null usage can retain
  a pending hold. Token counts are not a client-side cost quotation.
- SDK automatic retries are disabled, including 409/429/5xx. Repeating the same
  operation with the same UUID within 24 hours returns **409**, with the original
  request ID, **not a replayed response**. Changed payloads conflict too. The
  claim is organization/key/operation scoped; another operation or fresh UUID
  can execute and charge again. Reusing a UUID after expiry is not retry safety.
- Stop and inspect Activity on an ambiguous result; do not use a new UUID to
  “fix” a timeout. Cancellation stops local I/O but cannot undo upstream spend.
  Stream EOF alone is not successful completion. `response.incomplete` is
  reported distinctly; `response.failed` or a missing terminal fails the CLI.
- Keys stay in server process memory/environment. No browser SDK, localStorage,
  clipboard input, `dangerouslyAllowBrowser`, `.env` loader or plaintext files.
  Do not log SDK error objects: the CLI prints only bounded metadata, never the
  upstream message/body. No TLS validation bypass; install your trusted local
  development CA using `NODE_EXTRA_CA_CERTS` when necessary.

## Compatibility and validation

The upstream SDK exposes much more than UZLLM implements. Use only the supported
[chat contract](../../docs/11_API_CONTRACTS.md),
[Responses text subset](../../docs/32_RESPONSES_API.md),
[Responses SSE](../../docs/33_RESPONSES_SSE.md) and
[text/float embeddings](../../docs/34_EMBEDDINGS_API.md). No stored conversations,
retrieve/resume, tools in Responses, modalities, token-ID embeddings, base64 or
dimension controls. Do not use SDK higher-level helpers that add these fields.
The dashboard's Management catalog is chat-only; Gateway discovery includes
eligible embeddings. SDK static types do not make unsupported features available.

Offline checks: `npm test --prefix examples/node`. Tests exercise the **installed
SDK** with captured transport and real serializer fixtures/OpenAPI; no account,
provider or merchant network is used. For real composition, install frontend
dependencies/browser per [the acceptance guide](../../deploy/LAUNCH-002-VALIDATION.md),
then `node deploy/tests/run-launch-acceptance.mjs`. The browser vertical runs all
six shipped CLI commands against an isolated TLS/Gateway/PG/Redis/Worker stack,
synthetic native provider and funded wallet, checking exact charges, Activity,
hold release and replay rejection. It never uses your `.env` or paid provider.

SDK transport basis: [official OpenAI JavaScript/TypeScript documentation](https://developers.openai.com/api/reference/typescript),
reviewed 2026-10-17. Gateway limitations above take precedence over broader SDK
capabilities. [DX-003 scope and evidence](../../docs/36_SDK_EXAMPLES.md).
