export type ViewType =
  | 'landing'
  | 'overview'
  | 'apikeys'
  | 'models'
  | 'billing'
  | 'activity'
  | 'routing'
  | 'alerts'
  | 'team'
  | 'playground';

export type Currency = 'USD' | 'UZS';

export interface Organization {
  id: string;
  name: string;
  slug: string;
  tier: 'Developer' | 'Scale' | 'Enterprise';
  balanceUsd: number;
  balanceUzs: number;
}

export interface Project {
  id: string;
  orgId: string;
  name: string;
  slug: string;
  environment: 'production' | 'staging' | 'development';
  spendThisMonthUsd: number;
  requestCountThisMonth: number;
  activeKeysCount: number;
}

export interface ApiKeyItem {
  id: string;
  name: string;
  maskedKey: string;
  projectId: string;
  projectName: string;
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
  status: 'active' | 'revoked' | 'expired';
  spendLimitMonthlyUsd: number | null;
  currentSpendMonthlyUsd: number;
}

export interface ModelItem {
  id: string;
  name: string;
  provider: 'OpenAI' | 'Anthropic' | 'Google' | 'DeepSeek' | 'Local';
  contextWindow: number;
  inputPricePer1M: number;
  outputPricePer1M: number;
  latencyAvgMs: number;
  status: 'operational' | 'degraded' | 'maintenance';
  description: string;
  enabled: boolean;
  supportsStreaming: boolean;
  supportsVision: boolean;
}

export interface UsageLogItem {
  id: string;
  timestamp: string;
  model: string;
  provider: string;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  costUsd: number;
  latencyMs: number;
  statusCode: number;
  cached: boolean;
  failoverOccurred: boolean;
  failoverDetails?: string;
  projectId: string;
  keyName: string;
}

export interface TransactionItem {
  id: string;
  timestamp: string;
  paymentMethod: 'Payme' | 'CLICK' | 'Bank Transfer' | 'Card';
  amountUzs: number;
  creditedUsd: number;
  exchangeRate: number; // e.g. 13000 UZS / 1 USD
  status: 'Completed' | 'Pending' | 'Failed' | 'Refunded';
  externalTransactionId: string;
  receiptUrl?: string;
}

export interface TeamMember {
  id: string;
  name: string;
  email: string;
  role: 'Owner' | 'Admin' | 'Developer' | 'Billing';
  status: 'active' | 'invited';
  joinedAt: string;
}

export interface AlertConfig {
  telegramEnabled: boolean;
  telegramChatId: string;
  telegramBotName: string;
  lowBalanceThresholdUsd: number;
  errorRateThresholdPercent: number;
  notifyOnFailover: boolean;
  webhookUrl: string;
}
