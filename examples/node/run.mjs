import { configuration, createClient, safeError } from './client.mjs';
import { runExample } from './operations.mjs';

let config;
try {
  const operation = process.argv[2];
  config = configuration(process.env, operation);
  // Print the claim before network I/O so an interrupted invocation can be investigated.
  process.stderr.write(`Operation ${operation}; Idempotency-Key ${config.idempotencyKey}; automatic retries disabled.\n`);
  const result = await runExample(createClient(config), config, operation);
  process.stdout.write(`${JSON.stringify({ operation, idempotencyKey: config.idempotencyKey, ...result })}\n`);
} catch (error) {
  process.stderr.write(`${JSON.stringify(config ? safeError(error, config.idempotencyKey)
    // configuration() emits fixed messages only; it never interpolates supplied values.
    : { configurationError: `${error.message} Read examples/node/README.md; no request was sent.` })}\n`);
  process.exitCode = 1;
}
