import { parseTimestampMs } from '../timestamp';
import { collectUsageDetails } from './details';
import { calculateCost } from './cost';
import { formatDayLabel, formatHourLabel } from './chartSeries';
import { createNumberSeries } from './shared';
import type { CostSeries, ModelPrice } from './types';

/**
 * 按小时构建费用时间序列
 */
export function buildHourlyCostSeries(
  usageData: unknown,
  modelPrices: Record<string, ModelPrice>,
  hourWindow: number = 24
): CostSeries {
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

  const data = createNumberSeries(labels.length);
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

    const cost = calculateCost(detail, modelPrices);
    if (cost > 0) {
      data[bucketIndex] += cost;
      hasData = true;
    }
  });

  return { labels, data, hasData };
}

/**
 * 按天构建费用时间序列
 */
export function buildDailyCostSeries(
  usageData: unknown,
  modelPrices: Record<string, ModelPrice>
): CostSeries {
  const details = collectUsageDetails(usageData);
  const dayMap: Record<string, number> = {};
  let hasData = false;

  details.forEach((detail) => {
    const timestamp =
      typeof detail.__timestampMs === 'number'
        ? detail.__timestampMs
        : parseTimestampMs(detail.timestamp);
    if (!Number.isFinite(timestamp) || timestamp <= 0) return;
    const dayLabel = formatDayLabel(new Date(timestamp));
    if (!dayLabel) return;

    const cost = calculateCost(detail, modelPrices);
    if (cost > 0) {
      dayMap[dayLabel] = (dayMap[dayLabel] || 0) + cost;
      hasData = true;
    }
  });

  const labels = Object.keys(dayMap).sort();
  const data = labels.map((l) => dayMap[l]);

  return { labels, data, hasData };
}
