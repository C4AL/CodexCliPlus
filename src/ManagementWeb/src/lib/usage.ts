/**
 * 使用统计相关工具
 * 迁移自基线 modules/usage.js 的纯逻辑部分
 */

export type { DurationFormatOptions, LatencyStats } from './usage/latency';
export {
  LATENCY_SOURCE_FIELD,
  LATENCY_SOURCE_UNIT,
  calculateLatencyStatsFromDetails,
  extractLatencyMs,
  formatDurationMs,
} from './usage/latency';

export type {
  ApiStats,
  ChartData,
  ChartDataset,
  CostSeries,
  KeyStatBucket,
  KeyStats,
  ModelPrice,
  ModelStatsSummary,
  RateStats,
  ServiceHealthData,
  StatusBarData,
  StatusBlockDetail,
  StatusBlockState,
  TokenBreakdown,
  TokenBreakdownSeries,
  TokenCategory,
  UsageDetail,
  UsageDetailWithEndpoint,
  UsageTimeRange,
} from './usage/types';

export { normalizeAuthIndex } from './usage/shared';
export {
  buildCandidateUsageSourceIds,
  maskUsageSensitiveValue,
  normalizeUsageSourceId,
} from './usage/sourceId';
export { formatCompactNumber, formatPerMinuteValue, formatUsd } from './usage/formatting';
export {
  collectUsageDetails,
  collectUsageDetailsWithEndpoint,
  extractTotalTokens,
  filterUsageByTimeRange,
} from './usage/details';
export { calculateCost, calculateTotalCost, loadModelPrices, saveModelPrices } from './usage/cost';
export {
  calculateLatencyStats,
  calculateRecentPerMinuteRates,
  calculateTokenBreakdown,
  getApiStats,
  getModelNamesFromUsage,
  getModelStats,
} from './usage/stats';
export {
  buildChartData,
  buildDailySeriesByModel,
  buildHourlySeriesByModel,
  formatDayLabel,
  formatHourLabel,
} from './usage/chartSeries';
export {
  calculateServiceHealthData,
  calculateStatusBarData,
  calculateStatusBarDataFromRecentRequests,
} from './usage/status';
export {
  computeKeyStats,
  computeKeyStatsFromApiKeyUsage,
  computeKeyStatsFromDetails,
  mergeKeyStats,
} from './usage/keyStats';
export { buildDailyTokenBreakdown, buildHourlyTokenBreakdown } from './usage/tokenBreakdown';
export { buildDailyCostSeries, buildHourlyCostSeries } from './usage/costSeries';
