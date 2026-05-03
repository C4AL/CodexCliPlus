import { useMemo } from 'react';
import type { AuthFileItem } from '@/types';
import {
  calculateStatusBarData,
  calculateStatusBarDataFromRecentRequests,
  normalizeAuthIndex,
  type UsageDetail,
} from '@/utils/usage';

export type AuthFileStatusBarData = ReturnType<typeof calculateStatusBarData>;

export function useAuthFilesStatusBarCache(files: AuthFileItem[], usageDetails: UsageDetail[]) {
  return useMemo(() => {
    const cache = new Map<string, AuthFileStatusBarData>();

    const usageDetailsByAuthIndex = new Map<string, UsageDetail[]>();
    usageDetails.forEach((detail) => {
      const authIndexKey = normalizeAuthIndex(detail.auth_index);
      if (!authIndexKey) return;

      const list = usageDetailsByAuthIndex.get(authIndexKey);
      if (list) {
        list.push(detail);
      } else {
        usageDetailsByAuthIndex.set(authIndexKey, [detail]);
      }
    });

    const uniqueAuthIndexKeys = new Set<string>();
    files.forEach((file) => {
      const rawAuthIndex = file['auth_index'] ?? file.authIndex;
      const authIndexKey = normalizeAuthIndex(rawAuthIndex);
      if (!authIndexKey) return;
      uniqueAuthIndexKeys.add(authIndexKey);
    });

    uniqueAuthIndexKeys.forEach((authIndexKey) => {
      cache.set(
        authIndexKey,
        calculateStatusBarData(usageDetailsByAuthIndex.get(authIndexKey) ?? [])
      );
    });

    files.forEach((file) => {
      const rawAuthIndex = file['auth_index'] ?? file.authIndex;
      const authIndexKey = normalizeAuthIndex(rawAuthIndex);
      if (!authIndexKey) return;

      const recentRequests = file.recent_requests ?? file.recentRequests;
      if (!Array.isArray(recentRequests) || recentRequests.length === 0) return;

      const recentStatusData = calculateStatusBarDataFromRecentRequests(recentRequests);
      if (recentStatusData.totalSuccess + recentStatusData.totalFailure > 0) {
        cache.set(authIndexKey, recentStatusData);
      }
    });

    return cache;
  }, [files, usageDetails]);
}
