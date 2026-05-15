import { maskApiKey } from '../format';
import { getApisRecord, isRecord, normalizeAuthIndex } from './shared';
import { buildCandidateUsageSourceIds, normalizeUsageSourceId } from './sourceId';
import { calculateStatusBarDataFromRecentRequests } from './status';
import type { KeyStatBucket, KeyStats, UsageDetail } from './types';

type ApiKeyUsageEntry = {
  success?: unknown;
  failed?: unknown;
  recent_requests?: unknown;
  recentRequests?: unknown;
};

const readCount = (value: unknown): number => {
  if (typeof value === 'number' && Number.isFinite(value)) return Math.max(0, value);
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) return Math.max(0, parsed);
  }
  return 0;
};

const readCompositeApiKey = (value: string): string => {
  const separatorIndex = value.indexOf('|');
  return separatorIndex >= 0 ? value.slice(separatorIndex + 1).trim() : value.trim();
};

export function computeKeyStatsFromApiKeyUsage(payload: unknown): KeyStats {
  if (!isRecord(payload)) {
    return { bySource: {}, byAuthIndex: {}, statusBySource: {} };
  }

  const bySource: Record<string, KeyStatBucket> = {};
  const statusBySource: NonNullable<KeyStats['statusBySource']> = {};

  Object.values(payload).forEach((providerBucket) => {
    if (!isRecord(providerBucket)) return;

    Object.entries(providerBucket).forEach(([compositeKey, rawEntry]) => {
      if (!isRecord(rawEntry)) return;
      const entry = rawEntry as ApiKeyUsageEntry;
      const apiKey = readCompositeApiKey(compositeKey);
      if (!apiKey) return;

      const candidates = buildCandidateUsageSourceIds({ apiKey });
      if (!candidates.length) return;

      const stats = {
        success: readCount(entry.success),
        failure: readCount(entry.failed),
      };
      const recentRequests = entry.recent_requests ?? entry.recentRequests;
      const statusData = Array.isArray(recentRequests)
        ? calculateStatusBarDataFromRecentRequests(recentRequests)
        : undefined;

      candidates.forEach((candidate) => {
        bySource[candidate] = stats;
        if (statusData && statusData.totalSuccess + statusData.totalFailure > 0) {
          statusBySource[candidate] = statusData;
        }
      });
    });
  });

  return { bySource, byAuthIndex: {}, statusBySource };
}

export function mergeKeyStats(preferred: KeyStats, fallback: KeyStats): KeyStats {
  return {
    bySource: {
      ...(fallback.bySource ?? {}),
      ...(preferred.bySource ?? {}),
    },
    byAuthIndex: {
      ...(fallback.byAuthIndex ?? {}),
      ...(preferred.byAuthIndex ?? {}),
    },
    statusBySource: {
      ...(fallback.statusBySource ?? {}),
      ...(preferred.statusBySource ?? {}),
    },
  };
}

export function computeKeyStats(
  usageData: unknown,
  masker: (val: string) => string = maskApiKey
): KeyStats {
  const apis = getApisRecord(usageData);
  if (!apis) {
    return { bySource: {}, byAuthIndex: {} };
  }

  const sourceStats: Record<string, KeyStatBucket> = {};
  const authIndexStats: Record<string, KeyStatBucket> = {};

  const ensureBucket = (bucket: Record<string, KeyStatBucket>, key: string) => {
    if (!bucket[key]) {
      bucket[key] = { success: 0, failure: 0 };
    }
    return bucket[key];
  };

  Object.values(apis).forEach((apiEntry) => {
    if (!isRecord(apiEntry)) return;
    const modelsRaw = apiEntry.models;
    const models = isRecord(modelsRaw) ? modelsRaw : null;
    if (!models) return;

    Object.values(models).forEach((modelEntry) => {
      if (!isRecord(modelEntry)) return;
      const details = Array.isArray(modelEntry.details) ? modelEntry.details : [];

      details.forEach((detail) => {
        const detailRecord = isRecord(detail) ? detail : null;
        const source = normalizeUsageSourceId(detailRecord?.source, masker);
        const authIndexKey = normalizeAuthIndex(detailRecord?.auth_index);
        const isFailed = detailRecord?.failed === true;

        if (source) {
          const bucket = ensureBucket(sourceStats, source);
          if (isFailed) {
            bucket.failure += 1;
          } else {
            bucket.success += 1;
          }
        }

        if (authIndexKey) {
          const bucket = ensureBucket(authIndexStats, authIndexKey);
          if (isFailed) {
            bucket.failure += 1;
          } else {
            bucket.success += 1;
          }
        }
      });
    });
  });

  return {
    bySource: sourceStats,
    byAuthIndex: authIndexStats,
  };
}

export function computeKeyStatsFromDetails(usageDetails: UsageDetail[]): KeyStats {
  const bySource: Record<string, KeyStatBucket> = {};
  const byAuthIndex: Record<string, KeyStatBucket> = {};

  const ensureBucket = (bucket: Record<string, KeyStatBucket>, key: string) => {
    if (!bucket[key]) {
      bucket[key] = { success: 0, failure: 0 };
    }
    return bucket[key];
  };

  usageDetails.forEach((detail) => {
    const source = detail.source;
    const authIndexKey = normalizeAuthIndex(detail.auth_index);
    const isFailed = detail.failed === true;

    if (source) {
      const bucket = ensureBucket(bySource, source);
      if (isFailed) {
        bucket.failure += 1;
      } else {
        bucket.success += 1;
      }
    }

    if (authIndexKey) {
      const bucket = ensureBucket(byAuthIndex, authIndexKey);
      if (isFailed) {
        bucket.failure += 1;
      } else {
        bucket.success += 1;
      }
    }
  });

  return { bySource, byAuthIndex };
}
