import { parseTimestampMs } from '../timestamp';
import { collectUsageDetails } from './details';
import { formatDayLabel, formatHourLabel } from './chartSeries';
import { createNumberSeries } from './shared';
import type { TokenBreakdownSeries, TokenCategory } from './types';

/**
 * 按 token 类别构建小时级别的堆叠序列
 */
export function buildHourlyTokenBreakdown(
  usageData: unknown,
  hourWindow: number = 24
): TokenBreakdownSeries {
  const hourMs = 60 * 60 * 1000;
  const resolvedHourWindow =
    Number.isFinite(hourWindow) && hourWindow > 0
      ? Math.min(Math.max(Math.floor(hourWindow), 1), 24 * 31)
      : 24;
  const now = new Date();
  const currentHour = new Date(now);
  currentHour.setMinutes(0, 0, 0);

  const earliestBucket = new Date(currentHour);
  earliestBucket.setHours(earliestBucket.getHours() - (resolvedHourWindow - 1));
  const earliestTime = earliestBucket.getTime();

  const labels: string[] = [];
  for (let i = 0; i < resolvedHourWindow; i++) {
    labels.push(formatHourLabel(new Date(earliestTime + i * hourMs)));
  }

  const dataByCategory: Record<TokenCategory, number[]> = {
    input: createNumberSeries(labels.length),
    output: createNumberSeries(labels.length),
    cached: createNumberSeries(labels.length),
    reasoning: createNumberSeries(labels.length),
  };

  const details = collectUsageDetails(usageData);
  let hasData = false;

  details.forEach((detail) => {
    const timestamp =
      typeof detail.__timestampMs === 'number'
        ? detail.__timestampMs
        : parseTimestampMs(detail.timestamp);
    if (!Number.isFinite(timestamp) || timestamp <= 0) return;
    const normalized = new Date(timestamp);
    normalized.setMinutes(0, 0, 0);
    const bucketStart = normalized.getTime();
    const lastBucketTime = earliestTime + (labels.length - 1) * hourMs;
    if (bucketStart < earliestTime || bucketStart > lastBucketTime) return;
    const bucketIndex = Math.floor((bucketStart - earliestTime) / hourMs);
    if (bucketIndex < 0 || bucketIndex >= labels.length) return;

    const tokens = detail.tokens;
    const input = typeof tokens.input_tokens === 'number' ? Math.max(tokens.input_tokens, 0) : 0;
    const output = typeof tokens.output_tokens === 'number' ? Math.max(tokens.output_tokens, 0) : 0;
    const cached = Math.max(
      typeof tokens.cached_tokens === 'number' ? Math.max(tokens.cached_tokens, 0) : 0,
      typeof tokens.cache_tokens === 'number' ? Math.max(tokens.cache_tokens, 0) : 0
    );
    const reasoning =
      typeof tokens.reasoning_tokens === 'number' ? Math.max(tokens.reasoning_tokens, 0) : 0;

    dataByCategory.input[bucketIndex] += input;
    dataByCategory.output[bucketIndex] += output;
    dataByCategory.cached[bucketIndex] += cached;
    dataByCategory.reasoning[bucketIndex] += reasoning;
    hasData = true;
  });

  return { labels, dataByCategory, hasData };
}

/**
 * 按 token 类别构建日级别的堆叠序列
 */
export function buildDailyTokenBreakdown(usageData: unknown): TokenBreakdownSeries {
  const details = collectUsageDetails(usageData);
  const dayMap: Record<string, Record<TokenCategory, number>> = {};
  let hasData = false;

  details.forEach((detail) => {
    const timestamp =
      typeof detail.__timestampMs === 'number'
        ? detail.__timestampMs
        : parseTimestampMs(detail.timestamp);
    if (!Number.isFinite(timestamp) || timestamp <= 0) return;
    const dayLabel = formatDayLabel(new Date(timestamp));
    if (!dayLabel) return;

    if (!dayMap[dayLabel]) {
      dayMap[dayLabel] = { input: 0, output: 0, cached: 0, reasoning: 0 };
    }

    const tokens = detail.tokens;
    const input = typeof tokens.input_tokens === 'number' ? Math.max(tokens.input_tokens, 0) : 0;
    const output = typeof tokens.output_tokens === 'number' ? Math.max(tokens.output_tokens, 0) : 0;
    const cached = Math.max(
      typeof tokens.cached_tokens === 'number' ? Math.max(tokens.cached_tokens, 0) : 0,
      typeof tokens.cache_tokens === 'number' ? Math.max(tokens.cache_tokens, 0) : 0
    );
    const reasoning =
      typeof tokens.reasoning_tokens === 'number' ? Math.max(tokens.reasoning_tokens, 0) : 0;

    dayMap[dayLabel].input += input;
    dayMap[dayLabel].output += output;
    dayMap[dayLabel].cached += cached;
    dayMap[dayLabel].reasoning += reasoning;
    hasData = true;
  });

  const labels = Object.keys(dayMap).sort();
  const dataByCategory: Record<TokenCategory, number[]> = {
    input: labels.map((l) => dayMap[l].input),
    output: labels.map((l) => dayMap[l].output),
    cached: labels.map((l) => dayMap[l].cached),
    reasoning: labels.map((l) => dayMap[l].reasoning),
  };

  return { labels, dataByCategory, hasData };
}
