import {
  buildCandidateUsageSourceIds,
  normalizeAuthIndex,
  type KeyStatBucket,
  type KeyStats,
  type UsageDetail,
} from '@/utils/usage';
import {
  collectUsageDetailsForAuthIndices,
  collectUsageDetailsForCandidates,
  type UsageDetailsByAuthIndex,
  type UsageDetailsBySource,
} from '@/utils/usageIndex';

export const DISABLE_ALL_MODELS_RULE = '*';

export const hasDisableAllModelsRule = (models?: string[]) =>
  Array.isArray(models) &&
  models.some((model) => String(model ?? '').trim() === DISABLE_ALL_MODELS_RULE);

export const stripDisableAllModelsRule = (models?: string[]) =>
  Array.isArray(models)
    ? models.filter((model) => String(model ?? '').trim() !== DISABLE_ALL_MODELS_RULE)
    : [];

export const withDisableAllModelsRule = (models?: string[]) => {
  const base = stripDisableAllModelsRule(models);
  return [...base, DISABLE_ALL_MODELS_RULE];
};

export const withoutDisableAllModelsRule = (models?: string[]) => {
  const base = stripDisableAllModelsRule(models);
  return base;
};

export const parseTextList = (text: string): string[] =>
  text
    .split(/[\n,]+/)
    .map((item) => item.trim())
    .filter(Boolean);

export const parseExcludedModels = parseTextList;

export const excludedModelsToText = (models?: string[]) =>
  Array.isArray(models) ? models.join('\n') : '';

// 根据 source (apiKey) 获取统计数据
export const getStatsBySource = (
  apiKey: string,
  keyStats: KeyStats,
  prefix?: string
): KeyStatBucket => {
  const bySource = keyStats.bySource ?? {};
  const candidates = buildCandidateUsageSourceIds({ apiKey, prefix });
  if (!candidates.length) {
    return { success: 0, failure: 0 };
  }

  let success = 0;
  let failure = 0;
  candidates.forEach((candidate) => {
    const stats = bySource[candidate];
    if (!stats) return;
    success += stats.success;
    failure += stats.failure;
  });

  return { success, failure };
};

type UsageIdentity = {
  authIndex?: unknown;
  apiKey?: string;
  prefix?: string;
};

export const getStatsForIdentity = (
  identity: UsageIdentity,
  keyStats: KeyStats
): KeyStatBucket => {
  const authIndexKey = normalizeAuthIndex(identity.authIndex);
  if (authIndexKey) {
    const stats = keyStats.byAuthIndex?.[authIndexKey];
    if (stats) {
      return { success: stats.success, failure: stats.failure };
    }
  }

  return getStatsBySource(identity.apiKey ?? '', keyStats, identity.prefix);
};

export const collectUsageDetailsForIdentity = (
  identity: UsageIdentity,
  usageDetailsBySource: UsageDetailsBySource,
  usageDetailsByAuthIndex: UsageDetailsByAuthIndex
): UsageDetail[] => {
  const authIndexKey = normalizeAuthIndex(identity.authIndex);
  if (authIndexKey) {
    const details = collectUsageDetailsForAuthIndices(usageDetailsByAuthIndex, [authIndexKey]);
    if (details.length > 0) {
      return details;
    }
  }

  const candidates = buildCandidateUsageSourceIds({
    apiKey: identity.apiKey,
    prefix: identity.prefix,
  });
  if (!candidates.length) {
    return [];
  }

  return collectUsageDetailsForCandidates(usageDetailsBySource, candidates);
};

export const getProviderConfigKey = (
  config: {
    authIndex?: unknown;
    apiKey?: string;
    baseUrl?: string;
    proxyUrl?: string;
  },
  index: number
): string => {
  const authIndexKey = normalizeAuthIndex(config.authIndex);
  if (authIndexKey) {
    return authIndexKey;
  }
  return `${config.apiKey ?? ''}::${config.baseUrl ?? ''}::${config.proxyUrl ?? ''}::${index}`;
};
