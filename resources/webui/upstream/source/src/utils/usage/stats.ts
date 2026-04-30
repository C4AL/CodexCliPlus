import type { LatencyAccumulator, LatencyStats } from './latency';
import {
  addLatencySample,
  calculateLatencyStatsFromDetails,
  createLatencyAccumulator,
  extractLatencyMs,
  finalizeLatencyStats,
} from './latency';
import { parseTimestampMs } from '../timestamp';
import { calculateCost } from './cost';
import { collectUsageDetails, extractTotalTokens } from './details';
import { getApisRecord, isRecord } from './shared';
import { maskUsageSensitiveValue } from './sourceId';
import type {
  ApiStats,
  ModelPrice,
  ModelStatsSummary,
  RateStats,
  TokenBreakdown,
  UsageDetail,
} from './types';

/**
 * 计算耗时统计
 */
export function calculateLatencyStats(usageData: unknown): LatencyStats {
  return calculateLatencyStatsFromDetails(collectUsageDetails(usageData));
}

/**
 * 计算 token 分类统计
 */
export function calculateTokenBreakdown(usageData: unknown): TokenBreakdown {
  const details = collectUsageDetails(usageData);
  if (!details.length) {
    return { cachedTokens: 0, reasoningTokens: 0 };
  }

  let cachedTokens = 0;
  let reasoningTokens = 0;

  details.forEach((detail) => {
    const tokens = detail.tokens;
    cachedTokens += Math.max(
      typeof tokens.cached_tokens === 'number' ? Math.max(tokens.cached_tokens, 0) : 0,
      typeof tokens.cache_tokens === 'number' ? Math.max(tokens.cache_tokens, 0) : 0
    );
    if (typeof tokens.reasoning_tokens === 'number') {
      reasoningTokens += tokens.reasoning_tokens;
    }
  });

  return { cachedTokens, reasoningTokens };
}

/**
 * 计算最近 N 分钟的 RPM/TPM
 */
export function calculateRecentPerMinuteRates(
  windowMinutes: number = 30,
  usageData: unknown
): RateStats {
  const details = collectUsageDetails(usageData);
  const effectiveWindow = Number.isFinite(windowMinutes) && windowMinutes > 0 ? windowMinutes : 30;

  if (!details.length) {
    return { rpm: 0, tpm: 0, windowMinutes: effectiveWindow, requestCount: 0, tokenCount: 0 };
  }

  const now = Date.now();
  const windowStart = now - effectiveWindow * 60 * 1000;
  let requestCount = 0;
  let tokenCount = 0;

  details.forEach((detail) => {
    const timestamp =
      typeof detail.__timestampMs === 'number'
        ? detail.__timestampMs
        : parseTimestampMs(detail.timestamp);
    if (!Number.isFinite(timestamp) || timestamp < windowStart || timestamp > now) {
      return;
    }
    requestCount += 1;
    tokenCount += extractTotalTokens(detail);
  });

  const denominator = effectiveWindow > 0 ? effectiveWindow : 1;
  return {
    rpm: requestCount / denominator,
    tpm: tokenCount / denominator,
    windowMinutes: effectiveWindow,
    requestCount,
    tokenCount,
  };
}

/**
 * 从使用数据获取模型名称列表
 */
export function getModelNamesFromUsage(usageData: unknown): string[] {
  const apis = getApisRecord(usageData);
  if (!apis) return [];
  const names = new Set<string>();
  Object.values(apis).forEach((apiEntry) => {
    if (!isRecord(apiEntry)) return;
    const modelsRaw = apiEntry.models;
    const models = isRecord(modelsRaw) ? modelsRaw : null;
    if (!models) return;
    Object.keys(models).forEach((modelName) => {
      if (modelName) {
        names.add(modelName);
      }
    });
  });
  return Array.from(names).sort((a, b) => a.localeCompare(b));
}

/**
 * 获取 API 统计数据
 */
export function getApiStats(
  usageData: unknown,
  modelPrices: Record<string, ModelPrice>
): ApiStats[] {
  const apis = getApisRecord(usageData);
  if (!apis) return [];
  const result: ApiStats[] = [];

  Object.entries(apis).forEach(([endpoint, apiData]) => {
    if (!isRecord(apiData)) return;
    const models: Record<
      string,
      { requests: number; successCount: number; failureCount: number; tokens: number }
    > = {};
    let derivedSuccessCount = 0;
    let derivedFailureCount = 0;
    let totalCost = 0;

    const modelsData = isRecord(apiData.models) ? apiData.models : {};
    Object.entries(modelsData).forEach(([modelName, modelData]) => {
      if (!isRecord(modelData)) return;
      const details = Array.isArray(modelData.details) ? modelData.details : [];
      const hasExplicitCounts =
        typeof modelData.success_count === 'number' || typeof modelData.failure_count === 'number';

      let successCount = 0;
      let failureCount = 0;
      if (hasExplicitCounts) {
        successCount += Number(modelData.success_count) || 0;
        failureCount += Number(modelData.failure_count) || 0;
      }

      const price = modelPrices[modelName];
      if (details.length > 0 && (!hasExplicitCounts || price)) {
        details.forEach((detail) => {
          const detailRecord = isRecord(detail) ? detail : null;
          if (!hasExplicitCounts) {
            if (detailRecord?.failed === true) {
              failureCount += 1;
            } else {
              successCount += 1;
            }
          }

          if (price && detailRecord) {
            totalCost += calculateCost(
              { ...(detailRecord as unknown as UsageDetail), __modelName: modelName },
              modelPrices
            );
          }
        });
      }

      models[modelName] = {
        requests: Number(modelData.total_requests) || 0,
        successCount,
        failureCount,
        tokens: Number(modelData.total_tokens) || 0,
      };
      derivedSuccessCount += successCount;
      derivedFailureCount += failureCount;
    });

    const hasApiExplicitCounts =
      typeof apiData.success_count === 'number' || typeof apiData.failure_count === 'number';
    const successCount = hasApiExplicitCounts
      ? Number(apiData.success_count) || 0
      : derivedSuccessCount;
    const failureCount = hasApiExplicitCounts
      ? Number(apiData.failure_count) || 0
      : derivedFailureCount;

    result.push({
      endpoint: maskUsageSensitiveValue(endpoint) || endpoint,
      totalRequests: Number(apiData.total_requests) || 0,
      successCount,
      failureCount,
      totalTokens: Number(apiData.total_tokens) || 0,
      totalCost,
      models,
    });
  });

  return result;
}

/**
 * 获取模型统计数据
 */
export function getModelStats(
  usageData: unknown,
  modelPrices: Record<string, ModelPrice>
): ModelStatsSummary[] {
  const apis = getApisRecord(usageData);
  if (!apis) return [];

  const modelMap = new Map<
    string,
    {
      requests: number;
      successCount: number;
      failureCount: number;
      tokens: number;
      cost: number;
      latency: LatencyAccumulator;
    }
  >();

  Object.values(apis).forEach((apiData) => {
    if (!isRecord(apiData)) return;
    const modelsRaw = apiData.models;
    const models = isRecord(modelsRaw) ? modelsRaw : null;
    if (!models) return;

    Object.entries(models).forEach(([modelName, modelData]) => {
      if (!isRecord(modelData)) return;
      const existing = modelMap.get(modelName) || {
        requests: 0,
        successCount: 0,
        failureCount: 0,
        tokens: 0,
        cost: 0,
        latency: createLatencyAccumulator(),
      };
      existing.requests += Number(modelData.total_requests) || 0;
      existing.tokens += Number(modelData.total_tokens) || 0;

      const details = Array.isArray(modelData.details) ? modelData.details : [];

      const price = modelPrices[modelName];

      const hasExplicitCounts =
        typeof modelData.success_count === 'number' || typeof modelData.failure_count === 'number';
      if (hasExplicitCounts) {
        existing.successCount += Number(modelData.success_count) || 0;
        existing.failureCount += Number(modelData.failure_count) || 0;
      }

      if (details.length > 0) {
        details.forEach((detail) => {
          const detailRecord = isRecord(detail) ? detail : null;
          const latencyMs = extractLatencyMs(detailRecord);
          if (!hasExplicitCounts) {
            if (detailRecord?.failed === true) {
              existing.failureCount += 1;
            } else {
              existing.successCount += 1;
            }
          }

          addLatencySample(existing.latency, latencyMs);

          if (price && detailRecord) {
            existing.cost += calculateCost(
              { ...(detailRecord as unknown as UsageDetail), __modelName: modelName },
              modelPrices
            );
          }
        });
      }
      modelMap.set(modelName, existing);
    });
  });

  return Array.from(modelMap.entries())
    .map(([model, stats]) => {
      const latencyStats = finalizeLatencyStats(stats.latency);
      return {
        model,
        requests: stats.requests,
        successCount: stats.successCount,
        failureCount: stats.failureCount,
        tokens: stats.tokens,
        cost: stats.cost,
        averageLatencyMs: latencyStats.averageMs,
        totalLatencyMs: latencyStats.totalMs,
        latencySampleCount: latencyStats.sampleCount,
      };
    })
    .sort((a, b) => b.requests - a.requests);
}
