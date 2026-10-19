import { management } from '../../packages/api-client';
import type { paths as ManagementPaths } from '../../packages/api-client/generated/management-v1';
import type { paths as GatewayPaths, components as GatewayComponents } from '../../packages/api-client/generated/gateway-v1';

type StatementMetadata = ManagementPaths['/management/v1/organizations/{organizationId}/statements/{id}']['get']['responses'][200]['content']['application/json'];
const exactStatementMoney: StatementMetadata['totals']['openingMicroUsd'] = '9007199254740993';
// @ts-expect-error Statement money must never become a lossy JavaScript number.
const lossyStatementMoney: StatementMetadata['totals']['openingMicroUsd'] = 9007199254740993;
type StatementHeaders = ManagementPaths['/management/v1/admin/organizations/{organizationId}/reports/statements']['post']['parameters']['header'];
// @ts-expect-error Operator statements require the existing session CSRF proof.
const unprotectedStatement: StatementHeaders = {};
type StatementJob = ManagementPaths['/management/v1/admin/organizations/{organizationId}/reports/statements']['post']['responses'][202]['content']['application/json'];
const queuedStatement: StatementJob['operationStatus'] = 'Pending';
void [exactStatementMoney, lossyStatementMoney, unprotectedStatement, queuedStatement];

// Compile-only fixtures: unused @ts-expect-error directives fail tsc if a guard disappears.
type CreditCommand = NonNullable<ManagementPaths['/management/v1/admin/wallet-adjustments']['post']['requestBody']>['content']['application/json'];
const exactCredit: CreditCommand = { organizationId: 'uuid', adjustmentKey: 'example-key', amountMicroUsd: '9007199254740993', reason: 'Documented service credit' };
// @ts-expect-error Credits must use exact money strings, not JavaScript numbers.
const lossyCredit: CreditCommand = { ...exactCredit, amountMicroUsd: 9007199254740993 };
type CreditApproval = NonNullable<ManagementPaths['/management/v1/admin/wallet-adjustments/{id}/approve']['post']['requestBody']>['content']['application/json'];
const independentCreditApproval: CreditApproval = { reason: 'Independent approval' };
// @ts-expect-error Approval cannot override immutable amount or organization.
const changedCreditApproval: CreditApproval = { reason: 'Independent approval', amountMicroUsd: '1' };
type TopUpHeaders = ManagementPaths['/management/v1/organizations/{organizationId}/billing/topups']['post']['parameters']['header'];
const validTopUpHeaders: TopUpHeaders = { 'X-CSRF-Token': 'csrf', 'Idempotency-Key': 'replay-key' };
// @ts-expect-error Financial replay proof is required by the published operation.
const missingReplayProof: TopUpHeaders = { 'X-CSRF-Token': 'csrf' };
// @ts-expect-error A billing amount must not become a lossy JavaScript number.
const invalidQuoteAmount: Parameters<typeof management.quotes>[2] = 9007199254740992;
type ResponsesRequest = GatewayPaths['/v1/responses']['post']['requestBody']['content']['application/json'];
const statelessResponse: ResponsesRequest = { model: 'example-chat', input: 'Hello', store: false, stream: false };
const streamingResponse: ResponsesRequest = { model: 'example-chat', input: 'Hello', stream: true };
type ResponsesEvent = GatewayComponents['schemas']['ResponsesStreamEvent'];
const textDelta: ResponsesEvent = { type: 'response.output_text.delta', sequence_number: 0,
  item_id: 'msg_example', output_index: 0, content_index: 0, delta: 'Hello', logprobs: [] };
// @ts-expect-error Tool streaming is outside the supported Responses subset.
const unsupportedResponseEvent: ResponsesEvent = { type: 'response.function_call_arguments.delta', sequence_number: 1 };
void [textDelta, unsupportedResponseEvent];
// @ts-expect-error Conversation storage is not implemented.
const storedResponse: ResponsesRequest = { model: 'example-chat', input: 'Hello', store: true };
type EmbeddingsRequest = GatewayPaths['/v1/embeddings']['post']['requestBody']['content']['application/json'];
const embeddingBatch: EmbeddingsRequest = { model: 'example-embedding', input: ['Hello', 'World'], encoding_format: 'float' };
// @ts-expect-error Token-ID inputs are outside the bounded text contract.
const tokenEmbedding: EmbeddingsRequest = { model: 'example-embedding', input: [1, 2] };
// @ts-expect-error Base64 encoding is not implemented.
const base64Embedding: EmbeddingsRequest = { model: 'example-embedding', input: 'Hello', encoding_format: 'base64' };
void [embeddingBatch, tokenEmbedding, base64Embedding];
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

type Limits = Parameters<typeof management.setProjectLimits>[1];
const inheritedLimits: Limits = { requestsPerMinute: null, concurrency: null };
const finiteLimits: Limits = { requestsPerMinute: 10, concurrency: 2 };
// @ts-expect-error Limit knobs are JSON numbers, not numeric strings.
const stringLimit: Limits = { requestsPerMinute: '10' };
// @ts-expect-error Tenant configuration cannot change lease/recovery safety windows.
const unsafeLease: Limits = { leaseSeconds: 1 };

type Settings = Parameters<typeof management.setProjectSettings>[2];
type AlertEvent = Awaited<ReturnType<typeof management.alertEvents>>[number];
const safeDeliveryStatus: AlertEvent['status'] = 'Pending';
// @ts-expect-error Customer delivery evidence never includes a signing secret.
const deliverySecret: AlertEvent['signingSecret'] = 'forbidden';
// @ts-expect-error Retention minutes are JSON numbers, not strings.
const stringRetention: Parameters<typeof management.setPayloadPolicy>[3] = '60';
type ByokOptions = Awaited<ReturnType<typeof management.byokProviders>>;
const safeProviderIdentity: ByokOptions[number] = { id: 'uuid', code: 'google', name: 'Gemini' };
// @ts-expect-error BYOK provider discovery never exposes an operator secret.
const unsafeProviderIdentity: ByokOptions[number] = { id: 'uuid', code: 'google', name: 'Gemini', secret: 'no' };
// @ts-expect-error Exact monetary strings are required, not lossy numbers.
const unsafeByokCap: Parameters<typeof management.restrictByokKey>[3] = 9007199254740993;
const projectSettings: Settings = { routing: 'priority', environment: 'Production', allowedModels: null, allowedProviders: [] };
// @ts-expect-error Project settings cannot store financial or execution-mode knobs.
const unsafeSettings: Settings = { max_cost_usd: 1 };
// @ts-expect-error Strategies must match the supported wire enum.
const invalidStrategy: Settings = { routing: 'random' };

type KeyStatus = Parameters<typeof management.setKeyStatus>[1];
const enabledKey: KeyStatus = 0;
// @ts-expect-error Key deletion is not a status transition.
const deletedKey: KeyStatus = 2;
type BudgetWindow = Parameters<typeof management.setBudget>[1];
const monthlyBudget: BudgetWindow = 'Monthly';
// @ts-expect-error Only authoritative financial windows are supported.
const yearlyBudget: BudgetWindow = 'Yearly';
type InvitationRole = Parameters<typeof management.inviteMember>[2];
const developerInvite: InvitationRole = 'Developer';
// @ts-expect-error Invitations cannot grant Owner.
const ownerInvite: InvitationRole = 'Owner';
// @ts-expect-error Display labels are not API role codes.
const financeRole: Parameters<typeof management.changeMemberRole>[2] = 'Billing Viewer';

void [statelessResponse, streamingResponse, storedResponse, validTopUpHeaders, missingReplayProof, invalidQuoteAmount, unsupportedSessionMethod,
  exactWalletMoney, lossyWalletMoney, unknownUsage, csvDownload, invalidCsvDownload, invalidCsvKind,
  unrestrictedModels, denyAllModels, invalidPermission, inheritedLimits, finiteLimits, stringLimit, unsafeLease,
  projectSettings, unsafeSettings, invalidStrategy, enabledKey, deletedKey, monthlyBudget, yearlyBudget,
  developerInvite, ownerInvite, financeRole, safeProviderIdentity, unsafeProviderIdentity, unsafeByokCap];
