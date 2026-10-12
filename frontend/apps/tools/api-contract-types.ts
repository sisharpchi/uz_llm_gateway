import { management } from '../../packages/api-client';
import type { paths as ManagementPaths } from '../../packages/api-client/generated/management-v1';
import type { paths as GatewayPaths } from '../../packages/api-client/generated/gateway-v1';

// Compile-only fixtures: unused @ts-expect-error directives fail tsc if a guard disappears.
type TopUpHeaders = ManagementPaths['/management/v1/organizations/{organizationId}/billing/topups']['post']['parameters']['header'];
const validTopUpHeaders: TopUpHeaders = { 'X-CSRF-Token': 'csrf', 'Idempotency-Key': 'replay-key' };
// @ts-expect-error Financial replay proof is required by the published operation.
const missingReplayProof: TopUpHeaders = { 'X-CSRF-Token': 'csrf' };
// @ts-expect-error A billing amount must not become a lossy JavaScript number.
const invalidQuoteAmount: Parameters<typeof management.quotes>[2] = 9007199254740992;
// @ts-expect-error Future endpoints are not advertised as implemented.
type UnimplementedResponses = GatewayPaths['/v1/responses'];
// @ts-expect-error The contract does not expose a DELETE session operation.
const unsupportedSessionMethod: NonNullable<ManagementPaths['/management/v1/auth/session']['delete']> = {};
type WalletResult = Awaited<ReturnType<typeof management.wallet>>;
const exactWalletMoney: WalletResult['postedBalanceMicroUsd'] = '9007199254740993';
// @ts-expect-error Generated response money must remain a decimal string.
const lossyWalletMoney: WalletResult['postedBalanceMicroUsd'] = 9007199254740992;
type Completion = GatewayPaths['/v1/chat/completions']['post']['responses'][200]['content']['application/json'];
const unknownUsage: Completion['usage'] = null;
type CsvResult = Awaited<ReturnType<typeof management.exportUsage>>;
const csvDownload: CsvResult = new Blob(['csv'], { type: 'text/csv' });
// @ts-expect-error Binary downloads must not masquerade as JSON strings.
const invalidCsvDownload: CsvResult = 'csv';
// @ts-expect-error Only the documented CSV kinds are supported.
const invalidCsvKind: Parameters<typeof management.exportUsage>[1] = 'summary';
type KeyPermissions = Parameters<typeof management.setKeyPermissions>[1];
const unrestrictedModels: KeyPermissions = { allowedModels: null, allowedProviders: ['openai'] };
const denyAllModels: KeyPermissions = { allowedModels: [] };
// @ts-expect-error Permission rules are canonical codes, not catalog numeric IDs.
const invalidPermission: KeyPermissions = { allowedModels: [123] };

void [validTopUpHeaders, missingReplayProof, invalidQuoteAmount, unsupportedSessionMethod,
  exactWalletMoney, lossyWalletMoney, unknownUsage, csvDownload, invalidCsvDownload, invalidCsvKind,
  unrestrictedModels, denyAllModels, invalidPermission];
